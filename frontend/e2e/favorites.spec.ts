import { apiAs, expect, test } from './support';

// Favourite resources (ADR-0051): starred on Resources, kept with the account, and booked from
// the dashboard, where the booking form has them first.
test('a resource starred on Resources is on the dashboard, and books from there', async ({
  open,
  resource,
}) => {
  const ayse = await open('ayse');
  try {
    await ayse.goto('/resources');
    const star = ayse.getByRole('button', { name: `${resource.name} in your favourites` });
    await expect(star).toHaveAttribute('aria-pressed', 'false');
    // On at once; the API is told after, so wait for it before reloading.
    const saved = ayse.waitForResponse(
      (r) => r.url().endsWith(`/favorites/${resource.id}`) && r.request().method() === 'PUT',
    );
    await star.click();
    await expect(star).toHaveAttribute('aria-pressed', 'true');
    expect((await saved).status()).toBe(204);

    // Kept with the account: still starred after a reload, and only the starred when asked.
    await ayse.reload();
    await expect(star).toHaveAttribute('aria-pressed', 'true');
    await ayse.getByLabel('Favourites only').check();
    await expect(ayse.locator('tbody tr')).toHaveCount(1);
    await expect(ayse.locator('tbody tr')).toContainText(resource.name);

    // On the dashboard: what it's doing now, and a booking with a click.
    await ayse.goto('/dashboard');
    const card = ayse.locator('section', {
      has: ayse.getByRole('heading', { name: 'Your favourites' }),
    });
    const row = card.locator('li', { hasText: resource.name });
    await expect(row).toContainText('Free for the rest of the day');
    await row.getByRole('link', { name: 'Book' }).click();

    await ayse.waitForURL(/\/reservations\/new/);
    await expect(ayse.locator('#resource')).toHaveValue(resource.id);
    await expect(ayse.locator('#resource optgroup').first()).toHaveAttribute(
      'label',
      'Your favourites',
    );

    // Unstarred on the dashboard: the card says how to have some again.
    await ayse.goto('/dashboard');
    await row.getByRole('button', { name: `${resource.name} in your favourites` }).click();
    await expect(card.locator('li', { hasText: resource.name })).toHaveCount(0);
  } finally {
    const api = await apiAs('ayse');
    await api.delete(`favorites/${resource.id}`);
    await api.dispose();
  }
});
