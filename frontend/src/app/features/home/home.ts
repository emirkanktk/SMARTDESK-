import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LanguageSwitch } from '../../shared/components/language-switch/language-switch';
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
  imports: [RouterLink, LanguageSwitch],
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
    { email: 'employee1@smartdesk.local', role: $localize`:@@role.employee:Employee` },
    { email: 'employee2@smartdesk.local', role: $localize`:@@role.employee:Employee` },
    { email: 'manager@smartdesk.local', role: $localize`:@@role.manager:Manager` },
    { email: 'admin@smartdesk.local', role: $localize`:@@role.admin:Admin` },
  ];
  protected readonly demoPassword = 'SmartDesk123!';

  protected readonly features: readonly Feature[] = [
    {
      title: $localize`:@@home.feature.free.title:Find what’s free`,
      text: $localize`:@@home.feature.free.text:Search by time, type and capacity, and see only the resources you can book right then.`,
    },
    {
      title: $localize`:@@home.feature.noDouble.title:No double bookings`,
      text: $localize`:@@home.feature.noDouble.text:The server refuses overlapping reservations, even when two people click at the same moment.`,
    },
    {
      title: $localize`:@@home.feature.live.title:A live office calendar`,
      text: $localize`:@@home.feature.live.text:New bookings and cancellations appear on everyone’s screen straight away, without a reload.`,
    },
    {
      title: $localize`:@@home.feature.reminders.title:Reminders`,
      text: $localize`:@@home.feature.reminders.text:Pick 5 to 60 minutes before a reservation starts and get a nudge in the app or on your desktop.`,
    },
    {
      title: $localize`:@@home.feature.day.title:Your day at a glance`,
      text: $localize`:@@home.feature.day.text:The dashboard shows today’s bookings, what’s in use right now and your next reservations.`,
    },
    {
      title: $localize`:@@home.feature.admin.title:Tools for admins`,
      text: $localize`:@@home.feature.admin.text:Manage resources, block maintenance windows, handle anyone’s bookings and read usage reports.`,
    },
  ];

  protected readonly steps: readonly Feature[] = [
    {
      title: $localize`:@@home.step.signIn.title:Sign in`,
      text: $localize`:@@home.step.signIn.text:Use the account your office gave you.`,
    },
    {
      title: $localize`:@@home.step.pick.title:Pick a time`,
      text: $localize`:@@home.step.pick.text:Choose when, and a meeting room, desk, car or piece of equipment that’s free.`,
    },
    {
      title: $localize`:@@home.step.book.title:Book it`,
      text: $localize`:@@home.step.book.text:It’s yours - and everyone else sees the slot taken right away.`,
    },
  ];
}
