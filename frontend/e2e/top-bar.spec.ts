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

// From 1100px the links are a sidebar, with the user's card at the bottom. The account menu
// opens upwards from it, over the page when the email is long, and nothing may cut it off. On
// a screen too short for everything, the links scroll and the card stays in view.
test('the account menu in the sidebar opens over the page, and the card stays on a short screen', async ({
  open,
}) => {
  const page = await open('ayse');
  await page.setViewportSize({ width: 1280, height: 480 });
  await page.goto('/dashboard');
  await expect(page.locator('header.bar .user')).toBeInViewport({ ratio: 1 });

  await page.getByRole('button', { name: /Account menu/ }).click();
  const menu = page.getByRole('menu');
  await expect(menu).toBeVisible();
  // Measured once it settles: every animation done but the endless ones, such as the dashboard's
  // "Now" dot, which breathes for as long as one of Ayşe's bookings is under way.
  await page.waitForFunction(() =>
    document
      .getAnimations()
      .every(
        (a) => a.playState !== 'running' || a.effect?.getComputedTiming().iterations === Infinity,
      ),
  );
  const box = (await menu.boundingBox())!;
  const sidebar = (await page.locator('header.bar').boundingBox())!;
  expect(box.x + box.width, "Ayşe's email takes the menu past the sidebar").toBeGreaterThan(
    sidebar.x + sidebar.width,
  );
  // Just inside its right edge, the page shows the menu itself, not what's under it.
  const uncut = await page.evaluate(
    ({ x, y }) => !!document.elementFromPoint(x, y)?.closest('[role=menu]'),
    { x: box.x + box.width - 4, y: box.y + box.height / 2 },
  );
  expect(uncut).toBe(true);
});
