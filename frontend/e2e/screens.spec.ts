import AxeBuilder from '@axe-core/playwright';
import { Page } from '@playwright/test';
import { expect, test } from './support';

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
  { path: '/reservations/new' },
  { path: '/reservations/mine' },
  { path: '/inbox' },
  { path: '/admin' },
  { path: '/admin/users' },
  { path: '/admin/reports' },
  { path: '/admin/blocks' },
  { path: '/change-password' },
];

// A manager's booking form has the list of whom to invite (ADR-0032), and their inbox the
// answers.
const MANAGER: Screen[] = [
  { path: '/team' },
  { path: '/resources' },
  { path: '/reservations/new' },
  { path: '/inbox' },
];

const PHONE = { width: 390, height: 844 };

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
    test('the public pages', async ({ browser }) => {
      const context = await browser.newContext();
      const page = await context.newPage();
      await useTheme(page, theme);
      await check(page, PUBLIC);
      await context.close();
    });

    test("an admin's screens", async ({ open }) => {
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
