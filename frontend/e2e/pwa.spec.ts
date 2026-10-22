import { Page } from '@playwright/test';
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
  await workerActive(page);
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

// The number on the installed app's icon (ADR-0058): SmartDesk's own worker, around Angular's, sets
// it from each notification. Here the notifications come as DevTools sends them, and the worker's
// Badging API is a stand-in that notes what it's given: a browser test has no icon to look at.
test("each notification puts its owner's unread count on the app's icon", async ({ visit }) => {
  test.slow();
  const page = await visit();
  const ngsw = await page.request.get('/ngsw.json');
  test.skip(
    !ngsw.ok() || !(await ngsw.text()).includes('"configVersion"'),
    'The service worker is in a production build only: run this through nginx.',
  );

  const cdp = await page.context().newCDPSession(page);
  const registrationId = new Promise<string>((resolve) =>
    cdp.on('ServiceWorker.workerRegistrationUpdated', ({ registrations }) => {
      const live = registrations.find((r) => !r.isDeleted);
      if (live) resolve(live.registrationId);
    }),
  );
  await cdp.send('ServiceWorker.enable');
  await page.goto('/login');
  const origin = new URL(page.url()).origin;
  await page.context().grantPermissions(['notifications'], { origin });
  await workerActive(page);
  const ours = (url: string) => url.endsWith('/smartdesk-worker.js');
  const worker =
    page
      .context()
      .serviceWorkers()
      .find((w) => ours(w.url())) ??
    (await page.context().waitForEvent('serviceworker', { predicate: (w) => ours(w.url()) }));

  await worker.evaluate(() => {
    const scope = self as unknown as { badges: (number | 'cleared')[]; navigator: object };
    scope.badges = [];
    Object.defineProperties(scope.navigator, {
      setAppBadge: {
        configurable: true,
        value: async (count: number) => void scope.badges.push(count),
      },
      clearAppBadge: { configurable: true, value: async () => void scope.badges.push('cleared') },
    });
  });
  // Angular's worker passes each push on to the pages it controls, as SwPush hears it, before it
  // shows the notification: proof it still gets them, where notifications themselves can't be
  // seen (headless Chromium on Linux shows none).
  await expect.poll(() => page.evaluate(() => !!navigator.serviceWorker.controller)).toBe(true);
  await page.evaluate(() => {
    const window_ = window as unknown as { pushes: unknown[] };
    window_.pushes = [];
    navigator.serviceWorker.addEventListener('message', (event) => {
      if (event.data?.type === 'PUSH') window_.pushes.push(event.data.data.badge);
    });
  });
  const deliver = async (badge: number) =>
    cdp.send('ServiceWorker.deliverPushMessage', {
      origin,
      registrationId: await registrationId,
      data: JSON.stringify({
        notification: { title: 'SmartDesk', body: `${badge} unread`, tag: `unread-${badge}` },
        badge,
      }),
    });

  await deliver(3);
  await expect
    .poll(() => worker.evaluate(() => (self as unknown as { badges: unknown[] }).badges))
    .toEqual([3]);
  // Everything read: the next one takes the number off.
  await deliver(0);
  await expect
    .poll(() => worker.evaluate(() => (self as unknown as { badges: unknown[] }).badges))
    .toEqual([3, 'cleared']);

  await expect
    .poll(() => page.evaluate(() => (window as unknown as { pushes: unknown[] }).pushes))
    .toEqual([3, 0]);
});

/** Registered once the app has settled, within 30 seconds, and active: the page asks until it is. */
async function workerActive(page: Page): Promise<void> {
  await expect
    .poll(
      () => page.evaluate(async () => !!(await navigator.serviceWorker.getRegistration())?.active),
      { timeout: 45_000 },
    )
    .toBe(true);
}
