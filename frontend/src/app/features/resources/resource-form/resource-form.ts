import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import {
  FormControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import {
  RESOURCE_LIMITS,
  RESOURCE_STATUSES,
  Resource,
  ResourceStatus,
  ResourceType,
} from '../../../core/models/resource.model';
import { NotificationService } from '../../../core/services/notification.service';
import { ResourceService } from '../../../core/services/resource.service';
import { applyServerErrors } from '../../../shared/utils/server-errors';
import { integerValidator } from '../../../shared/validators/integer.validator';

const L = RESOURCE_LIMITS;

/** Create (`/resources/new`) and edit (`/resources/:id/edit`) form for admins. */
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-resource-form',
  styleUrl: './resource-form.scss',
  templateUrl: './resource-form.html',
})
export class ResourceForm implements OnInit {
  private readonly resourceService = inject(ResourceService);
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);

  /** Route parameter; absent when creating. */
  readonly id = input<string>();
  protected readonly isEdit = computed(() => !!this.id());

  protected readonly limits = L;
  protected readonly statuses = RESOURCE_STATUSES;

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
  });

  /** Text box for the next feature; not part of the submitted form. */
  protected readonly newFeature = new FormControl('', { nonNullable: true });
  protected readonly featureError = signal<string | null>(null);

  protected readonly types = signal<ResourceType[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);

  ngOnInit(): void {
    this.resourceService.getResourceTypes().subscribe({
      next: (types) => this.types.set(types),
      error: () => this.formError.set('Resource types could not be loaded.'),
    });

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

  protected removeFeature(feature: string): void {
    this.form.controls.features.setValue(this.features.filter((f) => f !== feature));
    this.form.controls.features.markAsDirty();
    this.featureError.set(null);
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
    const request = id
      ? this.resourceService.update(id, { ...body, status: value.status })
      : this.resourceService.create(body);

    this.saving.set(true);
    this.formError.set(null);
    request.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (saved) => {
        this.notifications.success(`"${saved.name}" was ${id ? 'updated' : 'created'}.`);
        void this.router.navigateByUrl('/resources');
      },
      error: (error: unknown) => this.handleSaveError(error),
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
    if (e['required']) return 'This field is required.';
    if (e['maxlength']) return `Use at most ${e['maxlength'].requiredLength} characters.`;
    if (e['min'] || e['max'])
      return `Enter a number between ${L.capacityMin} and ${L.capacityMax}.`;
    if (e['integer']) return 'Enter a whole number.';
    return 'This value is not valid.';
  }

  private fill(resource: Resource): void {
    this.form.setValue({
      name: resource.name,
      resourceTypeId: resource.resourceTypeId,
      capacity: resource.capacity,
      location: resource.location,
      description: resource.description ?? '',
      status: resource.status,
      features: [...resource.features],
    });
  }

  private handleSaveError(error: unknown): void {
    if (error instanceof ApiError && error.status === 400) {
      const unmatched = applyServerErrors(this.form, error);
      this.formError.set(unmatched.length ? unmatched.join(' ') : null);
      return;
    }
    // Other failures already raised a toast; keep a note on the form as well.
    this.formError.set('The resource could not be saved.');
  }
}

// Mirrors the API's feature rules: at most 20, 1-100 characters, no duplicates ignoring case.
// The case comparison is locale-independent like the API's OrdinalIgnoreCase:
// toLocaleLowerCase() follows the browser's locale and in Turkish can let "WIFI" and "wifi"
// through ("wıfı").
function featureProblem(feature: string, existing: string[]): string | null {
  if (feature.length > L.featureMaxLength) {
    return `A feature can be at most ${L.featureMaxLength} characters.`;
  }
  if (existing.some((f) => f.toLowerCase() === feature.toLowerCase())) {
    return `"${feature}" is already in the list.`;
  }
  if (existing.length >= L.maxFeatures) {
    return `A resource can have at most ${L.maxFeatures} features.`;
  }
  return null;
}
