import { acceptNextDialog, apiAs, bookingLink, day, expect, slot, test } from './support';

// Booking, the brief's conflict case, and live updates (ADR-0011, ADR-0014).
test('a booked slot is refused to the next person, who is offered what else is free', async ({
  open,
  resource,
}) => {
  const when = slot(1, 10);

  const ayse = await open('ayse');
  await ayse.goto(bookingLink(resource, when));
  await ayse.getByRole('button', { name: 'Book', exact: true }).click();
  await ayse.waitForURL('**/reservations/mine');
  await expect(ayse.getByText(`${resource.name} is booked for`)).toBeVisible();

  const mehmet = await open('mehmet');
  await mehmet.goto(bookingLink(resource, when));
  await mehmet.getByRole('button', { name: 'Book', exact: true }).click();
  const conflict = mehmet.locator('.conflict');
  await expect(conflict).toContainText(
    `${resource.name} is already reserved for part of that time.`,
  );
  await expect(conflict.getByRole('link', { name: 'See what else is free then' })).toBeVisible();
  await expect(mehmet).toHaveURL(/\/reservations\/new/);

  // Ayşe cancels from her list; the slot is free again for Mehmet.
  const item = ayse.locator('li.item', { hasText: resource.name });
  acceptNextDialog(ayse);
  await item.getByRole('button', { name: 'Cancel' }).click();
  await expect(
    ayse.getByText(new RegExp(`Your ${resource.name} reservation for .* was cancelled`)),
  ).toBeVisible();

  await mehmet.getByRole('button', { name: 'Book', exact: true }).click();
  await mehmet.waitForURL('**/reservations/mine');
  await expect(mehmet.locator('li.item', { hasText: resource.name })).toBeVisible();
});

test("another person's booking and its cancellation reach an open calendar without a reload", async ({
  open,
  resource,
}) => {
  const when = slot(1, 14);
  const mehmet = await open('mehmet');
  await mehmet.goto(`/calendar?week=${day(when.start)}`);
  const row = mehmet.locator('tr', { hasText: resource.name });
  await expect(row).toBeVisible();
  await expect(row.locator('a.entry')).toHaveCount(0);

  const ayse = await apiAs('ayse');
  const booked = await ayse.post('reservations', {
    data: {
      resourceId: resource.id,
      startDateTime: when.start.toISOString(),
      endDateTime: when.end.toISOString(),
      note: null,
      reminderMinutes: null,
    },
  });
  expect(booked.status()).toBe(201);
  const reservation = (await booked.json()) as { id: string };

  await expect(row.locator('a.entry', { hasText: 'Ayşe' })).toBeVisible();

  await ayse.post(`reservations/${reservation.id}/cancel`);
  await expect(row.locator('a.entry')).toHaveCount(0);
  await ayse.dispose();
});
