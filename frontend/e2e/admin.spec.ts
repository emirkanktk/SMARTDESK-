import { acceptNextDialog, bookingLink, expect, localInput, retire, slot, test } from './support';

// What admins and managers do to resources: blocked windows (ADR-0015) and team resources
// (ADR-0022).
test('a blocked window refuses bookings with its reason, until it is removed', async ({
  open,
  resource,
}) => {
  const window = slot(2, 9, 180);
  const reason = 'Projector replacement';

  const adminPage = await open('admin');
  await adminPage.goto('/admin/blocks');
  await adminPage.locator('#block-resource').selectOption(resource.id);
  await adminPage.locator('#block-start').fill(localInput(window.start));
  await adminPage.locator('#block-end').fill(localInput(window.end));
  await adminPage.locator('#block-reason').fill(reason);
  await adminPage.getByRole('button', { name: 'Block', exact: true }).click();
  await expect(adminPage.getByText(`${resource.name} is blocked for`)).toBeVisible();

  const ayse = await open('ayse');
  await ayse.goto(bookingLink(resource, slot(2, 10)));
  await ayse.getByRole('button', { name: 'Book', exact: true }).click();
  await expect(ayse.locator('.conflict')).toContainText(
    `${resource.name} is blocked for part of that time: ${reason}`,
  );

  const row = adminPage.locator('tr', { hasText: resource.name });
  acceptNextDialog(adminPage);
  await row.getByRole('button', { name: 'Remove' }).click();
  await expect(
    adminPage.getByText(new RegExp(`The block on ${resource.name} for .* was removed`)),
  ).toBeVisible();

  await ayse.getByRole('button', { name: 'Book', exact: true }).click();
  await ayse.waitForURL('**/reservations/mine');
});

test("a manager's new resource is their team's, and a shared one stays the admins'", async ({
  open,
  admin,
  resource,
}) => {
  const name = `E2E team ${Date.now().toString(36).toUpperCase()}`;
  const manager = await open('manager');

  await manager.goto('/resources/new');
  await expect(manager.getByText('It will belong to your team')).toBeVisible();
  await expect(manager.locator('fieldset.teams')).toHaveCount(0);
  await manager.locator('#name').fill(name);
  await manager.locator('#type').selectOption({ index: 1 });
  await manager.locator('#capacity').fill('6');
  await manager.locator('#location').fill('E2E floor');
  await manager.getByRole('button', { name: 'Create resource' }).click();
  await manager.waitForURL('**/resources');

  try {
    const own = manager.locator('tr', { hasText: name });
    await expect(own.locator('td.team')).toHaveText("Zeynep Kaya's team");
    await expect(own.getByRole('link', { name: 'Edit' })).toBeVisible();
    await expect(own.getByRole('button', { name: 'Deactivate' })).toBeVisible();

    const shared = manager.locator('tr', { hasText: resource.name });
    await expect(shared.locator('td.team')).toHaveText('Shared');
    await expect(shared.locator('td.actions a, td.actions button')).toHaveCount(0);
    await manager.goto(`/resources/${resource.id}/edit`);
    await expect(manager.getByText('Only an administrator can change this resource')).toBeVisible();

    // A member of the team sees it; the admin sees whose it is.
    const ayse = await open('ayse');
    await ayse.goto('/resources');
    await expect(ayse.locator('tr', { hasText: name })).toBeVisible();
  } finally {
    const all = (await (await admin.get('resources')).json()) as { id: string; name: string }[];
    const created = all.find((r) => r.name === name);
    if (created) await retire(admin, created.id);
  }
});
