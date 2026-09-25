// Runs the unit tests once per time zone. Date code can pass in one zone and fail in another
// (daylight saving, 30- or 45-minute offsets), and the tests otherwise only run in the
// machine's zone. TZ is set here for each run, so this works from any shell - Git Bash on
// Windows drops a TZ given on its own command line.
//
//   npm run test:time-zones                     # the default set below
//   npm run test:time-zones -- Asia/Kolkata     # just these zones
import { spawnSync } from 'node:child_process';

const DEFAULT_ZONES = [
  'Europe/Istanbul', // no DST, UTC+3
  'America/New_York', // DST ends 1 Nov 2026
  'Europe/Berlin', // DST ends 25 Oct 2026
  'Asia/Tokyo', // UTC+9: UTC dates differ from local ones for 9 hours a day
  'Australia/Sydney', // southern-hemisphere DST
  'Asia/Kathmandu', // UTC+05:45
];

const zones = process.argv.length > 2 ? process.argv.slice(2) : DEFAULT_ZONES;
const failed = [];

for (const zone of zones) {
  console.log(`\n=== ${zone} ===`);
  const run = spawnSync('npx ng test --watch=false', {
    shell: true,
    stdio: 'inherit',
    env: { ...process.env, TZ: zone },
  });
  if (run.status !== 0) {
    failed.push(zone);
  }
}

if (failed.length > 0) {
  console.error(`\nFailed in: ${failed.join(', ')}`);
  process.exit(1);
}
console.log(`\nPassed in all ${zones.length} zones.`);
