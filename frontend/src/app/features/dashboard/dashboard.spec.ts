import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/services/auth.service';
import { Dashboard } from './dashboard';

describe('Dashboard', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [{ provide: AuthService, useValue: { displayName: signal('Ayşe Yılmaz') } }],
    }).compileComponents();
  });

  it('greets the signed-in user', async () => {
    const fixture = TestBed.createComponent(Dashboard);
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('Welcome, Ayşe Yılmaz');
  });
});
