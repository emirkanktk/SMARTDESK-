import { APIRequestContext } from '@playwright/test';
import { apiAs, expect, test, TestResource } from './support';

/** Books `resource` from a minute ago until `minutes` from now: under way already. */
async function bookUnderWay(
  api: APIRequestContext,
  resource: TestResource,
  minutes: number,
): Promise<void> {
  const booked = await api.post('reservations', {
    data: {
      resourceId: resource.id,
      startDateTime: new Date(Date.now() - 60_000).toISOString(),
      endDateTime: new Date(Date.now() + minutes * 60_000).toISOString(),
      note: null,
      reminderMinutes: null,
    },
  });
  expect(booked.status(), 'book it').toBe(201);
}

// Running over (ADR-0033).
test('a booking about to end asks whether it needs longer, and runs longer with a click', async ({
  open,
  resource,
}) => {
  // The question comes from a job that looks every 30 seconds.
  test.setTimeout(120_000);
  // Signed in to the API first: a new sign-in ends the user's open live connections.
  const api = await apiAs('ayse');
  const ayse = await open('ayse');
  await ayse.goto('/reservations/mine');
  await expect(ayse.getByRole('heading', { name: 'My reservations' })).toBeVisible();

  // Booked once the page is open and connected: the question is asked once, live.
  await bookUnderWay(api, resource, 8);
  await api.dispose();

  const card = ayse.locator('app-ending-soon section');
  await expect(card.locator('h2')).toContainText(`${resource.name} ends at`, { timeout: 60_000 });
  await card.getByRole('button', { name: '+30 min' }).click();

  await expect(ayse.getByText(`${resource.name} is now booked until`)).toBeVisible();
  await expect(card).toHaveCount(0);
});

test('a booking in progress runs longer from My reservations', async ({ open, resource }) => {
  const api = await apiAs('ayse');
  await bookUnderWay(api, resource, 30);
  await api.dispose();
  const ayse = await open('ayse');
  await ayse.goto('/reservations/mine');

  const item = ayse.locator('li.item', { hasText: resource.name });
  await item.getByRole('button', { name: 'Extend' }).click();
  await item.getByRole('button', { name: '+15 min' }).click();

  await expect(ayse.getByText(`${resource.name} is now booked until`)).toBeVisible();
  await expect(item.locator('app-extend-booking')).toHaveCount(0);
});
