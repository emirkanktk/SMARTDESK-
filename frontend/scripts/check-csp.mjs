// Checks the page's Content Security Policy (ADR-0060) against the page itself: the browser runs
// an inline script only when the policy's script-src lists its hash, and an inline event handler
// (onload="...") never. The theme script in src/index.html is the one inline script; a build must
// not add another. Prints the hash a script needs when it isn't listed.
//
//   npm run csp:check                                        # src/index.html
//   node scripts/check-csp.mjs dist/frontend/browser/index.html
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';

const files = process.argv.length > 2 ? process.argv.slice(2) : ['src/index.html'];

// As the browser hashes a script: its text with the HTML parser's line endings.
const hashOf = (text) =>
  `'sha256-${createHash('sha256').update(text.replace(/\r\n?/g, '\n'), 'utf8').digest('base64')}'`;

let problems = 0;
const problem = (file, text) => {
  console.error(`${file}: ${text}`);
  problems++;
};

for (const file of files) {
  const before = problems;
  const html = readFileSync(file, 'utf8');
  const meta = [...html.matchAll(/<meta\b[^>]*>/gi)]
    .map(([tag]) => tag)
    .find((tag) => /http-equiv\s*=\s*"Content-Security-Policy"/i.test(tag));
  const policy = meta?.match(/\bcontent\s*=\s*"([^"]*)"/i)?.[1];
  if (!policy) {
    problem(file, 'no <meta http-equiv="Content-Security-Policy"> with a content');
    continue;
  }
  const firstScript = html.search(/<script\b/i);
  if (firstScript >= 0 && firstScript < html.indexOf(meta)) {
    problem(file, 'a script comes before the policy, which only covers what follows it');
  }

  const scriptSrc = policy
    .split(';')
    .map((directive) => directive.trim().split(/\s+/))
    .find(([name]) => name === 'script-src');
  if (!scriptSrc) {
    problem(file, 'the policy has no script-src');
    continue;
  }

  let inline = 0;
  for (const [, attributes, text] of html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)) {
    if (/\bsrc\s*=/i.test(attributes)) continue;
    inline++;
    const hash = hashOf(text);
    if (!scriptSrc.includes(hash)) {
      problem(
        file,
        `an inline script isn't allowed: add ${hash} to script-src\n  ${text.trim().split('\n')[0]}`,
      );
    }
  }

  for (const [tag] of html.matchAll(/<[a-z][^>]*\son[a-z]+\s*=[^>]*>/gi)) {
    problem(file, `an inline event handler, which the policy never runs: ${tag}`);
  }

  if (problems === before) console.log(`${file}: the policy allows its ${inline} inline script(s)`);
}

process.exit(problems ? 1 : 0);
