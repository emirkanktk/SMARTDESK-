import { expect, test } from './support';

// Light and dark: the system's setting until the user picks, then their pick, in every tab.
test("a first visit follows the system's dark setting, and a pick sticks over it", async ({
  browser,
}) => {
  const context = await browser.newContext({ colorScheme: 'dark' });
  const page = await context.newPage();
  await page.goto('/login');

  const html = page.locator('html');
  const toggle = page.getByRole('button', { name: 'Koyu mod' });
  await expect(html).toHaveAttribute('data-theme', 'dark');
  await expect(toggle).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(10, 14, 26)');

  await toggle.click();
  await expect(html).toHaveAttribute('data-theme', 'light');
  await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(245, 246, 251)');

  // Light was picked, so it wins over the system's dark from now on.
  await page.reload();
  await expect(html).toHaveAttribute('data-theme', 'light');

  await context.close();
});

test('the theme is on the page before the app starts, and other tabs follow', async ({
  browser,
}) => {
  const context = await browser.newContext({ colorScheme: 'light' });
  await context.addInitScript(() => localStorage.setItem('smartdesk_theme', 'dark'));
  const first = await context.newPage();

  // Read as early as the page allows: before Angular has drawn anything.
  await first.goto('/', { waitUntil: 'commit' });
  await first.waitForFunction(() => document.readyState !== 'loading');
  expect(await first.evaluate(() => document.documentElement.getAttribute('data-theme'))).toBe(
    'dark',
  );

  const second = await context.newPage();
  await second.goto('/login');
  await first.getByRole('button', { name: 'Koyu mod' }).click();
  await expect(second.locator('html')).toHaveAttribute('data-theme', 'light');

  await context.close();
});
