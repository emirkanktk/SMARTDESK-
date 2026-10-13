import { readFile } from 'node:fs/promises';
import { expect, test } from './support';

// The usage report (ADR-0017, ADR-0044) and its Excel workbook (ADR-0045).
test('an admin sees when it is busiest, and downloads the report as an Excel workbook', async ({
  open,
}) => {
  const admin = await open('admin');
  await admin.goto('/admin/reports?from=2026-09-01&to=2026-09-30');

  await expect(admin.getByRole('heading', { name: "When it's busiest" })).toBeVisible();
  await expect(admin.locator('table.heat thead th')).toHaveText([
    'Hour',
    'Mon',
    'Tue',
    'Wed',
    'Thu',
    'Fri',
    'Sat',
    'Sun',
  ]);

  const downloading = admin.waitForEvent('download');
  await admin.getByRole('button', { name: 'Download as Excel' }).click();
  const download = await downloading;

  expect(download.suggestedFilename()).toBe('smartdesk-usage-2026-09-01-2026-09-30.xlsx');
  // An .xlsx is a zip: it starts with "PK".
  const file = await readFile((await download.path())!);
  expect(file.subarray(0, 2).toString()).toBe('PK');
});
