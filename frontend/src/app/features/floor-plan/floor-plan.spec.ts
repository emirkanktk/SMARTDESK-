import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { Floor } from '../../core/models/floor-plan.model';
import { ResourceUsage, UsageReport } from '../../core/models/report.model';
import { Reservation } from '../../core/models/reservation.model';
import { ResourceBlock } from '../../core/models/resource-block.model';
import { Resource, ResourcePosition } from '../../core/models/resource.model';
import { AuthService } from '../../core/services/auth.service';
import { FloorPlanService } from '../../core/services/floor-plan.service';
import { ReportService } from '../../core/services/report.service';
import { ReservationHubService } from '../../core/services/reservation-hub.service';
import { ReservationService } from '../../core/services/reservation.service';
import { ResourceBlockService } from '../../core/services/resource-block.service';
import { ResourceService } from '../../core/services/resource.service';
import { fakeObjectUrls } from '../../../testing/object-urls';
import { fakeReservation } from '../../../testing/reservation-fixtures';
import { fakeResource } from '../../../testing/resource-fixtures';
import { FloorPlan, clampPosition, spotFor, usageMark } from './floor-plan';

// Local times on Monday 12 October, so what the page prints reads the same in every time zone.
const at = (time: string, day = '2026-10-12') => new Date(`${day}T${time}`);
const iso = (time: string, day?: string) => at(time, day).toISOString();
const TUESDAY = '2026-10-13';

const FLOOR = '5th Floor';
const room = fakeResource({
  id: 'res-room',
  name: 'Meeting Room A',
  location: FLOOR,
  position: { x: 0.25, y: 0.5 },
});
const desk = fakeResource({
  id: 'res-desk',
  name: 'Desk D-1',
  location: FLOOR,
  position: { x: 0.75, y: 0.25 },
});
const projector = fakeResource({
  id: 'res-projector',
  name: 'Projector P-1',
  location: FLOOR,
  status: 'Maintenance',
  position: { x: 0.5, y: 0.8 },
});
const camera = fakeResource({ id: 'res-camera', name: 'Camera C-1', location: FLOOR });
const lobby = fakeResource({ id: 'res-lobby', name: 'Lobby Desk', location: '2nd Floor' });
const ALL = [room, desk, projector, camera, lobby];

const PLAN_SAVED = '2026-10-12T06:00:00Z';
const fifth: Floor = {
  location: FLOOR,
  resources: 4,
  placed: 3,
  plan: { width: 1600, height: 900, updatedAt: PLAN_SAVED },
};
const second: Floor = { location: '2nd Floor', resources: 1, placed: 0, plan: null };

function booking(
  resource: Resource,
  start: string,
  end: string,
  overrides: Partial<Reservation> = {},
): Reservation {
  return fakeReservation({
    id: `rsv-${resource.id}-${start}`,
    resourceId: resource.id,
    resourceName: resource.name,
    resourceLocation: resource.location,
    startDateTime: start,
    endDateTime: end,
    ...overrides,
  });
}

/** The desk is in use from ten to eleven; the room is booked from two. */
const deskBooking = booking(desk, iso('10:00'), iso('11:00'));
const roomBooking = booking(room, iso('14:00'), iso('15:00'));

/** A row of the usage report (ADR-0059). */
function usageRow(
  resource: Resource,
  utilization: number,
  reservations: number,
  noShows: number,
): ResourceUsage {
  return {
    resourceId: resource.id,
    resourceName: resource.name,
    resourceTypeId: resource.resourceTypeId,
    resourceTypeName: 'Meeting Room',
    status: resource.status,
    reservations,
    cancelled: 0,
    bookedHours: 0,
    workingHoursBooked: 0,
    utilization,
    noShows,
    checkedIn: 0,
    noShowRate: 0,
  };
}

/** The room well used over the last days, the desk hardly; the others not at all. */
const usageReport = {
  resources: [usageRow(room, 0.6, 12, 1), usageRow(desk, 0.1, 3, 0)],
} as unknown as UsageReport;

/** Visible text, its white space as it reads. */
function text(element: Element): string {
  return element.textContent!.replace(/\s+/g, ' ').trim();
}

