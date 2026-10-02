import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SeriesDate } from '../../../core/models/reservation.model';
import { SeriesDates } from './series-dates';

const date = (day: number, free = true): SeriesDate => ({
  startDateTime: new Date(2026, 9, day, 10, 0).toISOString(),
  endDateTime: new Date(2026, 9, day, 11, 0).toISOString(),
  free,
  code: free ? null : 'reservation-overlaps',
  reason: free ? null : 'Meeting Room A is already reserved for part of that time.',
});

describe('SeriesDates', () => {
  let fixture: ComponentFixture<SeriesDates>;

  async function render(dates: SeriesDate[]): Promise<HTMLElement> {
    fixture = TestBed.createComponent(SeriesDates);
    fixture.componentRef.setInput('dates', dates);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  const text = (element: HTMLElement, selector: string) =>
    element.querySelector(selector)!.textContent!.replace(/\s+/g, ' ').trim();

  it('says when every date is free, and lists them', async () => {
    const element = await render([date(12), date(19), date(26)]);

    expect(text(element, '.summary')).toBe('3 dates, all free.');
    expect(Array.from(element.querySelectorAll('li .date')).map((d) => d.textContent)).toEqual([
      'Mon 12 Oct 2026',
      'Mon 19 Oct 2026',
      'Mon 26 Oct 2026',
    ]);
    expect(element.querySelector('.taken')).toBeNull();
  });

  it("marks the dates that aren't free, with why", async () => {
    const element = await render([date(12), date(19, false), date(26)]);

    expect(text(element, '.summary')).toBe("1 of the 3 dates aren't free; booking skips them.");
    expect(element.querySelector('.summary')!.classList).toContain('problem');
    const taken = element.querySelectorAll('li.taken');
    expect(taken).toHaveLength(1);
    expect(taken[0].textContent).toContain('Not free:');
    expect(taken[0].textContent).toContain(
      'Meeting Room A is already reserved for part of that time.',
    );
  });

  it('says when none is free', async () => {
    const element = await render([date(12, false), date(19, false)]);

    expect(text(element, '.summary')).toBe('None of these 2 dates is free.');
  });
});
