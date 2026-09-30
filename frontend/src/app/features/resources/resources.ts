import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, finalize, merge, of, startWith, switchMap, tap } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import {
  RESOURCE_STATUSES,
  Resource,
  ResourceStatus,
  ResourceType,
} from '../../core/models/resource.model';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { ResourceService } from '../../core/services/resource.service';
import { ResourcePhoto } from '../../shared/components/resource-photo/resource-photo';
import { StatusBadge } from '../../shared/components/status-badge/status-badge';
import { canManageResource, teamLabel } from '../../shared/utils/resource-access';
import { resourceStatusLabel } from '../../core/i18n/labels';

/** Resource list for everyone; admins also get create, edit and activate/deactivate. */
@Component({
  imports: [ReactiveFormsModule, RouterLink, StatusBadge, ResourcePhoto],
  selector: 'app-resources',
  styleUrl: './resources.scss',
  templateUrl: './resources.html',
})
export class Resources {
  private readonly resourceService = inject(ResourceService);
  private readonly notifications = inject(NotificationService);

  private readonly auth = inject(AuthService);
  /** Admins add any resource; a manager adds one to their own team (ADR-0022). */
  protected readonly canCreate = computed(() => this.auth.isAdmin() || this.auth.isManager());
  protected readonly teamLabel = teamLabel;
  protected readonly statuses = RESOURCE_STATUSES;
  protected readonly statusLabel = resourceStatusLabel;

  protected readonly filters = inject(NonNullableFormBuilder).group({
    search: [''],
    resourceTypeId: [''],
    status: ['' as ResourceStatus | ''],
  });

  protected readonly types = signal<ResourceType[]>([]);
  protected readonly resources = signal<Resource[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  /** Id of the resource whose status is being changed, to disable its button meanwhile. */
  protected readonly busyId = signal<string | null>(null);

  // Type and status are filtered by the API; the free-text search runs on the loaded list.
  private readonly search = toSignal(this.filters.controls.search.valueChanges, {
    initialValue: '',
  });

  protected readonly visibleResources = computed(() => {
    const term = foldCase(this.search().trim());
    if (!term) {
      return this.resources();
    }
    return this.resources().filter(
      (r) =>
        foldCase(r.name).includes(term) ||
        foldCase(r.location).includes(term) ||
        r.features.some((f) => foldCase(f).includes(term)),
    );
  });

  constructor() {
    this.resourceService
      .getResourceTypes()
      .pipe(takeUntilDestroyed())
      .subscribe({ next: (types) => this.types.set(types), error: () => this.types.set([]) });

    const { resourceTypeId, status } = this.filters.controls;
    merge(resourceTypeId.valueChanges, status.valueChanges)
      .pipe(
        startWith(null),
        tap(() => {
          this.loading.set(true);
          this.loadFailed.set(false);
        }),
        // switchMap drops the response of a filter the user has already changed again.
        switchMap(() =>
          this.resourceService
            .getResources({ resourceTypeId: resourceTypeId.value, status: status.value || null })
            .pipe(
              catchError(() => {
                this.loadFailed.set(true);
                return of([]);
              }),
            ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((resources) => {
        this.resources.set(resources);
        this.loading.set(false);
      });
  }

  /** Edit and activate/deactivate: an admin for any resource, a manager for their team's own. */
  protected canManage(resource: Resource): boolean {
    return canManageResource(resource, {
      id: this.auth.currentUser()?.id ?? '',
      isAdmin: this.auth.isAdmin(),
      isManager: this.auth.isManager(),
    });
  }

  protected clearFilters(): void {
    this.filters.reset();
  }

  protected toggleActive(resource: Resource): void {
    const next: ResourceStatus = resource.status === 'Active' ? 'Inactive' : 'Active';
    const question =
      next === 'Active'
        ? $localize`:@@resources.confirmActivate:Activate "${resource.name}:name:"?`
        : $localize`:@@resources.confirmDeactivate:Deactivate "${resource.name}:name:"?`;
    if (!confirm(question)) {
      return;
    }

    this.busyId.set(resource.id);
    // PATCH changes only the status, so another admin's edit since the list loaded stays.
    this.resourceService
      .changeStatus(resource.id, next)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          const statusFilter = this.filters.controls.status.value;
          this.resources.update((list) =>
            statusFilter && updated.status !== statusFilter
              ? list.filter((r) => r.id !== updated.id)
              : list.map((r) => (r.id === updated.id ? updated : r)),
          );
          this.notifications.success(
            updated.status === 'Active'
              ? $localize`:@@resources.nowActive:"${updated.name}:name:" is now active.`
              : $localize`:@@resources.nowInactive:"${updated.name}:name:" is now inactive.`,
          );
        },
        // 403/404/5xx already raise a toast in the error interceptor; a 409 (bookings still to
        // come, issue #41) is this screen's to explain.
        error: (error: unknown) => {
          if (error instanceof ApiError && error.status === 409) {
            this.notifications.error(error.detail ?? error.title);
          }
        },
      });
  }
}

// Locale-independent on purpose: toLocaleLowerCase() follows the browser's locale, and in
// Turkish that can turn "I" into dotless "ı", so "hdmi" would not match "HDMI". Same rule
// as the API's OrdinalIgnoreCase.
function foldCase(value: string): string {
  return value.toLowerCase();
}
