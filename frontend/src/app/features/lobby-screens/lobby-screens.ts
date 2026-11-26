import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { LobbyScreen, NewLobbyScreen } from '../../core/models/lobby.model';
import { FloorPlanService } from '../../core/services/floor-plan.service';
import { LobbyService } from '../../core/services/lobby.service';
import { NotificationService } from '../../core/services/notification.service';

/** As the API takes it. */
export const LOBBY_SCREEN_NAME_MAX = 60;

/** A screen that asked within this long is on: it asks every 30 seconds. */
const ON_WITHIN_MS = 2 * 60_000;

/**
 * The office's lobby screens (`/admin/lobby-screens`, ADR-0064), for Admins: one is set up with a
 * name and, for one floor only, that floor, and its link is shown once, to open on the TV. Each says
 * when it last asked, so one that's off stands out, and can be taken down, which stops its link at
 * once.
 */
@Component({
  imports: [DatePipe, ReactiveFormsModule, RouterLink],
  selector: 'app-lobby-screens',
  styleUrl: './lobby-screens.scss',
  templateUrl: './lobby-screens.html',
})
export class LobbyScreens implements OnInit {
  private readonly service = inject(LobbyService);
  private readonly floorPlans = inject(FloorPlanService);
  private readonly notifications = inject(NotificationService);

  protected readonly maxName = LOBBY_SCREEN_NAME_MAX;
  protected readonly screens = signal<LobbyScreen[] | null>(null);
  protected readonly floors = signal<string[]>([]);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  /** The screen just set up, with its link: shown until the page is left. */
  protected readonly created = signal<NewLobbyScreen | null>(null);

  protected readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      // Spaces alone aren't a name.
      validators: [
        Validators.required,
        Validators.pattern(/\S/),
        Validators.maxLength(LOBBY_SCREEN_NAME_MAX),
      ],
    }),
    location: new FormControl('', { nonNullable: true }),
  });

  ngOnInit(): void {
    this.load();
    this.floorPlans.getFloors().subscribe({
      next: (floors) => this.floors.set(floors.map((floor) => floor.location)),
      error: () => this.floors.set([]),
    });
  }

  protected load(): void {
    this.loadFailed.set(false);
    this.service.list().subscribe({
      next: (screens) => this.screens.set(screens),
      error: () => this.loadFailed.set(true),
    });
  }

  protected nameError(): string | null {
    const control = this.form.controls.name;
    if (!control.touched || control.valid) return null;
    return control.hasError('maxlength')
      ? $localize`:@@lobbyScreens.nameTooLong:Use at most ${LOBBY_SCREEN_NAME_MAX}:max: characters.`
      : $localize`:@@lobbyScreens.nameRequired:Give the screen a name.`;
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { name, location } = this.form.getRawValue();
    this.saving.set(true);
    this.formError.set(null);
    this.service
      .create(name.trim(), location || null)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (created) => {
          this.created.set(created);
          this.screens.update((list) =>
            [...(list ?? []), created.screen].sort((a, b) => a.name.localeCompare(b.name)),
          );
          this.form.reset();
        },
        error: (error: unknown) => {
          this.formError.set(
            error instanceof ApiError
              ? (Object.values(error.fieldErrors).flat()[0] ?? error.detail ?? error.message)
              : $localize`:@@lobbyScreens.createFailed:The screen could not be set up. Please try again.`,
          );
        },
      });
  }

  protected async copy(link: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(link);
      this.notifications.success($localize`:@@checkInCode.copied:Link copied.`);
    } catch {
      this.notifications.error($localize`:@@checkInCode.copyFailed:The link could not be copied.`);
    }
  }

  protected remove(screen: LobbyScreen): void {
    if (
      !confirm(
        $localize`:@@lobbyScreens.removeConfirm:Take ${screen.name}:name: down? It stops showing the office at once, and its link stops working.`,
      )
    ) {
      return;
    }
    this.service.remove(screen.id).subscribe({
      next: () => {
        this.screens.update((list) => (list ?? []).filter((s) => s.id !== screen.id));
        if (this.created()?.screen.id === screen.id) this.created.set(null);
        this.notifications.success(
          $localize`:@@lobbyScreens.removed:${screen.name}:name: was taken down.`,
        );
      },
      error: () => undefined,
    });
  }

  /** On, when it asked in the last couple of minutes. */
  protected isOn(screen: LobbyScreen): boolean {
    return !!screen.lastSeenAt && Date.now() - new Date(screen.lastSeenAt).getTime() < ON_WITHIN_MS;
  }
}
