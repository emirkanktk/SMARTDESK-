import { TestBed } from '@angular/core/testing';
import { Icon, IconName } from './icon';

describe('Icon', () => {
  async function render(name: IconName) {
    const fixture = TestBed.createComponent(Icon);
    fixture.componentRef.setInput('name', name);
    await fixture.whenStable();
    return fixture;
  }

  const path = (host: HTMLElement) => host.querySelector('path')!.getAttribute('d');

  it('draws the named icon as a single path, hidden from screen readers', async () => {
    const host: HTMLElement = (await render('calendar')).nativeElement;

    expect(host.getAttribute('aria-hidden')).toBe('true');
    expect(host.querySelectorAll('svg path')).toHaveLength(1);
    // The calendar's body, an 18 x 16.5 rectangle at (3, 4.5) with 2.5 corners: from the end of
    // the top-left corner, 13 along the top, round the corner and 11.5 down.
    expect(path(host)).toMatch(/^M5\.5 4\.5h13a2\.5 2\.5 0 0 1 2\.5 2\.5v11\.5/);
  });

  it('draws another icon when its name changes', async () => {
    const fixture = await render('check');
    const check = path(fixture.nativeElement);

    fixture.componentRef.setInput('name', 'bell');
    await fixture.whenStable();

    expect(path(fixture.nativeElement)).not.toBe(check);
  });

  it('writes circles and rounded corners as arcs, with no NaN from the arithmetic', async () => {
    for (const name of ['dashboard', 'id-card', 'key', 'clock'] as const) {
      const d = path((await render(name)).nativeElement)!;

      expect(d).toContain('a');
      expect(d).not.toContain('NaN');
    }
  });
});
