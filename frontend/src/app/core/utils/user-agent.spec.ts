import { describeUserAgent } from './user-agent';

describe('describeUserAgent', () => {
  const cases: [string, string, string | null, string | null, string][] = [
    [
      'Safari on an iPhone',
      'Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.1 Mobile/15E148 Safari/604.1',
      'Safari',
      'iPhone',
      'phone',
    ],
    [
      'Chrome on an iPhone',
      'Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/142.0.0.0 Mobile/15E148 Safari/604.1',
      'Chrome',
      'iPhone',
      'phone',
    ],
    [
      'Safari on an iPad',
      'Mozilla/5.0 (iPad; CPU OS 26_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.1 Mobile/15E148 Safari/604.1',
      'Safari',
      'iPad',
      'tablet',
    ],
    [
      'Chrome on an Android phone',
      'Mozilla/5.0 (Linux; Android 16; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Mobile Safari/537.36',
      'Chrome',
      'Android',
      'phone',
    ],
    [
      'Samsung Internet on an Android tablet',
      'Mozilla/5.0 (Linux; Android 15; SM-X710) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/28.0 Chrome/130.0.0.0 Safari/537.36',
      'Samsung Internet',
      'Android',
      'tablet',
    ],
    [
      'Edge on Windows',
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0',
      'Edge',
      'Windows',
      'computer',
    ],
    [
      'Firefox on a Mac',
      'Mozilla/5.0 (Macintosh; Intel Mac OS X 15.1; rv:145.0) Gecko/20100101 Firefox/145.0',
      'Firefox',
      'Mac',
      'computer',
    ],
    [
      'Opera on Linux',
      'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 OPR/125.0.0.0',
      'Opera',
      'Linux',
      'computer',
    ],
    [
      'Chrome on a Chromebook',
      'Mozilla/5.0 (X11; CrOS x86_64 16433.0.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36',
      'Chrome',
      'ChromeOS',
      'computer',
    ],
  ];

  it.each(cases)('tells %s', (_, agent, browser, system, kind) => {
    expect(describeUserAgent(agent)).toEqual({ browser, system, kind });
  });

  it('says nothing it does not know', () => {
    expect(describeUserAgent('curl/8.11.1')).toEqual({
      browser: null,
      system: null,
      kind: 'computer',
    });
    expect(describeUserAgent(null)).toEqual({ browser: null, system: null, kind: 'computer' });
  });
});
