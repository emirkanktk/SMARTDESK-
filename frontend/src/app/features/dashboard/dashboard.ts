import { DatePipe, NgTemplateOutlet, formatDate } from '@angular/common';
import { Component, LOCALE_ID, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, catchError, interval, merge, of, startWith, switchMap } from 'rxjs';
import { Dashboard as DashboardData } from '../../core/models/dashboard.model';
import { Reservation } from '../../core/models/reservation.model';
import { ResourceBlock } from '../../core/models/resource-block.model';
import { Resource } from '../../core/models/resource.model';
import { AuthService } from '../../core/services/auth.service';
import { DashboardService, browserTimeZone } from '../../core/services/dashboard.service';
import { FavoriteService } from '../../core/services/favorite.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ResourceBlockService } from '../../core/services/resource-block.service';
import { FavoriteToggle } from '../../shared/components/favorite-toggle/favorite-toggle';
import { Icon, IconName } from '../../shared/components/icon/icon';
import { roomNowFor } from '../../shared/utils/room-now';

const TIME = 'HH:mm';
const DAY_AND_TIME = 'EEE d MMM, HH:mm';

interface Row {
  reservation: Reservation;
  /** "You" for the signed-in user's own reservations. */
  who: string;
  mine: boolean;
  /** Date formats: time only when the moment falls on today. */
  startFormat: string;
  endFormat: string;
  runningNow: boolean;
  /** Checked in at the door (ADR-0030). */
  checkedIn: boolean;
}

/** A favourite resource (ADR-0051), with what it's doing now as a few words. */
interface Favorite {
  resource: Resource;
  now: string;
}

interface Tile {
  label: string;
  value: number;
  /** The screen it opens, if any. */
  link: string | null;
  icon: IconName;
  /** Its colour: the icon's and the soft glow behind it. */
  tone: 'indigo' | 'green' | 'amber' | 'violet';
}

/**
 * Today at a glance: counts, the user's favourite resources and whether each is free (ADR-0051),
 * what's in use right now, everything booked today and the user's next reservations. "Today" is
 * the browser's day (its time zone is sent to the API). Kept current by refetching on every
 * reservation event and once a minute, as bookings start and end.
 */
@Component({
  imports: [RouterLink, DatePipe, NgTemplateOutlet, Icon, FavoriteToggle],
  selector: 'app-dashboard',
  styleUrl: './dashboard.scss',
  templateUrl: './dashboard.html',
})
export class Dashboard {
  private readonly dashboardService = inject(DashboardService);
  private readonly hub = inject(ReservationHubService);
  private readonly authService = inject(AuthService);
  private readonly favoriteService = inject(FavoriteService);
  private readonly blockService = inject(ResourceBlockService);
  private readonly locale = inject(LOCALE_ID);
  private readonly timeZone = browserTimeZone();

  /** Today's blocked windows, for whether a favourite can be used; asked once a day. */
  private readonly blocks = signal<ResourceBlock[]>([]);
  private blocksDay: string | null = null;

  protected readonly displayName = this.authService.displayName;
  protected readonly data = signal<DashboardData | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  private readonly retries = new Subject<void>();

  protected readonly tiles = computed<Tile[]>(() => {
    const stats = this.data()?.stats;
    if (!stats) return [];
    return [
      {
        label: $localize`:@@dashboard.tile.active:Active resources`,
        value: stats.activeResources,
        link: '/resources',
        icon: 'building',
        tone: 'indigo',
      },
      {
        label: $localize`:@@dashboard.inUseNow:In use now`,
        value: stats.resourcesInUseNow,
        link: null,
        icon: 'zap',
        tone: 'green',
      },
      {
        label: $localize`:@@dashboard.tile.today:Reservations today`,
        value: stats.reservationsToday,
        link: null,
        icon: 'calendar',
        tone: 'amber',
      },
      {
        label: $localize`:@@dashboard.tile.upcoming:Your upcoming`,
        value: stats.myUpcomingReservations,
        link: '/reservations/mine',
        icon: 'clipboard',
        tone: 'violet',
      },
    ];
  });

