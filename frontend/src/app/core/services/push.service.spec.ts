import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SwPush } from '@angular/service-worker';
import { BehaviorSubject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PushService } from './push.service';

describe('PushService', () => {
  // Two P-256 public keys, base64url: RFC 8291's example ones.
  const serverKey =
    'BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8';
  const oldServerKey =
    'BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4';
  let subscription: BehaviorSubject<PushSubscription | null>;
  let swPush: {
    isEnabled: boolean;
    subscription: BehaviorSubject<PushSubscription | null>;
    requestSubscription: ReturnType<typeof vi.fn>;
    unsubscribe: ReturnType<typeof vi.fn>;
  };
  let permission: NotificationPermission;
  let requestPermission: ReturnType<typeof vi.fn>;
  let http: HttpTestingController;

  /** What a browser hands over: an endpoint at a push service, and the keys for it. */
  function browserSubscription(
    applicationServerKey: ArrayBuffer | null = keyBytes(serverKey),
  ): PushSubscription {
    return {
      endpoint: 'https://fcm.googleapis.com/fcm/send/device-1',
      options: { applicationServerKey, userVisibleOnly: true },
      toJSON: () => ({
        endpoint: 'https://fcm.googleapis.com/fcm/send/device-1',
        keys: { p256dh: 'BDevicePublicKey', auth: 'AuthSecret' },
      }),
    } as unknown as PushSubscription;
  }

  function keyBytes(base64Url: string): ArrayBuffer {
    const binary = atob(base64Url.replace(/-/g, '+').replace(/_/g, '/'));
    return Uint8Array.from(binary, (c) => c.charCodeAt(0)).buffer;
  }

  beforeEach(() => {
    subscription = new BehaviorSubject<PushSubscription | null>(null);
    swPush = {
      isEnabled: true,
      subscription,
      requestSubscription: vi.fn(),
      unsubscribe: vi.fn().mockImplementation(async () => subscription.next(null)),
    };
    permission = 'default';
    requestPermission = vi.fn();
    // jsdom has neither: stand in for a browser with the Push API.
    Object.assign(window, {
      PushManager: class {},
      Notification: {
        get permission() {
          return permission;
        },
        requestPermission,
      },
    });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SwPush, useValue: swPush },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    delete (window as { PushManager?: unknown }).PushManager;
    delete (window as { Notification?: unknown }).Notification;
  });

  const service = () => TestBed.inject(PushService);
  const flushStatus = (enabled = true) =>
    http
      .expectOne(`${environment.apiUrl}/push`)
      .flush({ enabled, publicKey: enabled ? serverKey : null });

  /** Lets the awaited steps after an answer run before the next request is looked for. */
  const settle = () => new Promise((resolve) => setTimeout(resolve));

  it('is off on a device that never turned them on, and on a server without keys it says so', async () => {
    const push = service();

    const refreshing = push.refresh();
    flushStatus();
    await refreshing;
    expect(push.state()).toBe('off');

    const again = push.refresh();
    flushStatus(false);
    await again;
    expect(push.state()).toBe('server-off');
  });

  it('turns them on from a click: permission first, then the subscription to the server', async () => {
    const push = service();
    const refreshing = push.refresh();
    flushStatus();
    await refreshing;
    requestPermission.mockImplementation(async () => (permission = 'granted'));
    swPush.requestSubscription.mockResolvedValue(browserSubscription());

    const turningOn = push.turnOn();
    await settle();
    const save = http.expectOne(`${environment.apiUrl}/push/subscription`);
    expect(save.request.method).toBe('PUT');
    expect(save.request.body).toEqual({
      endpoint: 'https://fcm.googleapis.com/fcm/send/device-1',
      p256dh: 'BDevicePublicKey',
      auth: 'AuthSecret',
    });
    save.flush(null);
    await turningOn;

    expect(requestPermission).toHaveBeenCalled();
    expect(swPush.requestSubscription).toHaveBeenCalledWith({ serverPublicKey: serverKey });
    expect(push.state()).toBe('on');
  });

  it('says so when the browser is told no, and never subscribes', async () => {
    const push = service();
    const refreshing = push.refresh();
    flushStatus();
    await refreshing;
    requestPermission.mockImplementation(async () => (permission = 'denied'));

    await push.turnOn();

    expect(swPush.requestSubscription).not.toHaveBeenCalled();
    expect(push.state()).toBe('blocked');
  });

  it('hands a device found on to the server again, and drops one made with an old key', async () => {
    permission = 'granted';
    subscription.next(browserSubscription());
    const push = service();

    const refreshing = push.refresh();
    flushStatus();
    await settle();
    http.expectOne({ method: 'PUT', url: `${environment.apiUrl}/push/subscription` }).flush(null);
    await refreshing;
    expect(push.state()).toBe('on');

    subscription.next(browserSubscription(keyBytes(oldServerKey)));
    const again = push.refresh();
    flushStatus();
    await again;
    expect(swPush.unsubscribe).toHaveBeenCalled();
    expect(push.state()).toBe('off');
  });

  it('turns them off: the server forgets the device, then the browser', async () => {
    permission = 'granted';
    subscription.next(browserSubscription());
    const push = service();

    const turningOff = push.turnOff();
    await settle();
    const forget = http.expectOne(`${environment.apiUrl}/push/subscription`);
    expect(forget.request.method).toBe('DELETE');
    expect(forget.request.body).toEqual({
      endpoint: 'https://fcm.googleapis.com/fcm/send/device-1',
    });
    forget.flush(null);
    await turningOff;

    expect(swPush.unsubscribe).toHaveBeenCalled();
    expect(push.state()).toBe('off');
  });

  it('lets this device go at sign-out, even when the server never answers', async () => {
    vi.useFakeTimers();
    try {
      permission = 'granted';
      subscription.next(browserSubscription());
      const push = service();

      const forgetting = push.forgetThisDevice();
      await vi.advanceTimersByTimeAsync(0);
      const forget = http.expectOne({
        method: 'DELETE',
        url: `${environment.apiUrl}/push/subscription`,
      });
      await vi.advanceTimersByTimeAsync(3000);
      await forgetting;

      expect(swPush.unsubscribe).toHaveBeenCalled();
      // The answer comes too late to matter.
      forget.flush(null);
    } finally {
      vi.useRealTimers();
    }
  });

  it('asks an iPhone to add the app to the Home Screen first, and has nothing to do without a service worker', async () => {
    delete (window as { PushManager?: unknown }).PushManager;
    const userAgent = vi
      .spyOn(navigator, 'userAgent', 'get')
      .mockReturnValue('Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Safari/604.1');
    try {
      const push = service();
      await push.refresh();
      expect(push.state()).toBe('install-first');

      swPush.isEnabled = false;
      userAgent.mockReturnValue('Mozilla/5.0 (Windows NT 10.0) Chrome/140');
      await push.refresh();
      expect(push.state()).toBe('unavailable');
      await push.forgetThisDevice();
    } finally {
      userAgent.mockRestore();
    }
  });
});
