import { DestroyRef, Directive, ElementRef, afterNextRender, inject, signal } from '@angular/core';

/**
 * For a box whose content may be wider than it (a table on a narrow screen, or with long names):
 * while it scrolls sideways it can take the keyboard's focus, so the arrow keys scroll it too
 * (WCAG 2.1.1). While everything fits, it's no stop on the way through the page.
 */
@Directive({
  selector: '[appScrollRegion]',
  host: { '[attr.tabindex]': 'scrolls() ? 0 : null' },
})
export class ScrollRegion {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly scrolls = signal(false);

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const box = this.host.nativeElement;
      const measure = () => this.scrolls.set(box.scrollWidth > box.clientWidth);
      measure();
      if (typeof ResizeObserver === 'undefined') {
        return;
      }
      // The box itself, and what's in it: new rows can widen it without the box changing size.
      const observer = new ResizeObserver(measure);
      observer.observe(box);
      if (box.firstElementChild) {
        observer.observe(box.firstElementChild);
      }
      destroyRef.onDestroy(() => observer.disconnect());
    });
  }
}
