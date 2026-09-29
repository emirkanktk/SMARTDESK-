import { APIRequestContext } from '@playwright/test';
import { acceptNextDialog, expect, submitSignIn, test } from './support';

// Joining a team and temporary passwords (ADR-0020, ADR-0021). Each test makes its own
// account, and at the end takes it out of any team and disables it: a used account can't be
// deleted, and the demo manager's team stays as seeded.

const PASSWORD = 'E2e-Pass-123!';

const unique = () => Date.now().toString(36);

async function retireAccount(admin: APIRequestContext, email: string): Promise<void> {
  const found = (await (await admin.get('users', { params: { search: email } })).json()) as {
    id: string;
    email: string;
    status: string;
    managerId: string | null;
  }[];
  const account = found.find((a) => a.email === email);
  if (account?.status === 'PendingApproval') {
    // Still waiting (the test stopped before a decision): rejecting deletes it.
    expect((await admin.post(`users/${account.id}/reject`)).ok(), `reject ${email}`).toBe(true);
    return;
  }
  if (account?.managerId) {
    const out = await admin.patch(`users/${account.id}/manager`, { data: { managerId: null } });
    expect(out.ok(), `take ${email} out of the team`).toBe(true);
  }
  if (account?.status === 'Active') {
    expect((await admin.post(`users/${account.id}/disable`)).ok(), `disable ${email}`).toBe(true);
  }
}

test("a sign-up with the manager's email waits until the manager lets them in", async ({
  visit,
  open,
  admin,
}) => {
  const id = unique();
  const name = `E2E Member ${id}`;
  const email = `e2e-member-${id}@example.com`;

  try {
    const applicant = await visit();
    await applicant.goto('/register');
    await applicant.locator('#first-name').fill('E2E');
    await applicant.locator('#last-name').fill(`Member ${id}`);
    await applicant.locator('#email').fill(email);
    await applicant.locator('#manager-email').fill('manager@smartdesk.local');
    await applicant.locator('#password').fill(PASSWORD);
    await applicant.locator('#confirm').fill(PASSWORD);
    await applicant.getByRole('button', { name: 'Sign up' }).click();
    await expect(applicant.getByRole('heading', { name: 'Check your email' })).toBeVisible();
    await expect(applicant.getByText('waiting for approval')).toBeVisible();

    await submitSignIn(applicant, email, PASSWORD);
    await expect(applicant.locator('.alert-error')).toContainText('waiting for approval');

    const manager = await open('manager');
    await manager.goto('/team');
    const request = manager.locator('.request-list li', { hasText: name });
    await request.getByRole('button', { name: 'Approve' }).click();
    await expect(manager.getByText(`${name} joined your team.`)).toBeVisible();
    await expect(manager.locator('table tr', { hasText: name })).toBeVisible();

    await submitSignIn(applicant, email, PASSWORD);
    await applicant.waitForURL('**/dashboard');
  } finally {
    await retireAccount(admin, email);
  }
});

test('a manager can turn a request down, and it is gone', async ({ visit, open, admin }) => {
  const id = unique();
  const name = `E2E Rejected ${id}`;
  const email = `e2e-rejected-${id}@example.com`;
  const applied = await admin.post('auth/register', {
    data: {
      firstName: 'E2E',
      lastName: `Rejected ${id}`,
      email,
      password: PASSWORD,
      managerEmail: 'manager@smartdesk.local',
    },
  });
  expect(applied.status()).toBe(202);

  const manager = await open('manager');
  await manager.goto('/team');
  const request = manager.locator('.request-list li', { hasText: name });
  acceptNextDialog(manager);
  await request.getByRole('button', { name: 'Reject' }).click();
  await expect(manager.getByText(`${name}'s request was rejected.`)).toBeVisible();
  await expect(request).toHaveCount(0);

  // Deleted, so the address can sign up again; signing in with it just fails.
  const applicant = await visit();
  await submitSignIn(applicant, email, PASSWORD);
  await expect(applicant.locator('.alert-error')).toHaveText('Invalid email or password.');
});

test('a temporary password has to be replaced before anything else opens', async ({
  visit,
  admin,
}) => {
  const id = unique();
  const email = `e2e-temp-${id}@example.com`;
  const temporary = 'Temp-Pass-123!';
  const created = await admin.post('users', {
    data: {
      firstName: 'E2E',
      lastName: `Temp ${id}`,
      email,
      role: 'Employee',
      password: temporary,
      managerId: null,
    },
  });
  expect(created.status()).toBe(201);

  try {
    const page = await visit();
    await submitSignIn(page, email, temporary);
    await page.waitForURL('**/change-password');
    await expect(page.getByRole('heading', { name: 'Choose your own password' })).toBeVisible();

    await page.goto('/calendar');
    await page.waitForURL('**/change-password');

    await page.locator('#current-password').fill(temporary);
    await page.locator('#new-password').fill(PASSWORD);
    await page.locator('#confirm').fill(PASSWORD);
    await page.getByRole('button', { name: 'Change password' }).click();
    await page.waitForURL('**/dashboard');

    await page.goto('/calendar');
    await expect(page).toHaveURL(/\/calendar/);
  } finally {
    await retireAccount(admin, email);
  }
});
