import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ScrollRegion } from './scroll-region';

@Component({
  imports: [ScrollRegion],
  template: `<div appScrollRegion><table></table></div>`,
})
class Host {}

describe('ScrollRegion', () => {
  // The test DOM lays nothing out: widths are stood in for.
  function widths(box: HTMLElement, scrollWidth: number, clientWidth: number): void {
    Object.defineProperty(box, 'scrollWidth', { configurable: true, get: () => scrollWidth });
    Object.defineProperty(box, 'clientWidth', { configurable: true, get: () => clientWidth });
  }

  let observed: (() => void) | null;

  beforeEach(() => {
    observed = null;
    vi.stubGlobal(
      'ResizeObserver',
      class {
        constructor(callback: () => void) {
          observed = callback;
        }
        observe(): void {}
        disconnect(): void {}
      },
    );
  });

  afterEach(() => vi.unstubAllGlobals());

  async function render(scrollWidth: number, clientWidth: number) {
    const fixture = TestBed.createComponent(Host);
    const box = fixture.nativeElement.querySelector('[appScrollRegion]') as HTMLElement;
    widths(box, scrollWidth, clientWidth);
    await fixture.whenStable();
    return { fixture, box };
  }

  it('takes the focus while its content is wider than it', async () => {
    const { box } = await render(900, 600);

    expect(box.getAttribute('tabindex')).toBe('0');
  });

  it('is no stop on the way while everything fits, and becomes one when it no longer does', async () => {
    const { fixture, box } = await render(600, 600);
    expect(box.hasAttribute('tabindex')).toBe(false);

    widths(box, 700, 600);
    observed!();
    await fixture.whenStable();

    expect(box.getAttribute('tabindex')).toBe('0');
  });
});