describe('spotFor', () => {
  const time = (date: Date) => date.toTimeString().slice(0, 5);
  const day = (bookings: Reservation[] = [], blocks: ResourceBlock[] = []) => ({
    bookings,
    blocks,
  });
  const block = (start: string, end: string): ResourceBlock => ({
    id: `blk-${start}`,
    resourceId: room.id,
    resourceName: room.name,
    startDateTime: start,
    endDateTime: end,
    reason: 'Carpet cleaning',
    createdByUserId: 'user-admin',
  });
  const offer = (start: string, end: string) => ({
    resourceId: room.id,
    start: iso(start),
    end: iso(end),
  });

  it('offers an hour from the next quarter hour while the resource is free', () => {
    const spot = spotFor(room, day(), at('10:05'), false, time);

    expect(spot.state.kind).toBe('free');
    expect(spot.label).toBe('Meeting Room A is free for the rest of the day.');
    expect(spot.book).toEqual(offer('10:15', '11:15'));
    expect(spotFor(room, day(), at('10:00'), false, time).book).toEqual(offer('10:00', '11:00'));
  });

  it('ends the booking it offers where the next one starts, when that comes within the hour', () => {
    const next = booking(room, iso('10:45'), iso('11:30'));

    const spot = spotFor(room, day([next]), at('10:05'), false, time);

    expect(spot.label).toBe('Meeting Room A is free until 10:45.');
    expect(spot.book).toEqual(offer('10:15', '10:45'));
  });

  it('passes over a gap shorter than a quarter of an hour, to the next free time', () => {
    const next = booking(room, iso('10:20'), iso('11:00'));

    const spot = spotFor(room, day([next]), at('10:05'), false, time);

    expect(spot.label).toBe('Meeting Room A is free until 10:20.');
    expect(spot.book).toEqual(offer('11:00', '12:00'));
  });

  it('says when a resource in use is free again, past bookings and blocks back to back', () => {
    const busy = day(
      [booking(room, iso('10:00'), iso('11:00')), booking(room, iso('11:00'), iso('11:50'))],
      [block(iso('11:50'), iso('12:20'))],
    );

    const spot = spotFor(room, busy, at('10:05'), false, time);

    expect(spot.state).toEqual({ kind: 'busy', until: at('11:00') });
    expect(spot.label).toBe('Meeting Room A is in use until 12:20.');
    expect(spot.book).toEqual(offer('12:30', '13:30'));
  });

  it('offers nothing for a resource out of use, and says why', () => {
    const blocked = spotFor(
      room,
      day([], [block(iso('09:00'), iso('17:00'))]),
      at('10:05'),
      true,
      time,
    );
    const repaired = spotFor({ ...room, status: 'Maintenance' }, day(), at('10:05'), true, time);

    expect(blocked.label).toBe("Meeting Room A can't be booked: Carpet cleaning.");
    expect(blocked.book).toBeNull();
    expect(blocked.mayMove).toBe(true);
    expect(repaired.label).toBe("Meeting Room A can't be booked: Maintenance.");
    expect(repaired.book).toBeNull();
  });

  it("leaves out other resources' bookings, and cancelled ones", () => {
    const others = day([
      booking(desk, iso('10:00'), iso('11:00')),
      booking(room, iso('10:00'), iso('11:00'), { status: 'Cancelled' }),
    ]);

    expect(spotFor(room, others, at('10:05'), false, time).state.kind).toBe('free');
  });
});

describe('clampPosition', () => {
  it('keeps a place inside the plan, to a thousandth', () => {
    expect(clampPosition({ x: -0.2, y: 1.3 })).toEqual({ x: 0, y: 1 });
    expect(clampPosition({ x: 0.12345, y: 0.5 + 0.05 })).toEqual({ x: 0.123, y: 0.55 });
  });
});

describe('usageMark', () => {
  it('puts a share on the heat scale, and says it with its numbers', () => {
    expect(usageMark(room, usageRow(room, 0.6, 12, 1), 30, 'en-US')).toEqual({
      level: 3,
      percent: '60%',
      label: 'Meeting Room A - last 30 days: 60% of office hours booked, 12 bookings, 1 no-shows.',
    });
    // A little, but not none.
    const little = usageMark(desk, usageRow(desk, 0.003, 1, 0), 7, 'en-US');
    expect([little.level, little.percent]).toEqual([1, '<1%']);
    expect(usageMark(camera, undefined, 90, 'en-US')).toEqual({
      level: 0,
      percent: '0%',
      label: 'Camera C-1 - last 90 days: 0% of office hours booked, 0 bookings, 0 no-shows.',
    });
  });
});

