import { formatDate } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  LOCALE_ID,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, finalize, forkJoin, interval, map, merge, of, switchMap } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { FLOOR_PLAN, Floor } from '../../core/models/floor-plan.model';
import { Reservation } from '../../core/models/reservation.model';
import { ResourceBlock } from '../../core/models/resource-block.model';
import { Resource, ResourcePosition } from '../../core/models/resource.model';
import { AuthService } from '../../core/services/auth.service';
import { FloorPlanService } from '../../core/services/floor-plan.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceBlockService } from '../../core/services/resource-block.service';
import { ResourceService } from '../../core/services/resource.service';
import {
  addDays,
  addMinutes,
  floorToQuarterHour,
  fromDateTimeLocal,
  parseIso,
  startOfDay,
  toDateTimeLocal,
} from '../../shared/utils/date-time';
import { preparePhoto } from '../../shared/utils/photo';
import { canManageResource } from '../../shared/utils/resource-access';
import { RoomNow, roomNowFor } from '../../shared/utils/room-now';

/** How long a booking started from the plan is, unless something else starts sooner. */
export const PLAN_BOOKING_MINUTES = 60;
const QUARTER_HOUR_MS = 15 * 60_000;
/** How far ahead the plan looks for a time to offer: a day, in quarter hours. */
const LOOK_AHEAD_QUARTERS = 96;
/** A time chosen this long ago is gone: the plan shows it, but offers nothing to book. */
const PAST_AFTER_MS = 60_000;
/** How far an arrow key moves a resource while arranging: a hundredth of the plan, or five. */
const NUDGE = 0.01;
const NUDGE_FAR = 0.05;
/** The shape of a floor without a plan, drawn as a grid. */
const GRID_RATIO = '16 / 10';

type Day = { bookings: Reservation[]; blocks: ResourceBlock[] };

/** A resource on the floor at the chosen time: what it's doing, and where choosing it leads. */
export interface Spot {
  resource: Resource;
  state: RoomNow;
  /**
   * The booking form's query parameters, from when it's free; null when it can't be booked, or
   * the time shown has gone by.
   */
  book: Record<string, string> | null;
  /** "Meeting Room A is free until 15:00." */
  label: string;
  /** The caller may move it on the plan (ADR-0022). */
  mayMove: boolean;
}

/**
 * Where everything is on a floor, and what it's doing at a chosen time (ADR-0041): each
 * resource sits on the plan an Admin uploaded, green when free, red when in use, grey when out
 * of use, and choosing one opens the booking form for it from when it's free. Bookings come in
 * live, and "now" follows the clock. Whoever may edit a resource arranges the floor: drags it
 * to its place, or moves it with the arrow keys; an Admin uploads the plan.
 */
@Component({
  imports: [RouterLink],
  selector: 'app-floor-plan',
  styleUrl: './floor-plan.scss',
  templateUrl: './floor-plan.html',
})
export class FloorPlan {
  /** Query parameters: the floor (a location), and the time to show, now when left out. */
  readonly floor = input<string>();
  readonly at = input<string>();

