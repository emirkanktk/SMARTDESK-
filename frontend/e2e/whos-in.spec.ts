import { apiAs, expect, slot, teamMember, test } from './support';

// Who's in the office on a day (ADR-0063): a booking puts its owner on that day's list, marked
// for their manager as their team, and a place opens the floor plan where it is.
const NAMES: Record<string, string> = { ayse: 'Ayşe Yılmaz', mehmet: 'Mehmet Demir' };

test("a booking puts its owner on that day's list for their team, and a place opens the plan", async ({
  open,
  resource,
}) => {
  const member = await teamMember();
  test.skip(!member, 'the demo manager has nobody in the team in this database');
  const name = NAMES[member!];
  const api = await apiAs(member!);
  const when = slot(2, 10);
  const booked = await api.post('reservations', {
    data: {
      resourceId: resource.id,
      startDateTime: when.start.toISOString(),
      endDateTime: when.end.toISOString(),
    },
  });
  expect(booked.status(), 'book for the day after tomorrow').toBe(201);
  await api.dispose();

  const page = await open('manager');
  await page.goto('/whos-in');
  await expect(page.getByRole('heading', { name: "Who's in" })).toBeVisible();
  await page.getByRole('button', { name: 'Next day' }).click();
  await page.getByRole('button', { name: 'Next day' }).click();
  await expect(page).toHaveURL(/\/whos-in\?date=\d{4}-\d{2}-\d{2}$/);
  await expect(page.getByRole('button', { name: 'Today' })).toBeVisible();

  const person = page.locator('.person').filter({ hasText: name });
  await expect(person).toContainText(resource.name);
  await expect(person).toContainText('10:00–11:00');
  await expect(person.locator('.badge')).toHaveText('Your team');

  await page.getByRole('button', { name: 'My team' }).click();
  await page.getByRole('searchbox', { name: 'Search by name' }).fill(name.slice(0, 4));
  await expect(page.locator('.person')).toHaveCount(1);
  await expect(person).toBeVisible();

  await person.getByRole('link', { name: resource.name }).click();
  await expect(page).toHaveURL(/\/floor-plan\?floor=E2E(%20|\+)floor&at=/);
});
