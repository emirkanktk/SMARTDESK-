import AxeBuilder from '@axe-core/playwright';
import { apiAs, expect, PASSWORD, retire, test, TestResource, USERS } from './support';

/** The path of an absolute link from the API, so the page keeps the test's base URL. */
const path = (url: string) => url.replace(/^https?:\/\/[^/]+/, '');

// The page a QR code opens on a phone with no account (ADR-0050): the room, a check-in for the
// booking waiting for it, whoever's it is, and a problem reported, all without signing in. The
// report reaches Reported problems at once, on the screen of whoever looks after the room.
test('without an account, the QR code opens the room, checks in and reports a problem', async ({
  admin,
  open,
  visit,
}) => {
  const types = (await (await admin.get('resource-types')).json()) as { id: string }[];
  const created = await admin.post('resources', {
    data: {
      name: `E2E visit ${Date.now().toString(36).toUpperCase()}`,
      resourceTypeId: types[0].id,
      capacity: 4,
      location: 'E2E floor',
      features: [],
      teamIds: [],
      checkInRequired: true,
    },
  });
  expect(created.status(), 'create the room').toBe(201);
  const room = (await created.json()) as TestResource;

  try {
    const printed = (
      (await (await admin.get(`resources/${room.id}/check-in-code`)).json()) as { url: string }
    ).url;
    const ayseApi = await apiAs('ayse');
    const booked = await ayseApi.post('reservations', {
      data: {
        resourceId: room.id,
        startDateTime: new Date(Date.now() - 60_000).toISOString(),
        endDateTime: new Date(Date.now() + 59 * 60_000).toISOString(),
        note: 'Budget talk',
        reminderMinutes: null,
      },
    });
    expect(booked.status()).toBe(201);
    const booking = (await booked.json()) as { id: string };

    // A phone that never signed in: the room's own page, not the sign-in form, and the code
    // out of the address at once. Times only: nobody's name, nobody's note.
    const phone = await visit();
    await phone.setViewportSize({ width: 390, height: 844 });
    await phone.goto(path(printed));
    await expect(phone.locator('h1')).toHaveText(room.name);
    await expect(phone).toHaveURL(new RegExp(`/check-in/${room.id}$`));
    await expect(phone.locator('.room-state')).toContainText('In use until');
    await expect(phone.getByText('is waiting for its check-in', { exact: false })).toBeVisible();
    await expect(phone.getByText('Budget talk')).toHaveCount(0);
    await expect(phone.getByText('Ayşe', { exact: false })).toHaveCount(0);
    const { violations } = await new AxeBuilder({ page: phone })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'])
      .analyze();
    expect(violations.map((v) => v.id)).toEqual([]);

    await phone.getByRole('button', { name: 'Check in', exact: true }).click();
    await expect(
      phone.getByText('is checked in, so the room is kept for it', { exact: false }),
    ).toBeVisible();
    const mine = (await (await ayseApi.get('reservations/mine')).json()) as {
      id: string;
      checkedInAt: string | null;
    }[];
    expect(mine.find((r) => r.id === booking.id)?.checkedInAt).not.toBeNull();
    await ayseApi.dispose();

    // A problem, reported without an account. The admin has Reported problems open: it comes in
    // there at once, with a toast, from nobody, and is still there after a reload.
    const selin = await open('admin');
    await selin.goto('/issues');
    await expect(selin.getByRole('heading', { name: 'Reported problems' })).toBeVisible();

    await phone.getByRole('button', { name: 'Report a problem' }).click();
    await phone.getByLabel('Cleaning').check();
    await phone.getByLabel("What's wrong?").fill('Coffee on the table');
    await phone.getByRole('button', { name: 'Send the report' }).click();
    await expect(
      phone.getByText('Thanks. The problem is reported', { exact: false }),
    ).toBeVisible();
    await expect(phone.locator('.issue', { hasText: 'Coffee on the table' })).toBeVisible();

    await expect(
      selin.getByText(`New problem at ${room.name}: Cleaning.`, { exact: false }),
    ).toBeVisible();
    const card = selin.locator('.issue', { hasText: 'Coffee on the table' });
    await expect(card).toContainText(room.name);
    await expect(card).toContainText('Reported at the door, without an account');
    await selin.reload();
    await expect(card).toContainText('Reported at the door, without an account');

    // Signing in comes back to the same page, signed in: the booking is hers, checked in.
    await phone.getByRole('link', { name: 'Sign in' }).click();
    await phone.locator('#email').fill(USERS.ayse);
    await phone.locator('#password').fill(PASSWORD);
    await phone.locator('button[type=submit]').click();
    await expect(phone.getByText("You're checked in", { exact: false })).toBeVisible();
    await expect(phone).toHaveURL(new RegExp(`/check-in/${room.id}$`));
  } finally {
    await retire(admin, room.id);
  }
});
