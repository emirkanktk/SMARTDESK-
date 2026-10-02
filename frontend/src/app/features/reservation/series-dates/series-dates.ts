import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { SeriesDate } from '../../../core/models/reservation.model';

/**
 * The dates a repeating booking would have (ADR-0035): how many are free, and each date, with
 * why it can't be booked when it can't. Booking skips those.
 */
@Component({
  imports: [DatePipe],
  selector: 'app-series-dates',
  styleUrl: './series-dates.scss',
  templateUrl: './series-dates.html',
})
export class SeriesDates {
  readonly dates = input.required<SeriesDate[]>();

  protected readonly free = computed(() => this.dates().filter((date) => date.free).length);
  protected readonly taken = computed(() => this.dates().length - this.free());
}
