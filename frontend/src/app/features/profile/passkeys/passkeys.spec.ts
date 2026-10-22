import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import { Passkey } from '../../../core/models/passkey.model';
import { NotificationService } from '../../../core/services/notification.service';
import { PasskeyService } from '../../../core/services/passkey.service';
import { Passkeys } from './passkeys';

describe('Passkeys', () => {
  const iphone: Passkey = {
    id: 'cred-1',
    name: 'iPhone',
    createdAt: '2026-10-20T09:00:00Z',
    synced: true,
  };
  let service: {
    supported: boolean;
    list: ReturnType<typeof vi.fn>;
    add: ReturnType<typeof vi.fn>;
    remove: ReturnType<typeof vi.fn>;
  };
  let notifications: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<Passkeys>;
  let element: HTMLElement;

  async function render(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [Passkeys],
      providers: [
        { provide: PasskeyService, useValue: service },
        { provide: NotificationService, useValue: notifications },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(Passkeys);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  beforeEach(() => {
    service = {
      supported: true,
      list: vi.fn(() => of([iphone])),
      add: vi.fn(),
      remove: vi.fn(() => of(undefined)),
    };
    notifications = { success: vi.fn(), error: vi.fn() };
  });

  afterEach(() => vi.restoreAllMocks());

  const names = () =>
    Array.from(element.querySelectorAll('.item .name')).map((n) => n.textContent!.trim());

  async function addWith(name: string, password: string): Promise<void> {
    const nameInput = element.querySelector<HTMLInputElement>('#passkey-name')!;
    nameInput.value = name;
    nameInput.dispatchEvent(new Event('input'));
    const passwordInput = element.querySelector<HTMLInputElement>('#passkey-password')!;
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));
    element.querySelector<HTMLFormElement>('form.add')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    // The add awaits the service; let it finish and render.
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  }

  it('lists the passkeys, saying when each was added and whether it syncs', async () => {
    await render();

    expect(names()).toEqual(['iPhone']);
    expect(element.querySelector('.item .meta')!.textContent).toContain('Oct 20, 2026');
    expect(element.querySelector('.item .meta')!.textContent).toContain(
      'synced to your other devices',
    );
  });

  it('adds one on this device with the password, and lists it first', async () => {
    const laptop: Passkey = {
      id: 'cred-2',
      name: 'Laptop',
      createdAt: '2026-10-22T09:00:00Z',
      synced: false,
    };
    service.add.mockResolvedValue(laptop);
    await render();

    await addWith(' Laptop ', 'Secret123!');

    expect(service.add).toHaveBeenCalledWith('Secret123!', 'Laptop');
    expect(names()).toEqual(['Laptop', 'iPhone']);
    expect(element.querySelector<HTMLInputElement>('#passkey-password')!.value).toBe('');
    expect(notifications.success).toHaveBeenCalled();
  });

  it('shows a wrong password at its field, and says so when the device prompt was closed', async () => {
    await render();

    service.add.mockRejectedValue(
      new ApiError(400, 'Validation failed', null, { Password: ['The password is wrong.'] }),
    );
    await addWith('iPad', 'guess');
    expect(element.querySelector('#passkey-password-error')!.textContent).toContain(
      'The password is wrong.',
    );

    service.add.mockRejectedValue(new DOMException('closed', 'NotAllowedError'));
    await addWith('iPad', 'Secret123!');
    expect(element.querySelector('[role="alert"]')!.textContent).toContain('No passkey was made.');

    service.add.mockRejectedValue(new DOMException('already', 'InvalidStateError'));
    await addWith('iPad', 'Secret123!');
    expect(element.querySelector('[role="alert"]')!.textContent).toContain(
      'This device already has a passkey for your account.',
    );
  });

  it('removes one once the person confirms', async () => {
    const confirm = vi
      .spyOn(window, 'confirm')
      .mockReturnValueOnce(false)
      .mockReturnValueOnce(true);
    await render();
    const remove = () => element.querySelector<HTMLButtonElement>('.item button')!;

    expect(remove().getAttribute('aria-label')).toBe('Remove iPhone');
    remove().click();
    expect(service.remove).not.toHaveBeenCalled();

    remove().click();
    await fixture.whenStable();

    expect(confirm).toHaveBeenCalledTimes(2);
    expect(service.remove).toHaveBeenCalledWith('cred-1');
    expect(names()).toEqual([]);
    expect(element.textContent).toContain('You have no passkeys yet.');
  });

  it("says when this browser can't make passkeys here, and when the list could not be loaded", async () => {
    service.supported = false;
    service.list.mockReturnValueOnce(throwError(() => new ApiError(500, 'Server error')));
    await render();

    expect(element.textContent).toContain('Your passkeys could not be loaded.');
    element.querySelector<HTMLButtonElement>('.load-error button')!.click();
    await fixture.whenStable();

    expect(names()).toEqual(['iPhone']);
    expect(element.querySelector('form.add')).toBeNull();
    expect(element.textContent).toContain("This browser can't make passkeys on this page.");
  });
});
