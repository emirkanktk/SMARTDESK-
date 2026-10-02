import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { CHECK_IN } from '../../core/models/reservation.model';
import { CheckInCode, Resource } from '../../core/models/resource.model';
import { ResourceService } from '../../core/services/resource.service';
import { Logo } from '../../shared/components/logo/logo';
import { QrCode } from '../../shared/components/qr-code/qr-code';

/**
 * A resource's QR code as a sheet to print and put up at the door (ADR-0030), at
 * `/resources/:id/check-in-poster`. Outside the shell, so only the sheet prints.
 */
@Component({
  imports: [Logo, QrCode, RouterLink],
  selector: 'app-check-in-poster',
  host: { role: 'main' },
  styleUrl: './check-in-poster.scss',
  templateUrl: './check-in-poster.html',
})
export class CheckInPoster implements OnInit {
  /** Route parameter. */
  readonly id = input.required<string>();

  private readonly resourceService = inject(ResourceService);

  protected readonly checkIn = CHECK_IN;
  protected readonly resource = signal<Resource | null>(null);
  protected readonly code = signal<CheckInCode | null>(null);
  protected readonly failed = signal(false);
  protected readonly qrLabel = computed(
    () =>
      $localize`:@@checkInCode.qrLabel:QR code for checking in to ${this.resource()?.name ?? ''}:resource:`,
  );

  ngOnInit(): void {
    forkJoin({
      resource: this.resourceService.getResource(this.id()),
      code: this.resourceService.getCheckInCode(this.id()),
    }).subscribe({
      next: ({ resource, code }) => {
        this.resource.set(resource);
        this.code.set(code);
      },
      error: () => this.failed.set(true),
    });
  }

  protected print(): void {
    window.print();
  }
}
