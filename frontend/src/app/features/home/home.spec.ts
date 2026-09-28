import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { Home } from './home';

describe('Home', () => {
  let fixture: ComponentFixture<Home>;
  let element: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Home],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(Home);
    element = fixture.nativeElement;
    await fixture.whenStable();
  });

  const texts = (selector: string) =>
    Array.from(element.querySelectorAll(selector)).map((el) => el.textContent!.trim());

  it('says what SmartDesk is for', () => {
    expect(texts('h1')).toEqual(['Book meeting rooms, desks and equipment in seconds.']);
    expect(texts('.feature h3')).toEqual([
      'Find what’s free',
      'No double bookings',
      'A live office calendar',
      'Reminders',
      'Your day at a glance',
      'Tools for admins',
    ]);
    expect(texts('.step h3')).toEqual(['Sign in', 'Pick a time', 'Book it']);
  });

  it('offers sign-in in the bar, the hero and at the end, all to the login page', () => {
    const signIns = Array.from(element.querySelectorAll<HTMLAnchorElement>('a')).filter((a) =>
      a.textContent!.includes('Sign in'),
    );

    expect(signIns).toHaveLength(3);
    for (const link of signIns) {
      expect(link.getAttribute('href')).toBe('/login');
    }
  });

  it('links "How it works" to its section on the page', () => {
    const link = Array.from(element.querySelectorAll<HTMLAnchorElement>('a')).find(
      (a) => a.textContent!.trim() === 'How it works',
    )!;

    expect(link.getAttribute('href')).toBe('#how-it-works');
    expect(element.querySelector('#how-it-works')).not.toBeNull();
  });

  it('lists the demo accounts only in a development build', () => {
    const demo = element.querySelector('.demo');

    expect(!!demo).toBe(!environment.production);
    if (demo) {
      expect(texts('.demo li code')).toEqual([
        'employee1@smartdesk.local',
        'employee2@smartdesk.local',
        'admin@smartdesk.local',
      ]);
      expect(demo.textContent).toContain('SmartDesk123!');
    }
  });

  it('marks the product preview as decoration for screen readers', () => {
    expect(element.querySelector('.preview')!.getAttribute('aria-hidden')).toBe('true');
  });
});