  /** The favourites, by name, each with what it's doing now, as its door screen would say it. */
  protected readonly favorites = computed<Favorite[]>(() => {
    const data = this.data();
    const now = data ? new Date(data.generatedAt).getTime() : Date.now();
    const today = data?.today ?? [];
    return this.favoriteService.favorites().map((resource) => {
      const room = roomNowFor(
        resource,
        {
          bookings: today.filter((r) => r.resourceId === resource.id),
          blocks: this.blocks().filter((b) => b.resourceId === resource.id),
        },
        now,
      );
      switch (room.kind) {
        case 'free':
          return {
            resource,
            now: room.until
              ? $localize`:@@dashboard.favorite.freeUntil:Free until ${this.when(room.until)}:time:`
              : $localize`:@@dashboard.favorite.freeDay:Free for the rest of the day`,
          };
        case 'busy':
          return {
            resource,
            now: $localize`:@@dashboard.favorite.busy:In use until ${this.when(room.until)}:time:`,
          };
        default:
          return {
            resource,
            now: room.until
              ? $localize`:@@dashboard.favorite.unavailableUntil:Not available until ${this.when(room.until)}:time:`
              : $localize`:@@dashboard.favorite.unavailable:Not available`,
          };
      }
    });
  });

  protected readonly inUseNow = computed(() => this.rows(this.data()?.inUseNow ?? []));
  protected readonly today = computed(() => this.rows(this.data()?.today ?? []));
  protected readonly myUpcoming = computed(() => this.rows(this.data()?.myUpcoming ?? []));
  /** How many upcoming reservations aren't in the capped `myUpcoming` list. */
  protected readonly moreUpcoming = computed(() => {
    const data = this.data();
    return data ? data.stats.myUpcomingReservations - data.myUpcoming.length : 0;
  });

  constructor() {
    this.favoriteService.load();

    const { created$, updated$, cancelled$, reconnected$ } = this.hub;
    merge(this.retries, created$, updated$, cancelled$, reconnected$, interval(60_000))
      .pipe(
        startWith(null),
        // switchMap: only the newest answer counts when events arrive in a burst.
        switchMap(() =>
          this.dashboardService.getDashboard(this.timeZone).pipe(catchError(() => of(null))),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((data) => {
        this.loading.set(false);
        if (data) {
          this.data.set(data);
          this.loadFailed.set(false);
          this.loadBlocks(data);
        } else if (!this.data()) {
          this.loadFailed.set(true); // a failed quiet refresh keeps the last good data
        }
      });
  }

  /** "14:30" today, "Fri 9 Oct, 14:30" on another day. */
  private when(value: Date): string {
    const data = this.data();
    const t = value.getTime();
    const today =
      data && t >= new Date(data.todayStart).getTime() && t <= new Date(data.todayEnd).getTime();
    return formatDate(value, today ? TIME : DAY_AND_TIME, this.locale);
  }

  /** Blocks change seldom: today's once, and again the next day. Without them, none are shown. */
  private loadBlocks(data: DashboardData): void {
    if (data.todayStart === this.blocksDay) {
      return;
    }
    this.blocksDay = data.todayStart;
    this.blockService
      .getBlocks({ from: data.todayStart, to: data.todayEnd })
      .pipe(catchError(() => of([])))
      .subscribe((blocks) => this.blocks.set(blocks));
  }

  protected retry(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.retries.next();
  }

  private rows(reservations: Reservation[]): Row[] {
    const data = this.data();
    const userId = this.authService.currentUser()?.id;
    const now = data ? new Date(data.generatedAt).getTime() : Date.now();
    const todayStart = data ? new Date(data.todayStart).getTime() : 0;
    const todayEnd = data ? new Date(data.todayEnd).getTime() : 0;
    // Time only when the moment is on today. Half-open like the API's ranges: a start at the
    // next midnight belongs to tomorrow, while an end there still reads "-00:00".
    const startFormat = (t: number) => (t >= todayStart && t < todayEnd ? TIME : DAY_AND_TIME);
    const endFormat = (t: number) => (t > todayStart && t <= todayEnd ? TIME : DAY_AND_TIME);

    return reservations.map((reservation) => {
      const mine = reservation.userId === userId;
      const start = new Date(reservation.startDateTime).getTime();
      const end = new Date(reservation.endDateTime).getTime();
      return {
        reservation,
        mine,
        who: mine
          ? $localize`:@@common.you:You`
          : (reservation.userDisplayName ?? $localize`:@@common.someone:Someone`),
        startFormat: startFormat(start),
        endFormat: endFormat(end),
        runningNow: start <= now && end > now,
        checkedIn: reservation.checkedInAt !== null,
      };
    });
  }
}
