import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AppUpdateService } from '../../core/services/app-update.service';
import { ConnectivityService } from '../../core/services/connectivity.service';
import { NotificationService } from '../../core/services/notification.service';
import { NotificationHost } from './notification-host';

describe('NotificationHost', () => {
  const online = signal(true);
  const ready = signal(false);
  const appUpdate = { ready, reload: vi.fn().mockResolvedValue(undefined), later: vi.fn() };
  let fixture: ComponentFixture<NotificationHost>;
  let element: HTMLElement;

  beforeEach(async () => {
    online.set(true);
    ready.set(false);
    appUpdate.reload.mockClear();
    appUpdate.later.mockClear();
    TestBed.configureTestingModule({
      providers: [
        { provide: ConnectivityService, useValue: { online } },
        { provide: AppUpdateService, useValue: appUpdate },
      ],
    });
    fixture = TestBed.createComponent(NotificationHost);
    element = fixture.nativeElement;
    await fixture.whenStable();
  });

  const toasts = () =>
    Array.from(element.querySelectorAll('.toast')).map((toast) =>
      toast.textContent!.replace(/\s+/g, ' ').trim(),
    );

  it('says so while offline, above the toasts, and stops once back', async () => {
    TestBed.inject(NotificationService).info('Booked Board Room');
    online.set(false);
    await fixture.whenStable();

    expect(toasts()[0]).toContain("You're offline.");
    expect(toasts()[1]).toContain('Booked Board Room');

    online.set(true);
    await fixture.whenStable();
    expect(toasts().join()).not.toContain("You're offline.");
  });

  it('offers the new version once it is ready: reload now, or not now', async () => {
    expect(element.querySelector('.update')).toBeNull();

    ready.set(true);
    await fixture.whenStable();
    const update = element.querySelector('.update')!;
    expect(update.textContent).toContain('A new version of SmartDesk is ready.');

    update.querySelector<HTMLButtonElement>('.btn')!.click();
    expect(appUpdate.reload).toHaveBeenCalled();
    update.querySelector<HTMLButtonElement>('.close')!.click();
    expect(appUpdate.later).toHaveBeenCalled();
  });
});
