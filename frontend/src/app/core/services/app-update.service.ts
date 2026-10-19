import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, InjectionToken, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { filter, interval } from 'rxjs';

/** How often a page left open asks for a new version: a door screen runs for days. */
export const UPDATE_CHECK_INTERVAL_MS = 60 * 60 * 1000;

/** Reloads the page. A token, so tests can see it asked without leaving the page. */
export const RELOAD_PAGE = new InjectionToken<() => void>('RELOAD_PAGE', {
  providedIn: 'root',
  factory: () => {
    const document = inject(DOCUMENT);
    return () => document.location.reload();
  },
});

/**
 * A new version of the app (ADR-0054). The service worker fetches it in the background; once
 * it's ready, the app says so and reloads when asked. A door screen, which nobody stands at,
 * reloads by itself. Does nothing without a service worker, as under `ng serve` and in tests.
 */
@Injectable({ providedIn: 'root' })
export class AppUpdateService {
  private readonly swUpdate = inject(SwUpdate, { optional: true });
  private readonly router = inject(Router);
  private readonly reloadPage = inject(RELOAD_PAGE);
  private readonly destroyRef = inject(DestroyRef);
  private readonly readyState = signal(false);
  private started = false;

  /** A new version is ready, and a reload shows it. */
  readonly ready = this.readyState.asReadonly();

  start(): void {
    const swUpdate = this.swUpdate;
    if (this.started || !swUpdate?.isEnabled) {
      return;
    }
    this.started = true;

    swUpdate.versionUpdates
      .pipe(
        filter((event): event is VersionReadyEvent => event.type === 'VERSION_READY'),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        if (this.router.url.startsWith('/door/')) {
          void this.reload();
        } else {
          this.readyState.set(true);
        }
      });
    // The version this page runs can't be loaded again - its files are gone from the cache
    // and the server - so only a reload gets a whole one.
    swUpdate.unrecoverable
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.reloadPage());
    interval(UPDATE_CHECK_INTERVAL_MS)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => void swUpdate.checkForUpdate().catch(() => false));
  }

  /** Shows the new version. */
  async reload(): Promise<void> {
    await this.swUpdate?.activateUpdate().catch(() => false);
    this.reloadPage();
  }

  /** Not now: the next time the app opens, it's the new version anyway. */
  later(): void {
    this.readyState.set(false);
  }
}
