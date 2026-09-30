import { expect, test } from './support';

// The bars on top get out of the way while a page scrolls down and come back on the way up,
// so the navigation is one flick away anywhere on a long page (HideOnScroll). The front page
// is long and the same everywhere, so it's where this is checked.
test('the bar on top hides on the way down, and is back on the way up and at the bottom', async ({
  visit,
}) => {
  const page = await visit('tr');
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');
  // The whole page first: a wheel turned before it's there scrolls nothing.
  await expect(page.locator('footer')).toBeVisible();
  await page.waitForLoadState('networkidle');
  const bar = page.locator('header.topbar');
  const top = async () => (await bar.boundingBox())!.y;

  await page.mouse.move(195, 420);
  await page.mouse.wheel(0, 900);
  await expect.poll(top).toBeLessThan(0);

  await page.mouse.wheel(0, -150);
  await expect.poll(top).toBe(0);

  await page.mouse.wheel(0, 600);
  await expect.poll(top).toBeLessThan(0);

  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
  await expect.poll(top).toBe(0);
});
