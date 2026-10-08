import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import { issueCategoryLabel } from '../../../core/i18n/labels';
import {
  ISSUE_CATEGORIES,
  ISSUE_LIMITS,
  IssueCategory,
  IssueReport,
  sameProblem,
} from '../../../core/models/issue.model';
import { RESOURCE_PHOTO } from '../../../core/models/resource.model';
import { IssueService } from '../../../core/services/issue.service';
import { preparePhoto } from '../../../shared/utils/photo';

/**
 * The short form for reporting a problem from the page a resource's QR code opens (ADR-0037):
 * what it's about, a few words, a photo straight from the phone's camera if it helps, and
 * whether the resource can be used meanwhile. Someone standing at the door should be done in
 * half a minute.
 */
@Component({
  imports: [ReactiveFormsModule],
  selector: 'app-report-issue',
  styleUrl: './report-issue.scss',
  templateUrl: './report-issue.html',
})
export class ReportIssue {
  readonly resourceId = input.required<string>();
  /** The problems already open there, so the same one isn't sent twice. */
  readonly open = input<readonly IssueReport[]>([]);

  readonly reported = output<IssueReport>();
  /** The API said someone reported it meanwhile: the page should look at the open ones again. */
  readonly alreadyReported = output<void>();
  readonly cancelled = output<void>();

  private readonly issues = inject(IssueService);

  protected readonly categories = ISSUE_CATEGORIES;
  protected readonly limits = ISSUE_LIMITS;
  protected readonly categoryLabel = issueCategoryLabel;

  protected readonly form = inject(FormBuilder).nonNullable.group({
    category: ['' as IssueCategory | '', Validators.required],
    description: [
      '',
      [
        Validators.required,
        Validators.pattern(/\S/),
        Validators.maxLength(ISSUE_LIMITS.descriptionMaxLength),
      ],
    ],
    isBlocking: [false],
  });

  protected readonly photo = signal<{ blob: Blob; preview: string } | null>(null);
  protected readonly preparingPhoto = signal(false);
  protected readonly photoError = signal<string | null>(null);
  protected readonly sending = signal(false);
  protected readonly error = signal<string | null>(null);
  /** Errors show once the form has been sent, not while it's being filled in. */
  protected readonly triedToSend = signal(false);

  private readonly category = toSignal(this.form.controls.category.valueChanges, {
    initialValue: this.form.controls.category.value,
  });

  /** The open report the chosen category would repeat. */
  protected readonly duplicate = computed(() => {
    const category = this.category();
    return category ? sameProblem(this.open(), category) : undefined;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.dropPreview());

    // Opened from a button that's gone now: the first choice takes the focus, so keyboards
    // and screen readers carry on in the form.
    const host = inject<ElementRef<HTMLElement>>(ElementRef);
    afterNextRender(() => host.nativeElement.querySelector<HTMLInputElement>('input')?.focus());
  }

  protected invalid(control: 'category' | 'description'): boolean {
    return this.triedToSend() && this.form.controls[control].invalid;
  }

  /** Makes the picture smaller, as a resource's photo, and shows it before it goes up. */
  protected async choosePhoto(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // So taking or choosing the same one again still counts.
    if (!file) {
      return;
    }

    this.photoError.set(null);
    this.preparingPhoto.set(true);
    try {
      const blob = await preparePhoto(file);
      if (!blob) {
        this.photoError.set(
          $localize`:@@reportIssue.photoUnreadable:This file could not be read as a picture. Take the photo again, or choose a JPEG, PNG or WebP.`,
        );
      } else if (blob.size > RESOURCE_PHOTO.maxBytes) {
        this.photoError.set(
          $localize`:@@reportIssue.photoTooLarge:This picture is too large even when made smaller. Choose another one.`,
        );
      } else {
        this.dropPreview();
        this.photo.set({ blob, preview: URL.createObjectURL(blob) });
      }
    } finally {
      this.preparingPhoto.set(false);
    }
  }

  protected removePhoto(): void {
    this.dropPreview();
    this.photo.set(null);
  }

  protected submit(): void {
    this.triedToSend.set(true);
    const { category, description, isBlocking } = this.form.getRawValue();
    if (this.form.invalid || !category || this.duplicate() || this.sending()) {
      return;
    }

    this.sending.set(true);
    this.error.set(null);
    this.issues
      .report(this.resourceId(), {
        category,
        description: description.trim(),
        isBlocking,
        photo: this.photo()?.blob ?? null,
      })
      .pipe(finalize(() => this.sending.set(false)))
      .subscribe({
        next: (issue) => this.reported.emit(issue),
        error: (error: unknown) => {
          this.error.set(reason(error));
          if (error instanceof ApiError && error.code === 'issue-already-reported') {
            this.alreadyReported.emit();
          }
        },
      });
  }

  private dropPreview(): void {
    const current = this.photo();
    if (current) {
      URL.revokeObjectURL(current.preview);
    }
  }
}

/** What the API said, or a plain "try again" when it said nothing useful. */
function reason(error: unknown): string {
  if (error instanceof ApiError && error.status !== 0 && error.status < 500) {
    const fieldMessage = Object.values(error.fieldErrors)[0]?.[0];
    return fieldMessage ?? error.detail ?? error.title;
  }
  return $localize`:@@reportIssue.failed:The report could not be sent. Check your connection and try again.`;
}
