import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { NavItem } from '../nav-item';
import { TabBar } from './tab-bar';

describe('TabBar', () => {
  const unread = signal(0);
  const openIssues = signal(0);
  const items: NavItem[] = [
    { path: '/dashboard', label: 'Dashboard', icon: 'dashboard' },
    { path: '/inbox', label: 'Inbox', icon: 'inbox', count: unread },
    { path: '/issues', label: 'Problems', icon: 'wrench', count: openIssues, counts: 'new' },
    { path: '/admin', label: 'Admin', icon: 'sliders' },
  ];

  beforeEach(async () => {
    unread.set(0);
    openIssues.set(0);
    await TestBed.configureTestingModule({
      imports: [TabBar],
      providers: [provideRouter([{ path: '**', children: [] }])],
    }).compileComponents();
  });

  let rendered: HTMLElement | undefined;

  async function render(): Promise<ComponentFixture<TabBar>> {
    const fixture = TestBed.createComponent(TabBar);
    fixture.componentRef.setInput('items', items);
    rendered = fixture.nativeElement;
    document.body.appendChild(rendered!);
    await fixture.whenStable();
    return fixture;
  }

  afterEach(() => rendered?.remove());

  const menu = (element: HTMLElement) =>
    Array.from(element.querySelectorAll<HTMLButtonElement>('.tab')).find(
      (tab) => tab.tagName === 'BUTTON',
    )!;
  const sheet = (element: HTMLElement) => element.querySelector<HTMLElement>('[role="dialog"]');

  it('has the screens used most on the move at hand, and Menu', async () => {
    const element: HTMLElement = (await render()).nativeElement;

    const tabs = Array.from(element.querySelectorAll<HTMLAnchorElement>('a.tab'));
    expect(tabs.map((tab) => [tab.textContent!.trim(), tab.getAttribute('href')])).toEqual([
      ['Home', '/dashboard'],
      ['Floor plan', '/floor-plan'],
      ['Book', '/reservations/new'],
      ['Mine', '/reservations/mine'],
    ]);
    expect(menu(element).textContent!.trim()).toBe('Menu');
    expect(menu(element).getAttribute('aria-expanded')).toBe('false');
    expect(sheet(element)).toBeNull();
  });

  it('opens every screen this user may open from Menu, with what they count', async () => {
    unread.set(3);
    const fixture = await render();
    const element: HTMLElement = fixture.nativeElement;
    expect(menu(element).querySelector('.attention')).not.toBeNull();

    menu(element).click();
    await fixture.whenStable();

    expect(menu(element).getAttribute('aria-expanded')).toBe('true');
    expect(sheet(element)!.getAttribute('aria-labelledby')).toBe('tab-menu-title');
    const links = Array.from(sheet(element)!.querySelectorAll<HTMLAnchorElement>('.sheet-link'));
    expect(links.map((link) => link.getAttribute('href'))).toEqual(items.map((item) => item.path));
    expect(links[1].querySelector('.nav-count')!.textContent!.trim()).toBe('3 unread');
    expect(links[2].querySelector('.nav-count')).toBeNull();
    // Into the sheet, where the screens are.
    expect(document.activeElement).toBe(sheet(element)!.querySelector('button, a'));

    openIssues.set(2);
    await fixture.whenStable();
    expect(links[2].querySelector('.nav-count')!.textContent!.trim()).toBe('2 new');
  });

  it('shows no dot on Menu when nothing is waiting', async () => {
    const element: HTMLElement = (await render()).nativeElement;

    expect(menu(element).querySelector('.attention')).toBeNull();
  });

  it('closes on Escape and gives the focus back to Menu', async () => {
    const fixture = await render();
    const element: HTMLElement = fixture.nativeElement;
    menu(element).click();
    await fixture.whenStable();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await fixture.whenStable();

    expect(sheet(element)).toBeNull();
    expect(menu(element).getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(menu(element));
  });

  it('closes when the user goes to another screen, and with Close or the backdrop', async () => {
    const fixture = await render();
    const element: HTMLElement = fixture.nativeElement;
    const open = async () => {
      menu(element).click();
      await fixture.whenStable();
      expect(sheet(element)).not.toBeNull();
    };

    await open();
    await TestBed.inject(Router).navigateByUrl('/inbox');
    await fixture.whenStable();
    expect(sheet(element)).toBeNull();

    await open();
    element.querySelector<HTMLButtonElement>('.close')!.click();
    await fixture.whenStable();
    expect(sheet(element)).toBeNull();

    await open();
    element.querySelector<HTMLElement>('.backdrop')!.click();
    await fixture.whenStable();
    expect(sheet(element)).toBeNull();
  });

  it('keeps Tab going round inside the open sheet', async () => {
    const fixture = await render();
    const element: HTMLElement = fixture.nativeElement;
    menu(element).click();
    await fixture.whenStable();
    const focusable = Array.from(sheet(element)!.querySelectorAll<HTMLElement>('a, button'));
    const first = focusable[0];
    const last = focusable[focusable.length - 1];

    last.focus();
    const forward = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
    last.dispatchEvent(forward);
    expect(forward.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(first);

    const back = new KeyboardEvent('keydown', {
      key: 'Tab',
      shiftKey: true,
      bubbles: true,
      cancelable: true,
    });
    first.dispatchEvent(back);
    expect(back.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(last);
  });

  it("marks the page while it's there, so what's fixed at the bottom moves up above it", async () => {
    const fixture = await render();
    expect(document.body.classList).toContain('has-tab-bar');

    fixture.destroy();
    expect(document.body.classList).not.toContain('has-tab-bar');
  });
});
