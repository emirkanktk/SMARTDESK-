import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PasswordField } from './password-field';

@Component({
  imports: [PasswordField],
  template: `
    <form (submit)="sent = input.type; $event.preventDefault()">
      <app-password-field>
        <input #input id="secret" type="password" class="input" value="Hunter-2026" />
      </app-password-field>
      <button type="submit">Send</button>
    </form>
  `,
})
class Host {
  sent: string | null = null;
}

describe('PasswordField', () => {
  let fixture: ComponentFixture<Host>;
  let element: HTMLElement;

  beforeEach(async () => {
    fixture = TestBed.createComponent(Host);
    element = fixture.nativeElement;
    await fixture.whenStable();
  });

  const input = () => element.querySelector<HTMLInputElement>('#secret')!;
  const eye = () => element.querySelector<HTMLButtonElement>('.reveal')!;

  it('starts hidden, with a button that says what it does and which field it shows', () => {
    expect(input().type).toBe('password');
    expect(eye().getAttribute('aria-label')).toBe('Show password');
    expect(eye().getAttribute('aria-pressed')).toBe('false');
    expect(eye().getAttribute('aria-controls')).toBe('secret');
    expect(eye().type).toBe('button');
  });

  it('shows what was typed, and hides it again', async () => {
    eye().click();
    await fixture.whenStable();

    expect(input().type).toBe('text');
    expect(eye().getAttribute('aria-pressed')).toBe('true');
    expect(eye().getAttribute('aria-label')).toBe('Hide password');

    eye().click();
    await fixture.whenStable();
    expect(input().type).toBe('password');
  });

  it('hides it before the form is sent', async () => {
    eye().click();
    await fixture.whenStable();

    element.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await fixture.whenStable();

    expect(fixture.componentInstance.sent).toBe('password');
    expect(eye().getAttribute('aria-pressed')).toBe('false');
  });

  it('keeps the value and the input the form knows', async () => {
    const before = input();
    eye().click();
    await fixture.whenStable();

    expect(input()).toBe(before);
    expect(input().value).toBe('Hunter-2026');
  });
});
