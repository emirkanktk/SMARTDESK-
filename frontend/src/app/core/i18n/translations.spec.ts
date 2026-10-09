import { clearTranslations, loadTranslations } from '@angular/localize';
import source from '../../../locale/messages.json';
import turkish from '../../../../public/i18n/tr.json';
import { formatDuration } from '../../shared/utils/date-time';
import { missingRulesMessage } from '../../shared/validators/password.validator';
import { Roles } from '../constants/roles';
import { byTypeLabel, resourceStatusLabel, resourceTypeLabel, roleLabel } from './labels';

// Every text the app shows has a Turkish version with the same placeholders (#58). A missing
// one would quietly show in English. After adding or changing a text, `npm run i18n:extract`
// updates messages.json and lists what tr.json still needs.
describe('Turkish translations', () => {
  const english: Record<string, string> = source.translations;
  const translated: Record<string, string> = turkish.translations;
  const placeholders = (text: string) => (text.match(/\{\$\w+\}/g) ?? []).sort();

  it('cover every message, and only those', () => {
    expect(Object.keys(translated).sort()).toEqual(Object.keys(english).sort());
  });

  it("keep each message's placeholders", () => {
    for (const [id, text] of Object.entries(english)) {
      expect(placeholders(translated[id] ?? ''), id).toEqual(placeholders(text));
    }
  });

  it('are for Turkish', () => {
    expect(turkish.locale).toBe('tr');
  });

  describe('once loaded', () => {
    beforeEach(() => loadTranslations(translated));
    afterEach(() => clearTranslations());

    it('translate texts built in code, placeholders included', () => {
      const start = new Date(2026, 9, 5, 9, 0);
      const end = new Date(2026, 9, 6, 10, 30);

      expect(formatDuration(start, end)).toBe('1 gün 1 sa 30 dk');
      expect(resourceStatusLabel('Maintenance')).toBe('Bakımda');
      expect(roleLabel(Roles.Manager)).toBe('Ekip yöneticisi');
      expect(missingRulesMessage(['bir rakam'])).toBe('Şunları ekleyin: bir rakam.');
    });

    it('name the seeded kinds of resources, sorted as they read, and leave any other as it is', () => {
      const kinds = ['Meeting Room', 'Desk', 'Company Car', 'Projector', 'Camera', 'Laptop'];

      expect(kinds.map(resourceTypeLabel)).toEqual([
        'Toplantı odası',
        'Masa',
        'Şirket aracı',
        'Projektör',
        'Kamera',
        'Laptop',
      ]);
      expect(
        kinds
          .map((name) => ({ name }))
          .sort(byTypeLabel)
          .map((kind) => resourceTypeLabel(kind.name)),
      ).toEqual(['Kamera', 'Laptop', 'Masa', 'Projektör', 'Şirket aracı', 'Toplantı odası']);
    });
  });
});