  private readonly floorPlans = inject(FloorPlanService);
  private readonly resourceService = inject(ResourceService);
  private readonly reservationService = inject(ReservationService);
  private readonly blockService = inject(ResourceBlockService);
  private readonly hub = inject(ReservationHubService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly locale = inject(LOCALE_ID);
  private readonly injector = inject(Injector);
  private readonly planElement = viewChild<ElementRef<HTMLElement>>('plan');

  protected readonly floors = signal<Floor[]>([]);
  private readonly resources = signal<Resource[]>([]);
  private readonly day = signal<Day>({ bookings: [], blocks: [] });
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  private readonly retries = new Subject<void>();
  private readonly now = signal(Date.now());

  protected readonly arranging = signal(false);
  protected readonly uploading = signal(false);
  protected readonly arrangeError = signal<string | null>(null);
  /** A resource being dragged, and where it is so far. */
  private readonly dragging = signal<{ id: string; position: ResourcePosition } | null>(null);
  /** The current floor's plan, as an object URL; null without one. */
  protected readonly planUrl = signal<string | null>(null);

  protected readonly isAdmin = this.auth.isAdmin;

  /**
   * The floor shown when none is asked for: the first with a plan, else the first, chosen as
   * the floors load, so it stays while its plan is replaced or removed.
   */
  private readonly firstFloor = signal<string | null>(null);

  /** The floor asked for, or the one shown first. */
  protected readonly current = computed((): Floor | null => {
    const floors = this.floors();
    const find = (location: string | null | undefined) =>
      floors.find((f) => f.location === location);
    return find(this.floor()) ?? find(this.firstFloor()) ?? floors[0] ?? null;
  });

  /** Following the clock, unless a time was chosen. */
  protected readonly live = computed(() => !parseIso(this.at()));
  protected readonly moment = computed(() => parseIso(this.at()) ?? new Date(this.now()));
  protected readonly momentInput = computed(() => toDateTimeLocal(this.moment()));

  private readonly onFloor = computed(() =>
    this.resources()
      .filter((r) => r.location === this.current()?.location)
      .sort((a, b) => a.name.localeCompare(b.name, 'en')),
  );

  protected readonly spots = computed((): Spot[] => {
    const user = this.auth.currentUser();
    const who = {
      id: user?.id ?? '',
      isAdmin: this.auth.isAdmin(),
      isManager: this.auth.isManager(),
    };
    const when = (date: Date) => this.when(date);
    const past = this.moment().getTime() < this.now() - PAST_AFTER_MS;
    return this.onFloor().map((resource) => {
      const spot = spotFor(
        resource,
        this.day(),
        this.moment(),
        canManageResource(resource, who),
        when,
      );
      return past ? { ...spot, book: null } : spot;
    });
  });
  protected readonly placed = computed(() => this.spots().filter((s) => s.resource.position));
  protected readonly unplaced = computed(() => this.spots().filter((s) => !s.resource.position));
  /** The resources the caller may move: the list to place or take off while arranging. */
  protected readonly movable = computed(() => this.spots().filter((s) => s.mayMove));

  /** An Admin arranges any floor; a Manager one with resources of their team's alone. */
  protected readonly mayArrange = computed(() => this.auth.isAdmin() || this.movable().length > 0);

  protected readonly aspectRatio = computed(() => {
    const plan = this.current()?.plan;
    return plan ? `${plan.width} / ${plan.height}` : GRID_RATIO;
  });

  constructor() {
    merge(of(undefined), this.retries)
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.loadFailed.set(false);
          return forkJoin({
            floors: this.floorPlans.getFloors(),
            resources: this.resourceService.getResources(),
          }).pipe(catchError(() => of(null)));
        }),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          const first = result.floors.find((f) => f.plan !== null) ?? result.floors[0];
          this.firstFloor.set(first?.location ?? null);
          this.floors.set(result.floors);
          this.resources.set(result.resources);
        } else {
          this.loadFailed.set(true);
        }
      });

    // The day of the chosen time, again after a reconnect: events missed meanwhile are lost,
    // and blocks send none.
    const dayStart = computed(() => startOfDay(this.moment()), {
      equal: (a, b) => a.getTime() === b.getTime(),
    });
    merge(toObservable(dayStart), this.hub.reconnected$.pipe(map(() => dayStart())))
      .pipe(
        switchMap((start) => {
          const range = { from: start.toISOString(), to: addDays(start, 1).toISOString() };
          return forkJoin({
            bookings: this.reservationService.getCalendar(range).pipe(catchError(() => of([]))),
            blocks: this.blockService.getBlocks(range).pipe(catchError(() => of([]))),
          });
        }),
        takeUntilDestroyed(),
      )
      .subscribe((day) => this.day.set(day));

    // A booking made, moved or cancelled anywhere changes its colour at once.
    merge(this.hub.created$, this.hub.updated$, this.hub.cancelled$)
      .pipe(takeUntilDestroyed())
      .subscribe((reservation) => this.follow(reservation));

    // The current floor's plan, fetched with the token and shown through an object URL.
    const planVersion = computed(() => {
      const floor = this.current();
      return floor?.plan ? `${floor.location}\n${floor.plan.updatedAt}` : null;
    });
    toObservable(planVersion)
      .pipe(
        switchMap((version) => {
          const floor = this.current();
          if (!version || !floor?.plan) return of(null);
          return this.floorPlans.getPlan(floor.location, floor.plan.updatedAt).pipe(
            map((blob) => URL.createObjectURL(blob)),
            catchError(() => of(null)),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((url) => this.showPlan(url));
    inject(DestroyRef).onDestroy(() => this.showPlan(null));

    interval(60_000)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.now.set(Date.now()));
  }

  protected retry(): void {
    this.retries.next();
  }

  protected selectFloor(event: Event): void {
    this.arrangeError.set(null);
    this.navigate({ floor: (event.target as HTMLSelectElement).value });
  }

  protected selectMoment(event: Event): void {
    const chosen = fromDateTimeLocal((event.target as HTMLInputElement).value);
    this.navigate({ at: chosen ? chosen.toISOString() : null });
  }

  protected followNow(): void {
    this.now.set(Date.now());
    this.navigate({ at: null });
  }

  /** Where a resource shows: where it's being dragged to, or its place. */
  protected shownPosition(spot: Spot): ResourcePosition {
    const dragged = this.dragging();
    return dragged?.id === spot.resource.id ? dragged.position : spot.resource.position!;
  }

  /** A fraction of the plan as a percentage for CSS: 0.55 is 55, not 55.00000000000001. */
  protected percent(fraction: number): number {
    return Math.round(fraction * 1000) / 10;
  }

  protected planAlt(): string {
    return $localize`:@@floorPlan.planAlt:Plan of ${this.current()?.location ?? ''}:floor:`;
  }

  protected moveLabel(spot: Spot): string {
    return $localize`:@@floorPlan.moveLabel:Move ${spot.resource.name}:resource:: drag it, or use the arrow keys`;
  }

  // --- Arranging ---

  /**
   * Moves it with the pointer, by as much as the pointer moves, and saves it where it's let go;
   * a click alone only selects it, for the arrow keys.
   */
  protected startDrag(event: PointerEvent, spot: Spot): void {
    const plan = this.planElement()?.nativeElement;
    if (!this.arranging() || !spot.mayMove || !plan || event.button !== 0) return;
    event.preventDefault();
    const handle = event.currentTarget as HTMLElement;
    handle.focus({ preventScroll: true });
    handle.setPointerCapture?.(event.pointerId);
    const rect = plan.getBoundingClientRect();
    const from = this.shownPosition(spot);
    const to = (e: PointerEvent) =>
      clampPosition({
        x: from.x + (e.clientX - event.clientX) / rect.width,
        y: from.y + (e.clientY - event.clientY) / rect.height,
      });
    let moved = false;
    const listening = new AbortController();
    const stop = (e: PointerEvent, keep: boolean) => {
      listening.abort();
      this.dragging.set(null);
      if (keep && moved) {
        this.save(spot.resource, to(e));
      }
    };
    const options = { signal: listening.signal };
    handle.addEventListener(
      'pointermove',
      (e) => {
        moved = true;
        this.dragging.set({ id: spot.resource.id, position: to(e) });
      },
      options,
    );
    handle.addEventListener('pointerup', (e) => stop(e, true), options);
    handle.addEventListener('pointercancel', (e) => stop(e, false), options);
  }

  /** Arrow keys move it on screen; letting go saves it, once for a key held down. */
  protected nudge(event: KeyboardEvent, spot: Spot): void {
    const position = this.shownPosition(spot);
    const step = event.shiftKey ? NUDGE_FAR : NUDGE;
    const delta: Record<string, [number, number]> = {
      ArrowLeft: [-step, 0],
      ArrowRight: [step, 0],
      ArrowUp: [0, -step],
      ArrowDown: [0, step],
    };
    const [dx, dy] = delta[event.key] ?? [0, 0];
    if (dx === 0 && dy === 0) return;
    event.preventDefault();
    this.dragging.set({
      id: spot.resource.id,
      position: clampPosition({ x: position.x + dx, y: position.y + dy }),
    });
  }

  protected saveNudged(event: KeyboardEvent, spot: Spot): void {
    const dragged = this.dragging();
    if (!event.key.startsWith('Arrow') || dragged?.id !== spot.resource.id) return;
    this.dragging.set(null);
    this.save(spot.resource, dragged.position);
  }

  /** On the plan in its middle, ready to drag or move with the keys. */
  protected place(spot: Spot): void {
    this.save(spot.resource, { x: 0.5, y: 0.5 });
    afterNextRender(
      () =>
        this.planElement()
          ?.nativeElement.querySelector<HTMLElement>(`[data-resource="${spot.resource.id}"]`)
          ?.focus(),
      { injector: this.injector },
    );
  }

  protected takeOff(spot: Spot): void {
    this.save(spot.resource, null);
  }

  protected async uploadPlan(event: Event): Promise<void> {
    const field = event.target as HTMLInputElement;
    const file = field.files?.[0];
    field.value = '';
    const floor = this.current();
    if (!file || !floor) return;

    this.arrangeError.set(null);
    this.uploading.set(true);
    const plan = await preparePhoto(file, FLOOR_PLAN.maxSide);
    if (!plan) {
      this.uploading.set(false);
      this.arrangeError.set(
        $localize`:@@floorPlan.unreadable:That file isn't a picture this browser can read.`,
      );
      return;
    }

    this.floorPlans
      .setPlan(floor.location, plan)
      .pipe(finalize(() => this.uploading.set(false)))
      .subscribe({
        next: (updated) => this.replaceFloor(updated),
        error: (error: unknown) =>
          this.arrangeError.set(
            reason(error, $localize`:@@floorPlan.uploadFailed:The plan could not be uploaded.`),
          ),
      });
  }

  protected removePlan(): void {
    const floor = this.current();
    if (
      !floor?.plan ||
      !confirm(
        $localize`:@@floorPlan.removeConfirm:Remove the plan of ${floor.location}:floor:? The resources keep their places for the next one.`,
      )
    ) {
      return;
    }

    this.arrangeError.set(null);
    this.floorPlans.removePlan(floor.location).subscribe({
      next: () => this.replaceFloor({ ...floor, plan: null }),
      error: () =>
        this.arrangeError.set(
          $localize`:@@floorPlan.removeFailed:The plan could not be removed. Try again.`,
        ),
    });
  }

  /** Shown at once, sent after: put back if the API says no. */
  private save(resource: Resource, position: ResourcePosition | null): void {
    const before = resource.position;
    this.arrangeError.set(null);
    this.patchPosition(resource.id, position);
    this.resourceService.setPosition(resource.id, position).subscribe({
      next: (saved) =>
        this.resources.update((list) => list.map((r) => (r.id === saved.id ? saved : r))),
      error: () => {
        this.patchPosition(resource.id, before);
        this.arrangeError.set(
          $localize`:@@floorPlan.moveFailed:${resource.name}:resource: could not be moved. Try again.`,
        );
      },
    });
  }

  private patchPosition(id: string, position: ResourcePosition | null): void {
    this.resources.update((list) => list.map((r) => (r.id === id ? { ...r, position } : r)));
  }

  private replaceFloor(updated: Floor): void {
    this.floors.update((list) =>
      list.some((f) => f.location === updated.location)
        ? list.map((f) => (f.location === updated.location ? updated : f))
        : [...list, updated],
    );
  }

  /** A booking of the day shown is kept, moved or dropped; any other day's isn't ours. */
  private follow(reservation: Reservation): void {
    const start = startOfDay(this.moment()).getTime();
    const end = addDays(new Date(start), 1).getTime();
    const onDay =
      new Date(reservation.startDateTime).getTime() < end &&
      new Date(reservation.endDateTime).getTime() > start;
    this.day.update((day) => ({
      ...day,
      bookings: [
        ...day.bookings.filter((r) => r.id !== reservation.id),
        ...(reservation.status === 'Confirmed' && onDay ? [reservation] : []),
      ],
    }));
  }

  private showPlan(url: string | null): void {
    const old = this.planUrl();
    if (old) {
      URL.revokeObjectURL(old);
    }
    this.planUrl.set(url);
  }

  private navigate(queryParams: Record<string, string | null>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
    });
  }

  /** "14:30" on the day shown, "Fri 9 Oct, 14:30" on another. */
  private when(date: Date): string {
    const sameDay = this.moment().toDateString() === date.toDateString();
    return formatDate(date, sameDay ? 'HH:mm' : 'EEE d MMM, HH:mm', this.locale);
  }
}

