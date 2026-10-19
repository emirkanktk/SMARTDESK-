import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { AuditEntry, AuditPage } from '../../core/models/audit.model';
import { AccountService } from '../../core/services/account.service';
import { AuditService } from '../../core/services/audit.service';
import { ResourceService } from '../../core/services/resource.service';
import { AuditHistory } from './history';

const utc = (local: string) => new Date(local).toISOString();

function entry(id: number, overrides: Partial<AuditEntry> = {}): AuditEntry {
  return {
    id,
    at: utc('2026-10-17T10:00'),
    actor: 'User',
    userId: 'u-ayse',
    entityType: 'Reservation',
    entityId: `rsv-${id}`,
    action: 'Updated',
    resourceId: 'res-board',
    label: `${utc('2026-10-17T11:00')} ${utc('2026-10-17T12:00')}`,
    changes: [{ property: 'Status', old: 'Confirmed', new: 'Cancelled' }],
    ...overrides,
  };
}

function page(items: AuditEntry[], hasMore = false): AuditPage {
  return {
    items,
    hasMore,
    people: { 'u-ayse': 'Ayşe Yılmaz', 'u-selin': 'Selin Kaya' },
    resources: { 'res-board': 'Board Room' },
  };
}

describe('AuditHistory', () => {
  let getPage: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<AuditHistory>;
  let element: HTMLElement;

  beforeEach(() => {
    getPage = vi.fn(() =>
      of(page([entry(12), entry(11, { actor: 'System', userId: null })], true)),
    );
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuditService, useValue: { getPage } },
        {
          provide: AccountService,
          useValue: {
            getAccounts: () =>
              of([
                { id: 'u-selin', displayName: 'Selin Kaya' },
                { id: 'u-ayse', displayName: 'Ayşe Yılmaz' },
              ]),
          },
        },
        {
          provide: ResourceService,
          useValue: { getResourceTypes: () => of([{ id: 'type-room', name: 'Meeting Room' }]) },
        },
      ],
    });
  });

  async function render(resourceId?: string): Promise<void> {
    fixture = TestBed.createComponent(AuditHistory);
    fixture.componentRef.setInput('resourceId', resourceId);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const text = (el: Element) => el.textContent!.replace(/\s+/g, ' ').trim();

  /** Visible text with a space between elements, which textContent leaves out. */
  function words(el: Element): string {
    const parts: string[] = [];
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    while (walker.nextNode()) parts.push(walker.currentNode.textContent!);
    return parts.join(' ').replace(/\s+/g, ' ').trim();
  }

  const entries = () => Array.from(element.querySelectorAll('.entry')).map(words);

  async function choose(id: string, value: string): Promise<void> {
    const field = element.querySelector<HTMLInputElement | HTMLSelectElement>(id)!;
    field.value = value;
    field.dispatchEvent(new Event(field instanceof HTMLSelectElement ? 'change' : 'input'));
    await fixture.whenStable();
  }

  it('lists who changed what and when, newest first, in words', async () => {
    await render();

    expect(getPage).toHaveBeenCalledWith({
      area: null,
      userId: null,
      resourceId: null,
      from: null,
      to: null,
    });
    expect(entries()[0]).toContain('Booking Board Room · Sat 17 Oct 2026, 11:00-12:00');
    expect(entries()[0]).toContain('Changed');
    expect(entries()[0]).toContain('Ayşe Yılmaz · Sat 17 Oct 2026, 10:00');
    expect(entries()[0]).toContain('Status Confirmed → Cancelled');
    expect(entries()[1]).toContain('SmartDesk, by itself');
  });

  it('asks again for an area, a person and whole days in the screen’s own zone', async () => {
    await render();

    await choose('#history-area', 'bookings');
    await choose('#history-user', 'u-selin');
    await choose('#history-from', '2026-10-17');
    await choose('#history-to', '2026-10-18');

    expect(getPage).toHaveBeenLastCalledWith({
      area: 'bookings',
      userId: 'u-selin',
      resourceId: null,
      from: utc('2026-10-17T00:00'),
      to: utc('2026-10-19T00:00'),
    });
    const people = Array.from(element.querySelectorAll('#history-user option')).map(text);
    expect(people).toEqual(['Anyone', 'Ayşe Yılmaz', 'Selin Kaya']);
  });

  it('shows older entries after the last one shown', async () => {
    await render();
    getPage.mockReturnValue(of(page([entry(10, { userId: 'u-selin' })], false)));

    element.querySelector<HTMLButtonElement>('.more')!.click();
    await fixture.whenStable();

    expect(getPage).toHaveBeenLastCalledWith(expect.objectContaining({ area: null }), 11);
    expect(entries()).toHaveLength(3);
    expect(element.querySelector('.more')).toBeNull();
  });

  it("narrows to one resource's history, and clears back to everything", async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await render('res-board');

    expect(getPage).toHaveBeenCalledWith(expect.objectContaining({ resourceId: 'res-board' }));
    expect(text(element.querySelector('.narrowed')!)).toBe('Only Board Room.');

    element.querySelector<HTMLButtonElement>('form.filters button')!.click();
    await fixture.whenStable();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { resourceId: null },
      queryParamsHandling: 'merge',
    });
  });

  it('says when there is nothing, or when it could not be loaded', async () => {
    getPage.mockReturnValue(of(page([])));
    await render();
    expect(text(element)).toContain('Nothing here. Every change from now on is kept');

    getPage.mockReturnValue(throwError(() => new ApiError(0, 'Unknown')));
    await choose('#history-area', 'accounts');
    expect(element.querySelector('.alert-error')!.textContent).toContain(
      'The history could not be loaded.',
    );
  });
});
