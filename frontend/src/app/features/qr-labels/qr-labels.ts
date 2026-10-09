import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { catchError, finalize, forkJoin, map, of, switchMap } from 'rxjs';
import { Resource, ResourceType } from '../../core/models/resource.model';
import { AuthService } from '../../core/services/auth.service';
import { ResourceService } from '../../core/services/resource.service';
import { Logo } from '../../shared/components/logo/logo';
import { QrCode } from '../../shared/components/qr-code/qr-code';
import { qrCodeLabel } from '../../shared/utils/qr';
import { canManageResource } from '../../shared/utils/resource-access';
import { resourceTypeLabel } from '../../core/i18n/labels';

/** How many labels an A4 sheet holds: two across, four down. */
export const LABELS_PER_SHEET = 8;

interface Label {
  resource: Resource;
  /** Its QR code's link; null when it couldn't be loaded. */
  url: string | null;
  qrLabel: string;
}

/**
 * The QR codes of many resources at once, as labels to print and put up at their doors
 * (ADR-0030, ADR-0037): eight to an A4 sheet, each with the resource's name, where it is and
 * what scanning it is for. Outside the shell, so only the labels print. Those who may manage a
 * resource print its label: an Admin every one, a Manager their team's own. Resources switched
 * off are left out; the rest can be picked one by one or by type.
 */
@Component({
  imports: [Logo, QrCode, RouterLink],
  selector: 'app-qr-labels',
  host: { role: 'main' },
  styleUrl: './qr-labels.scss',
  templateUrl: './qr-labels.html',
})
export class QrLabels implements OnInit {
  protected readonly typeLabel = resourceTypeLabel;

  private readonly resourceService = inject(ResourceService);
  private readonly auth = inject(AuthService);

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  /** The resources the user may print labels for, by name. */
  protected readonly resources = signal<Resource[]>([]);
  private readonly types = signal<ResourceType[]>([]);
  private readonly codes = signal<ReadonlyMap<string, string>>(new Map());
  /** The resources to print. */
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly perSheet = LABELS_PER_SHEET;

  /** The types the resources here have, for picking a whole type at once. */
  protected readonly typesHere = computed(() => {
    const here = new Set(this.resources().map((r) => r.resourceTypeId));
    return this.types().filter((type) => here.has(type.id));
  });

  protected readonly labels = computed<Label[]>(() =>
    this.resources()
      .filter((resource) => this.selected().has(resource.id))
      .map((resource) => ({
        resource,
        url: this.codes().get(resource.id) ?? null,
        qrLabel: qrCodeLabel(resource.name),
      })),
  );
  protected readonly sheets = computed(() => Math.ceil(this.labels().length / LABELS_PER_SHEET));
  /** Labels whose code couldn't be loaded: they'd print without one. */
  protected readonly missing = computed(() => this.labels().filter((label) => !label.url).length);

  ngOnInit(): void {
    const user = {
      id: this.auth.currentUser()?.id ?? '',
      isAdmin: this.auth.isAdmin(),
      isManager: this.auth.isManager(),
    };
    forkJoin({
      resources: this.resourceService.getResources(),
      types: this.resourceService.getResourceTypes().pipe(catchError(() => of([]))),
    })
      .pipe(
        map(({ resources, types }) => ({
          types,
          resources: resources
            .filter((r) => r.status !== 'Inactive' && canManageResource(r, user))
            .sort((a, b) => a.name.localeCompare(b.name)),
        })),
        switchMap(({ resources, types }) => {
          this.resources.set(resources);
          this.types.set(types);
          this.selected.set(new Set(resources.map((r) => r.id)));
          // One code each; a label whose code fails says so instead of failing the sheet.
          return resources.length === 0
            ? of([])
            : forkJoin(
                resources.map((resource) =>
                  this.resourceService.getCheckInCode(resource.id).pipe(
                    map((code) => [resource.id, code.url] as const),
                    catchError(() => of(null)),
                  ),
                ),
              );
        }),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (codes) => this.codes.set(new Map(codes.filter((c) => c !== null))),
        error: () => this.failed.set(true),
      });
  }

  protected toggle(id: string, event: Event): void {
    const on = (event.target as HTMLInputElement).checked;
    this.selected.update((ids) => {
      const next = new Set(ids);
      if (on) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }

  /** Everything, nothing, or one type only; then the box asks again. */
  protected pick(event: Event): void {
    const box = event.target as HTMLSelectElement;
    const value = box.value;
    const ids =
      value === 'none'
        ? []
        : this.resources()
            .filter((r) => value === 'all' || r.resourceTypeId === value)
            .map((r) => r.id);
    this.selected.set(new Set(ids));
    box.value = '';
  }

  protected print(): void {
    window.print();
  }
}
