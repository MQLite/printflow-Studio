import test from 'node:test';
import assert from 'node:assert/strict';
import { link, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import sharp from 'sharp';
import { ResultStore } from '../src/store.mjs';
import { startServer } from '../src/server.mjs';
import { monitorOnce, pollUntilReady } from '../src/monitor.mjs';

const png = await sharp({ create: { width: 2, height: 3, channels: 3, background: 'blue' } }).png().toBuffer();
const file = id => ({ download_url: `https://files.example.test/files/${id}`, file_id: id });
test('foreground monitor reads only bound exact version, verifies and publishes once', async t => {
  const root = await mkdtemp(join(tmpdir(), 'pf-monitor-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const store = await ResultStore.open(join(root, 'store'));
  const server = await startServer({ store, writerBearer: 'writer-secret', controlBearer: 'control-secret', readBearer: 'reader-secret', allowedSources: [{ origin: 'https://files.example.test', pathPrefix: '/files/' }], port: 0 });
  t.after(async () => { await server.close(); await store.close(); });
  const baseUrl = `http://127.0.0.1:${server.port}`;
  const bound = await store.enroll({ requestId: 'bound', attemptId: 'a1', expectedFileId: 'new-image' });
  const receipt = await store.receive({ requestId: 'bound', attemptId: 'a1', uploadTicket: bound.uploadTicket, file: file('new-image') }, async () => png);
  const opts = { baseUrl, requestId: 'bound', attemptId: 'a1', readBearer: 'reader-secret', readTicket: bound.readTicket, inboxRoot: join(root, 'inbox') };
  const [first, concurrent] = await Promise.all([monitorOnce(opts), monitorOnce(opts)]);
  assert.equal(concurrent.imagePath, first.imagePath);
  assert.equal(first.objectVersion, receipt.objectVersion);
  assert.deepEqual(await readFile(first.imagePath), png);
  const second = await monitorOnce(opts);
  assert.equal(second.imagePath, first.imagePath);
  assert.equal((await readdir(opts.inboxRoot)).length, 1);
  await writeFile(first.imagePath, 'changed');
  await assert.rejects(monitorOnce(opts), /changed|mismatch/i);
});

test('cancellation after blob transfer but before adoption prevents inbox publication', async t => {
  const root = await mkdtemp(join(tmpdir(), 'pf-monitor-revoke-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const store = await ResultStore.open(join(root, 'store'));
  let releaseBlob, blobStarted;
  const started = new Promise(resolve => { blobStarted = resolve; });
  const hold = new Promise(resolve => { releaseBlob = resolve; });
  const originalRead = store.readBlob.bind(store);
  store.readBlob = async (...args) => { const bytes = await originalRead(...args); blobStarted(); await hold; return bytes; };
  const server = await startServer({ store, writerBearer: 'writer-secret', controlBearer: 'control-secret', readBearer: 'reader-secret', allowedSources: [], port: 0 });
  t.after(async () => { await server.close(); await store.close(); });
  const tickets = await store.enroll({ requestId: 'revoke', attemptId: 'a1', expectedFileId: 'new' });
  await store.receive({ requestId: 'revoke', attemptId: 'a1', uploadTicket: tickets.uploadTicket, file: file('new') }, async () => png);
  const inboxRoot = join(root, 'inbox');
  const task = monitorOnce({ baseUrl: `http://127.0.0.1:${server.port}`, requestId: 'revoke', attemptId: 'a1', readBearer: 'reader-secret', readTicket: tickets.readTicket, inboxRoot });
  await started;
  await store.cancel('revoke', 'a1');
  releaseBlob();
  await assert.rejects(task);
  assert.deepEqual(await readdir(inboxRoot), []);
});

test('a previous unreceipted image is never overwritten and does not block a complete inbox unit', async t => {
  const root = await mkdtemp(join(tmpdir(), 'pf-monitor-orphan-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const store = await ResultStore.open(join(root, 'store'));
  const server = await startServer({ store, writerBearer: 'writer-secret', controlBearer: 'control-secret', readBearer: 'reader-secret', allowedSources: [], port: 0 });
  t.after(async () => { await server.close(); await store.close(); });
  const tickets = await store.enroll({ requestId: 'orphan', attemptId: 'a1', expectedFileId: 'new' });
  const receipt = await store.receive({ requestId: 'orphan', attemptId: 'a1', uploadTicket: tickets.uploadTicket, file: file('new') }, async () => png);
  const inboxRoot = join(root, 'inbox');
  await import('node:fs/promises').then(fs => fs.mkdir(inboxRoot));
  const stem = createHash('sha256').update(`orphan\0a1\0${receipt.objectId}\0${receipt.objectVersion}`).digest('hex');
  const orphan = join(inboxRoot, `${stem}.png`);
  await writeFile(orphan, png);
  const result = await monitorOnce({ baseUrl: `http://127.0.0.1:${server.port}`, requestId: 'orphan', attemptId: 'a1', readBearer: 'reader-secret', readTicket: tickets.readTicket, inboxRoot });
  assert.deepEqual(await readFile(orphan), png);
  assert.deepEqual(await readFile(result.imagePath), png);
  assert.notEqual(result.imagePath, orphan);
});

test('existing inbox candidate cannot be replaced by a hard linked image even with identical bytes', async t => {
  const root = await mkdtemp(join(tmpdir(), 'pf-monitor-link-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const store = await ResultStore.open(join(root, 'store'));
  const server = await startServer({ store, writerBearer: 'writer-secret', controlBearer: 'control-secret', readBearer: 'reader-secret', allowedSources: [], port: 0 });
  t.after(async () => { await server.close(); await store.close(); });
  const tickets = await store.enroll({ requestId: 'link', attemptId: 'a1', expectedFileId: 'new' });
  await store.receive({ requestId: 'link', attemptId: 'a1', uploadTicket: tickets.uploadTicket, file: file('new') }, async () => png);
  const opts = { baseUrl: `http://127.0.0.1:${server.port}`, requestId: 'link', attemptId: 'a1', readBearer: 'reader-secret', readTicket: tickets.readTicket, inboxRoot: join(root, 'inbox') };
  const candidate = await monitorOnce(opts);
  const outside = join(root, 'outside.png');
  await writeFile(outside, png);
  await rm(candidate.imagePath);
  await link(outside, candidate.imagePath);
  await assert.rejects(monitorOnce(opts));
});

test('unverified and cancelled attempts do not publish an inbox candidate', async t => {
  const root = await mkdtemp(join(tmpdir(), 'pf-monitor-no-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const store = await ResultStore.open(join(root, 'store'));
  const server = await startServer({ store, writerBearer: 'writer-secret', controlBearer: 'control-secret', readBearer: 'reader-secret', allowedSources: [{ origin: 'https://files.example.test', pathPrefix: '/files/' }], port: 0 });
  t.after(async () => { await server.close(); await store.close(); });
  const baseUrl = `http://127.0.0.1:${server.port}`;
  const unbound = await store.enroll({ requestId: 'unbound', attemptId: 'a2' });
  await store.receive({ requestId: 'unbound', attemptId: 'a2', uploadTicket: unbound.uploadTicket, file: file('unbound-image') }, async () => png);
  const cancelled = await store.enroll({ requestId: 'cancelled', attemptId: 'a3', expectedFileId: 'cancelled-image' });
  await store.cancel('cancelled', 'a3');
  for (const [requestId, attemptId, readTicket] of [['unbound', 'a2', unbound.readTicket], ['cancelled', 'a3', cancelled.readTicket]]) {
    const output = await monitorOnce({ baseUrl, requestId, attemptId, readBearer: 'reader-secret', readTicket, inboxRoot: join(root, 'inbox') });
    assert.equal(output, null);
  }
  await assert.rejects(readdir(join(root, 'inbox')));
});

test('foreground poll has a total deadline even when a local read stalls', async t => {
  const server = createServer((_req, res) => setTimeout(() => res.end('{}'), 500));
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => server.close());
  const root = await mkdtemp(join(tmpdir(), 'pf-poll-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const started = Date.now();
  await assert.rejects(pollUntilReady({ baseUrl: `http://127.0.0.1:${server.address().port}`, requestId: 'r', attemptId: 'a', readBearer: 'reader-secret', readTicket: 'ticket', inboxRoot: root }, { timeoutMs: 80 }));
  assert.ok(Date.now() - started < 350);
});

test('a direct monitorOnce call has a total deadline on a stalled local read', async t => {
  const server = createServer((_req, res) => setTimeout(() => res.end('{}'), 500));
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => server.close());
  const root = await mkdtemp(join(tmpdir(), 'pf-once-deadline-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const started = Date.now();
  await assert.rejects(monitorOnce({ baseUrl: `http://127.0.0.1:${server.address().port}`, requestId: 'r', attemptId: 'a', readBearer: 'reader-secret', readTicket: 'ticket', inboxRoot: root, timeoutMs: 80 }));
  assert.ok(Date.now() - started < 350);
});
