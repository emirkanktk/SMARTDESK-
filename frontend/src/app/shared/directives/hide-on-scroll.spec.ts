import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HideOnScroll } from './hide-on-scroll';

@Component({
  imports: [HideOnScroll],
  template: `<header appHideOnScroll>Bar</header>`,
})
class Page {}

describe('HideOnScroll', () => {
  // jsdom has no layout: a 60px bar on a page 3000px tall, in a 800px window.
  const PAGE_HEIGHT = 3000;
  const BOTTOM = PAGE_HEIGHT - window.innerHeight;
  let fixture: ComponentFixture<Page>;
  let bar: HTMLElement;
  // Frames (the directive's and Angular's own) run when the test says so, after the scroll
  // events, as a browser would.
  let frames: FrameRequestCallback[];

  beforeEach(async () => {
    frames = [];
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation((run) => frames.push(run));
    Object.defineProperty(document.documentElement, 'scrollHeight', {
      configurable: true,
      value: PAGE_HEIGHT,
    });
    scrollTo(0);
    fixture = TestBed.createComponent(Page);
    bar = fixture.nativeElement.querySelector('header');
    Object.defineProperty(bar, 'offsetHeight', { configurable: true, value: 60 });
    await fixture.whenStable();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    Reflect.deleteProperty(document.documentElement, 'scrollHeight');
    Reflect.deleteProperty(window, 'scrollY');
  });

  async function scrollTo(y: number): Promise<void> {
    Object.defineProperty(window, 'scrollY', { configurable: true, value: y });
    window.dispatchEvent(new Event('scroll'));
    window.dispatchEvent(new Event('scroll'));
    frames.splice(0).forEach((run) => run(0));
    await fixture?.whenStable();
  }

  const away = () => bar.classList.contains('scrolled-away');

  it('stays while the page is at its top', async () => {
    await scrollTo(40);

    expect(away()).toBe(false);
  });

  it('gets out of the way on the way down, and is back on the way up', async () => {
    await scrollTo(400);
    expect(away()).toBe(true);

    await scrollTo(380);
    expect(away()).toBe(false);

    await scrollTo(900);
    expect(away()).toBe(true);
  });

  it("doesn't flip on a small drift, but small moves add up", async () => {
    await scrollTo(400);
    await scrollTo(397);
    expect(away()).toBe(true);

    await scrollTo(393);
    expect(away()).toBe(false);
  });

  it('is there at the very bottom of the page, even on the way down', async () => {
    await scrollTo(1200);
    expect(away()).toBe(true);

    await scrollTo(BOTTOM);
    expect(away()).toBe(false);
  });

  it('reads an overscroll bounce as the nearest edge', async () => {
    await scrollTo(1200);
    await scrollTo(-30);
    expect(away()).toBe(false);

    await scrollTo(BOTTOM + 50);
    expect(away()).toBe(false);
  });
});
