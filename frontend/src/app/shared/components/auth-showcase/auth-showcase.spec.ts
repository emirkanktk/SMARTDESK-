import { TestBed } from '@angular/core/testing';
import { AuthShowcase } from './auth-showcase';

describe('AuthShowcase', () => {
  it('is decoration beside the form: hidden from screen readers, with nothing to focus', async () => {
    const fixture = TestBed.createComponent(AuthShowcase);
    await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement;

    expect(host.getAttribute('aria-hidden')).toBe('true');
    expect(host.querySelectorAll('a, button, input, select, textarea, [tabindex]')).toHaveLength(0);
    expect(host.textContent).toContain('Your office, booked in seconds.');
    expect(host.querySelectorAll('.points li')).toHaveLength(3);
  });
});
