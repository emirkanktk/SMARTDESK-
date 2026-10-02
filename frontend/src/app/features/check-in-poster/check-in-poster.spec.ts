import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../core/models/api-error.model';
import { ResourceService } from '../../core/services/resource.service';
import { fakeResource } from '../../../testing/resource-fixtures';
import { qrModules } from '../../shared/utils/qr';
import { CheckInPoster } from './check-in-poster';

describe('CheckInPoster', () => {
  const code = { code: 'c0de', url: 'http://localhost:4200/check-in/res-1?code=c0de' };
  let getCheckInCode: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    getCheckInCode = vi.fn(() => of(code));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: ResourceService,
          useValue: {
            getResource: () => of(fakeResource({ checkInRequired: true })),
            getCheckInCode,
          },
        },
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
