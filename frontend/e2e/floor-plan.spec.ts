import { Page } from '@playwright/test';
import { expect, retire, test, TestResource } from './support';

/** A drawn floor - walls and rooms - as PNG bytes, the way a drawing tool exports one. */
async function drawnPlan(page: Page): Promise<Buffer> {
  const bytes = await page.evaluate(async () => {
    const canvas = document.createElement('canvas');
    canvas.width = 1200;
    canvas.height = 700;
    const g = canvas.getContext('2d')!;
    g.fillStyle = '#ffffff';
    g.fillRect(0, 0, canvas.width, canvas.height);
    g.strokeStyle = '#334155';
    g.lineWidth = 6;
    g.strokeRect(10, 10, 1180, 680);
    g.strokeRect(10, 10, 400, 300);
    g.strokeRect(410, 10, 400, 300);
    const blob = await new Promise<Blob>((resolve) =>
      canvas.toBlob((b) => resolve(b!), 'image/png'),
    );
    return Array.from(new Uint8Array(await blob.arrayBuffer()));
  });
  return Buffer.from(bytes);
}

/** The floor plan page for one floor. */
const floorPage = (location: string) => `/floor-plan?floor=${encodeURIComponent(location)}`;

// The floor plan (ADR-0041): an Admin uploads a floor's plan and places a resource on it; an
// employee sees it free, sees it taken the moment someone books it, and books it from there.
test('an Admin draws up a floor, and an employee sees it fill up live', async ({ admin, open }) => {
  const location = `E2E plan ${Date.now().toString(36).toUpperCase()}`;
  const types = (await (await admin.get('resource-types')).json()) as { id: string }[];
  const created = await admin.post('resources', {
    data: {
      name: `E2E spot ${Date.now().toString(36).toUpperCase()}`,
      resourceTypeId: types[0].id,
      capacity: 4,
      location,
      features: [],
      teamIds: [],
    },
  });
  expect(created.status(), 'create the resource').toBe(201);
  const resource = (await created.json()) as TestResource;
  const positionOf = async () =>
    (
      (await (await admin.get(`resources/${resource.id}`)).json()) as {
        position: { x: number; y: number } | null;
      }
    ).position;

  try {
    // No plan yet: the floor is a grid, and the resource waits to be placed.
    const page = await open('admin');
    await page.goto(floorPage(location));
    await expect(
      page.getByText(`${location} has no plan yet: choose Arrange to upload one.`, {
        exact: false,
      }),
    ).toBeVisible();

    await page.getByRole('button', { name: 'Arrange' }).click();
    await page.locator('.file-button input[type=file]').setInputFiles({
      name: 'plan.png',
      mimeType: 'image/png',
      buffer: await drawnPlan(page),
    });
    await expect(page.getByRole('img', { name: `Plan of ${location}` })).toBeVisible();

    // Put on the plan, in its middle, then dragged to the top left room.
    const saved = () => page.waitForResponse((r) => r.url().endsWith('/position') && r.ok());
    let save = saved();
    await page
      .locator('.movable li', { hasText: resource.name })
      .getByRole('button', { name: 'Put on the plan' })
      .click();
    await save;
    const handle = page.getByRole('button', {
      name: `Move ${resource.name}: drag it, or use the arrow keys`,
    });
    await expect(handle).toBeFocused();

    const plan = (await page.locator('.plan').boundingBox())!;
    const from = (await handle.boundingBox())!;
    save = saved();
    await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
    await page.mouse.down();
    await page.mouse.move(plan.x + plan.width * 0.2, plan.y + plan.height * 0.3, { steps: 8 });
    await page.mouse.up();
    await save;
    let position = (await positionOf())!;
    expect(position.x).toBeCloseTo(0.2, 2);
    expect(position.y).toBeCloseTo(0.3, 2);

    // A step to the right with the keyboard.
    save = saved();
    await handle.press('ArrowRight');
    await save;
    position = (await positionOf())!;
    expect(position.x).toBeCloseTo(0.21, 2);

    await page.getByRole('button', { name: 'Done arranging' }).click();
    await expect(
      page.getByRole('link', { name: `${resource.name} is free for the rest of the day.` }),
    ).toBeVisible();

    // An employee sees it free, and taken as soon as someone books it.
    const ayse = await open('ayse');
    await ayse.goto(floorPage(location));
    await expect(ayse.getByRole('img', { name: `Plan of ${location}` })).toBeVisible();
    const spot = ayse.locator('.plan .spot', { hasText: resource.name });
    await expect(spot).toHaveClass(/\bfree\b/);

    const booked = await admin.post('reservations', {
      data: {
        resourceId: resource.id,
        startDateTime: new Date(Date.now() - 60_000).toISOString(),
        endDateTime: new Date(Date.now() + 59 * 60_000).toISOString(),
        note: null,
        reminderMinutes: null,
      },
    });
    expect(booked.status(), 'book it elsewhere').toBe(201);
    await expect(spot).toHaveClass(/\bbusy\b/);
    // "until 10:59", or near midnight "until Tue 13 Oct, 00:29".
    await expect(spot).toHaveAttribute('aria-label', /is in use until .*\d\d:\d\d\.$/);

    // From there, the booking form for when it's free again.
    await spot.click();
    await expect(ayse).toHaveURL(
      new RegExp(`/reservations/new\\?resourceId=${resource.id}&start=`),
    );
    await expect(ayse.locator('#resource')).toHaveValue(resource.id);

    // The Admin sees how much each place was used instead (ADR-0059), that booking counted. Its
    // share depends on the office's hours it falls in, so only the step is checked.
    await page.getByRole('button', { name: 'Usage' }).click();
    await expect(page).toHaveURL(/view=usage/);
    const used = page.locator('.plan .spot.usage', { hasText: resource.name });
    await expect(used).toHaveClass(/\bheat-level-[0-4]\b/);
    await expect(used).toHaveAttribute(
      'aria-label',
      new RegExp(
        `^${resource.name} - last 30 days: .+ of office hours booked, 1 bookings, 0 no-shows\\.$`,
      ),
    );
  } finally {
    await admin.delete('floor-plans/image', { params: { location } });
    await retire(admin, resource.id);
  }
});
