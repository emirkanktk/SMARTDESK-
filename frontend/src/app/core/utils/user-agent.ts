/**
 * A browser's description of itself (its User-Agent) in the words a person uses: which browser,
 * on what, and what kind of device - for the list of devices an account is signed in on
 * (ADR-0061).
 */
export interface DeviceDescription {
  /** Edge, Opera, Samsung Internet, Firefox, Chrome or Safari; null for anything else. */
  browser: string | null;
  /** iPhone, iPad, Android, Windows, ChromeOS, Mac or Linux; null for anything else. */
  system: string | null;
  kind: 'phone' | 'tablet' | 'computer';
}

export function describeUserAgent(agent: string | null | undefined): DeviceDescription {
  const text = agent ?? '';
  return { browser: browserOf(text), system: systemOf(text), kind: kindOf(text) };
}

// In this order: Edge, Opera and Samsung Internet name Chrome too, and Chrome names Safari. On an
// iPhone each browser has a word of its own (CriOS, FxiOS, EdgiOS) before Safari's.
function browserOf(agent: string): string | null {
  if (/\bEdg(?:e|A|iOS)?\//.test(agent)) return 'Edge';
  if (/\b(?:OPR|OPiOS)\//.test(agent)) return 'Opera';
  if (/\bSamsungBrowser\//.test(agent)) return 'Samsung Internet';
  if (/\b(?:Firefox|FxiOS)\//.test(agent)) return 'Firefox';
  if (/\b(?:Chrome|CriOS|HeadlessChrome)\//.test(agent)) return 'Chrome';
  if (/\bVersion\/[\d.]+.*\bSafari\//.test(agent)) return 'Safari';
  return null;
}

// An iPhone says "like Mac OS X" and Android says Linux, so they come first. An iPad set to show
// sites as on a computer says it's a Mac; its User-Agent alone can't tell.
function systemOf(agent: string): string | null {
  if (/\biPhone\b/.test(agent)) return 'iPhone';
  if (/\biPad\b/.test(agent)) return 'iPad';
  if (/\bAndroid\b/.test(agent)) return 'Android';
  if (/\bWindows\b/.test(agent)) return 'Windows';
  if (/\bCrOS\b/.test(agent)) return 'ChromeOS';
  if (/\b(?:Macintosh|Mac OS X)\b/.test(agent)) return 'Mac';
  if (/\bLinux\b/.test(agent)) return 'Linux';
  return null;
}

// Android tablets leave out "Mobile"; an iPad has it, so it's asked first.
function kindOf(agent: string): DeviceDescription['kind'] {
  if (/\biPad\b/.test(agent) || (/\bAndroid\b/.test(agent) && !/\bMobile\b/.test(agent))) {
    return 'tablet';
  }
  return /\b(?:iPhone|Mobile)\b/.test(agent) ? 'phone' : 'computer';
}
