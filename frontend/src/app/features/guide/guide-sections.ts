import { Role, Roles } from '../../core/constants/roles';
import { CHECK_IN, EXTENSION_OPTIONS } from '../../core/models/reservation.model';
import { IconName } from '../../shared/components/icon/icon';

/** How long before its end a booking under way asks whether it needs longer (ADR-0033). */
export const ASKED_BEFORE_END_MINUTES = 10;

/** One part of the guide: what to do, step by step, and where. */
export interface GuideSection {
  id: string;
  icon: IconName;
  title: string;
  steps: readonly string[];
  /** The page where it's done. */
  link?: { path: string; label: string };
  /** Only for these roles; for everyone when absent. */
  roles?: readonly Role[];
  /** Only when the server has the assistant (ADR-0029). */
  needsAssistant?: boolean;
}

/** Everything the app does, in the order a new user needs it. */
export const GUIDE_SECTIONS: readonly GuideSection[] = [
  {
    id: 'find',
    icon: 'search',
    title: $localize`:@@guide.find.title:Find a free resource`,
    steps: [
      $localize`:@@guide.find.when:Open Availability and say when you need it, and what kind of resource.`,
      $localize`:@@guide.find.listed:Only what's free for the whole time is listed, with its capacity, features and photo.`,
      $localize`:@@guide.find.book:Book opens the booking form, already filled in.`,
    ],
    link: { path: '/availability', label: $localize`:@@guide.find.link:Find a free resource` },
  },
  {
    id: 'floor',
    icon: 'map',
    title: $localize`:@@guide.floor.title:See a floor's plan`,
    steps: [
      $localize`:@@guide.floor.colours:Floor plan shows a floor with its resources where they are: green is free, red in use, grey out of use, changing as bookings are made.`,
      $localize`:@@guide.floor.book:Choose one to book it from when it's free. Pick a time to see the floor as it is then.`,
      $localize`:@@guide.floor.arrange:Admins upload each floor's plan, and they and team managers put the resources in place with Arrange.`,
    ],
    link: { path: '/floor-plan', label: $localize`:@@guide.floor.link:Open the floor plan` },
  },
  {
    id: 'book',
    icon: 'calendar-plus',
    title: $localize`:@@guide.book.title:Book it`,
    steps: [
      $localize`:@@guide.book.form:Check the times, add a note if you like, and choose a reminder.`,
      $localize`:@@guide.book.repeat:To repeat it, choose how often and the last day. The dates are listed first, any that aren't free with why, and booking skips those.`,
      $localize`:@@guide.book.taken:If it's taken, you're told so, with the nearest free times and what else is free then: choose one, and book again.`,
      $localize`:@@guide.book.wait:Or join the waiting list: if the time frees up, it's booked for you, and your inbox says so.`,
    ],
    link: { path: '/reservations/new', label: $localize`:@@guide.book.link:Book a resource` },
  },
  {
    id: 'door',
    icon: 'map-pin',
    title: $localize`:@@guide.door.title:Check in at the door`,
    // Read when shown: the times are the API's, and its grace is a setting (ADR-0040).
    get steps() {
      const { opensBeforeMinutes, graceMinutes, warnBeforeReleaseMinutes } = CHECK_IN();
      return [
        $localize`:@@guide.door.window:Some rooms ask for a check-in: scan the QR code at the door from ${opensBeforeMinutes}:before: minutes before the start until ${graceMinutes}:after: minutes after.`,
        $localize`:@@guide.door.released:If nobody checks in by then, the booking is released so others can use the room. Everyone on it is warned ${warnBeforeReleaseMinutes}:minutes: minutes before.`,
        $localize`:@@guide.door.screen:A room with a screen at the door shows a code that changes every 30 seconds: scan the screen itself, not a photo of it.`,
        $localize`:@@guide.door.end:Done early? End the booking from the check-in page, and the room is free again.`,
      ];
    },
  },
  {
    id: 'problem',
    icon: 'wrench',
    title: $localize`:@@guide.problem.title:Report a problem`,
    steps: [
      $localize`:@@guide.problem.where:Something wrong with a room or anything else you book? Scan the QR code at its door and press Report a problem, booked or not.`,
      $localize`:@@guide.problem.what:Say what it's about and write a few words. A photo from your phone helps, and tick it if it can't be used meanwhile.`,
      $localize`:@@guide.problem.once:The problems already reported there are listed first, and the same one isn't sent twice. Your inbox tells you when it's fixed.`,
    ],
  },
  {
    id: 'meetings',
    icon: 'team',
    title: $localize`:@@guide.meetings.title:Meetings and your inbox`,
    steps: [
      $localize`:@@guide.meetings.invite:Team managers and admins can invite people when they book; each gets a message in their inbox.`,
      $localize`:@@guide.meetings.answer:Answer from the inbox: coming, or not and why. Only the organizer sees the reason.`,
      $localize`:@@guide.meetings.news:If a meeting moves or is cancelled, everyone invited is told. A meeting that repeats is one message, answered once for all its dates.`,
    ],
    link: { path: '/inbox', label: $localize`:@@guide.meetings.link:Open your inbox` },
  },
  {
    id: 'longer',
    icon: 'clock',
    title: $localize`:@@guide.longer.title:Running over`,
    steps: [
      $localize`:@@guide.longer.asked:${ASKED_BEFORE_END_MINUTES}:minutes: minutes before a booking ends, you're asked whether you need longer.`,
      $localize`:@@guide.longer.extend:Make it run ${EXTENSION_OPTIONS[0]}:short:, ${EXTENSION_OPTIONS[1]}:medium: or ${EXTENSION_OPTIONS[2]}:long: minutes longer with a click, as far as the room is free. My reservations has the same for a booking under way.`,
    ],
  },
  {
    id: 'mine',
    icon: 'clipboard',
    title: $localize`:@@guide.mine.title:Your reservations`,
    steps: [
      $localize`:@@guide.mine.tabs:My reservations has what's coming, what's past, what was cancelled, and the meetings you're invited to.`,
      $localize`:@@guide.mine.change:Change or cancel a booking that hasn't started. For one that repeats, cancel a single date, or that date and every later one.`,
      $localize`:@@guide.mine.calendar:Put a booking in your own calendar with Add to calendar. Or make a calendar link on your profile: your calendar app then keeps up with all of them.`,
    ],
    link: { path: '/reservations/mine', label: $localize`:@@guide.mine.link:My reservations` },
  },
  {
    id: 'team',
    icon: 'team',
    title: $localize`:@@guide.team.title:Your team`,
    steps: [
      $localize`:@@guide.team.requests:People who sign up with your email ask to join your team: approve them, or turn them down, in My team.`,
      $localize`:@@guide.team.add:Add members yourself with a temporary password; they choose their own when they first sign in.`,
      $localize`:@@guide.team.resources:A resource you add belongs to your team: only your team sees and books it.`,
    ],
    link: { path: '/team', label: $localize`:@@guide.team.link:My team` },
    roles: [Roles.Manager],
  },
  {
    id: 'problems',
    icon: 'wrench',
    title: $localize`:@@guide.problems.title:Following up problems`,
    steps: [
      $localize`:@@guide.problems.live:Problems reported at the resources you look after come in as they're made: a message pops up, and Problems in the menu counts the new ones.`,
      $localize`:@@guide.problems.resolve:Start on one, and mark it resolved once it's fixed; whoever reported it is told.`,
      $localize`:@@guide.problems.close:If it can't be used meanwhile, take it out of use until a time you choose. You see first how many bookings that cancels, and their people are told why.`,
    ],
    link: { path: '/issues', label: $localize`:@@guide.problems.link:Reported problems` },
    roles: [Roles.Admin, Roles.Manager],
  },
  {
    id: 'admin',
    icon: 'sliders',
    title: $localize`:@@guide.admin.title:Running the office`,
    steps: [
      $localize`:@@guide.admin.overview:Admin shows every reservation, and the resources out of use.`,
      $localize`:@@guide.admin.blocks:Block a resource for cleaning or repairs under Blocked windows; bookings already there have to go first.`,
      $localize`:@@guide.admin.users:Approve sign-ups, change roles and disable accounts in Users; see how the resources are used in the usage report.`,
    ],
    link: { path: '/admin', label: $localize`:@@guide.admin.link:Open Admin` },
    roles: [Roles.Admin],
  },
  {
    id: 'you',
    icon: 'user',
    title: $localize`:@@guide.you.title:Make it yours`,
    steps: [
      $localize`:@@guide.you.switches:Switch between Türkçe and English, and between light and dark, at the top of the page.`,
      $localize`:@@guide.you.profile:Change your name or password from the menu under your name.`,
      $localize`:@@guide.you.notifications:Allow desktop notifications to see reminders and messages when SmartDesk isn't in front of you.`,
    ],
    link: { path: '/profile', label: $localize`:@@guide.you.link:My profile` },
  },
  {
    id: 'assistant',
    icon: 'sparkles',
    title: $localize`:@@guide.assistant.title:The assistant`,
    steps: [
      $localize`:@@guide.assistant.ask:Ask in your own words, like "a room for 4 on Friday afternoon".`,
      $localize`:@@guide.assistant.confirm:It suggests bookings and you confirm them: it never books by itself.`,
      $localize`:@@guide.assistant.open:Open it with the button in the corner.`,
    ],
    needsAssistant: true,
  },
];
