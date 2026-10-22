// Serves the production build as nginx does in Docker (ADR-0031): the files as built, the app's
// own routes as index.html, and /api and /hubs - SignalR's WebSocket too - passed on to the API.
// The service worker needs the files exactly as built (ADR-0054), which `ng serve` doesn't give,
// so this is how the installable app that opens offline runs without Docker.
//
//   npx ng build && npm run serve:build                       # port 4200, the API on :5291
//   npm run serve:build -- --port 4300 --api http://localhost:8080 --host 0.0.0.0
import { createReadStream, existsSync, statSync } from 'node:fs';
import http from 'node:http';
import net from 'node:net';
import { extname, join, resolve, sep } from 'node:path';

const args = process.argv.slice(2);
const option = (name, fallback) => {
  const at = args.indexOf(`--${name}`);
  return at >= 0 && args[at + 1] ? args[at + 1] : fallback;
};
const port = Number(option('port', '4200'));
const host = option('host', 'localhost');
const api = new URL(option('api', 'http://localhost:5291'));
const root = resolve(option('root', 'dist/frontend/browser'));

if (!existsSync(join(root, 'index.html'))) {
  console.error(`No build in ${root}: run "npx ng build" first.`);
  process.exit(1);
}

const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.webmanifest': 'application/manifest+json',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.webp': 'image/webp',
  '.ico': 'image/x-icon',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.txt': 'text/plain; charset=utf-8',
};

// Checked again on every visit, like nginx.conf: the page, and the worker and what it reads,
// which have no hash in their names. Hashed scripts, styles and fonts are kept a year.
const CHECKED_EVERY_TIME = new Set([
  '/index.html',
  '/ngsw-worker.js',
  '/smartdesk-worker.js',
  '/ngsw.json',
  '/safety-worker.js',
  '/worker-basic.min.js',
  '/manifest.webmanifest',
]);

const passedOn = (path) => path.startsWith('/api/') || path.startsWith('/hubs/');

// The client's address and scheme, which the API believes from a proxy on this machine
// (ADR-0028).
function forwarded(req) {
  const address = req.socket.remoteAddress ?? '';
  const before = req.headers['x-forwarded-for'];
  return {
    ...req.headers,
    'x-forwarded-for': before ? `${before}, ${address}` : address,
    'x-forwarded-proto': req.headers['x-forwarded-proto'] ?? 'http',
  };
}

function passOn(req, res) {
  const upstream = http.request(
    {
      hostname: api.hostname,
      port: api.port || 80,
      method: req.method,
      path: req.url,
      headers: forwarded(req),
    },
    (answer) => {
      res.writeHead(answer.statusCode ?? 502, answer.headers);
      answer.pipe(res);
    },
  );
  upstream.on('error', () => {
    if (!res.headersSent) res.writeHead(502, { 'content-type': 'text/plain' });
    res.end('The API is not answering.');
  });
  req.pipe(upstream);
}

function serve(req, res, path) {
  let file = resolve(join(root, path));
  if (file !== root && !file.startsWith(root + sep)) {
    res.writeHead(400).end();
    return;
  }
  if (!existsSync(file) || statSync(file).isDirectory()) {
    // A file that isn't there is a 404; anything else is one of the app's routes.
    if (extname(path)) {
      res.writeHead(404).end();
      return;
    }
    file = join(root, 'index.html');
    path = '/index.html';
  }
  res.writeHead(200, {
    'content-type': TYPES[extname(file).toLowerCase()] ?? 'application/octet-stream',
    'content-length': statSync(file).size,
    'cache-control':
      CHECKED_EVERY_TIME.has(path) || !/\.(?:js|css|woff2?)$/.test(path)
        ? 'no-cache'
        : 'public, max-age=31536000, immutable',
  });
  if (req.method === 'HEAD') res.end();
  else createReadStream(file).pipe(res);
}

const server = http.createServer((req, res) => {
  let path;
  try {
    path = decodeURIComponent(new URL(req.url ?? '/', 'http://local').pathname);
  } catch {
    res.writeHead(400).end();
    return;
  }
  if (passedOn(path)) passOn(req, res);
  else if (req.method === 'GET' || req.method === 'HEAD') serve(req, res, path);
  else res.writeHead(405).end();
});

// SignalR's WebSocket: the handshake goes on to the API as it came, then the two sockets are
// joined.
server.on('upgrade', (req, socket, head) => {
  if (!passedOn(new URL(req.url ?? '/', 'http://local').pathname)) {
    socket.destroy();
    return;
  }
  const upstream = net.connect(Number(api.port || 80), api.hostname, () => {
    const headers = Object.entries(forwarded(req)).map(
      ([name, value]) => `${name}: ${Array.isArray(value) ? value.join(', ') : value}`,
    );
    upstream.write(`${req.method} ${req.url} HTTP/1.1\r\n${headers.join('\r\n')}\r\n\r\n`);
    if (head.length) upstream.write(head);
    upstream.pipe(socket);
    socket.pipe(upstream);
  });
  upstream.on('error', () => socket.destroy());
  socket.on('error', () => upstream.destroy());
});

server.listen(port, host, () => {
  console.log(`SmartDesk's build at http://${host}:${port}, /api and /hubs on to ${api.origin}`);
});
