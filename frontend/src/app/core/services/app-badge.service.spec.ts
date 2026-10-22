import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AppBadgeService } from './app-badge.service';
import { InboxService } from './inbox.service';

describe('AppBadgeService', () => {
  const unread = signal(0);
  let setAppBadge: ReturnType<typeof vi.fn>;
  let clearAppBadge: ReturnType<typeof vi.fn>;

  /** jsdom has no Badging API: stand in for a browser with one. */
  function browserWithBadges(): void {
    setAppBadge = vi.fn(() => Promise.resolve());
    clearAppBadge = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, 'setAppBadge', { value: setAppBadge, configurable: true });
    Object.defineProperty(navigator, 'clearAppBadge', { value: clearAppBadge, configurable: true });
  }

  beforeEach(() => {
    unread.set(0);
    TestBed.configureTestingModule({
      providers: [{ provide: InboxService, useValue: { unread } }],
    });
  });

  afterEach(() => {
    delete (navigator as { setAppBadge?: unknown }).setAppBadge;
    delete (navigator as { clearAppBadge?: unknown }).clearAppBadge;
  });

  it('puts the unread count on the icon, and takes it off when nothing is unread', () => {
    browserWithBadges();
    TestBed.inject(AppBadgeService).start();
    TestBed.tick();
    expect(clearAppBadge).toHaveBeenCalledTimes(1);

    unread.set(3);
    TestBed.tick();
    expect(setAppBadge).toHaveBeenLastCalledWith(3);

    // Read, or signed out: the inbox counts 0 then.
    unread.set(0);
    TestBed.tick();
    expect(clearAppBadge).toHaveBeenCalledTimes(2);
    expect(setAppBadge).toHaveBeenCalledTimes(1);
  });

  it('lets a browser refuse quietly, and starts once', async () => {
    browserWithBadges();
    setAppBadge.mockRejectedValue(new DOMException('Not installed', 'NotAllowedError'));
    const badge = TestBed.inject(AppBadgeService);
    badge.start();
    badge.start();

    unread.set(2);
    TestBed.tick();
    await Promise.resolve();

    expect(setAppBadge).toHaveBeenCalledTimes(1);
  });

  it('does nothing in a browser without the Badging API', () => {
    TestBed.inject(AppBadgeService).start();
    unread.set(5);

    expect(() => TestBed.tick()).not.toThrow();
  });
});
