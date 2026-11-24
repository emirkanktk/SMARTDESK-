import { APIRequestContext } from '@playwright/test';
import { expect, noteRefused, test } from './support';

// What the app's pages let run, and what the server sends with each of its files (ADR-0060).

/** nginx and serve-build send the headers; `ng serve`, which has no service worker, doesn't. */
async function servedAsBuilt(request: APIRequestContext): Promise<boolean> {
  const ngsw = await request.get('/ngsw.json');
  return ngsw.ok() && (await ngsw.text()).includes('"configVersion"');
}

test('a script slipped into the page does not run, nor does a handler written into it', async ({
  browser,
}) => {
  const context = await browser.newContext();
  const refused: string[] = [];
  await noteRefused(context, refused);
  const page = await context.newPage();

  // The app's own scripts run, the theme's inline one too: nothing is refused on the way.
  await page.goto('/login');
  await expect(page.locator('#email')).toBeVisible();
  await expect(page.locator('html')).toHaveAttribute('data-theme', /^(light|dark)$/);
  expect(refused).toEqual([]);

  const ran = await page.evaluate(() => {
    const script = document.createElement('script');
    script.textContent = 'window.slippedIn = true';
    document.head.append(script);
    const button = document.createElement('button');
    button.setAttribute('onclick', 'window.slippedIn = true');
    document.body.append(button);
    button.click();
    button.remove();
    return 'slippedIn' in window;
  });
  expect(ran).toBe(false);
  await expect
    .poll(() => refused)
    .toEqual([
      'script-src-elem refused inline on /login',
      'script-src-attr refused inline on /login',
    ]);

  await context.close();
});

test("each of the app's files comes with the headers that keep it to itself", async ({
  request,
}) => {
  test.skip(
    !(await servedAsBuilt(request)),
    'ng serve sends no headers of its own: run this through nginx or serve-build.',
  );

  const page = await (await request.get('/')).text();
  const main = page.match(/src="(main-[\w-]+\.js)"/)?.[1];
  expect(main, 'the main script on the page').toBeTruthy();

  for (const path of [
    '/',
    '/reservations/mine',
    `/${main}`,
    '/smartdesk-worker.js',
    '/manifest.webmanifest',
    '/i18n/tr.json',
  ]) {
    const response = await request.get(path);
    expect(response.ok(), path).toBe(true);
    expect(response.headers(), path).toMatchObject({
      'content-security-policy': "frame-ancestors 'none'",
      'x-frame-options': 'DENY',
      'x-content-type-options': 'nosniff',
      'referrer-policy': 'same-origin',
      'cross-origin-opener-policy': 'same-origin',
      'permissions-policy': 'camera=(), microphone=(), geolocation=(), payment=(), usb=()',
    });
  }
});

test('another site can not show the app in a frame of its own', async ({
  browser,
  request,
  baseURL,
}) => {
  test.skip(
    !(await servedAsBuilt(request)),
    'ng serve sends no headers of its own: run this through nginx or serve-build.',
  );

  const context = await browser.newContext();
  const page = await context.newPage();
  const refusal = page.waitForEvent('console', {
    predicate: (message) => message.text().includes("frame-ancestors 'none'"),
  });
  // A page of no site at all, framing the app's sign-in: what a clickjacking page would do.
  await page.setContent(`<iframe src="${baseURL}/login" width="600" height="400"></iframe>`);

  // The browser refuses, saying why, and the frame stays empty. Without the header, the sign-in
  // form would be in it.
  expect((await refusal).text()).toContain('blocked');
  await expect(page.frameLocator('iframe').locator('#email')).toHaveCount(0);

  await context.close();
});
