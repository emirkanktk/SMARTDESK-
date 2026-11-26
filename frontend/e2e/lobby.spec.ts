import { expect, test } from './support';

// Lobby screens (ADR-0064): an Admin sets one up and opens its link on a screen nobody is signed
// in on; it shows the office's rooms, its free desks and how many are in, with no names. Taken
// down, the screen says so.
test('a lobby screen an admin sets up shows the office without a sign-in, until taken down', async ({
  open,
  visit,
}) => {
  const admin = await open('admin');
  await admin.goto('/admin/lobby-screens');
  const name = `Lobby ${Date.now().toString(36)}`;
  await admin.getByLabel('Name').fill(name);
  await admin.getByRole('button', { name: 'Set up the screen' }).click();
  const link = new URL(
    await admin.getByRole('textbox', { name: "The screen's link" }).inputValue(),
  );
  expect(link.pathname).toBe('/lobby');
  expect(link.hash).toMatch(/^#[0-9a-f]{64}$/);

  const screen = await visit();
  await screen.goto(`/lobby${link.hash}`);
  await expect(screen.getByRole('heading', { level: 1 })).toHaveText(name);
  await expect(screen.getByRole('heading', { name: /Meeting rooms/ })).toBeVisible();
  await expect(screen.locator('.room').first()).toBeVisible();
  await expect(screen.locator('.people-label')).toContainText('in the office today');
  expect(new URL(screen.url()).hash, 'the token is out of the address').toBe('');

  const row = admin.locator('.screen').filter({ hasText: name });
  await expect(row).toBeVisible();
  admin.once('dialog', (dialog) => void dialog.accept());
  await row.getByRole('button', { name: 'Take down' }).click();
  await expect(admin.getByText(`${name} was taken down.`)).toBeVisible();

  // The screen asks again every half minute; a reload asks at once.
  await screen.reload();
  await expect(screen.getByRole('heading', { name: "This screen isn't set up" })).toBeVisible();
});
