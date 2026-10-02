import { bookingLink, expect, slot, teamMember, test } from './support';

// The demo seed's names (ADR-0007).
const NAMES = { ayse: 'Ayşe Yılmaz', mehmet: 'Mehmet Demir' } as const;

// Inviting the team to a meeting, the inbox, and answering it (ADR-0032).
test('a manager invites the team, who answers from the inbox why they can not come', async ({
  open,
  resource,
}) => {
  const member = await teamMember();
  test.skip(!member, "no demo employee is in the demo manager's team");
  const name = NAMES[member as keyof typeof NAMES];
  const when = slot(2, 10);

  // The manager books and ticks a member of the team.
  const manager = await open('manager');
  await manager.goto(bookingLink(resource, when));
  await manager.locator('label.person', { hasText: name }).locator('input').check();
  await expect(manager.locator('.attendees .hint')).toContainText('1 invited.');
  await manager.getByRole('button', { name: 'Book', exact: true }).click();
  await manager.waitForURL('**/reservations/mine');
  await expect(manager.getByText('Your invitations were sent.')).toBeVisible();
  const booking = manager.locator('li.item', { hasText: resource.name });
  await expect(booking.locator('.attendees')).toHaveText(`No answer yet: ${name}`);

  // The invitation is in the member's inbox, with the room, and asks for an answer.
  const employee = await open(member!);
  await expect(
    employee.locator('.nav-link', { hasText: 'Inbox' }).locator('.nav-count'),
  ).toBeVisible();
  await employee.goto('/inbox');
  const invitation = employee.locator('li.message', { hasText: resource.name }).first();
  await expect(invitation.locator('.headline')).toContainText(
    'Zeynep Kaya invited you to a meeting',
  );
  await invitation.getByRole('button', { name: "I can't come" }).click();
  await invitation.getByLabel("Why can't you come?").fill('With a customer all morning');
  await invitation.getByRole('button', { name: 'Send' }).click();
  await expect(invitation).toContainText("You said you can't come.");

  // The organizer hears it, with the reason, and sees it on the booking.
  await expect(booking.locator('.attendees')).toHaveText(`Can't come: ${name}`);
  await manager.goto('/inbox');
  const answer = manager
    .locator('li.message', { hasText: `${name} can't come to your meeting` })
    .first();
  await expect(answer.locator('.note')).toHaveText('With a customer all morning');
});
