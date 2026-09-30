import { TestBed } from '@angular/core/testing';
import { ThemeSwitch } from './theme-switch';

describe('ThemeSwitch', () => {
  afterEach(() => {
    document.documentElement.removeAttribute('data-theme');
    localStorage.clear();
  });

  async function render() {
    const fixture = TestBed.createComponent(ThemeSwitch);
    await fixture.whenStable();
    return { fixture, button: fixture.nativeElement.querySelector('button') as HTMLButtonElement };
  }

  it('is a "Dark mode" toggle that offers the other theme', async () => {
    const { button } = await render();

    expect(button.getAttribute('aria-label')).toBe('Dark mode');
    expect(button.getAttribute('aria-pressed')).toBe('false');
    expect(button.title).toBe('Switch to dark mode');
  });

  it('switches the page to dark and back', async () => {
    const { fixture, button } = await render();

    button.click();
    await fixture.whenStable();
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(button.getAttribute('aria-pressed')).toBe('true');
    expect(button.title).toBe('Switch to light mode');

    button.click();
    await fixture.whenStable();
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    expect(button.getAttribute('aria-pressed')).toBe('false');
  });
});
