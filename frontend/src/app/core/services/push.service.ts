import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { SwPush } from '@angular/service-worker';
import { Observable, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';
import { PushStatus, PushTestResult } from '../models/push.model';

/**
 * Where notifications stand on this device (ADR-0056):
 * - `unavailable`: no service worker, as under `ng serve`, or a browser without the Push API;
 * - `install-first`: Safari on an iPhone or iPad, which gets them only in the app added to the
 *   Home Screen;
 * - `server-off`: the server has no keys to send them with;
 * - `blocked`: the browser's settings refuse SmartDesk's notifications;
 * - `off` and `on`: this device, for the signed-in user.
 */
export type PushState = 'unavailable' | 'install-first' | 'server-off' | 'blocked' | 'off' | 'on';

/** How long signing out waits for the server to forget this device: never longer than this. */
export const FORGET_TIMEOUT_MS = 3000;

/**
 * Notifications on this device (ADR-0056), through the app's service worker (ADR-0054): turned on
 * from a click, which the browser asks permission in, then subscribed with the server's key and
 * handed to the API, which sends through the device's push service from then on.
 */
@Injectable({ providedIn: 'root' })
export class PushService {
  private readonly http = inject(HttpClient);
  private readonly swPush = inject(SwPush, { optional: true });
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly baseUrl = `${environment.apiUrl}/push`;
  private readonly stateSignal = signal<PushState | null>(null);
  private publicKey: string | null = null;

  /** Null until refresh() has looked. */
  readonly state = this.stateSignal.asReadonly();

  /**
   * Looks where this device stands. Found on, it's handed to the server again: the device is the
   * signed-in user's, and the server has it even if it had let it go. One made with a key the
   * server no longer has can't be sent to, so it's dropped.
   */
  async refresh(): Promise<void> {
    const local = this.localState();
    if (local) {
      this.stateSignal.set(local);
      return;
    }

    const status = await firstValueFrom(
      this.http.get<PushStatus>(this.baseUrl, { context: quietly() }),
    );
    if (!status.enabled || !status.publicKey) {
      this.stateSignal.set('server-off');
      return;
    }

    this.publicKey = status.publicKey;
    if (this.permission() === 'denied') {
      this.stateSignal.set('blocked');
      return;
    }

    const subscription = await firstValueFrom(this.swPush!.subscription);
    if (!subscription || this.permission() !== 'granted') {
      this.stateSignal.set('off');
      return;
    }

    const key = subscription.options?.applicationServerKey;
    if (key && toBase64Url(key) !== status.publicKey) {
      await this.swPush!.unsubscribe();
      this.stateSignal.set('off');
      return;
    }

    await this.save(subscription);
    this.stateSignal.set('on');
  }

  /**
   * Turns them on, from a click: Safari on an iPhone asks for permission only in answer to one, so
   * that comes first, before anything waits on the network.
   */
  async turnOn(): Promise<void> {
    const notifications = this.window?.Notification;
    if (!this.swPush || !this.publicKey || !notifications) {
      return;
    }

    const permission = await notifications.requestPermission();
    if (permission !== 'granted') {
      this.stateSignal.set(permission === 'denied' ? 'blocked' : 'off');
      return;
    }

    const subscription = await this.swPush.requestSubscription({ serverPublicKey: this.publicKey });
    await this.save(subscription);
    this.stateSignal.set('on');
  }

  /** Turns them off here: the server forgets the device, and the browser its subscription. */
  async turnOff(): Promise<void> {
    const subscription = this.swPush ? await firstValueFrom(this.swPush.subscription) : null;
    if (subscription) {
      await firstValueFrom(this.forget(subscription.endpoint));
      await this.swPush!.unsubscribe();
    }
    this.stateSignal.set('off');
  }

  /** One notification to each of the user's devices, to see them arrive. */
  sendTest(): Observable<PushTestResult> {
    return this.http.post<PushTestResult>(`${this.baseUrl}/test`, null);
  }

  /**
   * At sign-out: this device stops notifying whoever signs out. Never fails, and never holds the
   * sign-out up for long - the subscription is dropped here even when the server can't be told, and
   * the server then forgets it the first time it sends.
   */
  async forgetThisDevice(): Promise<void> {
    const swPush = this.swPush;
    if (!swPush?.isEnabled) {
      return;
    }

    const forget = async () => {
      const subscription = await firstValueFrom(swPush.subscription);
      if (subscription) {
        await Promise.race([
          firstValueFrom(this.forget(subscription.endpoint)).catch(() => undefined),
          wait(FORGET_TIMEOUT_MS),
        ]);
        await swPush.unsubscribe();
      }
    };
    await Promise.race([forget().catch(() => undefined), wait(FORGET_TIMEOUT_MS * 2)]);
    this.stateSignal.set(null);
  }

  private save(subscription: PushSubscription): Promise<void> {
    const { endpoint, keys } = subscription.toJSON();
    return firstValueFrom(
      this.http.put<void>(
        `${this.baseUrl}/subscription`,
        { endpoint, p256dh: keys?.['p256dh'], auth: keys?.['auth'] },
        { context: quietly() },
      ),
    );
  }

  private forget(endpoint: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/subscription`, {
      body: { endpoint },
      context: quietly(),
    });
  }

  /** What this browser settles before the server is asked. */
  private localState(): PushState | null {
    const window = this.window;
    const pushApi = !!window && 'PushManager' in window && 'Notification' in window;
    if (this.swPush?.isEnabled && pushApi) {
      return null;
    }

    // Safari on an iPhone or iPad has the Push API only in the app added to the Home Screen.
    return window && isAppleMobile(window) && !isStandalone(window)
      ? 'install-first'
      : 'unavailable';
  }

  private permission(): NotificationPermission {
    return this.window?.Notification?.permission ?? 'default';
  }
}

/** The screen that asked says what went wrong, not a toast. */
function quietly(): HttpContext {
  return new HttpContext().set(SKIP_ERROR_NOTIFICATION, true);
}

function wait(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function toBase64Url(key: ArrayBuffer): string {
  return btoa(String.fromCharCode(...new Uint8Array(key)))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
}

/** An iPhone or iPad - which says it's a Mac, but one with a touch screen. */
function isAppleMobile(window: Window): boolean {
  const { userAgent, maxTouchPoints } = window.navigator;
  return (
    /iPhone|iPad|iPod/.test(userAgent) || (userAgent.includes('Macintosh') && maxTouchPoints > 1)
  );
}

/** Opened from the Home Screen, as its own app. */
function isStandalone(window: Window): boolean {
  return (
    window.matchMedia?.('(display-mode: standalone)').matches === true ||
    (window.navigator as Navigator & { standalone?: boolean }).standalone === true
  );
}
