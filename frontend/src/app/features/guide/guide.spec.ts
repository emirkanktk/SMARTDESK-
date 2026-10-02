import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { Role, Roles } from '../../core/constants/roles';
import { AssistantService } from '../../core/services/assistant.service';
import { AuthService } from '../../core/services/auth.service';
import { Guide } from './guide';

describe('Guide', () => {
  let roles: Role[];
  let getStatus: ReturnType<typeof vi.fn>;
  let scrollIntoView: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<Guide>;
  let element: HTMLElement;

  beforeEach(() => {
    roles = [Roles.Employee];
    getStatus = vi.fn(() => of({ enabled: false }));
    scrollIntoView = vi.fn();
    // jsdom draws nothing, so it has no scrolling to do.
    Element.prototype.scrollIntoView = scrollIntoView as unknown as Element['scrollIntoView'];
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'guide', component: Guide }]),
        {
          provide: AuthService,
          useValue: {
            currentUser: signal({ id: 'u-1' }),
            hasRole: (role: Role) => roles.includes(role),
          },
        },
        { provide: AssistantService, useValue: { getStatus } },
      ],
    });
  });

  afterEach(() => {
    delete (Element.prototype as Partial<Element>).scrollIntoView;
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(Guide);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const titles = () =>
    Array.from(element.querySelectorAll('section h2')).map((h) => h.textContent!.trim());
  const text = () => element.textContent!.replace(/\s+/g, ' ');

  it('walks everyone through the app in the order they need it', async () => {
    await render();

    expect(titles()).toEqual([
      'Find a free resource',
      'Book it',
      'Check in at the door',
      'Meetings and your inbox',
      'Running over',
      'Your reservations',
      'Make it yours',
    ]);
  });

  it("gives a manager their team's part, and an admin the office's", async () => {
    roles = [Roles.Manager];
    await render();
    expect(titles()).toContain('Your team');
    expect(titles()).not.toContain('Running the office');

    fixture.destroy();
    roles = [Roles.Admin];
    await render();
    expect(titles()).toContain('Running the office');
    expect(titles()).not.toContain('Your team');
  });

  it('has the assistant only where the server has one', async () => {
    getStatus.mockReturnValue(of({ enabled: true }));
    await render();
    expect(titles().at(-1)).toBe('The assistant');

    fixture.destroy();
    getStatus.mockReturnValue(throwError(() => new Error('offline')));
    await render();
    expect(titles()).not.toContain('The assistant');
  });

  it('says the rules as they are', async () => {
    await render();

    expect(text()).toContain('from 10 minutes before the start until 15 minutes after');
    expect(text()).toContain('Everyone on it is warned 5 minutes before');
    expect(text()).toContain('Make it run 15, 30 or 60 minutes longer');
  });

  it('links each part to where it is done', async () => {
    await render();

    const links = Array.from(element.querySelectorAll<HTMLAnchorElement>('section a.btn')).map(
      (a) => [a.textContent!.trim(), a.getAttribute('href')],
    );
    expect(links).toEqual([
      ['Find a free resource', '/availability'],
      ['Book a resource', '/reservations/new'],
      ['Open your inbox', '/inbox'],
      ['My reservations', '/reservations/mine'],
      ['My profile', '/profile'],
    ]);
  });

  it('lists the parts on top, and a click goes to one and moves the focus there', async () => {
    await render();

    const contents = Array.from(element.querySelectorAll<HTMLAnchorElement>('nav.toc a'));
    expect(contents.map((a) => a.textContent!.trim())).toEqual(titles());
    expect(contents[2].getAttribute('href')).toBe('/#door');

    contents[2].click();
    await fixture.whenStable();

    expect(scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' });
    expect(document.activeElement).toBe(element.querySelector('#door-title'));
  });

  it("opens at a part's address", async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/guide#longer', Guide);

    expect(scrollIntoView).toHaveBeenCalled();
    expect(document.activeElement?.id).toBe('longer-title');
  });
});
