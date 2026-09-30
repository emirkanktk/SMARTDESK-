import { LOCALE_ID, computed, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Role, Roles } from '../../core/constants/roles';
import { CurrentUser } from '../../core/models/auth.model';
import { AuthService } from '../../core/services/auth.service';
import { ProfileMenu } from './profile-menu';

describe('ProfileMenu', () => {
  const currentUser = signal<CurrentUser | null>(null);
  let logout: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<ProfileMenu>;
  let element: HTMLElement;

  function signInAs(role: Role, firstName = 'Ayşe', lastName = 'Yılmaz'): void {
    currentUser.set({
      id: 'u-1',
      firstName,
      lastName,
      email: 'ayse@smartdesk.local',
      roles: [role],
    });
  }

  beforeEach(async () => {
    signInAs(Roles.Employee);
    logout = vi.fn();
    const hasRole = (role: Role) => currentUser()?.roles.includes(role) ?? false;
    await TestBed.configureTestingModule({
      imports: [ProfileMenu],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            currentUser,
            isAdmin: computed(() => hasRole(Roles.Admin)),
            isManager: computed(() => hasRole(Roles.Manager)),
            displayName: computed(() => {
              const user = currentUser();
              return user ? `${user.firstName} ${user.lastName}`.trim() || user.email : '';
            }),
            logout,
          },
        },
      ],
    }).compileComponents();
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(ProfileMenu);
    element = fixture.nativeElement;
    document.body.appendChild(element);
    await fixture.whenStable();
  }

  afterEach(() => element?.remove());

  const trigger = () => element.querySelector<HTMLButtonElement>('.trigger')!;
  const menu = () => element.querySelector<HTMLElement>('[role="menu"]');
  const items = () => Array.from(element.querySelectorAll<HTMLElement>('[role="menuitem"]'));
  const settle = async () => {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  };

  it("shows the user's initials and name, and says what the button is", async () => {
    await render();

    expect(element.querySelector('.avatar')!.textContent!.trim()).toBe('AY');
    expect(element.querySelector('.user-name')!.textContent).toContain('Ayşe Yılmaz');
    expect(trigger().getAttribute('aria-label')).toBe('Account menu: Ayşe Yılmaz');
    expect(trigger().getAttribute('aria-haspopup')).toBe('menu');
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(menu()).toBeNull();
  });

  it('shows the role of an admin or a manager, also to a screen reader', async () => {
    signInAs(Roles.Manager);
    await render();

    expect(element.querySelector('.badge')!.textContent!.trim()).toBe('Manager');
    expect(trigger().getAttribute('aria-label')).toBe('Account menu: Ayşe Yılmaz, Manager');
  });

  it('falls back to the email for initials when there is no name', async () => {
    signInAs(Roles.Employee, '', '');
    await render();

    expect(element.querySelector('.avatar')!.textContent!.trim()).toBe('A');
  });

  it('writes the initials in capitals the way the language on screen does', async () => {
    TestBed.overrideProvider(LOCALE_ID, { useValue: 'tr' });
    signInAs(Roles.Employee, 'ilker', 'ışık');
    await render();

    expect(element.querySelector('.avatar')!.textContent!.trim()).toBe('İI');
  });

  it('opens a menu with the profile, the password page and signing out', async () => {
    await render();

    trigger().click();
    await settle();

    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    expect(menu()!.textContent).toContain('ayse@smartdesk.local');
    expect(items().map((item) => item.textContent!.trim())).toEqual([
      'My profile',
      'Change password',
      'Sign out',
    ]);
    expect(items()[0].getAttribute('href')).toBe('/profile');
    expect(items()[1].getAttribute('href')).toBe('/change-password');
    expect(document.activeElement).toBe(items()[0]);
  });

  it('moves through the items with the arrow keys, Home and End', async () => {
    await render();
    trigger().click();
    await settle();

    const press = (key: string) =>
      menu()!.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
    press('ArrowDown');
    expect(document.activeElement).toBe(items()[1]);
    press('ArrowDown');
    press('ArrowDown');
    expect(document.activeElement).toBe(items()[0]);
    press('ArrowUp');
    expect(document.activeElement).toBe(items()[2]);
    press('Home');
    expect(document.activeElement).toBe(items()[0]);
    press('End');
    expect(document.activeElement).toBe(items()[2]);
  });

  it('opens at the last item with the up arrow on the button', async () => {
    await render();

    trigger().dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }));
    await settle();

    expect(menu()).not.toBeNull();
    expect(document.activeElement).toBe(items()[2]);
  });

  it('closes on Escape and gives focus back to the button', async () => {
    await render();
    trigger().click();
    await settle();

    items()[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await fixture.whenStable();

    expect(menu()).toBeNull();
    expect(document.activeElement).toBe(trigger());
  });

  it('closes when someone clicks elsewhere', async () => {
    await render();
    trigger().click();
    await settle();

    document.body.click();
    await fixture.whenStable();

    expect(menu()).toBeNull();
  });

  it('signs out and goes to the front page', async () => {
    const navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await render();
    trigger().click();
    await settle();

    items()[2].click();
    await fixture.whenStable();

    expect(logout).toHaveBeenCalled();
    expect(navigateByUrl).toHaveBeenCalledWith('/');
    expect(menu()).toBeNull();
  });
});
