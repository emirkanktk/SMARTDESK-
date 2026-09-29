import {
  APIRequestContext,
  BrowserContext,
  Page,
  test as base,
  expect,
  request,
} from '@playwright/test';

/** The API the app calls in development. */
export const API_URL = process.env['E2E_API_URL'] ?? 'https://localhost:7065/api';

/** The Development seed's demo users (ADR-0007). Both employees are in the manager's team. */
export const USERS = {
  admin: 'admin@smartdesk.local',
  manager: 'manager@smartdesk.local',
  ayse: 'employee1@smartdesk.local',
  mehmet: 'employee2@smartdesk.local',
} as const;
export type User = keyof typeof USERS;
export const PASSWORD = process.env['E2E_PASSWORD'] ?? 'SmartDesk123!';

export type Language = 'en' | 'tr';

export interface TestResource {
  id: string;
  name: string;
}

/** An API client signed in as `user`, answered in English. Paths are relative: `resources`. */
export async function apiAs(user: User): Promise<APIRequestContext> {
  const baseURL = `${API_URL}/`;
  const anonymous = await request.newContext({ baseURL, ignoreHTTPSErrors: true });
  const response = await anonymous.post('auth/login', {
    data: { email: USERS[user], password: PASSWORD },
  });
  expect(response.ok(), `API sign-in as ${user}`).toBe(true);
  const { accessToken } = (await response.json()) as { accessToken: string };
  await anonymous.dispose();
  return request.newContext({
    baseURL,
    ignoreHTTPSErrors: true,
    extraHTTPHeaders: { Authorization: `Bearer ${accessToken}`, 'Accept-Language': 'en' },
  });
}

/** `days` from today at `hour`:00 local time, for `minutes`. */
export function slot(days: number, hour: number, minutes = 60): { start: Date; end: Date } {
  const start = new Date();
  start.setDate(start.getDate() + days);
  start.setHours(hour, 0, 0, 0);
  return { start, end: new Date(start.getTime() + minutes * 60_000) };
}

/** What a datetime-local input holds: "2026-10-05T10:00". */
export function localInput(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}

/** "2026-10-05", the calendar's `week` parameter. */
export function day(date: Date): string {
  return localInput(date).slice(0, 10);
}

/** The booking form, filled in for this resource and time, as the availability search links it. */
export function bookingLink(resource: TestResource, when: { start: Date; end: Date }): string {
  const query = new URLSearchParams({
    resourceId: resource.id,
    start: when.start.toISOString(),
    end: when.end.toISOString(),
  });
  return `/reservations/new?${query}`;
}

/**
 * Takes a resource out of the way: its upcoming reservations are cancelled, since a resource
 * with bookings to come can't be switched off (ADR-0024), and then it's made Inactive.
 * Resources can't be deleted.
 */
export async function retire(admin: APIRequestContext, resourceId: string): Promise<void> {
  const from = new Date(Date.now() - 60 * 60_000).toISOString();
  const to = new Date(Date.now() + 40 * 24 * 60 * 60_000).toISOString();
  const list = await admin.get('reservations', { params: { from, to, resourceId } });
  for (const reservation of (await list.json()) as { id: string }[]) {
    await admin.post(`reservations/${reservation.id}/cancel`);
  }
  const off = await admin.patch(`resources/${resourceId}/status`, { data: { status: 'Inactive' } });
  expect(off.ok(), `deactivate ${resourceId}`).toBe(true);
}

interface Fixtures {
  /** A signed-out window of its own (separate storage), in English unless asked. */
  visit: (language?: Language) => Promise<Page>;
  /** The same, signed in as a demo user. */
  open: (user: User, language?: Language) => Promise<Page>;
  /** The API as the admin. */
  admin: APIRequestContext;
  /** A new shared, active resource for this test alone, retired afterwards. */
  resource: TestResource;
}

export const test = base.extend<Fixtures>({
  visit: async ({ browser }, use) => {
    const contexts: BrowserContext[] = [];
    await use(async (language = 'en') => {
      const context = await browser.newContext();
      contexts.push(context);
      // Only a first visit gets it, so switching language in the test sticks.
      await context.addInitScript((lang) => {
        if (!localStorage.getItem('smartdesk_language')) {
          localStorage.setItem('smartdesk_language', lang);
        }
      }, language);
      return context.newPage();
    });
    await Promise.all(contexts.map((context) => context.close()));
  },

  open: async ({ visit }, use) => {
    await use(async (user, language = 'en') => {
      const page = await visit(language);
      await signIn(page, user);
      return page;
    });
  },

  // Playwright requires a destructuring pattern for the fixtures argument, even an empty one.
  admin: async ({}, use) => {
    const admin = await apiAs('admin');
    await use(admin);
    await admin.dispose();
  },

  resource: async ({ admin }, use) => {
    const types = (await (await admin.get('resource-types')).json()) as { id: string }[];
    const name = `E2E ${Date.now().toString(36).toUpperCase()}`;
    const created = await admin.post('resources', {
      data: {
        name,
        resourceTypeId: types[0].id,
        capacity: 4,
        location: 'E2E floor',
        description: 'Created by a browser test.',
        features: [],
        teamIds: [],
      },
    });
    expect(created.status(), 'create the test resource').toBe(201);
    const resource = (await created.json()) as TestResource;
    await use(resource);
    await retire(admin, resource.id);
  },
});

export { expect };

/** Signs in as a demo user, whatever the page's language, and waits for the dashboard. */
export async function signIn(page: Page, user: User): Promise<void> {
  await submitSignIn(page, USERS[user], PASSWORD);
  await page.waitForURL('**/dashboard');
}

/** Fills in and sends the sign-in form; where it leads is up to the caller. */
export async function submitSignIn(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/login');
  await page.locator('#email').fill(email);
  await page.locator('#password').fill(password);
  await page.locator('button[type=submit]').click();
}

/** Accepts the next confirm() the page shows. */
export function acceptNextDialog(page: Page): void {
  page.once('dialog', (dialog) => void dialog.accept());
}
