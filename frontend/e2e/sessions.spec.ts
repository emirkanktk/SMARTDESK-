import { acceptNextDialog, expect, noteRefused, signIn, test } from './support';

// The devices an account is signed in on (ADR-0061): a phone signed out from the profile on a
// laptop finds out over its live connection, and goes to the sign-in page without a tap.
const IPHONE =
  'Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.1 Mobile/15E148 Safari/604.1';

test('a phone signed out from the profile on a laptop goes to the sign-in page by itself', async ({
  browser,
  open,
}) => {
  const phoneContext = await browser.newContext({
    userAgent: IPHONE,
    viewport: { width: 390, height: 844 },
    isMobile: true,
    hasTouch: true,
  });
  const refused: string[] = [];
  await noteRefused(phoneContext, refused);
  await phoneContext.addInitScript(() => localStorage.setItem('smartdesk_language', 'en'));
  try {
    const phone = await phoneContext.newPage();
    await signIn(phone, 'mehmet');

    const laptop = await open('mehmet');
    await laptop.goto('/profile');
    const devices = laptop.getByRole('region', { name: 'Signed-in devices' });
    await expect(devices.getByRole('listitem').first()).toContainText('This device');
    // The phone signed in last, so of the account's other devices it's on top.
    const phoneRow = devices.getByRole('listitem').filter({ hasText: 'Safari · iPhone' }).first();
    await expect(phoneRow).toContainText('Signed in with the password');

    acceptNextDialog(laptop);
    await phoneRow.getByRole('button', { name: 'Sign out Safari · iPhone' }).click();
    await expect(laptop.getByText('Signed out of Safari · iPhone.')).toBeVisible();

    // Nothing is touched on the phone.
    await expect(phone).toHaveURL(/\/login/, { timeout: 20_000 });
    await expect(phone.getByText('Your session has expired. Please sign in again.')).toBeVisible();

    // The laptop goes on as it was.
    await laptop.reload();
    await expect(devices.getByRole('listitem').first()).toContainText('This device');
    expect(refused, "what the phone page's security policy refused").toEqual([]);
  } finally {
    await phoneContext.close();
  }
});
