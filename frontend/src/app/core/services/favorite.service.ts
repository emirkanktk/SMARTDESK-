import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { ApiError } from '../models/api-error.model';
import { Resource } from '../models/resource.model';
import { AuthService } from './auth.service';
import { NotificationService } from './notification.service';

/**
 * The signed-in user's favourite resources (ADR-0051), for every screen that stars them or books
 * from them: loaded once for whoever is signed in, and changed on screen at once, the API told
 * after. A star the API refuses goes back.
 */
@Injectable({ providedIn: 'root' })
export class FavoriteService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly baseUrl = `${environment.apiUrl}/favorites`;

  private readonly list = signal<Resource[]>([]);
  /** By name, as the API lists them: those the user may use, whatever their status. */
  readonly favorites = this.list.asReadonly();
  readonly ids = computed(() => new Set(this.list().map((r) => r.id)));

  /** Whose favourites are in `list`, or being fetched. */
  private loadedFor: string | null = null;

  /** Fetches them once for whoever is signed in, and again once someone else is. */
  load(): void {
    const userId = this.auth.currentUser()?.id ?? null;
    if (!userId || userId === this.loadedFor) {
      return;
    }

    this.loadedFor = userId;
    this.list.set([]);
    this.http.get<Resource[]>(this.baseUrl).subscribe({
      next: (favorites) => {
        if (this.loadedFor === userId) this.list.set(favorites);
      },
      // The next screen that asks tries again.
      error: () => {
        if (this.loadedFor === userId) this.loadedFor = null;
      },
    });
  }

  toggle(resource: Resource): void {
    const url = `${this.baseUrl}/${encodeURIComponent(resource.id)}`;
    if (this.ids().has(resource.id)) {
      this.drop(resource.id);
      // The error interceptor says why; the star comes back.
      this.http.delete<void>(url).subscribe({ error: () => this.keep(resource) });
      return;
    }

    this.keep(resource);
    this.http.put<void>(url, null).subscribe({
      error: (error: unknown) => {
        this.drop(resource.id);
        // A full list is this service's to explain; 404s and the like have a toast already.
        if (error instanceof ApiError && error.status === 409) {
          this.notifications.error(error.detail ?? error.title);
        }
      },
    });
  }

  private keep(resource: Resource): void {
    this.list.update((list) =>
      [...list.filter((r) => r.id !== resource.id), resource].sort((a, b) =>
        a.name.localeCompare(b.name),
      ),
    );
  }

  private drop(resourceId: string): void {
    this.list.update((list) => list.filter((r) => r.id !== resourceId));
  }
}
