import { TestBed } from '@angular/core/testing';
import { APP_LANGUAGE, Language, storedLanguage } from '../../../core/i18n/language';
import { LanguageSwitch, reloadPage } from './language-switch';

describe('LanguageSwitch', () => {
  let reload: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    reload = vi.spyOn(reloadPage, 'run').mockImplementation(() => undefined);
  });

  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  function render(current: Language): HTMLElement {
    TestBed.configureTestingModule({
      imports: [LanguageSwitch],
      providers: [{ provide: APP_LANGUAGE, useValue: current }],
    });
    const fixture = TestBed.createComponent(LanguageSwitch);
    fixture.detectChanges();
    return fixture.nativeElement;
  }

  const button = (element: HTMLElement, code: Language) =>
    element.querySelector<HTMLButtonElement>(`button[lang="${code}"]`)!;

  it('marks the language the app is in', () => {
    const element = render('tr');

    expect(button(element, 'tr').getAttribute('aria-pressed')).toBe('true');
    expect(button(element, 'en').getAttribute('aria-pressed')).toBe('false');
    expect(button(element, 'tr').getAttribute('aria-label')).toBe('Türkçe');
    expect(button(element, 'en').getAttribute('aria-label')).toBe('English');
  });

  it('remembers the other language and reloads into it', () => {
    const element = render('tr');

    button(element, 'en').click();

    expect(storedLanguage()).toBe('en');
    expect(reload).toHaveBeenCalledOnce();
  });

  it('does nothing for the language already shown', () => {
    const element = render('en');

    button(element, 'en').click();

    expect(localStorage.getItem('smartdesk_language')).toBeNull();
    expect(reload).not.toHaveBeenCalled();
  });
});
