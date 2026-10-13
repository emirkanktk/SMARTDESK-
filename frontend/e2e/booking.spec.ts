import {
  acceptNextDialog,
  apiAs,
  bookingLink,
  day,
  expect,
  localInput,
  slot,
  test,
} from './support';

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

// ADR-0042: a taken time comes back with the nearest free ones, and one is a click away.
test('a taken time is answered with the nearest free times, booked with the next click', async ({
  open,
  resource,
}) => {
  const when = slot(1, 10);

  const ayse = await open('ayse');
  await ayse.goto(bookingLink(resource, when));
  await ayse.getByRole('button', { name: 'Book', exact: true }).click();
  await ayse.waitForURL('**/reservations/mine');

  const mehmet = await open('mehmet');
  await mehmet.goto(bookingLink(resource, when));
  await mehmet.getByRole('button', { name: 'Book', exact: true }).click();
  const times = mehmet.getByRole('group', { name: 'Free at another time' });
  await expect(times.getByRole('button')).toHaveText(['09:00-10:00', '11:00-12:00']);

  await times.getByRole('button', { name: '11:00-12:00' }).click();
  await expect(mehmet.locator('#start')).toHaveValue(localInput(slot(1, 11).start));
  await expect(mehmet.locator('.conflict')).toHaveCount(0);
  await mehmet.getByRole('button', { name: 'Book', exact: true }).click();
  await mehmet.waitForURL('**/reservations/mine');
  await expect(mehmet.getByText(`${resource.name} is booked for`)).toContainText('11:00-12:00');
});

// ADR-0043: a taken time can be waited for, and it's booked for whoever waited the moment it
// frees up.
test('someone waiting for a taken time gets it as soon as it frees up', async ({
  open,
  resource,
}) => {
  const when = slot(1, 10);
  // Booked before any page opens: a new sign-in ends a user's open live connections.
  const ayseApi = await apiAs('ayse');
  const booked = await ayseApi.post('reservations', {
    data: {
      resourceId: resource.id,
      startDateTime: when.start.toISOString(),
      endDateTime: when.end.toISOString(),
    },
  });
  expect(booked.status()).toBe(201);
  const ayses = (await booked.json()) as { id: string };

  const mehmet = await open('mehmet');
  await mehmet.goto(bookingLink(resource, when));
  await mehmet.getByRole('button', { name: 'Book', exact: true }).click();
  await mehmet
    .getByRole('group', { name: 'Or wait for it' })
    .getByRole('button', { name: 'Join the waiting list' })
    .click();
  await mehmet.waitForURL('**/reservations/mine');
  const waiting = mehmet.getByRole('region', { name: 'Waiting for' });
  await expect(waiting).toContainText(resource.name);
  await expect(waiting).toContainText('Number 1 in line');

  // Ayşe cancels: the time is Mehmet's at once, and he's told.
  expect((await ayseApi.post(`reservations/${ayses.id}/cancel`)).ok()).toBe(true);
  await ayseApi.dispose();
  await expect(
    mehmet.getByText("A time you were waiting for freed up, and it's booked for you", {
      exact: false,
    }),
  ).toBeVisible();
  await expect(waiting).toHaveCount(0);
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

test('an open calendar stays live after a sign-in on another device', async ({
  open,
  resource,
}) => {
  const when = slot(1, 16);
  const mehmet = await open('mehmet');
  await mehmet.goto(`/calendar?week=${day(when.start)}`);
  const row = mehmet.locator('tr', { hasText: resource.name });
  await expect(row).toBeVisible();

  // Signing in elsewhere ends the user's open live connections (ADR-0026); the app connects
  // again and catches up.
  const elsewhere = await apiAs('mehmet');
  await elsewhere.dispose();

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
