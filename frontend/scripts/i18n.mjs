// Keeps the Turkish texts in step with the English ones in the code (#58).
//
//   npm run i18n:extract   # after adding or changing a text: updates src/locale/messages.json
//                          # and lists what public/i18n/tr.json still needs
//   npm run i18n:check     # changes nothing; fails if messages.json is out of date or
//                          # tr.json misses, has extra, or changes the placeholders of a text
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const SOURCE = 'src/locale/messages.json';
const TURKISH = 'public/i18n/tr.json';
const check = process.argv.includes('--check');

const readMessages = (path) => JSON.parse(readFileSync(path, 'utf8')).translations;
const placeholders = (text) => (text.match(/\{\$\w+\}/g) ?? []).sort().join(' ');

function extract(outputPath) {
  const run = spawnSync(`npx ng extract-i18n --format json --output-path "${outputPath}"`, {
    shell: true,
    stdio: ['ignore', 'ignore', 'inherit'],
  });
  if (run.status !== 0) process.exit(run.status ?? 1);
}

const problems = [];

let source;
if (check) {
  const dir = mkdtempSync(join(tmpdir(), 'smartdesk-i18n-'));
  try {
    extract(dir);
    source = readMessages(join(dir, 'messages.json'));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
  if (JSON.stringify(source) !== JSON.stringify(readMessages(SOURCE))) {
    problems.push(`${SOURCE} is out of date: run npm run i18n:extract.`);
  }
} else {
  extract('src/locale');
  source = readMessages(SOURCE);
}

const turkish = readMessages(TURKISH);
for (const [id, text] of Object.entries(source)) {
  if (!(id in turkish)) {
    problems.push(`Not translated: ${id} = "${text.trim()}"`);
  } else if (placeholders(turkish[id]) !== placeholders(text)) {
    problems.push(`Placeholders differ: ${id} needs ${placeholders(text) || 'none'}`);
  }
}
for (const id of Object.keys(turkish)) {
  if (!(id in source)) problems.push(`No longer used, remove it from ${TURKISH}: ${id}`);
}

if (problems.length > 0) {
  console.error(problems.join('\n'));
  process.exit(1);
}
console.log(`${Object.keys(source).length} messages, all translated.`);
