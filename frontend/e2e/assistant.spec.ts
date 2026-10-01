import AxeBuilder from '@axe-core/playwright';
import { Page } from '@playwright/test';
import { TestResource, expect, signIn, slot, test } from './support';

// The assistant (ADR-0029). Its answers are made up here - a test never reaches the paid
// model, and the API needs no key - but a card's booking goes to the real API.

interface Answer {
  status?: number;
  body: unknown;
}

/**
 * Before sign-in, so the app sees it when it asks: this server has an assistant, which gives
 * these answers in turn. Returns what each question sent.
 */
async function withAssistant(page: Page, ...answers: Answer[]): Promise<unknown[]> {
  const asked: unknown[] = [];
  await page.route('**/api/assistant', (route) => route.fulfill({ json: { enabled: true } }));
  await page.route('**/api/assistant/messages', async (route) => {
    asked.push(route.request().postDataJSON());
    const answer = answers[Math.min(asked.length, answers.length) - 1];
    await route.fulfill({ status: answer.status ?? 200, json: answer.body });
  });
  return asked;
}

function offer(resource: TestResource, when: { start: Date; end: Date }): Answer {
  return {
    body: {
      reply: `One is free then:\n- ${resource.name}, E2E floor, 4 people\n\nThe card is ready.`,
      proposals: [
        {
          kind: 'book',
          resourceId: resource.id,
          reservationId: null,
          resourceName: resource.name,
          startDateTime: when.start.toISOString(),
          endDateTime: when.end.toISOString(),
          note: 'Planning',
        },
      ],
    },
  };
}

const busy: Answer = {
  status: 503,
  body: {
    title: 'Service unavailable',
    status: 503,
    detail: "The assistant can't answer right now. Try again in a moment.",
  },
};

test('a card the assistant offers books nothing until it is pressed', async ({
  visit,
  resource,
}) => {
  const when = slot(2, 11);
  const page = await visit();
  const asked = await withAssistant(page, offer(resource, when));
  await signIn(page, 'ayse');

  await page.getByRole('button', { name: 'Assistant', exact: true }).click();
  const panel = page.getByRole('dialog', { name: 'Booking assistant' });
  const message = panel.getByRole('textbox', { name: 'Message to the assistant' });
  await expect(message).toBeFocused();
  await message.fill('A room for 4 the day after tomorrow at 11');
  await message.press('Enter');

  await expect(panel.getByRole('listitem')).toHaveText(`${resource.name}, E2E floor, 4 people`);
  expect(asked).toEqual([
    {
      messages: [{ role: 'user', text: 'A room for 4 the day after tomorrow at 11' }],
      timeZone: expect.any(String),
    },
  ]);

  // On another screen the conversation is still open, and still nothing is booked.
  await page.locator('nav').getByRole('link', { name: 'My reservations' }).click();
  await page.waitForURL('**/reservations/mine');
  await expect(page.getByRole('heading', { name: 'My reservations' })).toBeVisible();
  await expect(page.locator('li.item', { hasText: resource.name })).toHaveCount(0);

  // The card's button books, and the list shows it without a reload.
  await panel.getByRole('button', { name: 'Reserve' }).click();
  await expect(panel.getByText(`Booked: ${resource.name}`)).toBeVisible();
  await expect(page.locator('li.item', { hasText: resource.name })).toBeVisible();
});

async function settle(page: Page): Promise<void> {
  await page.waitForFunction(() =>
    document
      .getAnimations()
      .every(
        (a) => a.playState !== 'running' || a.effect?.getComputedTiming().endTime === Infinity,
      ),
  );
}

async function axe(page: Page): Promise<string[]> {
  const { violations } = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'])
    .analyze();
  return violations.map((v) => `${v.id} x${v.nodes.length}: ${v.nodes[0]?.target.join(' ') ?? ''}`);
}

for (const theme of ['light', 'dark'] as const) {
  test(`the conversation, its error and its card pass axe in the ${theme} theme and fit a phone`, async ({
    visit,
    resource,
  }) => {
    const page = await visit('tr');
    await page.context().addInitScript((t) => localStorage.setItem('smartdesk_theme', t), theme);
    await withAssistant(page, busy, offer(resource, slot(3, 9)));
    await signIn(page, 'ayse');

    await page.locator('.launcher').click();
    const panel = page.locator('#assistant-panel');
    await panel.locator('textarea').fill('Yarın 4 kişilik oda');
    await panel.locator('textarea').press('Enter');
    await expect(panel.getByRole('alert')).toContainText("The assistant can't answer right now.");
    await settle(page);
    expect.soft(await axe(page), 'with an error').toEqual([]);

    await panel.getByRole('button', { name: 'Tekrar dene' }).click();
    await expect(panel.locator('app-assistant-card')).toBeVisible();
    await settle(page);
    expect.soft(await axe(page), 'with a card').toEqual([]);

    await page.setViewportSize({ width: 390, height: 844 });
    const [scrollWidth, clientWidth] = await page.evaluate(() => [
      document.documentElement.scrollWidth,
      document.documentElement.clientWidth,
    ]);
    expect.soft(scrollWidth, 'sideways scroll at 390px').toBeLessThanOrEqual(clientWidth);
    await expect(panel.getByRole('button', { name: 'Rezerve et' })).toBeInViewport();
  });
}
