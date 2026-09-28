import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuthService } from '../../core/services/auth.service';
import { Register } from './register';

describe('Register', () => {
  let register: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<Register>;
  let element: HTMLElement;

  beforeEach(async () => {
    register = vi.fn(() => of(undefined));
    await TestBed.configureTestingModule({
      imports: [Register],
      providers: [provideRouter([]), { provide: AuthService, useValue: { register } }],
    }).compileComponents();
    fixture = TestBed.createComponent(Register);
    element = fixture.nativeElement;
    await fixture.whenStable();
  });

  async function type(selector: string, value: string): Promise<void> {
    const input = element.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
  }

  async function choose(type: 'member' | 'manager'): Promise<void> {
    element.querySelector<HTMLInputElement>(`input[type="radio"][value="${type}"]`)!.click();
    await fixture.whenStable();
  }

  async function fill(
    values: Partial<
      Record<'first' | 'last' | 'email' | 'manager' | 'password' | 'confirm', string>
    > = {},
  ): Promise<void> {
    await type('#first-name', values.first ?? ' Deniz ');
    await type('#last-name', values.last ?? 'Arslan');
    await type('#email', values.email ?? ' deniz@company.example ');
    if (element.querySelector('#manager-email')) {
      await type('#manager-email', values.manager ?? ' zeynep@company.example ');
    }
    await type('#password', values.password ?? 'Team-Member-Pass-4');
    await type('#confirm', values.confirm ?? values.password ?? 'Team-Member-Pass-4');
  }

  async function submit(): Promise<void> {
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  const text = () => element.textContent!.replace(/\s+/g, ' ');

  it('asks to join a team by default, with the manager’s email, then says to check the email', async () => {
    await fill();
    await submit();

    expect(register).toHaveBeenCalledWith({
      firstName: 'Deniz',
      lastName: 'Arslan',
      email: 'deniz@company.example',
      password: 'Team-Member-Pass-4',
      managerEmail: 'zeynep@company.example',
    });
    expect(element.querySelector('form')).toBeNull();
    expect(text()).toContain('Check your email');
    expect(text()).toContain('deniz@company.example');
    expect(text()).toContain("Your request to join your manager's team is waiting for approval");
  });

  it('signs up a manager without a manager’s email', async () => {
    await choose('manager');
    expect(element.querySelector('#manager-email')).toBeNull();

    await fill();
    await submit();

    expect(register).toHaveBeenCalledWith(expect.objectContaining({ managerEmail: null }));
    expect(text()).toContain('An administrator will review your sign-up');
  });

  it('needs the manager’s email to join a team', async () => {
    await fill({ manager: '' });
    await submit();

    expect(register).not.toHaveBeenCalled();
    expect(text()).toContain("Enter your manager's work email.");
  });

  it('checks the form before asking the API', async () => {
    await fill({ first: '', email: 'not-an-email', password: 'short', confirm: 'other' });
    await submit();

    expect(register).not.toHaveBeenCalled();
    expect(text()).toContain('This field is required.');
    expect(text()).toContain('Enter a valid email address.');
    expect(text()).toContain('Add at least 8 characters, an uppercase letter, a digit');
    expect(text()).toContain('The two passwords are different.');
  });

  it('shows the API’s reasons next to the field they belong to', async () => {
    register.mockReturnValue(
      throwError(
        () =>
          new ApiError(400, 'One or more validation errors occurred.', null, {
            Password: ['Passwords must use at least 1 different characters.'],
            ManagerEmail: ["Your manager's email must be someone else's."],
          }),
      ),
    );
    await fill();
    await submit();

    expect(element.querySelector('#password-help')!.textContent).toContain(
      'Passwords must use at least 1 different characters.',
    );
    expect(element.querySelector('#manager-email-help')!.textContent).toContain(
      "Your manager's email must be someone else's.",
    );
    expect(element.querySelector('form')).not.toBeNull();
  });

  it('asks to wait after too many attempts', async () => {
    register.mockReturnValue(throwError(() => new ApiError(429, 'Too Many Requests')));
    await fill();
    await submit();

    expect(element.querySelector('[role="alert"]')!.textContent).toContain(
      'Too many attempts. Please wait a minute and try again.',
    );
  });
});
