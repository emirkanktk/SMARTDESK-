import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';
import { ResetPassword } from './reset-password';

describe('ResetPassword', () => {
  const EMAIL = 'ayse@smartdesk.local';
  const TOKEN = 'Q2ZESjhEeGtr-_';
  let resetPassword: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let fixture: ComponentFixture<ResetPassword>;
  let element: HTMLElement;

  beforeEach(async () => {
    resetPassword = vi.fn(() => of(undefined));
    await TestBed.configureTestingModule({
      imports: [ResetPassword],
      providers: [provideRouter([]), { provide: AuthService, useValue: { resetPassword } }],
    }).compileComponents();
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  async function render(inputs: Record<string, string> = { email: EMAIL, token: TOKEN }) {
    fixture = TestBed.createComponent(ResetPassword);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
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

  async function submit(password: string, confirm = password): Promise<void> {
    await type('#new-password', password);
    await type('#confirm', confirm);
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  const text = () => element.textContent!.replace(/\s+/g, ' ');

  it('sets the new password with the link’s email and token, then goes to sign in', async () => {
    await render();

    expect(text()).toContain(`For ${EMAIL}`);
    await submit('Brand-New-Pass-7');

    expect(resetPassword).toHaveBeenCalledWith({
      email: EMAIL,
      token: TOKEN,
      newPassword: 'Brand-New-Pass-7',
    });
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { reset: 'done' } });
  });

  it('says what a weak password lacks, and that the repeat must match, without asking the API', async () => {
    await render();

    await submit('short', 'shorter');

    expect(resetPassword).not.toHaveBeenCalled();
    expect(text()).toContain('Add at least 8 characters, an uppercase letter, a digit');
    expect(text()).toContain('The two passwords are different.');
  });

  it('shows the password rules until something is typed', async () => {
    await render();

    expect(text()).toContain('At least 8 characters, with upper and lower case letters');
  });

  it('turns an invalid or expired link into a way to ask for a new one', async () => {
    resetPassword.mockReturnValue(
      throwError(
        () =>
          new ApiError(400, 'One or more validation errors occurred.', null, {
            Token: ['This reset link is invalid or has expired. Ask for a new one.'],
          }),
      ),
    );
    await render();

    await submit('Brand-New-Pass-7');

    expect(element.querySelector('form')).toBeNull();
    expect(text()).toContain('This reset link is invalid or has expired.');
    expect(element.querySelector('a[href="/forgot-password"]')).not.toBeNull();
  });

  it('shows the API’s reasons when it refuses the password', async () => {
    resetPassword.mockReturnValue(
      throwError(
        () =>
          new ApiError(400, 'One or more validation errors occurred.', null, {
            NewPassword: ['Passwords must have at least one non alphanumeric character.'],
          }),
      ),
    );
    await render();

    await submit('Brand-New-Pass-7');

    expect(text()).toContain('Passwords must have at least one non alphanumeric character.');
  });

  it('asks for a new link when the one opened is incomplete', async () => {
    await render({ email: EMAIL });

    expect(element.querySelector('form')).toBeNull();
    expect(text()).toContain('This reset link is incomplete');
  });
});
