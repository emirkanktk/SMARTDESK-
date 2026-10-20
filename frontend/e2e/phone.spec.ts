import AxeBuilder from '@axe-core/playwright';
import { Page } from '@playwright/test';
import { expect, test } from './support';

// On a phone the app is laid out like one (ADR-0055): the screens used most are in a bar at the
// bottom, where a thumb reaches, and Menu opens every other one in a sheet from below. Nothing
// fixed at the bottom hides behind the bar, and the floor plan keeps a size its names can be
// read at, scrolling sideways in its own box. Wider screens keep the bar on top or the sidebar.

const PHONE = { width: 390, height: 844 };

/** The top of the tab bar, where the page's room ends. */
async function barTop(page: Page): Promise<number> {
  return (await page.locator('nav.tabs').boundingBox())!.y;
}

test('on a phone the main screens are at the bottom, and Menu opens every other one', async ({
  open,
}) => {
  const page = await open('ayse');
  await page.setViewportSize(PHONE);
  await page.goto('/dashboard');

  const tabs = page.getByRole('navigation', { name: 'Shortcuts' });
  await expect(tabs).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Main', exact: true })).toBeHidden();
  await expect(tabs.getByRole('link', { name: 'Home' })).toHaveAttribute('aria-current', 'page');

  await tabs.getByRole('link', { name: 'Floor plan' }).click();
  await expect(page).toHaveURL(/\/floor-plan/);
  await expect(tabs.getByRole('link', { name: 'Floor plan' })).toHaveAttribute(
    'aria-current',
    'page',
  );

  const menu = tabs.getByRole('button', { name: 'Menu' });
  await menu.click();
  const sheet = page.getByRole('dialog', { name: 'Every screen' });
  await expect(sheet).toBeVisible();
  await expect(menu).toHaveAttribute('aria-expanded', 'true');
  // Settled, so axe measures the colours where the sheet stops.
  await page.waitForFunction(() =>
    document
      .getAnimations()
      .every(
        (a) => a.playState !== 'running' || a.effect?.getComputedTiming().endTime === Infinity,
      ),
  );
  const { violations } = await new AxeBuilder({ page })
    .include('app-tab-bar')
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'])
    .analyze();
  expect(violations.map((v) => `${v.id}: ${v.nodes[0]?.target.join(' ')}`)).toEqual([]);

  await sheet.getByRole('link', { name: 'Calendar' }).click();
  await expect(page).toHaveURL(/\/calendar/);
  await expect(sheet).toBeHidden();

  // Escape closes it too, back on Menu.
  await menu.click();
  await expect(sheet).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(sheet).toBeHidden();
  await expect(menu).toBeFocused();

  // With room for the bar on top, the app has it again, and no bar at the bottom.
  await page.setViewportSize({ width: 1280, height: 800 });
  await expect(tabs).toBeHidden();
  await expect(page.getByRole('navigation', { name: 'Main', exact: true })).toBeVisible();
});

test("on a phone nothing hides behind the bar: the page's end, and the assistant's button", async ({
  open,
}) => {
  const page = await open('ayse');
  await page.setViewportSize(PHONE);
  // Whether the server has an assistant, as far as the app knows when it loads: a machine with a
  // key for it would otherwise leave room for its button where CI leaves none.
  const assistant = async (enabled: boolean) => {
    await page.unroute('**/api/assistant');
    await page.route('**/api/assistant', (route) => route.fulfill({ json: { enabled } }));
  };
  const contentEnd = () =>
    page.evaluate(() => {
      window.scrollTo(0, document.documentElement.scrollHeight);
      const main = document.querySelector('main.content')!;
      return main.getBoundingClientRect().bottom - parseFloat(getComputedStyle(main).paddingBottom);
    });

  // A long page, scrolled to its end: what's on it ends above the bar.
  await assistant(false);
  await page.goto('/guide');
  await page.waitForLoadState('networkidle');
  await expect(page.locator('main.content')).not.toHaveClass(/with-assistant/);
  await expect.poll(contentEnd).toBeLessThanOrEqual(await barTop(page));

  // With an assistant, its button is fixed at the bottom too, above the bar, and the page ends
  // above it.
  await assistant(true);
  await page.reload();
  const launcher = page.getByRole('button', { name: 'Assistant' });
  await expect(launcher).toBeVisible();
  const button = (await launcher.boundingBox())!;
  expect(button.y + button.height).toBeLessThan(await barTop(page));
  await expect.poll(contentEnd).toBeLessThanOrEqual(button.y);
});

test('on a phone the floor plan keeps its size and scrolls sideways in its own box', async ({
  open,
}) => {
  const page = await open('ayse');
  await page.setViewportSize(PHONE);
  await page.goto('/floor-plan');
  const plan = page.locator('.plan');
  await expect(plan).toBeVisible();
  await expect(page.getByText('Swipe the plan sideways to see all of it.')).toBeVisible();

  const sizes = await page.evaluate(() => {
    const box = document.querySelector('.plan-scroll')!;
    return {
      plan: document.querySelector('.plan')!.getBoundingClientRect().width,
      box: [box.scrollWidth, box.clientWidth],
      page: [document.documentElement.scrollWidth, document.documentElement.clientWidth],
    };
  });
  expect(sizes.plan).toBeGreaterThanOrEqual(640);
  expect(sizes.box[0], 'the plan scrolls in its box').toBeGreaterThan(sizes.box[1]);
  expect(sizes.page[0], 'and the page does not').toBeLessThanOrEqual(sizes.page[1]);

  // A wide screen has room for the plan as it is.
  await page.setViewportSize({ width: 1280, height: 800 });
  await expect(page.getByText('Swipe the plan sideways to see all of it.')).toBeHidden();
});