/** A resource at `at`: its colour, its sentence, and the booking it offers. */
export function spotFor(
  resource: Resource,
  day: Day,
  at: Date,
  mayMove: boolean,
  when: (date: Date) => string,
): Spot {
  const own = {
    bookings: day.bookings.filter((r) => r.resourceId === resource.id),
    blocks: day.blocks.filter((b) => b.resourceId === resource.id),
  };
  const stateAt = (time: Date) => roomNowFor(resource, own, time.getTime());
  const state = stateAt(at);
  const name = resource.name;
  switch (state.kind) {
    case 'free':
      return {
        resource,
        state,
        mayMove,
        book: offerFrom(resource.id, at, stateAt),
        label: state.until
          ? $localize`:@@floorPlan.freeUntil:${name}:resource: is free until ${when(state.until)}:time:.`
          : $localize`:@@floorPlan.freeAllDay:${name}:resource: is free for the rest of the day.`,
      };
    case 'busy': {
      const freeAt = freeAgain(state.until, stateAt);
      return {
        resource,
        state,
        mayMove,
        book: offerFrom(resource.id, freeAt, stateAt),
        label: $localize`:@@floorPlan.busyUntil:${name}:resource: is in use until ${when(freeAt)}:time:.`,
      };
    }
    case 'unavailable':
      return {
        resource,
        state,
        mayMove,
        book: null,
        label: $localize`:@@floorPlan.unavailable:${name}:resource: can't be booked: ${state.reason}:reason:.`,
      };
  }
}

