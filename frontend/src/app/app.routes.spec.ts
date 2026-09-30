import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { routes } from './app.routes';
import { AuthService } from './core/services/auth.service';

// The app's real routes and guards, with only the session faked: where does each visitor land?
describe('app routes', () => {
  // These navigate to the real, lazily loaded screens. On a cold cache in a busy full run the
  // first load of a screen can take longer than Vitest's default 5 s, so allow more.
  beforeAll(() => vi.setConfig({ testTimeout: 20_000 }));
  afterAll(() => vi.resetConfig());

  let signedIn: boolean;
  let roles: string[];

  beforeEach(() => {
    signedIn = false;
    roles = [];
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: {
            hasValidSession: () => signedIn,
            hasRole: (role: string) => roles.includes(role),
            getToken: () => (signedIn ? 'token' : null),
            // An expired token that the session can't renew: the visitor is signed out.
            validToken: () => of(signedIn ? 'token' : null),
            isAuthenticated: signal(false),
            isAdmin: signal(false),
            isManager: signal(false),
            passwordChangeRequired: signal(false),
            displayName: signal('Ayşe Yılmaz'),
            currentUser: signal({ id: 'user-1' }),
            logout: vi.fn(),
          },
        },
      ],
    });
  });

  async function visit(url: string): Promise<{ path: string; page: HTMLElement }> {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    return { path: TestBed.inject(Router).url, page: harness.routeNativeElement! };
  }

  it('shows the front page to a visitor who is not signed in', async () => {
    const { path, page } = await visit('/');

    expect(path).toBe('/');
    expect(page.querySelector('h1')!.textContent).toContain('Book meeting rooms');
  });

  it('sends a signed-in user from the front page to the dashboard', async () => {
    signedIn = true;

    const { path } = await visit('/');

    expect(path).toBe('/dashboard');
  });

  it('turns an unknown address into the front page for a visitor', async () => {
    const { path } = await visit('/no-such-page');

    expect(path).toBe('/');
  });

  it('turns an unknown address into the dashboard for a signed-in user', async () => {
    signedIn = true;

    const { path } = await visit('/no-such-page');

    expect(path).toBe('/dashboard');
  });

  it('shows the sign-up page to a visitor', async () => {
    const { path, page } = await visit('/register');

    expect(path).toBe('/register');
    expect(page.querySelector('h1')!.textContent).toContain('Create an account');
  });

  it('sends a signed-in user from the sign-up page to the dashboard', async () => {
    signedIn = true;

    expect((await visit('/register')).path).toBe('/dashboard');
  });

  it('keeps the Users screen to admins', async () => {
    signedIn = true;

    expect((await visit('/admin/users')).path).toBe('/dashboard');
  });

  it('keeps the My team screen to managers', async () => {
    signedIn = true;

    expect((await visit('/team')).path).toBe('/dashboard');
  });

  it('opens the new-resource form to a manager, for their own team', async () => {
    signedIn = true;
    roles = ['Manager'];

    const { path, page } = await visit('/resources/new');

    expect(path).toBe('/resources/new');
    expect(page.querySelector('h1')!.textContent).toContain('New resource');
  });

  it('keeps the resource forms from employees', async () => {
    signedIn = true;
    roles = ['Employee'];

    expect((await visit('/resources/new')).path).toBe('/dashboard');
  });

  it('still asks a visitor to sign in before a screen, and remembers it', async () => {
    const { path } = await visit('/calendar');

    expect(path).toBe('/login?returnUrl=%2Fcalendar');
  });
});
