import { expect, test } from './support';

// Announcements (ADR-0065): an Admin posts one, seeing it as it will show while writing it. It is on
// everyone's dashboard at once, where each person can hide it for themselves; taken down, it's gone
// for everyone.
test("an announcement an admin posts is on everyone's dashboard until taken down", async ({
  open,
}) => {
  const admin = await open('admin');
  await admin.goto('/admin/announcements');
  const title = `Breakfast ${Date.now().toString(36)}`;
  await admin.getByLabel('Title').fill(title);
  await admin.getByLabel('Text').fill('Croissants on the 5th floor.');
  await admin.locator('label.tone', { hasText: 'Good news' }).click();
  const preview = admin.locator('.preview app-announcement-card');
  await expect(preview.getByRole('heading', { level: 3 })).toHaveText(title);
  await expect(preview).toHaveAttribute('data-tone', 'Good');
  await admin.getByRole('button', { name: 'Post it' }).click();
  await expect(admin.getByText("Posted: it's on everyone's dashboard now.")).toBeVisible();
  const row = admin.locator('.item').filter({ hasText: title });
  await expect(row).toContainText('showing, last day');

  const ayse = await open('ayse');
  const card = ayse.locator('app-announcement-card').filter({ hasText: title });
  await expect(card).toContainText('Good news');
  await expect(card).toContainText('Croissants on the 5th floor.');
  await card.getByRole('button', { name: `Hide ${title}` }).click();
  await expect(card).toHaveCount(0);
  const asked = ayse.waitForResponse((response) => response.url().endsWith('/announcements'));
  await ayse.reload();
  await asked;
  await expect(card, 'hidden for her on this device').toHaveCount(0);

  await admin.goto('/dashboard');
  const own = admin.locator('app-announcement-card').filter({ hasText: title });
  await expect(own, 'still there for everyone else').toBeVisible();

  await admin.goto('/admin/announcements');
  admin.once('dialog', (dialog) => void dialog.accept());
  await row.getByRole('button', { name: 'Take down' }).click();
  await expect(admin.getByText(`${title} was taken down.`)).toBeVisible();
  const askedAgain = admin.waitForResponse((response) => response.url().endsWith('/announcements'));
  await admin.goto('/dashboard');
  await askedAgain;
  await expect(own).toHaveCount(0);
});
