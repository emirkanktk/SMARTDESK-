import { TestBed } from '@angular/core/testing';
import { Logo } from './logo';

describe('Logo', () => {
  async function render(light = false): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(Logo);
    fixture.componentRef.setInput('light', light);
    await fixture.whenStable();
    return fixture.nativeElement;
  }

  it('reads "SmartDesk", so a link around it is named that', async () => {
    const host = await render();

    expect(host.textContent!.trim()).toBe('SmartDesk');
    expect(host.querySelector('.mark')!.getAttribute('aria-hidden')).toBe('true');
  });

  it('turns white for coloured backgrounds when asked', async () => {
    expect((await render()).classList).not.toContain('light');
    expect((await render(true)).classList).toContain('light');
  });
});
