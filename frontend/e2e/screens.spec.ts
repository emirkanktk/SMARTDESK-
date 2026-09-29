import AxeBuilder from '@axe-core/playwright';
import { Page } from '@playwright/test';
import { expect, test } from './support';

// Every screen, in Turkish (the default, and the longer words): nothing axe finds against
// WCAG 2.1 AA and its best practices, and nothing wider than a 390px phone. One test per
// user, with soft checks, so a run lists every screen that fails.

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
  { path: '/admin' },
  { path: '/admin/users' },
  { path: '/admin/reports' },
  { path: '/admin/blocks' },
  { path: '/change-password' },
];

const MANAGER: Screen[] = [{ path: '/team' }, { path: '/resources' }];

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

/** Loaded: no request in flight and no "…yükleniyor" / "…sayılıyor" left on screen. */
async function settle(page: Page): Promise<void> {
  await page.waitForLoadState('networkidle');
  await expect(page.getByText(/yükleniyor…|sayılıyor…/)).toHaveCount(0);
}

test('the public pages', async ({ browser }) => {
  const context = await browser.newContext();
  await check(await context.newPage(), PUBLIC);
  await context.close();
});

test("an admin's screens", async ({ open }) => {
  await check(await open('admin', 'tr'), ADMIN);
});

test("a manager's screens", async ({ open }) => {
  await check(await open('manager', 'tr'), MANAGER);
});
