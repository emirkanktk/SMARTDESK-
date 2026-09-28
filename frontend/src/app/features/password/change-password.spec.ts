import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { ChangePassword } from './change-password';

describe('ChangePassword', () => {
  let changePassword: ReturnType<typeof vi.fn>;
  let logout: ReturnType<typeof vi.fn>;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;
  const passwordChangeRequired = signal(false);
  let fixture: ComponentFixture<ChangePassword>;
  let element: HTMLElement;

  beforeEach(async () => {
    passwordChangeRequired.set(false);
    changePassword = vi.fn(() =>
      of({ accessToken: 'new', expiresAt: '', passwordChangeRequired: false }),
    );
    logout = vi.fn();
    await TestBed.configureTestingModule({
      imports: [ChangePassword],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { changePassword, logout, passwordChangeRequired } },
      ],
    }).compileComponents();
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(ChangePassword);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  async function type(selector: string, value: string): Promise<void> {
    const input = element.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
  }

  async function submit(current: string, next: string, confirm = next): Promise<void> {
    await type('#current-password', current);
    await type('#new-password', next);
    await type('#confirm', confirm);
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  const text = () => element.textContent!.replace(/\s+/g, ' ');

  it('changes the password, then goes on to the dashboard', async () => {
    await render();

    await submit('Old-Pass-2026', 'Brand-New-Pass-7');

    expect(changePassword).toHaveBeenCalledWith({
      currentPassword: 'Old-Pass-2026',
      newPassword: 'Brand-New-Pass-7',
    });
    expect(TestBed.inject(NotificationService).notifications()[0]?.message).toContain(
      'Your password has been changed',
    );
    expect(navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('explains a forced change, asks for the temporary password, and offers to sign out', async () => {
    passwordChangeRequired.set(true);
    await render();

    expect(text()).toContain('Choose your own password');
    expect(text()).toContain('Your password was set by someone else');
    expect(element.querySelector('label[for="current-password"]')!.textContent).toContain(
      'Temporary password',
    );
    expect(element.querySelector('a.back')).toBeNull();

    element.querySelector<HTMLButtonElement>('.link-button')!.click();
    expect(logout).toHaveBeenCalled();
    expect(navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('offers a way back when the change is by choice', async () => {
    await render();

    expect(text()).toContain('Change your password');
    expect(element.querySelector('a.back')!.getAttribute('href')).toBe('/dashboard');
    expect(element.querySelector('.link-button')).toBeNull();
  });

  it('checks the new password and its repeat without asking the API', async () => {
    await render();

    await submit('Old-Pass-2026', 'short', 'shorter');

    expect(changePassword).not.toHaveBeenCalled();
    expect(text()).toContain('Add at least 8 characters, an uppercase letter, a digit');
    expect(text()).toContain('The two passwords are different.');
  });

  it('shows a wrong current password under that field until it’s edited', async () => {
    changePassword.mockReturnValue(
      throwError(
        () =>
          new ApiError(400, 'One or more validation errors occurred.', null, {
            CurrentPassword: ['The current password is wrong.'],
          }),
      ),
    );
    await render();
    await submit('Wrong-Pass-1', 'Brand-New-Pass-7');

    expect(element.querySelector('#current-password-error')!.textContent).toContain(
      'The current password is wrong.',
    );
    expect(navigateByUrl).not.toHaveBeenCalled();

    await type('#current-password', 'Old-Pass-2026');
    expect(element.querySelector('#current-password-error')).toBeNull();
  });

  it('shows the API’s reasons for refusing the new password', async () => {
    changePassword.mockReturnValue(
      throwError(
        () =>
          new ApiError(400, 'One or more validation errors occurred.', null, {
            NewPassword: ['Choose a password different from the current one.'],
          }),
      ),
    );
    await render();
    await submit('Temp-Pass-2026', 'Temp-Pass-2027');

    expect(element.querySelector('#new-password-help')!.textContent).toContain(
      'Choose a password different from the current one.',
    );
  });
});
