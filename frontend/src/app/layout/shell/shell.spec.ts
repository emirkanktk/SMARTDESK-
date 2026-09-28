import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { Shell } from './shell';

describe('Shell', () => {
  const isAdmin = signal(false);
  let logout: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    isAdmin.set(false);
    logout = vi.fn();
    await TestBed.configureTestingModule({
      imports: [Shell],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: { isAdmin, displayName: signal('Ayşe Yılmaz'), logout },
        },
      ],
    }).compileComponents();
  });

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();
    return fixture.nativeElement;
  }

  function navLabels(element: HTMLElement): string[] {
    return Array.from(element.querySelectorAll('.nav-link')).map((a) => a.textContent!.trim());
  }

  it('shows the user and hides the admin link from employees', async () => {
    const element = await render();

    expect(element.textContent).toContain('Ayşe Yılmaz');
    expect(navLabels(element)).not.toContain('Admin');
  });

  it('shows the admin link to admins', async () => {
    isAdmin.set(true);
    const element = await render();

    expect(navLabels(element)).toContain('Admin');
  });

  it('signs out and goes to the front page', async () => {
    const navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const element = await render();

    element.querySelector<HTMLButtonElement>('button')!.click();

    expect(logout).toHaveBeenCalled();
    expect(navigateByUrl).toHaveBeenCalledWith('/');
  });
});
