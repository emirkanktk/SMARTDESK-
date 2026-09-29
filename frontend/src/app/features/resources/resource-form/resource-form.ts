import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import {
  AbstractControl,
  FormControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize, of, switchMap, tap } from 'rxjs';
import { Roles } from '../../../core/constants/roles';
import { ApiError } from '../../../core/models/api-error.model';
import {
  RESOURCE_LIMITS,
  RESOURCE_STATUSES,
  Resource,
  ResourceStatus,
  ResourceTeam,
  ResourceType,
} from '../../../core/models/resource.model';
import { AccountService } from '../../../core/services/account.service';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/services/notification.service';
import { ResourceService } from '../../../core/services/resource.service';
import { resourceStatusLabel } from '../../../core/i18n/labels';
import { canManageResource, teamName } from '../../../shared/utils/resource-access';
import { applyServerErrors } from '../../../shared/utils/server-errors';
import { integerValidator } from '../../../shared/validators/integer.validator';

const L = RESOURCE_LIMITS;

/** A team an admin can give the resource to: its manager's id and name. */
interface TeamOption {
  id: string;
  name: string;
  /** Still on the resource, but no longer an active manager's team: can only be unticked. */
  inactive: boolean;
}

/**
 * Create (`/resources/new`) and edit (`/resources/:id/edit`) form. Admins pick the teams that
 * may use the resource (none: shared by everyone); a manager's resource always belongs to their
 * own team, and they can only edit one that belongs to their team alone (ADR-0022).
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-resource-form',
  styleUrl: './resource-form.scss',
  templateUrl: './resource-form.html',
})
export class ResourceForm implements OnInit {
  private readonly resourceService = inject(ResourceService);
  private readonly accountService = inject(AccountService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);

  /** Route parameter; absent when creating. */
  readonly id = input<string>();
  protected readonly isEdit = computed(() => !!this.id());
  protected readonly isAdmin = this.auth.isAdmin;

  protected readonly limits = L;
  protected readonly statuses = RESOURCE_STATUSES;
  protected readonly statusLabel = resourceStatusLabel;
  protected readonly teamName = teamName;

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(L.nameMaxLength)]],
    resourceTypeId: ['', Validators.required],
    capacity: [
      1,
      [
        Validators.required,
        Validators.min(L.capacityMin),
        Validators.max(L.capacityMax),
        integerValidator,
      ],
    ],
    location: ['', [Validators.required, Validators.maxLength(L.locationMaxLength)]],
    description: ['', Validators.maxLength(L.descriptionMaxLength)],
    status: ['Active' as ResourceStatus],
    features: [[] as string[]],
    teamIds: new FormControl<string[]>([], { nonNullable: true, validators: teamListValidator }),
  });

  /** Text box for the next feature; not part of the submitted form. */
  protected readonly newFeature = new FormControl('', { nonNullable: true });
  protected readonly featureError = signal<string | null>(null);

  protected readonly types = signal<ResourceType[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  /** A resource a manager may see but not change: shared, or used by other teams too. */
  protected readonly readOnly = signal(false);

  /** Active managers, for an admin's team picker; null until they arrive. */
  private readonly managers = signal<TeamOption[] | null>(null);
  protected readonly managersFailed = signal(false);
  /** The teams the resource had when loaded, so an unchanged list isn't sent again. */
  private readonly loadedTeams = signal<ResourceTeam[]>([]);
  protected readonly teamOptions = computed<TeamOption[]>(() => {
    const active = this.managers();
    const others = this.loadedTeams()
      .filter((team) => !active?.some((m) => m.id === team.id))
      .map((team) => ({ id: team.id, name: team.managerName, inactive: active !== null }));
    return [...(active ?? []), ...others].sort((a, b) => a.name.localeCompare(b.name));
  });

  ngOnInit(): void {
    this.resourceService.getResourceTypes().subscribe({
      next: (types) => this.types.set(types),
      error: () =>
        this.formError.set(
          $localize`:@@resourceForm.typesFailed:Resource types could not be loaded.`,
        ),
    });

    if (this.isAdmin()) {
      this.accountService.getAccounts({ role: Roles.Manager, status: 'Active' }).subscribe({
        next: (managers) =>
          this.managers.set(
            managers.map((m) => ({ id: m.id, name: m.displayName, inactive: false })),
          ),
        error: () => this.managersFailed.set(true),
      });
    }

    const id = this.id();
    if (id) {
      this.loading.set(true);
      this.resourceService
        .getResource(id)
        .pipe(finalize(() => this.loading.set(false)))
        .subscribe({
          next: (resource) => this.fill(resource),
          error: () => this.loadFailed.set(true),
        });
    }
  }

  protected get features(): string[] {
    return this.form.controls.features.value;
  }

  protected addFeature(event?: Event): void {
    event?.preventDefault(); // Enter in the feature box must not submit the whole form.
    const feature = this.newFeature.value.trim();
    if (!feature) {
      return;
    }

    const error = featureProblem(feature, this.features);
    this.featureError.set(error);
    if (error) {
      return;
    }

    this.form.controls.features.setValue([...this.features, feature]);
    this.form.controls.features.markAsDirty();
    this.newFeature.reset();
  }

  protected removeLabel(feature: string): string {
    return $localize`:@@resourceForm.removeFeature:Remove ${feature}:feature:`;
  }

  protected removeFeature(feature: string): void {
    this.form.controls.features.setValue(this.features.filter((f) => f !== feature));
    this.form.controls.features.markAsDirty();
    this.featureError.set(null);
  }

  protected isTeamSelected(id: string): boolean {
    return this.form.controls.teamIds.value.includes(id);
  }

  protected toggleTeam(id: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const control = this.form.controls.teamIds;
    const others = control.value.filter((t) => t !== id);
    control.setValue(checked ? [...others, id] : others);
    control.markAsDirty();
    control.markAsTouched();
  }

  protected teamError(): string | null {
    const errors = this.form.controls.teamIds.errors;
    if (errors?.['server']) return errors['server'] as string;
    if (errors?.['maxTeams']) {
      return $localize`:@@resourceForm.maxTeams:A resource can belong to at most ${L.maxTeams}:max: teams.`;
    }
    return null;
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const body = {
      name: value.name.trim(),
      resourceTypeId: value.resourceTypeId,
      capacity: Number(value.capacity),
      location: value.location.trim(),
      description: value.description.trim() || null,
      features: value.features,
    };

    const id = this.id();
    // Changed teams make an edit two calls: the details can be saved while the teams fail.
    let detailsSaved = false;
    const request = id
      ? this.resourceService.update(id, { ...body, status: value.status }).pipe(
          tap(() => (detailsSaved = true)),
          switchMap((saved) =>
            this.teamsChanged(value.teamIds)
              ? this.resourceService.setTeams(id, value.teamIds)
              : of(saved),
          ),
        )
      : this.resourceService.create(this.isAdmin() ? { ...body, teamIds: value.teamIds } : body);

    this.saving.set(true);
    this.formError.set(null);
    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (saved) => {
        this.notifications.success(
          id
            ? $localize`:@@resourceForm.updated:"${saved.name}:name:" was updated.`
            : $localize`:@@resourceForm.created:"${saved.name}:name:" was created.`,
        );
        void this.router.navigateByUrl('/resources');
      },
      error: (error: unknown) => this.handleSaveError(error, detailsSaved),
    });
  }

  /** First validation message for a control, once the user has touched it. */
  protected errorFor(
    name: 'name' | 'resourceTypeId' | 'capacity' | 'location' | 'description',
  ): string | null {
    const control = this.form.controls[name];
    if (!control.touched || !control.errors) {
      return null;
    }
    const e = control.errors;
    if (e['server']) return e['server'] as string;
    if (e['required']) return $localize`:@@field.required:This field is required.`;
    if (e['maxlength']) {
      return $localize`:@@field.maxLength:Use at most ${e['maxlength'].requiredLength}:max: characters.`;
    }
    if (e['min'] || e['max']) {
      return $localize`:@@field.numberBetween:Enter a number between ${L.capacityMin}:min: and ${L.capacityMax}:max:.`;
    }
    if (e['integer']) return $localize`:@@field.wholeNumber:Enter a whole number.`;
    return $localize`:@@field.invalid:This value is not valid.`;
  }

  private fill(resource: Resource): void {
    const user = this.auth.currentUser();
    this.readOnly.set(
      !canManageResource(resource, {
        id: user?.id ?? '',
        isAdmin: this.isAdmin(),
        isManager: this.auth.isManager(),
      }),
    );
    this.loadedTeams.set(resource.teams);
    this.form.setValue({
      name: resource.name,
      resourceTypeId: resource.resourceTypeId,
      capacity: resource.capacity,
      location: resource.location,
      description: resource.description ?? '',
      status: resource.status,
      features: [...resource.features],
      teamIds: resource.teams.map((team) => team.id),
    });
  }

  /** Only an admin changes teams, and only a list that differs from the loaded one is sent. */
  private teamsChanged(teamIds: string[]): boolean {
    const loaded = this.loadedTeams().map((team) => team.id);
    return (
      this.isAdmin() &&
      (teamIds.length !== loaded.length || teamIds.some((id) => !loaded.includes(id)))
    );
  }

  private handleSaveError(error: unknown, detailsSaved: boolean): void {
    const partly = detailsSaved
      ? $localize`:@@resourceForm.teamsNotSaved:Your other changes were saved, but the teams were not.`
      : null;
    if (error instanceof ApiError && error.status === 400) {
      const unmatched = applyServerErrors(this.form, error);
      const message = [partly, ...unmatched].filter(Boolean).join(' ');
      this.formError.set(message || null);
      return;
    }
    if (error instanceof ApiError && error.status === 409) {
      // E.g. switching it off while bookings are still to come (issue #41).
      this.formError.set(error.detail ?? error.title);
      return;
    }
    // Other failures already raised a toast; keep a note on the form as well.
    this.formError.set(
      partly ?? $localize`:@@resourceForm.saveFailed:The resource could not be saved.`,
    );
  }
}

// Mirrors the API's limit on how many teams one resource can belong to.
function teamListValidator(control: AbstractControl<string[]>): ValidationErrors | null {
  return control.value.length > L.maxTeams ? { maxTeams: true } : null;
}

// Mirrors the API's feature rules: at most 20, 1-100 characters, no duplicates ignoring case.
// The case comparison is locale-independent like the API's OrdinalIgnoreCase:
// toLocaleLowerCase() follows the browser's locale and in Turkish can let "WIFI" and "wifi"
// through ("wıfı").
function featureProblem(feature: string, existing: string[]): string | null {
  if (feature.length > L.featureMaxLength) {
    return $localize`:@@resourceForm.featureTooLong:A feature can be at most ${L.featureMaxLength}:max: characters.`;
  }
  if (existing.some((f) => f.toLowerCase() === feature.toLowerCase())) {
    return $localize`:@@resourceForm.featureDuplicate:"${feature}:feature:" is already in the list.`;
  }
  if (existing.length >= L.maxFeatures) {
    return $localize`:@@resourceForm.tooManyFeatures:A resource can have at most ${L.maxFeatures}:max: features.`;
  }
  return null;
}
