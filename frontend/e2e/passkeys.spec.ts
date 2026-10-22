import { APIRequestContext, Page, request } from '@playwright/test';
import { API_URL, acceptNextDialog, expect, submitSignIn, test } from './support';

// Passkeys (ADR-0057), with the browser's virtual authenticator standing in for Face ID or
// Windows Hello: one added from the profile with the password signs in with a single tap, no
// email or password, and once removed it signs nobody in. The test makes an account of its own
// and disables it at the end: a used account can't be deleted.

const TEMPORARY = 'E2e-Temp-123!';
const PASSWORD = 'E2e-Pass-123!';

/**
 * A device's own authenticator that keeps its passkeys and verifies the person at once, as Face ID
 * does when looked at. It lives as long as the page, across navigations.
 */
async function addAuthenticator(page: Page): Promise<void> {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: {
      protocol: 'ctap2',
      transport: 'internal',
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  });
}

/** An account an Admin added, whose owner has since chosen their own password. */
async function newAccount(admin: APIRequestContext, email: string): Promise<string> {
  const created = await admin.post('users', {
    data: { firstName: 'E2E', lastName: 'Passkey', email, role: 'Employee', password: TEMPORARY },
  });
  expect(created.status(), `create ${email}`).toBe(201);

  const owner = await request.newContext({ baseURL: `${API_URL}/`, ignoreHTTPSErrors: true });
  const login = await owner.post('auth/login', { data: { email, password: TEMPORARY } });
  const { accessToken } = (await login.json()) as { accessToken: string };
  const changed = await owner.post('auth/change-password', {
    data: { currentPassword: TEMPORARY, newPassword: PASSWORD },
    headers: { Authorization: `Bearer ${accessToken}` },
  });
  expect(changed.ok(), `choose ${email}'s own password`).toBe(true);
  await owner.dispose();
  return ((await created.json()) as { id: string }).id;
}

async function signOut(page: Page): Promise<void> {
  await page.locator('app-profile-menu .trigger').first().click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await page.waitForURL((url) => url.pathname === '/');
}

test('a passkey added on the profile signs in without a password, until it is removed', async ({
  visit,
  admin,
}) => {
  const email = `e2e-passkey-${Date.now().toString(36)}@example.com`;
  const id = await newAccount(admin, email);

  try {
    const page = await visit();
    await addAuthenticator(page);
    await submitSignIn(page, email, PASSWORD);
    await page.waitForURL('**/dashboard');

    await page.goto('/profile');
    const passkeys = page.locator('app-passkeys');
    await expect(passkeys.getByText('You have no passkeys yet.')).toBeVisible();
    await passkeys.locator('#passkey-name').fill('E2E key');
    await passkeys.locator('#passkey-password').fill(PASSWORD);
    await passkeys.getByRole('button', { name: 'Add a passkey' }).click();
    await expect(passkeys.locator('.item .name')).toHaveText(['E2E key']);
    await expect(page.getByText('Passkey added.')).toBeVisible();

    await signOut(page);
    await page.goto('/login');
    await page.getByRole('button', { name: 'Sign in with a passkey' }).click();
    await page.waitForURL('**/dashboard');
    await expect(page.locator('app-profile-menu').first()).toContainText('E2E Passkey');

    await page.goto('/profile');
    acceptNextDialog(page);
    await passkeys.getByRole('button', { name: 'Remove E2E key' }).click();
    await expect(passkeys.getByText('You have no passkeys yet.')).toBeVisible();

    // Still on the device, but no longer on the account.
    await signOut(page);
    await page.goto('/login');
    await page.getByRole('button', { name: 'Sign in with a passkey' }).click();
    await expect(page.locator('.alert-error')).toContainText("isn't on a SmartDesk account any more");
    await expect(page).toHaveURL(/\/login/);
  } finally {
    expect((await admin.post(`users/${id}/disable`)).ok(), `disable ${email}`).toBe(true);
  }
});
