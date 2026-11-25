import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import { SignInSession } from '../../../core/models/session.model';
import { NotificationService } from '../../../core/services/notification.service';
import { SessionService } from '../../../core/services/session.service';
import { Sessions } from './sessions';

describe('Sessions', () => {
  const laptop: SignInSession = {
    id: 's-laptop',
    userAgent:
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0',
    method: 'Password',
    startedAt: '2026-11-24T09:00:00Z',
    lastActiveAt: '2026-11-25T08:00:00Z',
    current: true,
  };
  const phone: SignInSession = {
    id: 's-phone',
    userAgent:
      'Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.1 Mobile/15E148 Safari/604.1',
    method: 'Passkey',
    startedAt: '2026-11-20T18:30:00Z',
    lastActiveAt: '2026-11-24T19:00:00Z',
    current: false,
  };
  const old: SignInSession = {
    id: 's-old',
    userAgent: null,
    method: null,
    startedAt: '2026-11-19T07:00:00Z',
    lastActiveAt: '2026-11-19T07:00:00Z',
    current: false,
  };
  let service: {
    list: ReturnType<typeof vi.fn>;
    end: ReturnType<typeof vi.fn>;
    endOthers: ReturnType<typeof vi.fn>;
  };
  let notifications: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<Sessions>;
  let element: HTMLElement;

  async function render(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [Sessions],
      providers: [
        { provide: SessionService, useValue: service },
        { provide: NotificationService, useValue: notifications },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(Sessions);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  beforeEach(() => {
    service = {
      list: vi.fn(() => of([laptop, phone, old])),
      end: vi.fn(() => of(undefined)),
      endOthers: vi.fn(() => of({ ended: 2 })),
    };
    notifications = { success: vi.fn(), error: vi.fn() };
  });

  afterEach(() => vi.restoreAllMocks());

  const names = () =>
    Array.from(element.querySelectorAll('.item .name')).map((n) =>
      n.textContent!.replace(/\s+/g, ' ').trim(),
    );
  const signOutButtons = () =>
    Array.from(element.querySelectorAll<HTMLButtonElement>('.item button'));
  const everywhereElse = () =>
    Array.from(element.querySelectorAll<HTMLButtonElement>('.actions button')).find((b) =>
      b.textContent!.includes('Sign out everywhere else'),
    );

  it('lists each device by its browser, this one marked, with how and when it signed in', async () => {
    await render();

    expect(names()).toEqual(['Edge · Windows This device', 'Safari · iPhone', 'Unknown browser']);
    const metas = Array.from(element.querySelectorAll('.item .meta')).map((m) =>
      m.textContent!.replace(/\s+/g, ' ').trim(),
    );
    expect(metas[0]).toMatch(/^Signed in with the password .+ · last active .+$/);
    expect(metas[1]).toMatch(/^Signed in with a passkey .+/);
    expect(metas[2]).toMatch(/^Signed in .+/);
    expect(element.querySelectorAll('.item app-icon path')).toHaveLength(3);
    // Every device but this one has its own button, named for it.
    expect(signOutButtons().map((b) => b.getAttribute('aria-label'))).toEqual([
      'Sign out Safari · iPhone',
      'Sign out Unknown browser',
    ]);
  });

  it('signs a device out after asking, and takes it off the list', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    await render();

    signOutButtons()[0].click();
    await fixture.whenStable();

    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('Safari · iPhone'));
    expect(service.end).toHaveBeenCalledWith('s-phone');
    expect(names()).toEqual(['Edge · Windows This device', 'Unknown browser']);
    expect(notifications.success).toHaveBeenCalledWith('Signed out of Safari · iPhone.');
  });

  it('leaves the device signed in when the question is declined', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    await render();

    signOutButtons()[0].click();
    await fixture.whenStable();

    expect(service.end).not.toHaveBeenCalled();
    expect(names()).toHaveLength(3);
  });

  it('takes a device off the list that has signed out meanwhile', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    service.end.mockReturnValue(throwError(() => new ApiError(404, 'Not found')));
    await render();

    signOutButtons()[0].click();
    await fixture.whenStable();

    expect(names()).toEqual(['Edge · Windows This device', 'Unknown browser']);
    expect(notifications.error).not.toHaveBeenCalled();
  });

  it('says so when a device could not be signed out', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    service.end.mockReturnValue(throwError(() => new ApiError(500, 'Error')));
    await render();

    signOutButtons()[0].click();
    await fixture.whenStable();

    expect(names()).toHaveLength(3);
    expect(notifications.error).toHaveBeenCalledWith(
      'That device could not be signed out. Please try again.',
    );
  });

  it('signs out everywhere else, keeping this device', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await render();

    everywhereElse()!.click();
    await fixture.whenStable();

    expect(service.endOthers).toHaveBeenCalled();
    expect(names()).toEqual(['Edge · Windows This device']);
    expect(notifications.success).toHaveBeenCalledWith('Signed out of 2 other devices.');
    expect(everywhereElse()).toBeUndefined();
    expect(element.textContent).toContain("You're signed in on this device only.");
  });

  it('says when it could not load the devices, and tries again', async () => {
    service.list.mockReturnValueOnce(throwError(() => new ApiError(0, 'Offline')));
    await render();

    expect(element.querySelector('[role="alert"]')!.textContent).toContain(
      'Your devices could not be loaded.',
    );
    element.querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    await fixture.whenStable();

    expect(names()).toHaveLength(3);
  });
});
