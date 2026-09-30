import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { Roles } from '../../core/constants/roles';
import { ApiError } from '../../core/models/api-error.model';
import { CurrentUser } from '../../core/models/auth.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { Profile } from './profile';

describe('Profile', () => {
  const ayse: CurrentUser = {
    id: 'u-1',
    firstName: 'Ayşe',
    lastName: 'Yılmaz',
    email: 'ayse@smartdesk.local',
    roles: [Roles.Manager],
  };
  let updateProfile: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<Profile>;
  let element: HTMLElement;

  beforeEach(async () => {
    updateProfile = vi.fn((request: { firstName: string; lastName: string }) =>
      of({ ...ayse, ...request }),
    );
    await TestBed.configureTestingModule({
      imports: [Profile],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: signal(ayse), updateProfile } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(Profile);
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

  async function submit(): Promise<void> {
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  const saveButton = () => element.querySelector<HTMLButtonElement>('button[type="submit"]')!;

  it('shows the email and role, which an administrator changes', () => {
    const details = element.querySelector('.details')!.textContent!;
    expect(details).toContain('ayse@smartdesk.local');
    expect(details).toContain('Manager');
    expect(element.querySelector<HTMLInputElement>('#firstName')!.value).toBe('Ayşe');
    expect(element.querySelector<HTMLInputElement>('#lastName')!.value).toBe('Yılmaz');
  });

  it('offers to save only once something changed', async () => {
    expect(saveButton().disabled).toBe(true);

    await type('#firstName', 'Ayşe Nur');

    expect(saveButton().disabled).toBe(false);
  });

  it('saves the trimmed name and says so', async () => {
    const success = vi.spyOn(TestBed.inject(NotificationService), 'success');
    await type('#firstName', '  Ayşe Nur ');
    await type('#lastName', ' Demir ');
    await submit();

    expect(updateProfile).toHaveBeenCalledWith({ firstName: 'Ayşe Nur', lastName: 'Demir' });
    expect(success).toHaveBeenCalledWith('Your name was saved.');
    expect(element.querySelector<HTMLInputElement>('#firstName')!.value).toBe('Ayşe Nur');
    expect(saveButton().disabled).toBe(true);
  });

  it('asks for both names, not just spaces, and at most 100 characters', async () => {
    await type('#firstName', '   ');
    await type('#lastName', '');
    await submit();

    expect(updateProfile).not.toHaveBeenCalled();
    expect(element.querySelectorAll('.field-error').length).toBe(2);

    await type('#firstName', 'a'.repeat(101));
    expect(element.textContent).toContain('Use at most 100 characters.');
  });

  it('shows the API messages next to the fields', async () => {
    updateProfile.mockReturnValue(
      throwError(
        () =>
          new ApiError(400, 'One or more validation errors occurred.', null, {
            FirstName: ['Names cannot contain line breaks or other control characters.'],
          }),
      ),
    );
    await type('#firstName', 'Ayşe Nur');
    await submit();

    expect(element.querySelector('.field-error')!.textContent).toContain('control characters');
  });

  it('keeps a note on the form when saving fails otherwise', async () => {
    updateProfile.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
    await type('#firstName', 'Ayşe Nur');
    await submit();

    expect(element.querySelector('[role="alert"]')!.textContent).toContain(
      'Your name could not be saved.',
    );
  });
});
