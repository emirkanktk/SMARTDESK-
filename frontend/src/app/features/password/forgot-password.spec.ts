import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';
import { ForgotPassword } from './forgot-password';

describe('ForgotPassword', () => {
  let forgotPassword: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<ForgotPassword>;
  let element: HTMLElement;

  beforeEach(async () => {
    forgotPassword = vi.fn(() => of(undefined));
    await TestBed.configureTestingModule({
      imports: [ForgotPassword],
      providers: [provideRouter([]), { provide: AuthService, useValue: { forgotPassword } }],
    }).compileComponents();
    fixture = TestBed.createComponent(ForgotPassword);
    element = fixture.nativeElement;
    await fixture.whenStable();
  });

  async function submit(email: string): Promise<void> {
    const input = element.querySelector<HTMLInputElement>('#email')!;
    input.value = email;
    input.dispatchEvent(new Event('input'));
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  const text = () => element.textContent!.replace(/\s+/g, ' ');

  it('checks the email before asking the API', async () => {
    await submit('not-an-email');

    expect(forgotPassword).not.toHaveBeenCalled();
    expect(text()).toContain('Enter a valid email address.');
  });

  it('asks for a link, then says one is on its way if the account exists', async () => {
    await submit('  ayse@smartdesk.local ');

    expect(forgotPassword).toHaveBeenCalledWith('ayse@smartdesk.local');
    expect(element.querySelector('form')).toBeNull();
    expect(text()).toContain('If an account exists for ayse@smartdesk.local, we');
    expect(text()).toContain('The link works for 2 hours.');
  });

  it('can go back to try another email', async () => {
    await submit('ayse@smartdesk.local');

    element.querySelector<HTMLButtonElement>('.link-button')!.click();
    await fixture.whenStable();

    expect(element.querySelector('form')).not.toBeNull();
  });

  it('says so when there have been too many attempts', async () => {
    forgotPassword.mockReturnValue(throwError(() => new ApiError(429, 'Too Many Requests')));

    await submit('ayse@smartdesk.local');

    expect(element.querySelector('[role="alert"]')!.textContent).toContain('Too many attempts');
    expect(element.querySelector('form')).not.toBeNull();
  });

  it('is the main landmark of the page', () => {
    expect(element.getAttribute('role')).toBe('main');
  });

  it('links back to sign in', () => {
    expect(element.querySelector('a.back')!.getAttribute('href')).toBe('/login');
  });
});
