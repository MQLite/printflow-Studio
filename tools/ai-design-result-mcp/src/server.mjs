import { createServer } from 'node:http';
import { timingSafeEqual } from 'node:crypto';
import { toNodeHandler, localhostHostValidation, localhostOriginValidation } from '@modelcontextprotocol/node';
import { makeMcpHandler } from './mcp.mjs';

const validHost = localhostHostValidation();
const validOrigin = localhostOriginValidation();
const cap = 16_384;
function authorized(req, secret) {
  const actual = req.headers.authorization;
  if (typeof actual !== 'string' || !actual.startsWith('Bearer ')) return false;
  const a = Buffer.from(actual.slice(7)), b = Buffer.from(secret);
  return a.length === b.length && timingSafeEqual(a, b);
}
function json(res, code, value) {
  const bytes = Buffer.from(JSON.stringify(value));
  res.writeHead(code, { 'content-type': 'application/json', 'content-length': bytes.length, 'cache-control': 'no-store' });
  res.end(bytes);
}
async function body(req) {
  const chunks = [];
  let length = 0;
  for await (const chunk of req) {
    length += chunk.length;
    if (length > cap) throw new Error('body limit');
    chunks.push(chunk);
  }
  return JSON.parse(Buffer.concat(chunks).toString('utf8'));
}

export async function startServer({ store, writerBearer, controlBearer, readBearer, allowedSources = [], fileFetcher, port = 32179 }) {
  if (![writerBearer, controlBearer, readBearer].every(x => typeof x === 'string' && x.length >= 8 && !x.startsWith('replace-with-'))) throw new Error('three distinct bearer secrets required');
  if (new Set([writerBearer, controlBearer, readBearer]).size !== 3) throw new Error('bearer roles must be distinct');
  if (!Number.isInteger(port) || port < 0 || port > 65535) throw new Error('invalid port');
  const mcp = makeMcpHandler({ store, allowedSources, fileFetcher });
  const nodeMcp = toNodeHandler(mcp, { maxRequestBodySize: 65_536 });
  let active = 0;
  const server = createServer((req, res) => {
    if (!validHost(req, res) || !validOrigin(req, res)) return;
    if (++active > 8) { --active; json(res, 503, { error: 'busy' }); return; }
    res.once('close', () => { --active; });
    const path = new URL(req.url, 'http://127.0.0.1').pathname;
    if (path === '/mcp') {
      if (!authorized(req, writerBearer)) { json(res, 401, { error: 'unauthorized' }); return; }
      void nodeMcp(req, res);
      return;
    }
    void (async () => {
      try {
        if (path === '/control/attempts' && req.method === 'POST') {
          if (!authorized(req, controlBearer)) return json(res, 401, { error: 'unauthorized' });
          const args = await body(req);
          return json(res, 201, await store.enroll(args));
        }
        const cancel = /^\/control\/attempts\/([A-Za-z0-9_-]+)\/([A-Za-z0-9_-]+)\/cancel$/.exec(path);
        if (cancel && req.method === 'POST') {
          if (!authorized(req, controlBearer)) return json(res, 401, { error: 'unauthorized' });
          return json(res, 200, await store.cancel(cancel[1], cancel[2]));
        }
        const blob = /^\/results\/([A-Za-z0-9_-]+)\/([A-Za-z0-9_-]+)\/blob\/([A-Za-z0-9_-]+)$/.exec(path);
        const status = /^\/results\/([A-Za-z0-9_-]+)\/([A-Za-z0-9_-]+)$/.exec(path);
        if ((blob || status) && req.method === 'GET') {
          if (!authorized(req, readBearer)) return json(res, 401, { error: 'unauthorized' });
          const ticket = req.headers['x-read-ticket'];
          if (typeof ticket !== 'string') return json(res, 401, { error: 'unauthorized' });
          if (blob) {
            const bytes = await store.readBlob(blob[1], blob[2], blob[3], ticket);
            res.writeHead(200, { 'content-type': 'application/octet-stream', 'content-length': bytes.length, 'cache-control': 'no-store' });
            res.end(bytes);
            return;
          }
          return json(res, 200, await store.status(status[1], status[2], ticket));
        }
        json(res, 404, { error: 'not found' });
      } catch (e) {
        json(res, /unauthorized|revoked/.test(e.message) ? 403 : /already|conflict|used/.test(e.message) ? 409 : 400, { error: 'request rejected' });
      }
    })();
  });
  server.requestTimeout = 130_000;
  server.headersTimeout = 10_000;
  server.maxHeadersCount = 32;
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(port, '127.0.0.1', resolve); });
  return { port: server.address().port, close: async () => { await mcp.close(); server.closeAllConnections(); await new Promise((resolve, reject) => server.close(e => e ? reject(e) : resolve())); } };
}
