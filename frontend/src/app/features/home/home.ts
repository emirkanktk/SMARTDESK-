import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';

interface Feature {
  title: string;
  text: string;
}

/**
 * The public front page at "/": what SmartDesk does and a way to sign in. Signed-in users
 * never see it - guestGuard sends them straight to their dashboard.
 */
@Component({
  imports: [RouterLink],
  selector: 'app-home',
  styleUrl: './home.scss',
  templateUrl: './home.html',
})
export class Home {
  protected readonly year = new Date().getFullYear();

  // The seeded demo accounts exist only in Development (ADR-0007), so they're only listed
  // there; a production build leaves the box out.
  protected readonly showDemoAccounts = !environment.production;
  protected readonly demoAccounts = [
    { email: 'employee1@smartdesk.local', role: 'Employee' },
    { email: 'employee2@smartdesk.local', role: 'Employee' },
    { email: 'admin@smartdesk.local', role: 'Admin' },
  ];
  protected readonly demoPassword = 'SmartDesk123!';

  protected readonly features: readonly Feature[] = [
    {
      title: 'Find what’s free',
      text: 'Search by time, type and capacity, and see only the resources you can book right then.',
    },
    {
      title: 'No double bookings',
      text: 'The server refuses overlapping reservations, even when two people click at the same moment.',
    },
    {
      title: 'A live office calendar',
      text: 'New bookings and cancellations appear on everyone’s screen straight away, without a reload.',
    },
    {
      title: 'Reminders',
      text: 'Pick 5 to 60 minutes before a reservation starts and get a nudge in the app or on your desktop.',
    },
    {
      title: 'Your day at a glance',
      text: 'The dashboard shows today’s bookings, what’s in use right now and your next reservations.',
    },
    {
      title: 'Tools for admins',
      text: 'Manage resources, block maintenance windows, handle anyone’s bookings and read usage reports.',
    },
  ];

  protected readonly steps: readonly Feature[] = [
    { title: 'Sign in', text: 'Use the account your office gave you.' },
    {
      title: 'Pick a time',
      text: 'Choose when, and a meeting room, desk, car or piece of equipment that’s free.',
    },
    { title: 'Book it', text: 'It’s yours - and everyone else sees the slot taken right away.' },
  ];
}
