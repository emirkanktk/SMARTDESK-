import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { NotificationService } from '../../../core/services/notification.service';
import { PushService, PushState } from '../../../core/services/push.service';
import { PushSettings } from './push-settings';

describe('PushSettings', () => {
  const state = signal<PushState | null>(null);
  let push: {
    state: typeof state;
    refresh: ReturnType<typeof vi.fn>;
    turnOn: ReturnType<typeof vi.fn>;
    turnOff: ReturnType<typeof vi.fn>;
    sendTest: ReturnType<typeof vi.fn>;
  };
  let success: ReturnType<typeof vi.fn>;
  let error: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<PushSettings>;

  beforeEach(async () => {
    state.set(null);
    push = {
      state,
      refresh: vi.fn().mockResolvedValue(undefined),
      turnOn: vi.fn().mockImplementation(async () => state.set('on')),
      turnOff: vi.fn().mockImplementation(async () => state.set('off')),
      sendTest: vi.fn(),
    };
    success = vi.fn();
    error = vi.fn();
    await TestBed.configureTestingModule({
      imports: [PushSettings],
      providers: [
        { provide: PushService, useValue: push },
        { provide: NotificationService, useValue: { success, error } },
      ],
    }).compileComponents();
  });

  async function render(as: PushState | null): Promise<HTMLElement> {
    state.set(as);
    fixture = TestBed.createComponent(PushSettings);
    await fixture.whenStable();
    return fixture.nativeElement;
  }

  const button = (element: HTMLElement, label: string) =>
    Array.from(element.querySelectorAll('button')).find((b) => b.textContent!.trim() === label);

  it('looks where this device stands as it opens', async () => {
    const element = await render(null);

    expect(push.refresh).toHaveBeenCalled();
    expect(element.textContent).toContain('Checking this device…');
  });

  it('turns them on from a click, and says so', async () => {
    const element = await render('off');
    expect(element.textContent).toContain('Off for this device.');

    button(element, 'Turn on for this device')!.click();
    await fixture.whenStable();

    expect(push.turnOn).toHaveBeenCalled();
    expect(success).toHaveBeenCalledWith('Notifications are on for this device.');
    expect(element.textContent).toContain('On for this device.');
  });

  it('sends a test to every device and says how many it went to', async () => {
    push.sendTest.mockReturnValue(of({ devices: 2 }));
    const element = await render('on');

    button(element, 'Send a test')!.click();
    await fixture.whenStable();

    expect(success).toHaveBeenCalledWith('A test notification is on its way to your 2 devices.');
  });

  it('turns them off, and says when that fails', async () => {
    const element = await render('on');

    button(element, 'Turn off')!.click();
    await fixture.whenStable();
    expect(success).toHaveBeenCalledWith('Notifications are off for this device.');

    state.set('on');
    push.turnOff.mockRejectedValue(new Error('offline'));
    await fixture.whenStable();
    button(element, 'Turn off')!.click();
    await fixture.whenStable();
    expect(error).toHaveBeenCalledWith('Notifications could not be turned off. Please try again.');
  });

  it('says why they can be had nowhere else: an iPhone first wants the Home Screen app', async () => {
    expect((await render('install-first')).textContent).toContain(
      'add SmartDesk to the Home Screen',
    );
    expect((await render('blocked')).textContent).toContain('This browser blocks SmartDesk');
    expect((await render('server-off')).textContent).toContain("doesn't send notifications yet");
  });

  it('offers to look again when the device could not be checked', async () => {
    push.refresh.mockRejectedValueOnce(new Error('offline'));
    const element = await render(null);
    await fixture.whenStable();

    expect(element.querySelector('[role="alert"]')!.textContent).toContain('could not be checked');
    button(element, 'Try again')!.click();
    expect(push.refresh).toHaveBeenCalledTimes(2);
  });
});
