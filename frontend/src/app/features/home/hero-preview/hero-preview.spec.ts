import { TestBed } from '@angular/core/testing';
import { HeroPreview } from './hero-preview';

describe('HeroPreview', () => {
  let element: HTMLElement;

  beforeEach(async () => {
    const fixture = TestBed.createComponent(HeroPreview);
    await fixture.whenStable();
    element = fixture.nativeElement;
  });

  const texts = (selector: string) =>
    Array.from(element.querySelectorAll(selector)).map((el) => el.textContent!.trim());

  it("pictures a day's bookings, taken and free", () => {
    expect(texts('.slot strong')).toEqual([
      'Meeting Room A',
      'Desk 3-01',
      'Company Car',
      'Board Room',
    ]);
    expect(texts('.state')).toEqual(['Booked', 'Free', 'Booked', 'Free']);
    expect(texts('.live')).toEqual(['Live']);
  });

  it('floats a confirmation and a reminder beside it', () => {
    expect(texts('.float strong')).toEqual(['Reserved', 'Starts in 10 minutes']);
  });
});
