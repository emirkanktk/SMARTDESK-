import { HttpClient } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiError } from '../models/api-error.model';
import { fakeResource } from '../../../testing/resource-fixtures';
import { AuthService } from './auth.service';
import { FavoriteService } from './favorite.service';
import { NotificationService } from './notification.service';

describe('FavoriteService', () => {
  const url = `${environment.apiUrl}/favorites`;
  const board = fakeResource({ id: 'res-board', name: 'Board Room' });
  const desk = fakeResource({ id: 'res-desk', name: 'Desk 12' });
  const currentUser = signal<{ id: string } | null>({ id: 'u-ayse' });
  let http: {
    get: ReturnType<typeof vi.fn>;
    put: ReturnType<typeof vi.fn>;
    delete: ReturnType<typeof vi.fn>;
  };
  let error: ReturnType<typeof vi.fn>;
  let service: FavoriteService;

  beforeEach(() => {
    currentUser.set({ id: 'u-ayse' });
    http = {
      get: vi.fn(() => of([desk])),
      put: vi.fn(() => of(undefined)),
      delete: vi.fn(() => of(undefined)),
    };
    error = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: HttpClient, useValue: http },
        { provide: AuthService, useValue: { currentUser } },
        { provide: NotificationService, useValue: { error } },
      ],
    });
    service = TestBed.inject(FavoriteService);
  });

  const names = () => service.favorites().map((r) => r.name);

  it('loads them once for whoever is signed in, and again for someone else', () => {
    service.load();
    service.load();
    expect(http.get).toHaveBeenCalledTimes(1);
    expect(http.get).toHaveBeenCalledWith(url);
    expect(names()).toEqual(['Desk 12']);
    expect(service.ids().has('res-desk')).toBe(true);

    currentUser.set({ id: 'u-mehmet' });
    http.get.mockReturnValue(of([]));
    service.load();
    expect(http.get).toHaveBeenCalledTimes(2);
    expect(names()).toEqual([]);
  });

  it('asks nothing for nobody, and asks again after a failed load', () => {
    currentUser.set(null);
    service.load();
    expect(http.get).not.toHaveBeenCalled();

    currentUser.set({ id: 'u-ayse' });
    http.get.mockReturnValueOnce(throwError(() => new ApiError(0, 'Unknown')));
    service.load();
    service.load();
    expect(http.get).toHaveBeenCalledTimes(2);
    expect(names()).toEqual(['Desk 12']);
  });

  it('stars and unstars at once, by name, and tells the API after', () => {
    service.load();
    const put = new Subject<void>();
    http.put.mockReturnValue(put);

    service.toggle(board);
    expect(names()).toEqual(['Board Room', 'Desk 12']);
    expect(http.put).toHaveBeenCalledWith(`${url}/res-board`, null);
    put.next();
    put.complete();

    service.toggle(desk);
    expect(names()).toEqual(['Board Room']);
    expect(http.delete).toHaveBeenCalledWith(`${url}/res-desk`);
  });

  it('puts the star back when the API refuses, saying why when the list is full', () => {
    service.load();
    const full = 'You can keep up to 30 favourites. Remove one before you add another.';
    http.put.mockReturnValue(
      throwError(() => new ApiError(409, 'Conflict', full, {}, 'favorites-full')),
    );
    service.toggle(board);
    expect(names()).toEqual(['Desk 12']);
    expect(error).toHaveBeenCalledWith(full);

    // Other failures have their toast from the error interceptor already.
    http.put.mockReturnValue(throwError(() => new ApiError(404, 'Not Found')));
    service.toggle(board);
    expect(error).toHaveBeenCalledTimes(1);

    http.delete.mockReturnValue(throwError(() => new ApiError(0, 'Unknown')));
    service.toggle(desk);
    expect(names()).toEqual(['Desk 12']);
  });

  it('has one starred again, after unstarring it, once', () => {
    service.load();
    service.toggle(board);
    service.toggle(board);
    service.toggle(board);
    expect([...service.ids()]).toEqual(['res-board', 'res-desk']);
  });
});
