import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AnnouncementCard, AnnouncementView, announcementIcon } from './announcement-card';

describe('AnnouncementCard', () => {
  let fixture: ComponentFixture<AnnouncementCard>;
  let element: HTMLElement;

  async function render(announcement: AnnouncementView, dismissible = false): Promise<void> {
    fixture = TestBed.createComponent(AnnouncementCard);
    fixture.componentRef.setInput('announcement', announcement);
    fixture.componentRef.setInput('dismissible', dismissible);
    element = fixture.nativeElement;
    await fixture.whenStable();
  }

  const text = (selector: string) => element.querySelector(selector)?.textContent?.trim();

  it('says what kind it is, with its title, text and who posted it when', async () => {
    await render({
      title: 'Breakfast on Friday',
      body: 'Croissants on the 5th floor.\nFrom 9:00.',
      tone: 'Good',
      authorName: 'Zeynep Kaya',
      createdAt: new Date(2026, 10, 27, 10, 0).toISOString(),
    });

    expect(element.dataset['tone']).toBe('Good');
    expect(text('.kind')).toBe('Good news');
    expect(text('h3')).toBe('Breakfast on Friday');
    expect(text('.body')).toBe('Croissants on the 5th floor.\nFrom 9:00.');
    expect(text('.meta')).toBe('Zeynep Kaya · Fri 27 Nov');
    expect(element.querySelector('.dismiss')).toBeNull();
    expect(element.querySelector('.more')).toBeNull();
  });

  it('shows a title alone, and nothing for who posted it while it is being written', async () => {
    await render({ title: 'Lifts out of order', body: null, tone: 'Warning' });

    expect(text('.kind')).toBe('Heads-up');
    expect(element.querySelector('.body')).toBeNull();
    expect(element.querySelector('.meta')).toBeNull();
  });

  it('shows the start of a long text until the rest is asked for', async () => {
    await render({ title: 'Moving day', body: 'A long story. '.repeat(30), tone: 'Info' });
    const more = element.querySelector<HTMLButtonElement>('.more')!;

    expect(element.querySelector('.body')!.classList).toContain('clamped');
    expect(more.getAttribute('aria-expanded')).toBe('false');

    more.click();
    await fixture.whenStable();

    expect(element.querySelector('.body')!.classList).not.toContain('clamped');
    expect(more.getAttribute('aria-expanded')).toBe('true');
    expect(more.textContent!.trim()).toBe('Show less');
  });

  it('can be hidden, with a button that says which one it hides', async () => {
    await render({ title: 'Breakfast on Friday', body: null, tone: 'Info' }, true);
    const dismissed = vi.fn();
    fixture.componentInstance.dismissed.subscribe(dismissed);
    const button = element.querySelector<HTMLButtonElement>('.dismiss')!;

    expect(button.getAttribute('aria-label')).toBe('Hide Breakfast on Friday');
    button.click();

    expect(dismissed).toHaveBeenCalledTimes(1);
  });

  it('gives each look its own icon', () => {
    expect([
      announcementIcon('Info'),
      announcementIcon('Good'),
      announcementIcon('Warning'),
    ]).toEqual(['megaphone', 'sparkles', 'alert']);
  });
});
