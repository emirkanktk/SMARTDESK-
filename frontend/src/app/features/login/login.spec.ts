import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { LoginResponse } from '../../core/models/auth.model';
import { AuthService } from '../../core/services/auth.service';
import { PasskeyService } from '../../core/services/passkey.service';
import { Login } from './login';

describe('Login', () => {
  let fixture: ComponentFixture<Login>;
  let element: HTMLElement;
  let response: Subject<LoginResponse>;
  let login: ReturnType<typeof vi.fn>;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;
  let passkeys: { supported: boolean; signIn: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    response = new Subject<LoginResponse>();
    login = vi.fn(() => response);
    passkeys = { supported: true, signIn: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { login } },
        { provide: PasskeyService, useValue: passkeys },
      ],
    }).compileComponents();

    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(Login);
    element = fixture.nativeElement;
    await fixture.whenStable();
  });

  async function fillAndSubmit(email: string, password: string): Promise<void> {
    setValue('#email', email);
    setValue('#password', password);
    element.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function setValue(selector: string, value: string): void {
    const input = element.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('is the main landmark of the page', () => {
    expect(element.getAttribute('role')).toBe('main');
  });

  it('links back to the front page', () => {
    const back = element.querySelector<HTMLAnchorElement>('a.back')!;

    expect(back.textContent).toContain('Back to home');
    expect(back.getAttribute('href')).toBe('/');
  });

  it('offers a way to reset a forgotten password', () => {
    expect(element.querySelector('a.forgot')!.getAttribute('href')).toBe('/forgot-password');
  });

  it('confirms a password reset when coming back from one', async () => {
    expect(element.querySelector('.notice')).toBeNull();

    fixture.componentRef.setInput('reset', 'done');
    await fixture.whenStable();

    expect(element.querySelector('.notice')!.textContent).toContain(
      'Your password has been changed.',
    );
  });

  it('shows field errors and does not call the API when the form is invalid', async () => {
    await fillAndSubmit('not-an-email', '');

    expect(login).not.toHaveBeenCalled();
    expect(element.textContent).toContain('Enter a valid email address.');
    expect(element.textContent).toContain('Password is required.');
  });

  it('signs in and goes to the dashboard by default', async () => {
    await fillAndSubmit('employee1@smartdesk.local', 'secret');

    expect(login).toHaveBeenCalledWith({ email: 'employee1@smartdesk.local', password: 'secret' });
    response.next({ accessToken: 't', expiresAt: '' });

    expect(navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('returns to the requested in-app page after signing in', async () => {
    fixture.componentRef.setInput('returnUrl', '/reservations/mine');
    await fillAndSubmit('employee1@smartdesk.local', 'secret');
    response.next({ accessToken: 't', expiresAt: '' });

    expect(navigateByUrl).toHaveBeenCalledWith('/reservations/mine');
  });

  it('ignores a return url that points off-site', async () => {
    fixture.componentRef.setInput('returnUrl', '//evil.example.com');
    await fillAndSubmit('employee1@smartdesk.local', 'secret');
    response.next({ accessToken: 't', expiresAt: '' });

    expect(navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('shows a message when the credentials are wrong', async () => {
    await fillAndSubmit('employee1@smartdesk.local', 'wrong');
    response.error(new ApiError(401, 'Unauthorized', 'Invalid email or password.'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'Invalid email or password.',
    );
    expect(element.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(false);
  });

  it('says why an account that is waiting for approval or switched off cannot sign in', async () => {
    const waiting =
      "Your account is waiting for an administrator's approval. We'll email you when it's ready.";
    await fillAndSubmit('zeynep@example.com', 'right-password');
    response.error(new ApiError(403, 'Forbidden', waiting));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(element.querySelector('[role="alert"]')?.textContent).toContain(waiting);
  });

  it('asks to wait after too many attempts', async () => {
    await fillAndSubmit('employee1@smartdesk.local', 'wrong');
    response.error(new ApiError(429, 'Too Many Requests'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'Too many attempts. Please wait a minute and try again.',
    );
  });

  it('offers a way to sign up', () => {
    expect(element.querySelector('a[href="/register"]')?.textContent).toContain('Sign up');
  });

  describe('with a passkey (ADR-0057)', () => {
    const passkeyButton = () => element.querySelector<HTMLButtonElement>('button.passkey');

    async function signInWithPasskey(): Promise<void> {
      passkeyButton()!.click();
      await fixture.whenStable();
      // The sign-in awaits the device and the API; let it finish and render.
      await new Promise((resolve) => setTimeout(resolve));
      await fixture.whenStable();
    }

    it('signs in without an email or password and goes where it was asked to', async () => {
      passkeys.signIn.mockResolvedValue({ accessToken: 't', expiresAt: '' });
      fixture.componentRef.setInput('returnUrl', '/reservations/mine');

      expect(passkeyButton()!.textContent).toContain('Sign in with a passkey');
      await signInWithPasskey();

      expect(passkeys.signIn).toHaveBeenCalled();
      expect(login).not.toHaveBeenCalled();
      expect(navigateByUrl).toHaveBeenCalledWith('/reservations/mine');
    });

    it("says so when no passkey was used, and what the API answered when one didn't work", async () => {
      passkeys.signIn.mockRejectedValueOnce(new DOMException('closed', 'NotAllowedError'));
      await signInWithPasskey();
      expect(element.querySelector('[role="alert"]')?.textContent).toContain(
        'No passkey was used. Try again, or sign in with your password.',
      );

      const unknown =
        "This passkey isn't on a SmartDesk account any more. Sign in with your password, and delete it from your device.";
      passkeys.signIn.mockRejectedValueOnce(new ApiError(401, 'Unauthorized', unknown));
      await signInWithPasskey();
      expect(element.querySelector('[role="alert"]')?.textContent).toContain(unknown);
      expect(navigateByUrl).not.toHaveBeenCalled();
      expect(passkeyButton()!.disabled).toBe(false);
    });

    it("isn't offered where the browser can't use passkeys", async () => {
      passkeys.supported = false;
      fixture = TestBed.createComponent(Login);
      element = fixture.nativeElement;
      await fixture.whenStable();

      expect(passkeyButton()).toBeNull();
      expect(element.querySelector('.or')).toBeNull();
    });
  });
});
