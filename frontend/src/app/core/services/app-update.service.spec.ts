import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { SwUpdate, UnrecoverableStateEvent, VersionEvent } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { AppUpdateService, RELOAD_PAGE, UPDATE_CHECK_INTERVAL_MS } from './app-update.service';

const version = (hash: string) => ({ hash, appData: undefined });

const READY: VersionEvent = {
  type: 'VERSION_READY',
  currentVersion: version('old'),
  latestVersion: version('new'),
};

describe('AppUpdateService', () => {
  let versionUpdates: Subject<VersionEvent>;
  let unrecoverable: Subject<UnrecoverableStateEvent>;
  let checkForUpdate: ReturnType<typeof vi.fn>;
  let activateUpdate: ReturnType<typeof vi.fn>;
  let reloadPage: ReturnType<typeof vi.fn>;

  function setUp(
    options: { enabled?: boolean; url?: string; worker?: boolean } = {},
  ): AppUpdateService {
    const { enabled = true, url = '/dashboard', worker = true } = options;
    versionUpdates = new Subject();
    unrecoverable = new Subject();
    checkForUpdate = vi.fn().mockResolvedValue(false);
    activateUpdate = vi.fn().mockResolvedValue(true);
    reloadPage = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: Router, useValue: { url } },
        { provide: RELOAD_PAGE, useValue: reloadPage },
        worker
          ? {
              provide: SwUpdate,
              useValue: {
                isEnabled: enabled,
                versionUpdates,
                unrecoverable,
                checkForUpdate,
                activateUpdate,
              },
            }
          : [],
      ],
    });
    return TestBed.inject(AppUpdateService);
  }

  afterEach(() => vi.useRealTimers());

  it('says a new version is ready once it is, and reloads into it when asked', async () => {
    const service = setUp();
    service.start();

    versionUpdates.next({ type: 'VERSION_DETECTED', version: version('new') });
    expect(service.ready()).toBe(false);
    versionUpdates.next(READY);
    expect(service.ready()).toBe(true);
    expect(reloadPage).not.toHaveBeenCalled();

    await service.reload();
    expect(activateUpdate).toHaveBeenCalled();
    expect(reloadPage).toHaveBeenCalledTimes(1);
  });

  it('puts the notice away when it is not the time', () => {
    const service = setUp();
    service.start();
    versionUpdates.next(READY);

    service.later();

    expect(service.ready()).toBe(false);
    expect(reloadPage).not.toHaveBeenCalled();
  });

  it('reloads a door screen by itself, as nobody stands there to ask', async () => {
    const service = setUp({ url: '/door/room-1' });
    service.start();

    versionUpdates.next(READY);
    await vi.waitFor(() => expect(reloadPage).toHaveBeenCalledTimes(1));

    expect(service.ready()).toBe(false);
  });

  it('reloads when the version it runs can no longer be loaded', () => {
    setUp().start();

    unrecoverable.next({
      type: 'UNRECOVERABLE_STATE',
      reason: 'Gone from the cache and the server',
    });

    expect(reloadPage).toHaveBeenCalledTimes(1);
  });

  it('asks for a new version every hour, for a page left open', () => {
    vi.useFakeTimers();
    setUp().start();

    vi.advanceTimersByTime(UPDATE_CHECK_INTERVAL_MS - 1);
    expect(checkForUpdate).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(checkForUpdate).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(UPDATE_CHECK_INTERVAL_MS);
    expect(checkForUpdate).toHaveBeenCalledTimes(2);
  });

  it('does nothing without a service worker, as under ng serve', async () => {
    const off = setUp({ enabled: false });
    off.start();
    versionUpdates.next(READY);
    expect(off.ready()).toBe(false);

    TestBed.resetTestingModule();
    const none = setUp({ worker: false });
    none.start();
    await none.reload();
    expect(reloadPage).toHaveBeenCalledTimes(1);
  });
});
