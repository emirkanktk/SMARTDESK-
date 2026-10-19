import AxeBuilder from '@axe-core/playwright';
import { APIRequestContext, Page } from '@playwright/test';
import { expect, retire, test } from './support';

// Every screen, in Turkish (the default, and the longer words) and in both colour themes:
// nothing axe finds against WCAG 2.1 AA and its best practices, and nothing wider than a
// 390px phone. One test per user and theme, with soft checks, so a run lists every screen
// that fails.

interface Screen {
  path: string;
  /** Brings the screen into the state worth checking, e.g. with search results. */
  act?: (page: Page) => Promise<void>;
}

const PUBLIC: Screen[] = [
  { path: '/' },
  { path: '/login' },
  { path: '/register' },
  { path: '/forgot-password' },
  { path: '/reset-password' },
];

const ADMIN: Screen[] = [
  { path: '/dashboard' },
  { path: '/resources' },
  { path: '/resources/new' },
  {
    path: '/availability',
    act: async (page) => {
      await page.locator('form button[type=submit]').click();
      await expect(page.locator('.results')).toBeVisible();
    },
  },
  { path: '/calendar' },
  { path: '/calendar?view=month' },
  { path: '/floor-plan' },
  {
    path: '/floor-plan',
    act: async (page) => {
      await page.locator('button.arrange').click();
      await expect(page.locator('.arrange-panel')).toBeVisible();
    },
  },
  { path: '/reservations/new' },
  { path: '/reservations/mine' },
  { path: '/inbox' },
  { path: '/admin' },
  { path: '/admin/users' },
  { path: '/admin/reports' },
  { path: '/admin/blocks' },
  { path: '/admin/rules' },
  { path: '/admin/approvals' },
  { path: '/admin/history' },
  { path: '/issues' },
  { path: '/resources/qr-labels' },
  { path: '/guide' },
  { path: '/change-password' },
];

// A manager's booking form has the list of whom to invite (ADR-0032), and their inbox the
// answers.
const MANAGER: Screen[] = [
  { path: '/team' },
  { path: '/issues' },
  { path: '/resources/qr-labels' },
  { path: '/resources' },
  { path: '/floor-plan' },
  { path: '/reservations/new' },
  { path: '/inbox' },
  { path: '/guide' },
];

const PHONE = { width: 390, height: 844 };

/**
 * A room of its own that asks for check-in, and the path its QR code opens: the page without an
 * account (ADR-0050) needs both.
 */
async function roomWithCode(admin: APIRequestContext): Promise<{ id: string; path: string }> {
  const types = (await (await admin.get('resource-types')).json()) as { id: string }[];
  const created = await admin.post('resources', {
    data: {
      name: `E2E screens ${Date.now().toString(36).toUpperCase()}`,
      resourceTypeId: types[0].id,
      capacity: 4,
      location: 'E2E floor',
      features: ['TV'],
      teamIds: [],
      checkInRequired: true,
    },
  });
  expect(created.status(), 'create the room').toBe(201);
  const { id } = (await created.json()) as { id: string };
  const { url } = (await (await admin.get(`resources/${id}/check-in-code`)).json()) as {
    url: string;
  };
  return { id, path: url.replace(/^https?:\/\/[^/]+/, '') };
}

async function check(page: Page, screens: Screen[]): Promise<void> {
  for (const screen of screens) {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(screen.path);
    await settle(page);
    await screen.act?.(page);

    const { violations } = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'])
      .analyze();
    const found = violations.map(
      (v) => `${v.id} x${v.nodes.length}: ${v.nodes[0]?.target.join(' ') ?? ''}`,
    );
    expect.soft(found, `axe on ${screen.path}`).toEqual([]);

    await page.setViewportSize(PHONE);
    const [scrollWidth, clientWidth] = await page.evaluate(() => [
      document.documentElement.scrollWidth,
      document.documentElement.clientWidth,
    ]);
    expect
      .soft(scrollWidth, `sideways scroll at 390px on ${screen.path}`)
      .toBeLessThanOrEqual(clientWidth);
  }
}

/**
 * Loaded: no request in flight, no "…yükleniyor" / "…sayılıyor" left on screen, and the
 * screen's entrance over - axe would measure text halfway through fading in. Endless
 * animations (a drifting background, a pulsing dot) never finish, and don't fade text.
 */
async function settle(page: Page): Promise<void> {
  await page.waitForLoadState('networkidle');
  await expect(page.getByText(/yükleniyor…|sayılıyor…/)).toHaveCount(0);
  await page.waitForFunction(() =>
    document
      .getAnimations()
      .every(
        (a) => a.playState !== 'running' || a.effect?.getComputedTiming().endTime === Infinity,
      ),
  );
}

/** From the next page load on, in this theme (as if the user had picked it). */
async function useTheme(page: Page, theme: 'light' | 'dark'): Promise<void> {
  await page.context().addInitScript((t) => localStorage.setItem('smartdesk_theme', t), theme);
}

for (const theme of ['light', 'dark'] as const) {
  test.describe(`${theme} theme`, () => {
    test('the public pages', async ({ browser, admin }) => {
      const room = await roomWithCode(admin);
      const context = await browser.newContext();
      const page = await context.newPage();
      await useTheme(page, theme);
      try {
        await check(page, [...PUBLIC, { path: room.path }]);
      } finally {
        await context.close();
        await retire(admin, room.id);
      }
    });

    test("an admin's screens", async ({ open }) => {
      // Some twenty screens, each checked with axe and at phone width: more than the usual minute.
      test.slow();
      const page = await open('admin', 'tr');
      await useTheme(page, theme);
      await check(page, ADMIN);
    });

    test("a manager's screens", async ({ open }) => {
      const page = await open('manager', 'tr');
      await useTheme(page, theme);
      await check(page, MANAGER);
    });
  });
}
