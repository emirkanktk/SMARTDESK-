import { acceptNextDialog, apiAs, bookingLink, day, expect, slot, test } from './support';

// Bookings that repeat (ADR-0035). Within four weeks, so the test resource can be retired.
test('a weekly booking shows its dates first, books around a taken one, and ends early', async ({
  open,
  resource,
}) => {
  const first = slot(7, 10);
  const second = slot(14, 10);

  // Someone else has the second date.
  const mehmet = await apiAs('mehmet');
  const booked = await mehmet.post('reservations', {
    data: {
      resourceId: resource.id,
      startDateTime: second.start.toISOString(),
      endDateTime: second.end.toISOString(),
      note: null,
      reminderMinutes: null,
    },
  });
  expect(booked.status(), 'book the second date').toBe(201);
  await mehmet.dispose();

  const ayse = await open('ayse');
  await ayse.goto(bookingLink(resource, first));
  await ayse.getByLabel('How it repeats').selectOption('weekly');
  await ayse.locator('#until').fill(day(slot(28, 10).start));

  const dates = ayse.locator('app-series-dates');
  await expect(dates).toContainText("1 of the 4 dates aren't free; booking skips them.");
  await expect(dates.locator('li.taken')).toHaveCount(1);
  await ayse.getByRole('button', { name: 'Book the 3 free dates' }).click();

  await expect(ayse.getByText(`${resource.name} is booked on 3 dates`)).toBeVisible();
  await ayse.waitForURL('**/reservations/mine');
  const items = ayse.locator('li.item', { hasText: resource.name });
  await expect(items).toHaveCount(3);
  await expect(items.first()).toContainText('Repeats every');

  // Ended from its second booked date: only the first is still to come.
  acceptNextDialog(ayse);
  await items.nth(1).getByRole('button', { name: 'Cancel this and later dates' }).click();
  await expect(
    ayse.getByText(`2 dates of your ${resource.name} booking were cancelled`),
  ).toBeVisible();
  await expect(items).toHaveCount(1);
});
