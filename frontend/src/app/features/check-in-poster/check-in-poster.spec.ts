import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { DEFAULT_CHECK_IN_TIMES, setCheckInTimes } from '../../core/models/reservation.model';
import { ResourceService } from '../../core/services/resource.service';
import { fakeResource } from '../../../testing/resource-fixtures';
import { qrModules } from '../../shared/utils/qr';
import { CheckInPoster } from './check-in-poster';

describe('CheckInPoster', () => {
  const code = { code: 'c0de', url: 'http://localhost:4200/check-in/res-1?code=c0de' };
  let getCheckInCode: ReturnType<typeof vi.fn>;
  let getResource: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    getCheckInCode = vi.fn(() => of(code));
    getResource = vi.fn(() => of(fakeResource({ checkInRequired: true })));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ResourceService, useValue: { getResource, getCheckInCode } },
      ],
    });
  });

  afterEach(() => vi.unstubAllGlobals());

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(CheckInPoster);
    fixture.componentRef.setInput('id', 'res-1');
    await fixture.whenStable();
    return fixture.nativeElement;
  }

  it('puts the resource and its QR code on a sheet, with how to use it', async () => {
    const element = await render();

    expect(element.querySelector('h1')!.textContent).toBe('Check in here');
    expect(element.querySelector('.resource-name')!.textContent).toBe('Meeting Room A');
    expect(element.querySelector('.resource-location')!.textContent).toBe('2nd Floor');
    expect(element.querySelector('app-qr-code path')!.getAttribute('d')).toBe(
      qrModules(code.url).path,
    );
    expect(element.querySelector('.how')!.textContent).toContain(
      'from 10 minutes before your start until 15 minutes after',
    );
    expect(element.querySelector('.report')!.textContent).toContain('report the problem');
  });

  it('says a one-minute grace as a minute', async () => {
    setCheckInTimes({ ...DEFAULT_CHECK_IN_TIMES, graceMinutes: 1 });
    try {
      const element = await render();

      expect(element.querySelector('.how')!.textContent).toContain(
        'from 10 minutes before your start until 1 minute after',
      );
    } finally {
      setCheckInTimes(DEFAULT_CHECK_IN_TIMES);
    }
  });

  it("for a resource without check-in, says what scanning is for, and doesn't ask to check in", async () => {
    getResource.mockReturnValue(of(fakeResource({ checkInRequired: false })));
    const element = await render();

    expect(element.querySelector('h1')!.textContent).toBe('Scan with your phone');
    expect(element.textContent).not.toContain('minutes before your start');
    expect(element.textContent).toContain('book it on the spot');
    expect(element.querySelector('.report')!.textContent).toContain('report the problem');
    expect(element.querySelector('app-qr-code svg')!.getAttribute('aria-label')).toBe(
      'QR code for Meeting Room A',
    );
  });

  it('prints from its button', async () => {
    const print = vi.fn();
    vi.stubGlobal('print', print);
    const element = await render();

    element.querySelector<HTMLButtonElement>('.toolbar button')!.click();

    expect(print).toHaveBeenCalled();
  });

  it('says so when the code could not be loaded', async () => {
    getCheckInCode.mockReturnValue(throwError(() => new ApiError(403, 'Forbidden')));
    const element = await render();

    expect(element.textContent).toContain('The QR code could not be loaded.');
    expect(element.querySelector('.sheet')).toBeNull();
    expect(element.querySelector<HTMLButtonElement>('.toolbar button')!.disabled).toBe(true);
  });
});
