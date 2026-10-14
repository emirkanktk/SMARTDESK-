import { apiAs, bookingLink, expect, slot, test } from './support';

const WEEK = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

// The rules an Admin sets for each kind of resource (ADR-0046): the booking form shows them and
// keeps to them, and so does the API.
test("an admin sets a kind's rules, and booking keeps to them", async ({
  admin,
  open,
  resource,
}) => {
  const kinds = (await (await admin.get('resource-types')).json()) as {
    id: string;
    name: string;
  }[];
  // The test resource is of the first kind by name.
  const kind = kinds[0];

  const page = await open('admin');
  // Open every day in the browser's own zone, so the times below read the same anywhere.
  const zone = await page.evaluate(() => Intl.DateTimeFormat().resolvedOptions().timeZone);
  try {
    const hours = await admin.put('booking-rules/office-hours', {
      data: { timeZone: zone, opensAt: '09:00', closesAt: '18:00', workdays: WEEK },
    });
    expect(hours.ok(), 'set the office hours').toBe(true);

    await page.goto('/admin/rules');
    const card = page.locator('li.kind', { has: page.getByRole('heading', { name: kind.name }) });
    await card.getByLabel('Longest booking, in hours').fill('2');
    await card.getByLabel('Within office hours only').check();
    await card.getByRole('button', { name: 'Save changes' }).click();
    await expect(page.getByText(`Rules for ${kind.name} saved.`)).toBeVisible();

    // A booking after closing time: the form says why before asking.
    const ayse = await open('ayse');
    await ayse.goto(bookingLink(resource, slot(1, 20)));
    await expect(ayse.locator('.rules-note')).toHaveText(
      `${kind.name} bookings: within office hours (Mon-Sun 09:00-18:00), at most 2 h long.`,
    );
    await ayse.getByRole('button', { name: 'Book', exact: true }).click();
    await expect(
      ayse.getByText(`${kind.name} bookings must be within office hours: Mon-Sun 09:00-18:00.`),
    ).toBeVisible();
    await expect(ayse).toHaveURL(/\/reservations\/new/);

    // The API says the same to anyone who asks it directly.
    const api = await apiAs('ayse');
    const late = slot(1, 20);
    const refused = await api.post('reservations', {
      data: {
        resourceId: resource.id,
        startDateTime: late.start.toISOString(),
        endDateTime: late.end.toISOString(),
      },
    });
    expect(refused.status()).toBe(400);
    await api.dispose();

    // Within the rules, it's booked.
    await ayse.goto(bookingLink(resource, slot(1, 10)));
    await ayse.getByRole('button', { name: 'Book', exact: true }).click();
    await ayse.waitForURL('**/reservations/mine');
  } finally {
    // As the migration leaves them, for the tests after this one.
    await admin.put(`booking-rules/types/${kind.id}`, {
      data: {
        maxDurationMinutes: null,
        maxDaysAhead: null,
        maxActivePerUser: null,
        officeHoursOnly: false,
      },
    });
    await admin.put('booking-rules/office-hours', {
      data: {
        timeZone: 'Europe/Istanbul',
        opensAt: '09:00',
        closesAt: '18:00',
        workdays: WEEK.slice(0, 5),
      },
    });
  }
});
