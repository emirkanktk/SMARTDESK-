import AxeBuilder from '@axe-core/playwright';
import { apiAs, expect, test } from './support';

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

// Following a report up (ADR-0037, ADR-0038): it reaches the admin's screen as it's made, the
// resource is closed while it's fixed, which cancels the booking in that time and tells its
// owner why, and once it's resolved the resource is opened again.
test('a report comes in live, its resource is closed while it is fixed, and opened again', async ({
  resource,
  open,
}) => {
  // Mehmet's booking in half an hour: closing the resource until tomorrow evening cancels it.
  const mehmetApi = await apiAs('mehmet');
  const start = new Date(Date.now() + 30 * 60_000);
  const booked = await mehmetApi.post('reservations', {
    data: {
      resourceId: resource.id,
      startDateTime: start.toISOString(),
      endDateTime: new Date(start.getTime() + 30 * 60_000).toISOString(),
      note: null,
      reminderMinutes: null,
    },
  });
  expect(booked.status(), 'book it').toBe(201);
  const booking = (await booked.json()) as { id: string };

  const page = await open('admin');
  page.on('dialog', (dialog) => void dialog.accept());
  await page.goto('/issues');
  await expect(page.getByRole('heading', { name: 'Reported problems' })).toBeVisible();

  // Ayşe reports at the door; it's on the admin's screen at once, with a toast.
  const ayseApi = await apiAs('ayse');
  const reported = await ayseApi.post(`resources/${resource.id}/issues`, {
    multipart: {
      category: 'AirConditioning',
      description: 'Too hot to sit in',
      isBlocking: 'true',
    },
  });
  expect(reported.status(), 'report it').toBe(201);
  await expect(
    page.getByText(`New problem at ${resource.name}: Air conditioning.`, { exact: false }),
  ).toBeVisible();
  const card = page.locator('.issue', { hasText: resource.name });
  await expect(card).toContainText('Too hot to sit in');

  // Closed until the end of tomorrow: Mehmet's booking goes, and he's told why.
  await card.getByRole('button', { name: 'Take it out of use' }).click();
  await card.getByLabel('the end of tomorrow').check();
  await expect(
    card.getByText('1 booking in that time is cancelled', { exact: false }),
  ).toBeVisible();
  await card.getByRole('button', { name: 'Close it' }).click();
  await expect(card.locator('.closed')).toContainText('Closed until');
  const cancelled = (await (await mehmetApi.get(`reservations/${booking.id}`)).json()) as {
    status: string;
  };
  expect(cancelled.status).toBe('Cancelled');
  const told = (await (await mehmetApi.get('inbox')).json()) as {
    kind: string;
    reason: string | null;
  }[];
  expect(
    told.some(
      (m) => m.kind === 'CancelledByBlock' && m.reason === 'Air conditioning: Too hot to sit in',
    ),
  ).toBe(true);

  // Fixed: resolved, which tells Ayşe, and opened again when asked.
  await card.getByRole('button', { name: 'Mark resolved' }).click();
  await expect(page.getByText(`${resource.name} can be booked again.`)).toBeVisible();
  const blocks = (await (
    await ayseApi.get('resource-blocks', {
      params: {
        from: new Date().toISOString(),
        to: new Date(Date.now() + 3 * 24 * 60 * 60_000).toISOString(),
        resourceId: resource.id,
      },
    })
  ).json()) as unknown[];
  expect(blocks).toEqual([]);
  const ayseInbox = (await (await ayseApi.get('inbox')).json()) as { kind: string }[];
  expect(ayseInbox.some((m) => m.kind === 'IssueResolved')).toBe(true);

  await mehmetApi.dispose();
  await ayseApi.dispose();
});
