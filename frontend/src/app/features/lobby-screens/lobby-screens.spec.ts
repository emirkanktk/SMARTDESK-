import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { LobbyScreen } from '../../core/models/lobby.model';
import { FloorPlanService } from '../../core/services/floor-plan.service';
import { LobbyService } from '../../core/services/lobby.service';
import { NotificationService } from '../../core/services/notification.service';
import { LobbyScreens } from './lobby-screens';

describe('LobbyScreens', () => {
  const now = new Date('2026-11-26T10:00:00');
  const entrance: LobbyScreen = {
    id: 'screen-entrance',
    name: 'Entrance',
    location: null,
    createdAt: '2026-11-20T08:00:00Z',
    lastSeenAt: new Date(now.getTime() - 30_000).toISOString(),
  };
  const kitchen: LobbyScreen = {
    id: 'screen-kitchen',
    name: 'Kitchen',
    location: '5th Floor',
    createdAt: '2026-11-21T08:00:00Z',
    lastSeenAt: null,
  };
  let service: {
    list: ReturnType<typeof vi.fn>;
    create: ReturnType<typeof vi.fn>;
    remove: ReturnType<typeof vi.fn>;
  };
  let notifications: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<LobbyScreens>;
  let element: HTMLElement;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(now);
    service = {
      list: vi.fn(() => of([entrance, kitchen])),
      create: vi.fn((name: string, location: string | null) =>
        of({
          screen: { ...kitchen, id: 'screen-new', name, location },
          link: `http://localhost:4200/lobby#${'d'.repeat(64)}`,
        }),
      ),
      remove: vi.fn(() => of(undefined)),
    };
    notifications = { success: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: LobbyService, useValue: service },
        { provide: NotificationService, useValue: notifications },
        {
          provide: FloorPlanService,
          useValue: {
            getFloors: () =>
              of([
                { location: '2nd Floor', resources: 4, placed: 4, plan: null },
                { location: '5th Floor', resources: 6, placed: 6, plan: null },
              ]),
          },
        },
      ],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(LobbyScreens);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const text = (node: Element) => node.textContent!.replace(/\s+/g, ' ').trim();
  // A row's name, what it shows and its button are boxes of their own, apart on the page.
  const rows = () =>
    [...element.querySelectorAll('.screen')].map((row) =>
      ['.name', '.meta', 'button'].map((part) => text(row.querySelector(part)!)).join(' '),
    );
  async function setUp(name: string, location = ''): Promise<void> {
    const input = element.querySelector<HTMLInputElement>('#screen-name')!;
    input.value = name;
    input.dispatchEvent(new Event('input'));
    const select = element.querySelector<HTMLSelectElement>('#screen-floor')!;
    select.value = location;
    select.dispatchEvent(new Event('change'));
    element.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await fixture.whenStable();
  }

  it('lists the screens: which floor each shows, and whether it is on', async () => {
    await render();

    expect(rows()).toEqual([
      'Entrance The whole office · On now Take down',
      'Kitchen 5th Floor · not opened yet Take down',
    ]);
    expect(
      [...element.querySelectorAll<HTMLOptionElement>('#screen-floor option')].map((o) => o.text),
    ).toEqual(['The whole office', '2nd Floor', '5th Floor']);
  });

  it('sets one up and shows its link once, to copy or open on the screen', async () => {
    await render();

    await setUp('  Lobby  ', '2nd Floor');

    expect(service.create).toHaveBeenCalledWith('Lobby', '2nd Floor');
    const link = element.querySelector<HTMLInputElement>('.new-link input')!;
    expect(link.value).toBe(`http://localhost:4200/lobby#${'d'.repeat(64)}`);
    expect(text(element.querySelector('.new-link')!)).toContain("It's shown only now");
    expect(rows().map((row) => row.split(' ')[0])).toEqual(['Entrance', 'Kitchen', 'Lobby']);
    expect(element.querySelector<HTMLInputElement>('#screen-name')!.value).toBe('');
  });

  it("asks for a name first, and says what the API didn't take", async () => {
    await render();

    await setUp('   ');
    expect(service.create).not.toHaveBeenCalled();
    expect(text(element.querySelector('#screen-name-error')!)).toBe('Give the screen a name.');

    service.create.mockReturnValueOnce(
      throwError(
        () =>
          new ApiError(
            409,
            'Conflict',
            'An office can have at most 20 lobby screens.',
            {},
            'lobby-screens-full',
          ),
      ),
    );
    await setUp('One more');
    expect(text(element.querySelector('[role="alert"]')!)).toBe(
      'An office can have at most 20 lobby screens.',
    );
  });

  it('takes one down after asking, and leaves it when the question is declined', async () => {
    await render();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false);

    element.querySelector<HTMLButtonElement>('.screen button')!.click();
    await fixture.whenStable();
    expect(service.remove).not.toHaveBeenCalled();

    confirm.mockReturnValueOnce(true);
    element.querySelector<HTMLButtonElement>('.screen button')!.click();
    await fixture.whenStable();
    expect(confirm).toHaveBeenLastCalledWith(
      'Take Entrance down? It stops showing the office at once, and its link stops working.',
    );
    expect(service.remove).toHaveBeenCalledWith('screen-entrance');
    expect(rows()).toHaveLength(1);
    expect(notifications.success).toHaveBeenCalledWith('Entrance was taken down.');
  });

  it('says when it could not load the screens, and tries again', async () => {
    service.list.mockReturnValueOnce(throwError(() => new ApiError(0, 'Offline')));
    await render();

    expect(text(element.querySelector('[role="alert"]')!)).toContain(
      'The screens could not be loaded.',
    );
    element.querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    await fixture.whenStable();

    expect(rows()).toHaveLength(2);
  });
});
