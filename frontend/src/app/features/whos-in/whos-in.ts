import { DatePipe, formatDate } from '@angular/common';
import { Component, LOCALE_ID, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, debounceTime, interval, map, merge, of, switchMap, tap } from 'rxjs';
import { OfficePerson, OfficeVisit, WhosIn as WhosInDay } from '../../core/models/whos-in.model';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { WhosInService } from '../../core/services/whos-in.service';
import { Icon } from '../../shared/components/icon/icon';
import {
  addDays,
  parseDateParam,
  parseIso,
  startOfDay,
  toDateParam,
} from '../../shared/utils/date-time';

/** How soon after a booking changes the list is asked for again: changes come in bursts. */
const RELOAD_AFTER_MS = 400;

/** A place someone is at, as a row shows it. */
interface VisitRow {
  visit: OfficeVisit;
  /** "HH:mm", with the day too when it isn't the day shown. */
  startFormat: string;
  endFormat: string;
  /** Under way now. */
  now: boolean;
  /** The floor plan at its floor: at its time, or now while it's under way. */
  plan: Record<string, string>;
}

interface PersonRow {
  person: OfficePerson;
  initials: string;
  visits: VisitRow[];
}

/**
 * Who's in the office on a day (ADR-0063): everyone whose booking brings them in, or a meeting
 * they said they'd come to, with where and when - to pick a day the team is in, or to find
 * someone. Today unless the address names another day (`?date=2026-11-27`); "My team" narrows it
 * to the user's team, and a name narrows it further. A place opens the floor plan at it. Bookings
 * made, moved, checked in or cancelled meanwhile show at once.
 */
@Component({
  imports: [DatePipe, RouterLink, Icon],
  selector: 'app-whos-in',
  styleUrl: './whos-in.scss',
  templateUrl: './whos-in.html',
})
export class WhosIn {
  /** Query parameter: the day, "2026-11-27"; today when left out. */
  readonly date = input<string>();

  private readonly service = inject(WhosInService);
  private readonly hub = inject(ReservationHubService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly locale = inject(LOCALE_ID);

  /** The clock, a minute at a time: what's under way, and which day is today. */
  private readonly now = signal(Date.now());

  /** The day shown, from its local midnight. */
  protected readonly day = computed(
    () => parseDateParam(this.date()) ?? startOfDay(new Date(this.now())),
    {
      equal: (a, b) => a.getTime() === b.getTime(),
    },
  );
  protected readonly isToday = computed(
    () => this.day().getTime() === startOfDay(new Date(this.now())).getTime(),
  );
  protected readonly dayValue = computed(() => toDateParam(this.day()));

  protected readonly whosIn = signal<WhosInDay | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly teamOnly = signal(false);
  protected readonly search = signal('');

  private readonly retries = new Subject<void>();

  /** "12 people are in the office on Thursday 26 November." */
  protected readonly summary = computed(() => {
    const count = this.whosIn()?.people.length ?? 0;
    const day = formatDate(this.day(), 'EEEE d MMMM', this.locale);
    if (count === 0) {
      return $localize`:@@whosIn.nobody:Nobody is in the office on ${day}:day:.`;
    }
    return count === 1
      ? $localize`:@@whosIn.one:1 person is in the office on ${day}:day:.`
      : $localize`:@@whosIn.many:${count}:count: people are in the office on ${day}:day:.`;
  });

  protected readonly teamCount = computed(
    () => this.whosIn()?.people.filter((person) => person.inMyTeam).length ?? 0,
  );

  /** The people shown: just the team's when asked, and those whose name has what's typed. */
  protected readonly rows = computed<PersonRow[]>(() => {
    const whosIn = this.whosIn();
    if (!whosIn) {
      return [];
    }
    const needle = this.search().trim().toLocaleLowerCase(this.locale);
    return whosIn.people
      .filter((person) => !this.teamOnly() || person.inMyTeam)
      .filter((person) => !needle || person.name.toLocaleLowerCase(this.locale).includes(needle))
      .map((person) => ({
        person,
        initials: initialsOf(person.name, this.locale),
        visits: person.visits.map((visit) => this.visitRow(visit)),
      }));
  });

  /** Narrowed down to nobody, rather than nobody in at all. */
  protected readonly narrowedToNone = computed(
    () => this.rows().length === 0 && (this.whosIn()?.people.length ?? 0) > 0,
  );

  constructor() {
    interval(60_000)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.now.set(Date.now()));

    // A new day starts empty rather than showing the last one's people. A change to any booking
    // asks again, as does coming back after a lost connection: a booking moved off this day
    // says nothing about it once it's moved.
    const ask = computed(() => this.dayValue());
    const newDay = toObservable(ask).pipe(tap(() => this.whosIn.set(null)));
    const changed = merge(this.hub.created$, this.hub.updated$, this.hub.cancelled$).pipe(
      debounceTime(RELOAD_AFTER_MS),
      map(() => ask()),
    );
    merge(
      newDay,
      changed,
      this.hub.reconnected$.pipe(map(() => ask())),
      this.retries.pipe(map(() => ask())),
    )
      .pipe(
        switchMap((date) => {
          this.loadFailed.set(false);
          return this.service.get(date).pipe(
            catchError(() => {
              this.loadFailed.set(true);
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((whosIn) => {
        if (whosIn) {
          this.whosIn.set(whosIn);
        }
      });
  }

  protected retry(): void {
    this.retries.next();
  }

  protected goBy(days: number): void {
    this.show(addDays(this.day(), days));
  }

  protected goToday(): void {
    this.navigate(null);
  }

  protected pickDay(event: Event): void {
    const day = parseDateParam((event.target as HTMLInputElement).value);
    if (day) {
      this.show(day);
    }
  }

  protected setSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  /** Today's address has no date, so it stays today's. */
  private show(day: Date): void {
    this.navigate(day.getTime() === startOfDay(new Date()).getTime() ? null : toDateParam(day));
  }

  private navigate(date: string | null): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { date },
      queryParamsHandling: 'merge',
    });
  }

  private visitRow(visit: OfficeVisit): VisitRow {
    const start = parseIso(visit.startDateTime)!;
    const end = parseIso(visit.endDateTime)!;
    const now = this.now();
    const day = this.day();
    const sameDay = (date: Date) => startOfDay(date).getTime() === day.getTime();
    const underWay = start.getTime() <= now && now < end.getTime();
    // An end at the next midnight is still this day's.
    const endsToday = sameDay(end) || end.getTime() === addDays(day, 1).getTime();
    return {
      visit,
      startFormat: sameDay(start) ? 'HH:mm' : 'EEE d MMM, HH:mm',
      endFormat: endsToday ? 'HH:mm' : 'EEE d MMM, HH:mm',
      now: underWay,
      plan: underWay
        ? { floor: visit.location }
        : { floor: visit.location, at: start.toISOString() },
    };
  }
}

/** "AY" for "Ayşe Yılmaz": the first letters of the first and the last name, in capitals. */
export function initialsOf(name: string, locale: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  const first = words[0]?.[0] ?? '?';
  const last = words.length > 1 ? words[words.length - 1][0] : '';
  return (first + last).toLocaleUpperCase(locale);
}