/** When a resource in use until `until` is free again, past bookings and blocks back to back. */
function freeAgain(until: Date, stateAt: (time: Date) => RoomNow): Date {
  let at = until;
  for (let step = 0; step < LOOK_AHEAD_QUARTERS; step++) {
    const state = stateAt(at);
    if (state.kind === 'free' || !state.until) break;
    at = state.until;
  }
  return at;
}

/**
 * The booking the plan offers from `from` on: from the first quarter hour, the booking form's
 * step, that leaves the resource free for a quarter of an hour at least, for an hour - or until
 * it's taken, when that comes sooner. Null when the day ahead has no such time.
 */
function offerFrom(
  resourceId: string,
  from: Date,
  stateAt: (time: Date) => RoomNow,
): Record<string, string> | null {
  const floored = floorToQuarterHour(from);
  let start = floored.getTime() < from.getTime() ? addMinutes(floored, 15) : floored;
  for (let step = 0; step < LOOK_AHEAD_QUARTERS; step++, start = addMinutes(start, 15)) {
    const state = stateAt(start);
    if (state.kind !== 'free') continue;
    const hour = addMinutes(start, PLAN_BOOKING_MINUTES);
    const end = state.until && state.until < hour ? state.until : hour;
    if (end.getTime() - start.getTime() >= QUARTER_HOUR_MS) {
      return { resourceId, start: start.toISOString(), end: end.toISOString() };
    }
  }
  return null;
}

/** Inside the plan, to a thousandth. */
export function clampPosition(position: ResourcePosition): ResourcePosition {
  const clamp = (value: number) => Math.min(1, Math.max(0, Math.round(value * 1000) / 1000));
  return { x: clamp(position.x), y: clamp(position.y) };
}

/** The API's own words for a 400, or `fallback`. */
function reason(error: unknown, fallback: string): string {
  if (error instanceof ApiError && error.status === 400) {
    return Object.values(error.fieldErrors).flat()[0] ?? error.detail ?? fallback;
  }
  return fallback;
}
