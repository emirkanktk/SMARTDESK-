import { TestBed } from '@angular/core/testing';
import { QrCode } from '../components/qr-code/qr-code';
import { qrModules } from './qr';

describe('qrModules', () => {
  const url =
    'http://localhost:4200/check-in/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b?code=0123456789abcdef0123456789abcdef';

  /** Whether the path draws the module at (column, row). */
  function isDark(path: string, column: number, row: number): boolean {
    return path.includes(`M${column} ${row}h1v1h-1z`);
  }

  it('makes a square code, one unit per module', () => {
    const { size, path } = qrModules(url);

    // Sizes go 21, 25, 29, ...: a version's worth of modules.
    expect((size - 21) % 4).toBe(0);
    expect(size).toBeGreaterThanOrEqual(21);
    expect(path).toMatch(/^(M\d+ \d+h1v1h-1z)+$/);
  });

  it('has the three finder squares in their corners', () => {
    const { size, path } = qrModules(url);

    for (const [column, row] of [
      [0, 0],
      [size - 7, 0],
      [0, size - 7],
    ]) {
      // A dark 7x7 ring with a light ring inside it and a dark 3x3 centre.
      expect(isDark(path, column, row)).toBe(true);
      expect(isDark(path, column + 6, row + 6)).toBe(true);
      expect(isDark(path, column + 1, row + 1)).toBe(false);
      expect(isDark(path, column + 3, row + 3)).toBe(true);
    }
  });

  it('draws the same text the same way, and other text differently', () => {
    expect(qrModules(url)).toEqual(qrModules(url));
    expect(qrModules(url.replace('0123', '3210')).path).not.toBe(qrModules(url).path);
  });
});

describe('QrCode', () => {
  it('draws the code on white, with a quiet zone of four modules, under the given name', async () => {
    const fixture = TestBed.createComponent(QrCode);
    fixture.componentRef.setInput('value', 'hello');
    fixture.componentRef.setInput('label', 'QR code for checking in to Board Room');
    await fixture.whenStable();

    const svg: SVGElement = fixture.nativeElement.querySelector('svg');
    const { size, path } = qrModules('hello');
    expect(svg.getAttribute('viewBox')).toBe(`-4 -4 ${size + 8} ${size + 8}`);
    expect(svg.getAttribute('role')).toBe('img');
    expect(svg.getAttribute('aria-label')).toBe('QR code for checking in to Board Room');
    expect(svg.querySelector('rect')!.getAttribute('fill')).toBe('#fff');
    expect(svg.querySelector('path')!.getAttribute('d')).toBe(path);
  });
});
