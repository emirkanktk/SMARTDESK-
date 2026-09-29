import { defineConfig, devices } from '@playwright/test';

// Browser tests against a running app: the API (`dotnet run --launch-profile https`, with the
// Development seed) and `ng serve`. See "Browser tests" in the README.
const ci = !!process.env['CI'];

export default defineConfig({
  testDir: './e2e',
  // One at a time: several windows sign in per test, and sign-ins are rate-limited.
  workers: 1,
  retries: ci ? 1 : 0,
  forbidOnly: ci,
  reporter: ci ? [['list'], ['html', { open: 'never' }]] : 'list',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:4200',
    trace: 'retain-on-failure',
    // Well inside the test's minute, so a wait that fails still leaves time for the cleanup
    // in a test's `finally`.
    actionTimeout: 15_000,
    navigationTimeout: 15_000,
  },
  projects: [
    {
      name: 'edge',
      use: {
        ...devices['Desktop Edge'],
        // The Edge installed on the machine; CI sets E2E_CHANNEL='' to use Playwright's Chromium.
        channel: process.env['E2E_CHANNEL'] ?? 'msedge',
      },
    },
  ],
});
