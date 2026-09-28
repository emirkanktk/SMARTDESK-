import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { LoginResponse } from '../../core/models/auth.model';
import { AuthService } from '../../core/services/auth.service';
import { Login } from './login';

describe('Login', () => {
  let fixture: ComponentFixture<Login>;
  let element: HTMLElement;
  let response: Subject<LoginResponse>;
  let login: ReturnType<typeof vi.fn>;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    response = new Subject<LoginResponse>();
    login = vi.fn(() => response);

    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [provideRouter([]), { provide: AuthService, useValue: { login } }],
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

    expect(element.querySelector('.notice')!.textContent).toContain('Your password has been changed.');
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
});
