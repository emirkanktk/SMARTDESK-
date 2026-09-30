import { Component } from '@angular/core';
import { Icon } from '../../../shared/components/icon/icon';

interface Slot {
  time: string;
  name: string;
  note: string;
  free: boolean;
  /** The colour of its bar: one per kind of resource. */
  tone: 'indigo' | 'green' | 'amber' | 'pink';
}

/**
 * The front page's picture of the product: a day's bookings in a window, with a confirmation
 * and a reminder floating beside it. An illustration, not live data - resource names are as
 * seeded - so the front page marks it hidden from screen readers.
 */
@Component({
  selector: 'app-hero-preview',
  imports: [Icon],
  templateUrl: './hero-preview.html',
  styleUrl: './hero-preview.scss',
})
export class HeroPreview {
  protected readonly booked = $localize`:@@home.preview.booked:Booked`;
  protected readonly free = $localize`:@@home.preview.free:Free`;

  protected readonly slots: readonly Slot[] = [
    {
      time: '09:30',
      name: 'Meeting Room A',
      note: $localize`:@@home.preview.sprintPlanning:Sprint planning`,
      free: false,
      tone: 'indigo',
    },
    {
      time: '11:00',
      name: 'Desk 3-01',
      note: $localize`:@@home.preview.thirdFloor:3rd Floor`,
      free: true,
      tone: 'green',
    },
    {
      time: '14:00',
      name: 'Company Car',
      note: $localize`:@@home.preview.clientVisit:Client visit`,
      free: false,
      tone: 'amber',
    },
    {
      time: '16:30',
      name: 'Board Room',
      note: $localize`:@@home.preview.fifthFloor:5th Floor`,
      free: true,
      tone: 'pink',
    },
  ];
}
