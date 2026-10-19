import { TestBed } from '@angular/core/testing';
import { ConnectivityService } from './connectivity.service';

describe('ConnectivityService', () => {
  it('follows the browser going offline and coming back', () => {
    const service = TestBed.inject(ConnectivityService);
    expect(service.online()).toBe(true);

    window.dispatchEvent(new Event('offline'));
    expect(service.online()).toBe(false);

    window.dispatchEvent(new Event('online'));
    expect(service.online()).toBe(true);
  });

  it('stops listening once the app is gone', () => {
    const service = TestBed.inject(ConnectivityService);

    TestBed.resetTestingModule();
    window.dispatchEvent(new Event('offline'));

    expect(service.online()).toBe(true);
    window.dispatchEvent(new Event('online'));
  });
});
