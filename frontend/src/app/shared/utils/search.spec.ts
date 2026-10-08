import { simulateTurkishBrowser } from '../../../testing/turkish-locale';
import { searchKey } from './search';

describe('searchKey', () => {
  function finds(text: string, term: string): boolean {
    return searchKey(text).includes(searchKey(term));
  }

  it('finds a capital dotted İ by a plain i, in any case', () => {
    expect(finds('İstanbul Ofisi', 'ist')).toBe(true);
    expect(finds('İlker Yıldız', 'ilker')).toBe(true);
    expect(finds('İlker Yıldız', 'İLK')).toBe(true);
    expect(finds('Toplantı Odası', 'TOPLANTI')).toBe(true);
  });

  it('finds a dotless ı whether it is typed with or without the dot', () => {
    expect(finds('Işıl Demir', 'ışıl')).toBe(true);
    expect(finds('Işıl Demir', 'isil')).toBe(true);
    expect(finds('Toplantı Odası', 'odasi')).toBe(true);
  });

  it('finds Turkish letters typed without their accents', () => {
    expect(finds('Çağrı Öztürk', 'cagri')).toBe(true);
    expect(finds('Çağrı Öztürk', 'ozturk')).toBe(true);
    expect(finds('Şişli', 'sisli')).toBe(true);
  });

  it('still tells different words apart', () => {
    expect(finds('Meeting Room A', 'desk')).toBe(false);
    expect(finds('Çağrı', 'cagla')).toBe(false);
  });

  it('works the same in a Turkish browser ("hdmi" finds "HDMI")', () => {
    const restore = simulateTurkishBrowser();
    try {
      expect(finds('HDMI', 'hdmi')).toBe(true);
      expect(finds('İstanbul', 'ISTANBUL')).toBe(true);
    } finally {
      restore();
    }
  });
});
