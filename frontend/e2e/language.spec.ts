import { expect, signIn, test } from './support';

// TR/EN (ADR-0025): Turkish on a first visit, English one click away, and the API asked in
// the language on screen.
test('a first visit is in Turkish, and English is remembered after a reload', async ({
  browser,
}) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  await page.goto('/login');
  await expect(page.locator('html')).toHaveAttribute('lang', 'tr');
  await expect(page).toHaveTitle('Giriş yap · SmartDesk');
  await expect(
    page.getByText('Toplantı odası, masa ve ekipman ayırmak için giriş yapın.'),
  ).toBeVisible();
  await expect(page.getByRole('button', { name: 'Türkçe' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );

  const turkishCall = page.waitForRequest((r) => r.url().includes('/api/dashboard'));
  await signIn(page, 'ayse');
  await expect(page.getByRole('heading', { name: /Hoş geldiniz/ })).toBeVisible();
  expect(await (await turkishCall).headerValue('accept-language')).toBe('tr');

  const englishCall = page.waitForRequest((r) => r.url().includes('/api/dashboard'));
  await page.getByRole('button', { name: 'English' }).click();
  await expect(page.getByRole('heading', { name: /Welcome/ })).toBeVisible();
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  expect(await (await englishCall).headerValue('accept-language')).toBe('en-US');

  await page.reload();
  await expect(page.getByRole('link', { name: 'My reservations' }).first()).toBeVisible();
  await expect(page).toHaveTitle('Dashboard · SmartDesk');

  await page.getByRole('button', { name: 'Türkçe' }).click();
  await expect(page.getByRole('heading', { name: /Hoş geldiniz/ })).toBeVisible();

  await context.close();
});

test('a wrong password is explained in the language on screen', async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();
  const tryWrongPassword = async () => {
    await page.locator('#email').fill('nobody@smartdesk.local');
    await page.locator('#password').fill('not-the-password');
    await page.locator('button[type=submit]').click();
  };

  await page.goto('/login');
  await tryWrongPassword();
  await expect(page.locator('.alert-error')).toHaveText('E-posta veya parola hatalı.');

  await page.getByRole('button', { name: 'English' }).click();
  await expect(page.getByText('Sign in to book meeting rooms, desks and equipment.')).toBeVisible();
  await tryWrongPassword();
  await expect(page.locator('.alert-error')).toHaveText('Invalid email or password.');

  await context.close();
});
