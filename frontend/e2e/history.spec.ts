import AxeBuilder from '@axe-core/playwright';
import { apiAs, expect, test } from './support';

// The history of changes (ADR-0052): what an Admin finds about a resource from its own page -
// who made it, who booked it, and who cancelled the booking - in words.
test("a resource's history says who made it, booked it and cancelled the booking", async ({
  open,
  resource,
}) => {
  const ayseApi = await apiAs('ayse');
  const start = new Date(Date.now() + 2 * 24 * 60 * 60_000);
  const booked = await ayseApi.post('reservations', {
    data: {
      resourceId: resource.id,
      startDateTime: start.toISOString(),
      endDateTime: new Date(start.getTime() + 60 * 60_000).toISOString(),
      note: 'Quarterly review',
      reminderMinutes: null,
    },
  });
  expect(booked.status(), 'book it').toBe(201);
  const booking = (await booked.json()) as { id: string };
  expect((await ayseApi.post(`reservations/${booking.id}/cancel`)).ok(), 'cancel it').toBe(true);
  await ayseApi.dispose();

  const selin = await open('admin');
  await selin.goto(`/resources/${resource.id}/edit`);
  await selin.getByRole('link', { name: 'History' }).click();
  await selin.waitForURL(`**/admin/history?resourceId=${resource.id}`);
  await expect(selin.getByRole('heading', { name: 'History' })).toBeVisible();
  await expect(selin.locator('.narrowed')).toHaveText(`Only ${resource.name}.`);

  const entries = selin.locator('.entry');
  await expect(entries).toHaveCount(3);
  // Newest first: the cancellation, the booking, the resource itself.
  await expect(entries.nth(0)).toContainText('Booking');
  await expect(entries.nth(0)).toContainText('Changed');
  await expect(entries.nth(0)).toContainText('Ayşe Yılmaz');
  await expect(entries.nth(0)).toContainText('Confirmed → Cancelled');
  await expect(entries.nth(1)).toContainText('Added');
  await expect(entries.nth(1)).toContainText('Quarterly review');
  await expect(entries.nth(2)).toContainText('Resource');
  await expect(entries.nth(2)).toContainText(resource.name);

  const { violations } = await new AxeBuilder({ page: selin })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'])
    .analyze();
  expect(violations.map((v) => v.id)).toEqual([]);

  // Only the bookings of it.
  await selin.getByLabel('What').selectOption('bookings');
  await expect(entries).toHaveCount(2);
});
