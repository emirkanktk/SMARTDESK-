import AxeBuilder from '@axe-core/playwright';
import { request } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { API_URL, acceptNextDialog, apiAs, day, expect, slot, test } from './support';

// Bookings in a calendar app (ADR-0048): one as an .ics file from My reservations, and all of them
// through the personal link the profile makes, which a calendar app fetches without signing in.
test('a booking goes to a calendar app as a file, and the profile makes a link a calendar subscribes to', async ({
  open,
  resource,
}) => {
  const when = slot(1, 10);
  const ayseApi = await apiAs('ayse');
  const booked = await ayseApi.post('reservations', {
    data: {
      resourceId: resource.id,
      startDateTime: when.start.toISOString(),
      endDateTime: when.end.toISOString(),
      note: 'Calendar check',
      reminderMinutes: null,
    },
  });
  expect(booked.status()).toBe(201);
  const reservation = (await booked.json()) as { id: string };
  await ayseApi.dispose();

  // One booking, as a file.
  const ayse = await open('ayse');
  await ayse.goto('/reservations/mine');
  const item = ayse.locator('.item', { hasText: resource.name });
  const downloading = ayse.waitForEvent('download');
  await item.getByRole('button', { name: 'Add to calendar' }).click();
  const download = await downloading;
  expect(download.suggestedFilename()).toBe(`smartdesk-${day(when.start)}.ics`);
  const file = await readFile((await download.path())!, 'utf8');
  expect(file).toContain(`UID:${reservation.id}@smartdesk`);
  expect(file).toContain(`SUMMARY:${resource.name}`);

  // All of them, through the link on the profile, shown once.
  await ayse.getByRole('link', { name: 'Calendar link' }).click();
  await expect(ayse).toHaveURL(/\/profile$/);
  // A local database may still have the link of a run that stopped half way: a new one then.
  const make = ayse.getByRole('button', { name: 'Make my calendar link' });
  const renew = ayse.getByRole('button', { name: 'New link' });
  await expect(make.or(renew)).toBeVisible();
  if (await renew.isVisible()) {
    acceptNextDialog(ayse);
    await renew.click();
  } else {
    await make.click();
  }
  const link = await ayse.getByRole('textbox', { name: 'Your calendar link' }).inputValue();
  expect(link).toMatch(/\/api\/calendar\/feed\.ics\?token=[0-9a-f]{64}$/);
  await expect(ayse.getByRole('link', { name: 'Open in my calendar app' })).toHaveAttribute(
    'href',
    /^webcal:\/\//,
  );
  await expect(ayse.getByText('No calendar has fetched it yet.')).toBeVisible();
  const { violations } = await new AxeBuilder({ page: ayse })
    .include('#calendar')
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'])
    .analyze();
  expect(violations.map((v) => v.id)).toEqual([]);

  // A calendar app fetches it without signing in. The link carries the app's own address;
  // here the API is asked directly, wherever the test's app is served.
  const feedUrl = `${API_URL}/calendar/feed.ics${new URL(link).search}`;
  const calendarApp = await request.newContext({ ignoreHTTPSErrors: true });
  try {
    const feed = await calendarApp.get(feedUrl);
    expect(feed.status()).toBe(200);
    expect(feed.headers()['content-type']).toContain('text/calendar');
    expect(await feed.text()).toContain(`UID:${reservation.id}@smartdesk`);

    await ayse.reload();
    await expect(ayse.getByText('A calendar last fetched it on', { exact: false })).toBeVisible();

    // Removed, it gives nothing more.
    acceptNextDialog(ayse);
    await ayse.getByRole('button', { name: 'Remove the link' }).click();
    await expect(ayse.getByRole('button', { name: 'Make my calendar link' })).toBeVisible();
    expect((await calendarApp.get(feedUrl)).status()).toBe(404);
  } finally {
    await calendarApp.dispose();
  }
});
