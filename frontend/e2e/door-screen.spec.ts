import AxeBuilder from '@axe-core/playwright';
import { APIRequestContext } from '@playwright/test';
import { apiAs, expect, retire, test, TestResource } from './support';

/** A room of the test's own that asks for check-in. */
async function roomWithCheckIn(admin: APIRequestContext): Promise<TestResource> {
  const types = (await (await admin.get('resource-types')).json()) as { id: string }[];
  const created = await admin.post('resources', {
    data: {
      name: `E2E door ${Date.now().toString(36).toUpperCase()}`,
      resourceTypeId: types[0].id,
      capacity: 4,
      location: 'E2E floor',
      features: [],
      teamIds: [],
      checkInRequired: true,
    },
  });
  expect(created.status(), 'create the room').toBe(201);
  return (await created.json()) as TestResource;
}

/** The path of an absolute link from the API, so the page keeps the test's base URL. */
const path = (url: string) => url.replace(/^https?:\/\/[^/]+/, '');

// The door screen and checking in at the door (ADR-0034).
test("a door screen's changing code checks in, and the printed one no longer does", async ({
  admin,
  visit,
  open,
}) => {
  const room = await roomWithCheckIn(admin);
  try {
    const printed = (
      (await (await admin.get(`resources/${room.id}/check-in-code`)).json()) as { url: string }
    ).url;
    const link = (
      (await (await admin.post(`resources/${room.id}/door-screen`)).json()) as { url: string }
    ).url;

    // Ayşe's booking, under way. Booked before any page opens: a new sign-in ends a user's
    // open live connections.
    const ayseApi = await apiAs('ayse');
    const booked = await ayseApi.post('reservations', {
      data: {
        resourceId: room.id,
        startDateTime: new Date(Date.now() - 60_000).toISOString(),
        endDateTime: new Date(Date.now() + 59 * 60_000).toISOString(),
        note: null,
        reminderMinutes: null,
      },
    });
    expect(booked.status()).toBe(201);
    await ayseApi.dispose();

    // The screen at the door: opened with its link, the token then out of the address.
    const door = await visit();
    await door.goto(path(link));
    await expect(door.locator('h1')).toHaveText(room.name);
    await expect(door).toHaveURL(new RegExp(`/door/${room.id}$`));
    await expect(door.getByText('In use', { exact: true })).toBeVisible();
    await expect(door.getByText('Nobody has checked in yet.', { exact: false })).toBeVisible();
    const { violations } = await new AxeBuilder({ page: door })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'])
      .analyze();
    expect(violations.map((v) => v.id)).toEqual([]);
    const changing = (await door.locator('.scan').getAttribute('data-url'))!;

    // The printed code is refused, saying why.
    const ayse = await open('ayse');
    await ayse.goto(path(printed));
    await ayse.getByRole('button', { name: 'Check in', exact: true }).click();
    await expect(ayse.getByRole('alert')).toContainText('door screen');

    // The screen's code checks in, and leaves the address at once.
    await ayse.goto(path(changing));
    await expect(ayse).not.toHaveURL(/code=/);
    await ayse.getByRole('button', { name: 'Check in', exact: true }).click();
    await expect(ayse.getByText("You're checked in", { exact: false })).toBeVisible();

    // The screen follows on its next look.
    await expect(door.getByText('Checked in', { exact: true })).toBeVisible({ timeout: 15_000 });

    // Done early: the room is free again, on the screen too.
    ayse.once('dialog', (dialog) => void dialog.accept());
    await ayse.getByRole('button', { name: "We're done: free the room" }).click();
    await expect(ayse.getByText(`${room.name} is free again.`)).toBeVisible();
    await expect(door.getByText('Free', { exact: true })).toBeVisible({ timeout: 15_000 });
  } finally {
    await retire(admin, room.id);
  }
});

test('at a free room, the door books it on the spot and checks in at once', async ({
  admin,
  open,
}) => {
  const room = await roomWithCheckIn(admin);
  try {
    const printed = (
      (await (await admin.get(`resources/${room.id}/check-in-code`)).json()) as { url: string }
    ).url;
    const mehmet = await open('mehmet');
    await mehmet.goto(path(printed));

    await expect(mehmet.getByText(`${room.name} is free for the rest of the day.`)).toBeVisible();
    await mehmet.getByRole('button', { name: '30 min' }).click();

    await expect(mehmet.getByText(`${room.name} is yours until`, { exact: false })).toBeVisible();
    await expect(mehmet.getByText("You're checked in", { exact: false })).toBeVisible();
  } finally {
    await retire(admin, room.id);
  }
});