describe('FloorPlan', () => {
  const currentUser = signal<{ id: string } | null>(null);
  const isAdmin = signal(false);
  const isManager = signal(false);
  let getFloors: ReturnType<typeof vi.fn>;
  let getPlan: ReturnType<typeof vi.fn>;
  let setPlan: ReturnType<typeof vi.fn>;
  let removePlan: ReturnType<typeof vi.fn>;
  let getResources: ReturnType<typeof vi.fn>;
  let setPosition: ReturnType<typeof vi.fn>;
  let getCalendar: ReturnType<typeof vi.fn>;
  let getBlocks: ReturnType<typeof vi.fn>;
  let getUsage: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let hub: {
    created$: Subject<Reservation>;
    updated$: Subject<Reservation>;
    cancelled$: Subject<Reservation>;
    reconnected$: Subject<void>;
  };
  let urls: ReturnType<typeof fakeObjectUrls>;
  let fixture: ComponentFixture<FloorPlan>;
  let element: HTMLElement;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    vi.setSystemTime(at('10:05'));
    currentUser.set({ id: 'user-admin' });
    isAdmin.set(true);
    isManager.set(false);
    urls = fakeObjectUrls();
    getFloors = vi.fn(() => of([second, fifth]));
    getPlan = vi.fn(() => of(new Blob(['plan'], { type: 'image/webp' })));
    setPlan = vi.fn(() =>
      of({ ...fifth, plan: { width: 2400, height: 1350, updatedAt: '2026-10-12T08:10:00Z' } }),
    );
    removePlan = vi.fn(() => of(undefined));
    getResources = vi.fn(() => of(ALL));
    setPosition = vi.fn((id: string, position: ResourcePosition | null) =>
      of({ ...ALL.find((r) => r.id === id)!, position }),
    );
    getCalendar = vi.fn(() => of([deskBooking, roomBooking]));
    getBlocks = vi.fn(() => of([]));
    getUsage = vi.fn(() => of(usageReport));
    hub = {
      created$: new Subject(),
      updated$: new Subject(),
      cancelled$: new Subject(),
      reconnected$: new Subject(),
    };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: FloorPlanService, useValue: { getFloors, getPlan, setPlan, removePlan } },
        { provide: ResourceService, useValue: { getResources, setPosition } },
        { provide: ReservationService, useValue: { getCalendar } },
        { provide: ResourceBlockService, useValue: { getBlocks } },
        { provide: ReportService, useValue: { getUsage } },
        { provide: ReservationHubService, useValue: hub },
        { provide: AuthService, useValue: { currentUser, isAdmin, isManager } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => {
    // Let go of the plan's picture while the stand-in for object URLs is still there.
    fixture?.destroy();
    urls.restore();
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  async function render(
    inputs: { floor?: string; at?: string; view?: string; days?: string } = {},
  ): Promise<void> {
    fixture = TestBed.createComponent(FloorPlan);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  async function click(target: HTMLElement): Promise<void> {
    target.click();
    await fixture.whenStable();
  }

  /** A resource on the plan, by its name. */
  function spot(name: string): HTMLElement | null {
    const spots = [...element.querySelectorAll<HTMLElement>('.plan .spot')];
    return spots.find((s) => text(s.querySelector('.name')!) === name) ?? null;
  }

  function button(label: string, within: ParentNode = element): HTMLButtonElement {
    const buttons = [...within.querySelectorAll<HTMLButtonElement>('button')];
    const found = buttons.find((b) => text(b) === label);
    if (!found) throw new Error(`No "${label}" button`);
    return found;
  }

  function arrangeButton(): HTMLButtonElement | null {
    return element.querySelector<HTMLButtonElement>('button.arrange');
  }

  /** A resource's line in the list to arrange. */
  function row(name: string): HTMLElement {
    const rows = [...element.querySelectorAll<HTMLElement>('.movable li')];
    return rows.find((li) => text(li.querySelector('.name')!) === name)!;
  }

  /** Where a link leads: the booking form, with what it starts with. */
  function bookingOf(link: Element): Record<string, string> {
    const url = new URL(link.getAttribute('href')!, 'http://smartdesk.test');
    expect(url.pathname).toBe('/reservations/new');
    return Object.fromEntries(url.searchParams);
  }

  function position(target: HTMLElement): [string, string] {
    return [target.style.left, target.style.top];
  }

  /** Lets an upload's picture be read and written, which happens outside Angular's sight. */
  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  }

  it('shows the floor with a plan, each resource where it stands, coloured by what it does now', async () => {
    await render();

    expect(element.querySelector<HTMLSelectElement>('#floor')!.value).toBe(FLOOR);
    expect(getPlan).toHaveBeenCalledWith(FLOOR, PLAN_SAVED);
    const image = element.querySelector<HTMLImageElement>('.plan-image')!;
    expect(image.getAttribute('src')).toBe('blob:test-1');
    expect(image.alt).toBe('Plan of 5th Floor');
    expect(element.querySelector<HTMLElement>('.plan')!.style.aspectRatio).toBe('1600 / 900');

    const free = spot('Meeting Room A')!;
    expect(free.tagName).toBe('A');
    expect(free.classList.contains('free')).toBe(true);
    expect(position(free)).toEqual(['25%', '50%']);
    expect(free.getAttribute('aria-label')).toBe('Meeting Room A is free until 14:00.');
    const busy = spot('Desk D-1')!;
    expect(busy.classList.contains('busy')).toBe(true);
    expect(busy.getAttribute('aria-label')).toBe('Desk D-1 is in use until 11:00.');
    const outOfUse = spot('Projector P-1')!;
    expect(outOfUse.tagName).toBe('SPAN');
    expect(outOfUse.getAttribute('role')).toBe('img');
    expect(outOfUse.classList.contains('unavailable')).toBe(true);
    expect(outOfUse.getAttribute('aria-label')).toBe("Projector P-1 can't be booked: Maintenance.");

    // Not placed yet: listed under the plan. Another floor's resources are left out.
    expect(spot('Camera C-1')).toBeNull();
    expect(text(element.querySelector('.unplaced h2')!)).toBe('Not on the plan yet');
    expect(text(element.querySelector('.unplaced li')!)).toBe(
      'Camera C-1 is free for the rest of the day.',
    );
    expect(element.textContent).not.toContain('Lobby Desk');
    expect(button('Now').disabled).toBe(true);
  });

  it('opens the booking form from the next quarter hour, or from when a resource is free again', async () => {
    await render();

    expect(bookingOf(spot('Meeting Room A')!)).toEqual({
      resourceId: room.id,
      start: iso('10:15'),
      end: iso('11:15'),
    });
    expect(bookingOf(spot('Desk D-1')!)).toEqual({
      resourceId: desk.id,
      start: iso('11:00'),
      end: iso('12:00'),
    });
    expect(bookingOf(element.querySelector('.unplaced a')!)).toEqual({
      resourceId: camera.id,
      start: iso('10:15'),
      end: iso('11:15'),
    });
  });

  it('turns a resource red as soon as someone books it, and back when they move or cancel it', async () => {
    await render();
    const now = booking(room, iso('10:00'), iso('10:45'), { id: 'rsv-new' });

    hub.created$.next(now);
    await fixture.whenStable();
    expect(spot('Meeting Room A')!.classList.contains('busy')).toBe(true);
    expect(spot('Meeting Room A')!.getAttribute('aria-label')).toBe(
      'Meeting Room A is in use until 10:45.',
    );

    hub.updated$.next({ ...now, startDateTime: iso('12:00'), endDateTime: iso('13:00') });
    await fixture.whenStable();
    expect(spot('Meeting Room A')!.getAttribute('aria-label')).toBe(
      'Meeting Room A is free until 12:00.',
    );

    hub.cancelled$.next({ ...deskBooking, status: 'Cancelled' });
    await fixture.whenStable();
    expect(spot('Desk D-1')!.classList.contains('free')).toBe(true);

    // Another day's booking is none of this day's business.
    hub.created$.next(booking(camera, iso('08:00', TUESDAY), iso('09:00', TUESDAY)));
    await fixture.whenStable();
    expect(text(element.querySelector('.unplaced li')!)).toBe(
      'Camera C-1 is free for the rest of the day.',
    );
  });

  it('follows the clock', async () => {
    await render();

    vi.advanceTimersByTime(55 * 60_000);
    await fixture.whenStable();

    expect(spot('Desk D-1')!.classList.contains('free')).toBe(true);
    expect(spot('Desk D-1')!.getAttribute('aria-label')).toBe(
      'Desk D-1 is free for the rest of the day.',
    );
  });

  it('loads the day again after the connection comes back, for what it missed', async () => {
    await render();
    getCalendar.mockReturnValue(of([roomBooking]));

    hub.reconnected$.next();
    await fixture.whenStable();

    expect(getCalendar).toHaveBeenCalledTimes(2);
    expect(getBlocks).toHaveBeenCalledTimes(2);
    expect(spot('Desk D-1')!.classList.contains('free')).toBe(true);
  });

  it("shows the floor at a chosen time with that day's bookings, and goes back to now", async () => {
    getCalendar.mockReturnValue(of([booking(room, iso('09:30', TUESDAY), iso('10:30', TUESDAY))]));
    await render({ at: iso('10:00', TUESDAY) });

    const day = { from: iso('00:00', TUESDAY), to: iso('00:00', '2026-10-14') };
    expect(getCalendar).toHaveBeenCalledWith(day);
    expect(getBlocks).toHaveBeenCalledWith(day);
    expect(element.querySelector<HTMLInputElement>('#at')!.value).toBe('2026-10-13T10:00');
    expect(spot('Meeting Room A')!.getAttribute('aria-label')).toBe(
      'Meeting Room A is in use until 10:30.',
    );
    expect(bookingOf(spot('Meeting Room A')!)).toEqual({
      resourceId: room.id,
      start: iso('10:30', TUESDAY),
      end: iso('11:30', TUESDAY),
    });

    await click(button('Now'));

    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({ queryParams: { at: null }, queryParamsHandling: 'merge' }),
    );
  });

  it('puts the time chosen in the address', async () => {
    await render();
    const field = element.querySelector<HTMLInputElement>('#at')!;
    expect(field.value).toBe('2026-10-12T10:05');

    field.value = '2026-10-12T16:30';
    field.dispatchEvent(new Event('change'));

    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({ queryParams: { at: iso('16:30') } }),
    );
  });

  it('shows a time gone by without offering to book it', async () => {
    await render({ at: iso('09:00') });

    const desk = spot('Desk D-1')!;
    expect(desk.tagName).toBe('SPAN');
    expect(desk.classList.contains('free')).toBe(true);
    expect(desk.getAttribute('aria-label')).toBe('Desk D-1 is free until 10:00.');
    expect(element.querySelector('.plan a, .unplaced a')).toBeNull();
    expect(text(element.querySelector('.unplaced li')!)).toBe(
      'Camera C-1 is free for the rest of the day.',
    );
  });

  it('changes floors through the address, and stands a floor without a plan on a grid', async () => {
    isAdmin.set(false);
    await render();
    const select = element.querySelector<HTMLSelectElement>('#floor')!;
    expect([...select.options].map((o) => text(o))).toEqual(['2nd Floor', '5th Floor']);

    select.value = '2nd Floor';
    select.dispatchEvent(new Event('change'));
    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({ queryParams: { floor: '2nd Floor' } }),
    );

    fixture.componentRef.setInput('floor', '2nd Floor');
    await fixture.whenStable();

    const plan = element.querySelector<HTMLElement>('.plan')!;
    expect(plan.classList.contains('no-plan')).toBe(true);
    expect(plan.style.aspectRatio).toBe('16 / 10');
    expect(element.querySelector('.plan-image')).toBeNull();
    expect(urls.revoked).toEqual(['blob:test-1']);
    expect(text(element.querySelector('.no-plan-note')!)).toBe(
      '2nd Floor has no plan yet, so the resources stand on a grid.',
    );
    expect(text(element.querySelector('.unplaced li')!)).toBe(
      'Lobby Desk is free for the rest of the day.',
    );
  });

  it('tells an Admin where to upload a plan for a floor without one', async () => {
    await render({ floor: '2nd Floor' });

    expect(getPlan).not.toHaveBeenCalled();
    expect(text(element.querySelector('.no-plan-note')!)).toBe(
      '2nd Floor has no plan yet: choose Arrange to upload one. Meanwhile the resources stand on a grid.',
    );
  });

  it('shows the grid when the plan does not load', async () => {
    getPlan.mockReturnValue(throwError(() => new ApiError(404, 'Not Found')));
    await render();

    expect(element.querySelector('.plan-image')).toBeNull();
    expect(element.querySelector('.plan')!.classList.contains('no-plan')).toBe(true);
    expect(spot('Meeting Room A')).not.toBeNull();
  });

  it('says when the floors could not be loaded, and tries again', async () => {
    getFloors.mockReturnValueOnce(throwError(() => new ApiError(500, 'Server error')));
    await render();
    expect(text(element.querySelector('.load-error')!)).toBe(
      'The floor plan could not be loaded. Try again',
    );

    await click(button('Try again'));

    expect(element.querySelector('.load-error')).toBeNull();
    expect(spot('Meeting Room A')).not.toBeNull();
  });

  it('says so when there is nothing to put on a plan', async () => {
    getFloors.mockReturnValue(of([]));
    getResources.mockReturnValue(of([]));
    await render();

    expect(text(element.querySelector('.empty')!)).toBe(
      'There are no resources to show on a plan yet.',
    );
  });

  it('lets go of the plan picture when the page closes', async () => {
    await render();

    fixture.destroy();

    expect(urls.revoked).toEqual(['blob:test-1']);
  });

  describe('arranging', () => {
    it('lets an Admin put a resource on the plan, move it with the arrow keys, and take one off', async () => {
      await render();

      await click(arrangeButton()!);
      expect(arrangeButton()!.getAttribute('aria-pressed')).toBe('true');
      expect(text(arrangeButton()!)).toBe('Done arranging');
      expect(text(element.querySelector('#arrange-title')!)).toBe('Arranging 5th Floor');
      expect(element.querySelectorAll('.plan .movable-spot')).toHaveLength(3);
      expect(spot('Meeting Room A')!.getAttribute('aria-label')).toBe(
        'Move Meeting Room A: drag it, or use the arrow keys',
      );
      expect(element.querySelector('.unplaced')).toBeNull();

      await click(button('Put on the plan', row('Camera C-1')));
      expect(setPosition).toHaveBeenCalledWith(camera.id, { x: 0.5, y: 0.5 });
      const placed = spot('Camera C-1')!;
      expect(position(placed)).toEqual(['50%', '50%']);
      expect(document.activeElement).toBe(placed);

      placed.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
      placed.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'ArrowDown', shiftKey: true, bubbles: true }),
      );
      await fixture.whenStable();
      expect(position(placed)).toEqual(['51%', '55%']);
      expect(setPosition).toHaveBeenCalledTimes(1);

      placed.dispatchEvent(new KeyboardEvent('keyup', { key: 'ArrowDown', bubbles: true }));
      await fixture.whenStable();
      expect(setPosition).toHaveBeenLastCalledWith(camera.id, { x: 0.51, y: 0.55 });

      await click(button('Take off the plan', row('Desk D-1')));
      expect(setPosition).toHaveBeenLastCalledWith(desk.id, null);
      expect(spot('Desk D-1')).toBeNull();
      expect(button('Put on the plan', row('Desk D-1'))).toBeTruthy();

      await click(arrangeButton()!);
      expect(element.querySelector('.arrange-panel')).toBeNull();
      expect(spot('Camera C-1')!.tagName).toBe('A');
    });

    it('moves a resource as far as it is dragged, and saves it where it is let go', async () => {
      await render();
      await click(arrangeButton()!);
      const plan = element.querySelector<HTMLElement>('.plan')!;
      vi.spyOn(plan, 'getBoundingClientRect').mockReturnValue(new DOMRect(100, 50, 800, 400));
      const handle = spot('Meeting Room A')!;
      const pointer = (type: string, clientX: number, clientY: number) =>
        handle.dispatchEvent(
          new PointerEvent(type, { clientX, clientY, pointerId: 1, button: 0, bubbles: true }),
        );

      pointer('pointerdown', 300, 250);
      pointer('pointermove', 500, 150);
      await fixture.whenStable();
      expect(position(handle)).toEqual(['50%', '25%']);
      expect(setPosition).not.toHaveBeenCalled();

      // Past the edge, it stays on the plan.
      pointer('pointerup', 1000, 330);
      await fixture.whenStable();
      expect(setPosition).toHaveBeenCalledWith(room.id, { x: 1, y: 0.7 });
      expect(position(handle)).toEqual(['100%', '70%']);

      // Taken from it, it goes back; and a click alone moves nothing.
      pointer('pointerdown', 900, 330);
      pointer('pointermove', 500, 100);
      pointer('pointercancel', 500, 100);
      pointer('pointerdown', 900, 330);
      pointer('pointerup', 900, 330);
      await fixture.whenStable();
      expect(position(handle)).toEqual(['100%', '70%']);
      expect(setPosition).toHaveBeenCalledTimes(1);
      expect(document.activeElement).toBe(handle);
    });

    it('puts a resource back when it could not be moved', async () => {
      setPosition.mockReturnValue(throwError(() => new ApiError(403, 'Forbidden')));
      await render();
      await click(arrangeButton()!);

      await click(button('Take off the plan', row('Meeting Room A')));

      expect(position(spot('Meeting Room A')!)).toEqual(['25%', '50%']);
      expect(text(element.querySelector('.arrange-panel [role=alert]')!)).toBe(
        'Meeting Room A could not be moved. Try again.',
      );
    });

    it("lets a Manager arrange what belongs to their team alone, but not the floor's plan", async () => {
      isAdmin.set(false);
      isManager.set(true);
      currentUser.set({ id: 'mgr-zeynep' });
      const ours = { ...room, teams: [{ id: 'mgr-zeynep', managerName: 'Zeynep Kaya' }] };
      getResources.mockReturnValue(of([ours, desk, projector, camera]));
      await render();

      await click(arrangeButton()!);

      expect([...element.querySelectorAll('.movable .name')].map((n) => text(n))).toEqual([
        'Meeting Room A',
      ]);
      expect(element.querySelector('.file-button')).toBeNull();
      expect(element.textContent).not.toContain('Remove the plan');
      expect(spot('Meeting Room A')!.tagName).toBe('BUTTON');
      expect(spot('Desk D-1')!.tagName).toBe('A');
    });

    it('is not offered to an Employee, nor to a Manager with nothing of their own on the floor', async () => {
      isAdmin.set(false);
      currentUser.set({ id: 'user-employee' });
      await render();
      expect(arrangeButton()).toBeNull();

      isManager.set(true);
      await fixture.whenStable();
      expect(arrangeButton()).toBeNull();
    });

    describe('the plan', () => {
      let decode: ReturnType<typeof vi.fn>;

      // The test DOM can neither decode pictures nor draw them: both are stood in for. The
      // canvas "writes" its size, so the tests can see what was uploaded.
      beforeEach(() => {
        decode = vi.fn(async () => ({ width: 4800, height: 2700, close: vi.fn() }));
        vi.stubGlobal('createImageBitmap', decode);
        vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
          drawImage: vi.fn(),
          fillRect: vi.fn(),
        } as unknown as CanvasRenderingContext2D);
        vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (
          this: HTMLCanvasElement,
          callback: BlobCallback,
          type?: string,
        ) {
          callback(new Blob([`${this.width}x${this.height}`], { type }));
        });
      });

      async function choose(file: File): Promise<void> {
        const field = element.querySelector<HTMLInputElement>('.file-button input[type=file]')!;
        Object.defineProperty(field, 'files', { value: [file], configurable: true });
        field.dispatchEvent(new Event('change'));
        await settle();
      }

      const drawing = () => new File(['png'], 'plan.png', { type: 'image/png' });

      it('lets an Admin upload a plan, made smaller first, and shows it at once', async () => {
        await render();
        await click(arrangeButton()!);
        expect(text(element.querySelector('.file-button')!)).toBe('Choose another plan');

        await choose(drawing());

        expect(setPlan).toHaveBeenCalledWith(FLOOR, expect.any(Blob));
        const uploaded = setPlan.mock.calls[0][1] as Blob;
        expect(uploaded.type).toBe('image/webp');
        expect(await uploaded.text()).toBe('2400x1350');
        expect(getPlan).toHaveBeenLastCalledWith(FLOOR, '2026-10-12T08:10:00Z');
        expect(element.querySelector('.plan-image')!.getAttribute('src')).toBe('blob:test-2');
        expect(element.querySelector<HTMLElement>('.plan')!.style.aspectRatio).toBe('2400 / 1350');
        expect(urls.revoked).toEqual(['blob:test-1']);
        expect(element.querySelector('.arrange-panel [role=alert]')).toBeNull();
      });

      it("says so when the file isn't a picture, or the API turns the plan down", async () => {
        await render();
        await click(arrangeButton()!);

        decode.mockRejectedValueOnce(new DOMException('The source image could not be decoded.'));
        await choose(new File(['notes'], 'plan.txt', { type: 'text/plain' }));
        expect(setPlan).not.toHaveBeenCalled();
        expect(text(element.querySelector('.arrange-panel [role=alert]')!)).toBe(
          "That file isn't a picture this browser can read.",
        );

        const tooBig = 'The plan can be at most 4096 pixels on its longest side.';
        setPlan.mockReturnValue(
          throwError(() => new ApiError(400, 'Bad Request', null, { Plan: [tooBig] })),
        );
        await choose(drawing());
        expect(text(element.querySelector('.arrange-panel [role=alert]')!)).toBe(tooBig);
        expect(text(element.querySelector('.file-button')!)).toBe('Choose another plan');
      });

      it('removes the plan once the Admin confirms, and the floor stands on its grid', async () => {
        const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
        await render();
        await click(arrangeButton()!);

        await click(button('Remove the plan'));
        expect(confirm).toHaveBeenCalledWith(
          'Remove the plan of 5th Floor? The resources keep their places for the next one.',
        );
        expect(removePlan).not.toHaveBeenCalled();

        confirm.mockReturnValue(true);
        await click(button('Remove the plan'));

        expect(removePlan).toHaveBeenCalledWith(FLOOR);
        expect(element.querySelector('.plan-image')).toBeNull();
        expect(urls.revoked).toEqual(['blob:test-1']);
        expect(text(element.querySelector('.file-button')!)).toBe('Upload a plan');
        // Still the same floor, though it was only shown first for its plan.
        expect(element.querySelector<HTMLSelectElement>('#floor')!.value).toBe(FLOOR);
        expect(spot('Meeting Room A')).not.toBeNull();
      });
    });
  });

  describe('how much each place was used (ADR-0059)', () => {
    const range = (from: string) => ({ from, to: '2026-10-12', timeZone: expect.any(String) });

    it("shows an Admin each place's share of the office's hours over the last 30 days", async () => {
      await render({ view: 'usage' });

      expect(getUsage).toHaveBeenCalledWith(range('2026-09-13'));
      const roomSpot = spot('Meeting Room A')!;
      expect(roomSpot.classList).toContain('heat-level-3');
      expect(text(roomSpot.querySelector('.share')!)).toBe('60%');
      expect(roomSpot.getAttribute('aria-label')).toBe(
        'Meeting Room A - last 30 days: 60% of office hours booked, 12 bookings, 1 no-shows.',
      );
      expect(spot('Desk D-1')!.classList).toContain('heat-level-1');
      // Out of use and not booked at all: dashed, and nothing on the scale.
      const projectorSpot = spot('Projector P-1')!;
      expect(projectorSpot.classList).toContain('heat-level-0');
      expect(projectorSpot.classList).toContain('unavailable');
      expect(text(projectorSpot.querySelector('.share')!)).toBe('0%');
      // Nothing to book from here, and no time to choose.
      expect(element.querySelector('.plan a.spot')).toBeNull();
      expect(element.querySelector('#at')).toBeNull();
      expect(element.querySelector<HTMLSelectElement>('#period')!.value).toBe('30');
      expect([...element.querySelectorAll('.legend li')].map(text)).toEqual([
        '0%',
        '1%–25%',
        '26%–50%',
        '51%–75%',
        '76%–100%',
      ]);
      const report = [...element.querySelectorAll<HTMLAnchorElement>('a')].find((a) =>
        text(a).includes('usage report'),
      )!;
      expect(report.getAttribute('href')).toBe('/admin/reports?from=2026-09-13&to=2026-10-12');
      // The one not on the plan says it in the list below.
      expect(text(element.querySelector('.unplaced li')!)).toBe(
        'Camera C-1 - last 30 days: 0% of office hours booked, 0 bookings, 0 no-shows.',
      );
    });

    it('asks again for another period, and says so when it could not, until it can', async () => {
      getUsage.mockReturnValueOnce(throwError(() => new ApiError(500, 'Server error')));
      await render({ view: 'usage' });

      expect(text(element.querySelector('[role=alert]')!)).toBe(
        'How much each one was used could not be loaded. Try again',
      );
      await click(button('Try again'));
      expect(getUsage).toHaveBeenCalledTimes(2);
      expect(spot('Meeting Room A')!.classList).toContain('heat-level-3');

      const period = element.querySelector<HTMLSelectElement>('#period')!;
      period.value = '7';
      period.dispatchEvent(new Event('change'));
      expect(navigate).toHaveBeenLastCalledWith(
        [],
        expect.objectContaining({ queryParams: { days: '7' } }),
      );
      fixture.componentRef.setInput('days', '7');
      await fixture.whenStable();
      expect(getUsage).toHaveBeenLastCalledWith(range('2026-10-06'));
    });

    it('switches between what the floor is doing and how much it was used, for Admins only', async () => {
      await render();
      await click(arrangeButton()!);

      await click(button('Usage'));

      expect(navigate).toHaveBeenLastCalledWith(
        [],
        expect.objectContaining({ queryParams: { view: 'usage' } }),
      );
      expect(element.querySelector('.arrange-panel')).toBeNull();

      fixture.destroy();
      isAdmin.set(false);
      await render({ view: 'usage' });

      expect(element.querySelector('.view-switch')).toBeNull();
      expect(getUsage).not.toHaveBeenCalled();
      expect(spot('Meeting Room A')!.classList).toContain('free');
    });
  });
});
