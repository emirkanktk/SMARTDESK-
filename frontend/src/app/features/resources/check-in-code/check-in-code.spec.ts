import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../../core/models/api-error.model';
import { NotificationService } from '../../../core/services/notification.service';
import { ResourceService } from '../../../core/services/resource.service';
import { CheckInCodePanel } from './check-in-code';

describe('CheckInCodePanel', () => {
  const printed = { code: 'aaaa', url: 'http://localhost:4200/check-in/res-1?code=aaaa' };
  const fresh = { code: 'bbbb', url: 'http://localhost:4200/check-in/res-1?code=bbbb' };

  let getCheckInCode: ReturnType<typeof vi.fn>;
  let replaceCheckInCode: ReturnType<typeof vi.fn>;
  let success: ReturnType<typeof vi.fn>;
  let error: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<CheckInCodePanel>;
  let element: HTMLElement;

  beforeEach(() => {
    getCheckInCode = vi.fn(() => of(printed));
    replaceCheckInCode = vi.fn(() => of(fresh));
    success = vi.fn();
    error = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ResourceService, useValue: { getCheckInCode, replaceCheckInCode } },
        { provide: NotificationService, useValue: { success, error } },
      ],
    });
  });

  afterEach(() => vi.unstubAllGlobals());

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(CheckInCodePanel);
    fixture.componentRef.setInput('resourceId', 'res-1');
    fixture.componentRef.setInput('resourceName', 'Board Room');
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  function button(text: string): HTMLButtonElement {
    return [...element.querySelectorAll('button')].find((b) => b.textContent!.trim() === text)!;
  }

  it('shows the code, its link, and a page to print it from', async () => {
    await render();

    expect(getCheckInCode).toHaveBeenCalledWith('res-1');
    expect(element.querySelector('svg')!.getAttribute('aria-label')).toBe(
      'QR code for checking in to Board Room',
    );
    const link = element.querySelector<HTMLAnchorElement>('a.link')!;
    expect(link.href).toBe(printed.url);
    const print = [...element.querySelectorAll('a')].find(
      (a) => a.textContent!.trim() === 'Print QR code',
    )!;
    expect(print.getAttribute('href')).toBe('/resources/res-1/check-in-poster');
    expect(print.target).toBe('_blank');
  });

  it('replaces the code only when asked twice, and shows the new one', async () => {
    await render();
    const confirm = vi.fn(() => false);
    vi.stubGlobal('confirm', confirm);

    button('Replace code').click();
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('"Board Room"'));
    expect(replaceCheckInCode).not.toHaveBeenCalled();

    confirm.mockReturnValue(true);
    button('Replace code').click();
    await fixture.whenStable();

    expect(replaceCheckInCode).toHaveBeenCalledWith('res-1');
    expect(element.querySelector<HTMLAnchorElement>('a.link')!.href).toBe(fresh.url);
    expect(success).toHaveBeenCalledWith(expect.stringContaining('new QR code is ready'));
  });

  it('keeps the old code when replacing it fails', async () => {
    replaceCheckInCode.mockReturnValue(throwError(() => new ApiError(500, 'Server error')));
    vi.stubGlobal('confirm', () => true);
    await render();

    button('Replace code').click();
    await fixture.whenStable();

    expect(element.querySelector<HTMLAnchorElement>('a.link')!.href).toBe(printed.url);
    expect(button('Replace code').disabled).toBe(false);
  });

  it('copies the link, and says so when the browser refuses', async () => {
    const writeText = vi.fn(() => Promise.resolve());
    vi.stubGlobal('navigator', { clipboard: { writeText } });
    await render();

    button('Copy link').click();
    await fixture.whenStable();
    expect(writeText).toHaveBeenCalledWith(printed.url);
    expect(success).toHaveBeenCalledWith('Link copied.');

    writeText.mockRejectedValueOnce(new Error('denied'));
    button('Copy link').click();
    await fixture.whenStable();
    expect(error).toHaveBeenCalledWith('The link could not be copied.');
  });

  it('says so when the code could not be loaded', async () => {
    getCheckInCode.mockReturnValue(throwError(() => new ApiError(403, 'Forbidden')));
    await render();

    expect(element.textContent).toContain('The QR code could not be loaded.');
    expect(element.querySelector('svg')).toBeNull();
  });
});
