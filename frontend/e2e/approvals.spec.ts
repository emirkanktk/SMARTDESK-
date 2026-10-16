import AxeBuilder from '@axe-core/playwright';
import { bookingLink, expect, retire, slot, test, TestResource } from './support';

// Bookings that wait for an Admin's approval (ADR-0049): a kind asks for it, a booking of it is
// asked for and holds its time, and an Admin approves one and declines another, saying why.
test('a car is asked for, and an Admin approves one request and declines another', async ({
  admin,
  open,
}) => {
  const kinds = (await (await admin.get('resource-types')).json()) as {
    id: string;
    name: string;
  }[];
  const cars = kinds.find((k) => k.name === 'Company Car')!;
  const created = await admin.post('resources', {
    data: {
      name: `E2E car ${Date.now().toString(36).toUpperCase()}`,
      resourceTypeId: cars.id,
      capacity: 5,
      location: 'E2E parking',
      features: [],
      teamIds: [],
    },
  });
  expect(created.status(), 'create the car').toBe(201);
  const car = (await created.json()) as TestResource;
  const rules = (approval: boolean) =>
    admin.put(`booking-rules/types/${cars.id}`, {
      data: {
        maxDurationMinutes: null,
        maxDaysAhead: null,
        maxActivePerUser: null,
        officeHoursOnly: false,
        requiresApproval: approval,
      },
    });

  try {
    // The Admin asks for approval on cars, on the rules page.
    const selin = await open('admin');
    await selin.goto('/admin/rules');
    const card = selin.locator('li.kind', {
      has: selin.getByRole('heading', { name: 'Company Car' }),
    });
    await card.getByLabel("Wait for an Admin's approval", { exact: false }).check();
    await card.getByRole('button', { name: 'Save changes' }).click();
    await expect(selin.getByText('Rules for Company Car saved.')).toBeVisible();

    // Ayşe asks for the car twice: the form says it waits for approval and doesn't repeat.
    const ayse = await open('ayse');
    for (const day of [2, 3]) {
      await ayse.goto(bookingLink(car, slot(day, 9, 480)));
      await expect(ayse.locator('.rules-note')).toHaveText(
        'Company Car bookings: approved by an Admin first.',
      );
      await expect(ayse.locator('.repeat-note')).toBeVisible();
      await ayse.getByRole('button', { name: 'Ask for it', exact: true }).click();
      await expect(ayse.getByText(`${car.name} is asked for`, { exact: false })).toBeVisible();
      await ayse.waitForURL('**/reservations/mine');
    }
    await expect(ayse.locator('.item', { hasText: car.name })).toHaveCount(2);
    await expect(ayse.locator('.item', { hasText: car.name }).first()).toContainText(
      'Awaiting approval',
    );

    // The Admin decides: approves the sooner one, declines the later one saying why.
    await selin.goto('/admin/approvals');
    const requests = selin.locator('.request', { hasText: car.name });
    await expect(requests).toHaveCount(2);
    const { violations } = await new AxeBuilder({ page: selin })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'])
      .analyze();
    expect(violations.map((v) => v.id)).toEqual([]);

    await requests.first().getByRole('button', { name: 'Approve' }).click();
    await expect(
      selin.getByText(`Approved: ${car.name} for Ayşe Yılmaz`, { exact: false }),
    ).toBeVisible();
    await expect(requests).toHaveCount(1);
    await requests.first().getByRole('button', { name: 'Decline' }).click();
    await requests
      .first()
      .getByLabel("Why it's declined")
      .fill('The car is at the service that day.');
    await requests.first().getByRole('button', { name: 'Decline it' }).click();
    await expect(selin.getByText('Nothing waits for approval.')).toBeVisible();

    // Ayşe sees both, live: one booked, approved by Selin, the other declined with the reason.
    const approved = ayse.locator('.item', { hasText: car.name });
    await expect(approved).toHaveCount(1);
    await expect(approved).toContainText('Approved by');
    await expect(approved).not.toContainText('Awaiting approval');
    await ayse.getByRole('tab', { name: /Cancelled/ }).click();
    await expect(ayse.locator('.item', { hasText: car.name })).toContainText(
      'The car is at the service that day.',
    );
  } finally {
    expect((await rules(false)).ok(), 'put the kind back').toBe(true);
    await retire(admin, car.id);
  }
});
