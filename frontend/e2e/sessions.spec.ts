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

// ADR-0062: a sign-in names the browser in a cookie of its own, which signing out leaves, so the
// next sign-in there is one the account knows; the link in a new sign-in's notice opens the list.
test('a browser keeps its own token across sign-outs, and the notice link opens the device list', async ({
  open,
}) => {
  const page = await open('mehmet');
  const deviceCookie = async () =>
    (await page.context().cookies()).find((cookie) => cookie.name === 'smartdesk_device');
  const first = await deviceCookie();
  expect(first, "the browser's own token").toBeDefined();
  expect(first).toMatchObject({
    httpOnly: true,
    secure: true,
    sameSite: 'Strict',
    path: '/api/auth',
  });

  await page.locator('app-profile-menu .trigger').first().click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await page.waitForURL((url) => url.pathname === '/');
  expect((await deviceCookie())?.value, 'still there after signing out').toBe(first!.value);
  await signIn(page, 'mehmet');
  expect((await deviceCookie())?.value, 'the same browser, the same token').toBe(first!.value);

  // Where the notice's link leads, on a phone: the list, scrolled to, its heading focused.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/profile#sessions');
  const heading = page.getByRole('heading', { name: 'Signed-in devices' });
  await expect(heading).toBeFocused();
  await expect(heading).toBeInViewport();
  await expect(page.getByRole('region', { name: 'Signed-in devices' })).toContainText(
    "If your account is signed in on a device it hasn't been used on before, we email you",
  );
});
