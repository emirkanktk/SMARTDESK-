import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { fakeJwt } from '../testing/fake-jwt';
import { App } from './app';
import { AuthService } from './core/services/auth.service';
import { ReservationHubService } from './core/services/reservation-hub.service';

const TOKEN_STORAGE_KEY = 'smartdesk_access_token';

describe('App', () => {
  let hub: jasmine.SpyObj<ReservationHubService>;

  beforeEach(async () => {
    localStorage.clear();
    hub = jasmine.createSpyObj<ReservationHubService>('ReservationHubService', ['connect', 'disconnect']);
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ReservationHubService, useValue: hub },
      ],
    }).compileComponents();
  });

  afterEach(() => localStorage.clear());

  it('disconnects the hub when nobody is signed in', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(hub.disconnect).toHaveBeenCalled();
    expect(hub.connect).not.toHaveBeenCalled();
  });

  it('connects the hub on sign-in and disconnects it on sign-out', () => {
    localStorage.setItem(TOKEN_STORAGE_KEY, fakeJwt());
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    expect(hub.connect).toHaveBeenCalled();

    TestBed.inject(AuthService).logout();
    fixture.detectChanges();

    expect(hub.disconnect).toHaveBeenCalled();
  });
});
