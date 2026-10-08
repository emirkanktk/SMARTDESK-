import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './support';

/** The path of an absolute link from the API, so the page keeps the test's base URL. */
const path = (url: string) => url.replace(/^https?:\/\/[^/]+/, '');

// Reporting a problem from the page a resource's QR code opens (ADR-0037). The test's resource
// asks for no check-in: the code at its door is for everyone all the same.
test('a problem reported at the door is known to the next person, and its reporter hears it is fixed', async ({
  admin,
  resource,
  open,
}) => {
  const printed = (
    (await (await admin.get(`resources/${resource.id}/check-in-code`)).json()) as { url: string }
  ).url;

  // At the door, on a phone.
  const ayse = await open('ayse');
  await ayse.setViewportSize({ width: 390, height: 844 });
  await ayse.goto(path(printed));
  await expect(ayse.locator('h1')).toHaveText(resource.name);
  await expect(ayse.getByText(`${resource.name} doesn't need a check-in.`)).toBeVisible();
  await expect(ayse.getByText('No problems reported here.')).toBeVisible();

  await ayse.getByRole('button', { name: 'Report a problem' }).click();
  await ayse.getByLabel('Projector').check();
  await ayse.getByLabel("What's wrong?").fill('No signal from HDMI');
  await ayse.getByLabel("It can't be used until this is fixed").check();
  const wide = await ayse.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
  );
  expect(wide, 'no sideways scrolling on a phone').toBe(false);
  const { violations } = await new AxeBuilder({ page: ayse })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'])
    .analyze();
  expect(violations.map((v) => v.id)).toEqual([]);

  await ayse.getByRole('button', { name: 'Send the report' }).click();
  await expect(
    ayse.getByText('Thanks. The problem is reported', { exact: false }).first(),
  ).toBeVisible();
  await expect(ayse.locator('.issue')).toContainText('No signal from HDMI');
  await expect(ayse.locator('.issue')).toContainText("Can't be used");

  // The same page with the problem on it and the form open, in the dark theme.
  await ayse.evaluate(() => localStorage.setItem('smartdesk_theme', 'dark'));
  await ayse.reload();
  await expect(ayse.locator('.issue')).toContainText('No signal from HDMI');
  await ayse.getByRole('button', { name: 'Report a problem' }).click();
  await ayse.waitForFunction(() =>
    document
      .getAnimations()
      .every(
        (a) => a.playState !== 'running' || a.effect?.getComputedTiming().endTime === Infinity,
      ),
  );
  const dark = await new AxeBuilder({ page: ayse })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'])
    .analyze();
  expect(dark.violations.map((v) => v.id)).toEqual([]);

  // The next person at the door sees it, and isn't asked to send it again.
  const mehmet = await open('mehmet');
  await mehmet.goto(path(printed));
  await expect(mehmet.locator('.issue')).toContainText('No signal from HDMI');
  await mehmet.getByRole('button', { name: 'Report a problem' }).click();
  await mehmet.getByLabel('Projector').check();
  await expect(mehmet.getByText('This is already reported', { exact: false })).toBeVisible();
  await expect(mehmet.getByRole('button', { name: 'Send the report' })).toBeDisabled();

  // Resolved, and Ayşe hears about it in her inbox.
  const reports = (await (
    await admin.get('issues', { params: { resourceId: resource.id } })
  ).json()) as { id: string }[];
  expect(reports).toHaveLength(1);
  const resolved = await admin.patch(`issues/${reports[0].id}`, { data: { status: 'Resolved' } });
  expect(resolved.ok(), 'resolve the report').toBe(true);
  await ayse.goto('/inbox');
  await expect(ayse.getByText('The problem you reported is resolved').first()).toBeVisible();
  await expect(ayse.getByText(`${resource.name} · Projector, reported`).first()).toBeVisible();
});
