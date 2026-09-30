import { LOCALE_ID, computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Role, Roles } from '../../core/constants/roles';
import { CurrentUser } from '../../core/models/auth.model';
import { AuthService } from '../../core/services/auth.service';
import { Shell } from './shell';

describe('Shell', () => {
  const currentUser = signal<CurrentUser | null>(null);
  let logout: ReturnType<typeof vi.fn>;

  function signInAs(role: Role): void {
    currentUser.set({
      id: 'u-1',
      firstName: 'Ayşe',
      lastName: 'Yılmaz',
      email: 'ayse@smartdesk.local',
      roles: [role],
    });
  }

  beforeEach(async () => {
    signInAs(Roles.Employee);
    logout = vi.fn();
    const hasRole = (role: Role) => currentUser()?.roles.includes(role) ?? false;
    await TestBed.configureTestingModule({
      imports: [Shell],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            currentUser,
            hasRole,
            isAdmin: computed(() => hasRole(Roles.Admin)),
            isManager: computed(() => hasRole(Roles.Manager)),
            displayName: signal('Ayşe Yılmaz'),
            logout,
          },
        },
      ],
    }).compileComponents();
  });

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();
    return fixture.nativeElement;
  }

  function navLabels(element: HTMLElement): string[] {
    return Array.from(element.querySelectorAll('.nav-link')).map((a) => a.textContent!.trim());
  }

  const badge = (element: HTMLElement) => element.querySelector('.badge')?.textContent?.trim();

  it('shows the user and hides the admin and team links from employees', async () => {
    const element = await render();

    expect(element.textContent).toContain('Ayşe Yılmaz');
    expect(navLabels(element)).not.toContain('Admin');
    expect(navLabels(element)).not.toContain('Users');
    expect(navLabels(element)).not.toContain('My team');
    expect(badge(element)).toBeUndefined();
  });

  it('shows the admin links to admins', async () => {
    signInAs(Roles.Admin);
    const element = await render();

    expect(navLabels(element)).toEqual(expect.arrayContaining(['Admin', 'Users']));
    expect(navLabels(element)).not.toContain('My team');
    expect(badge(element)).toBe('Admin');
  });

  const avatar = (element: HTMLElement) => element.querySelector('.avatar')!.textContent!.trim();

  it("puts the user's initials in the avatar", async () => {
    expect(avatar(await render())).toBe('AY');
  });

  it("uses the email's first letter for a user without a name", async () => {
    currentUser.set({ id: 'u-2', firstName: '', lastName: '', email: 'zeynep@x.io', roles: [] });

    expect(avatar(await render())).toBe('Z');
  });

  it('writes the initials in capitals the way the language on screen does', async () => {
    TestBed.overrideProvider(LOCALE_ID, { useValue: 'tr' });
    currentUser.set({
      id: 'u-3',
      firstName: 'ilker',
      lastName: 'ışık',
      email: 'i@x.io',
      roles: [],
    });

    expect(avatar(await render())).toBe('İI');
  });

  it('shows My team to managers, and no admin links', async () => {
    signInAs(Roles.Manager);
    const element = await render();

    expect(navLabels(element)).toContain('My team');
    expect(navLabels(element)).not.toContain('Admin');
    expect(navLabels(element)).not.toContain('Users');
    expect(badge(element)).toBe('Manager');
  });

  it('signs out and goes to the front page', async () => {
    const navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const element = await render();

    const buttons = Array.from(element.querySelectorAll<HTMLButtonElement>('button'));
    buttons.find((b) => b.textContent!.trim() === 'Sign out')!.click();

    expect(logout).toHaveBeenCalled();
    expect(navigateByUrl).toHaveBeenCalledWith('/');
  });
});
