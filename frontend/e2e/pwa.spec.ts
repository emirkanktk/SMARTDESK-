import { expect, test } from './support';

// The installed app (ADR-0054). Its service worker is in a production build only, so this runs
// through nginx (the Docker job) and skips itself under `ng serve`. Installable by Chrome's own
// check, and opening without a network, saying it's offline.
test('the app installs, and opens without a network saying so', async ({ visit }) => {
  test.slow();
  const page = await visit();
  const ngsw = await page.request.get('/ngsw.json');
  test.skip(
    !ngsw.ok() || !(await ngsw.text()).includes('"configVersion"'),
    'The service worker is in a production build only: run this through nginx.',
  );

  await page.goto('/login');
  await expect(page.locator('#email')).toBeVisible();
  // Registered once the app has settled, within 30 seconds, and in charge from the next load.
  await page.waitForFunction(
    async () => !!(await navigator.serviceWorker.getRegistration())?.active,
    null,
    { timeout: 45_000 },
  );
  await page.reload();
  await expect.poll(() => page.evaluate(() => !!navigator.serviceWorker.controller)).toBe(true);

  // Every reason Chrome would refuse to install it, but the test's own private window.
  const cdp = await page.context().newCDPSession(page);
  const { installabilityErrors } = await cdp.send('Page.getInstallabilityErrors');
  expect(
    installabilityErrors.map((error) => error.errorId).filter((id) => id !== 'in-incognito'),
  ).toEqual([]);

  await page.context().setOffline(true);
  await page.reload();
  await expect(page.locator('#email')).toBeVisible();

  // Playwright's offline mode cuts the network but leaves navigator.onLine as it was, so the
  // browser's own signal is sent by hand.
  await page.evaluate(() => window.dispatchEvent(new Event('offline')));
  await expect(page.getByText("You're offline.", { exact: false })).toBeVisible();

  await page.context().setOffline(false);
  await page.evaluate(() => window.dispatchEvent(new Event('online')));
  await expect(page.getByText("You're offline.", { exact: false })).toHaveCount(0);
});
