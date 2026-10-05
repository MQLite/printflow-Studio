import test from 'node:test';
import assert from 'node:assert/strict';
import { link, mkdtemp, readFile, rm, symlink, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import sharp from 'sharp';
import { ResultStore } from '../src/store.mjs';

const png = await sharp({ create: { width: 3, height: 2, channels: 4, background: '#234567' } }).png().toBuffer();
const jpeg = await sharp({ create: { width: 3, height: 2, channels: 3, background: '#234567' } }).jpeg().toBuffer();
const file = id => ({ download_url: `https://files.example.test/files/${id}`, file_id: id, mime_type: 'image/png', file_name: 'result.png' });
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'pf-mcp-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const store = await ResultStore.open(root);
  t.after(() => store.close());
  return { root, store };
}

test('bound PNG becomes immutable Ready receipt and exact bytes survive restart', async t => {
  const { root, store } = await fixture(t);
  const tickets = await store.enroll({ requestId: 'req1', attemptId: 'a1', expectedFileId: 'file1' });
  const receipt = await store.receive({ requestId: 'req1', attemptId: 'a1', uploadTicket: tickets.uploadTicket, file: file('file1') }, async () => png);
  assert.equal(receipt.state, 'Ready');
  assert.equal(receipt.sha256, sha(png));
  assert.equal(receipt.width, 3);
  assert.equal(receipt.height, 2);
  assert.deepEqual(await store.readBlob('req1', 'a1', receipt.objectVersion, tickets.readTicket), png);
  assert.deepEqual(await store.receive({ requestId: 'req1', attemptId: 'a1', uploadTicket: tickets.uploadTicket, file: file('file1') }, async () => png), receipt);
  await store.close();
  const reopened = await ResultStore.open(root);
  t.after(() => reopened.close());
  assert.deepEqual(await reopened.status('req1', 'a1', tickets.readTicket), receipt);
  assert.deepEqual(await reopened.readBlob('req1', 'a1', receipt.objectVersion, tickets.readTicket), png);
});

test('unbound source is received_unverified and cannot be read as Ready', async t => {
  const { store } = await fixture(t);
  const tickets = await store.enroll({ requestId: 'req2', attemptId: 'a2' });
  const receipt = await store.receive({ requestId: 'req2', attemptId: 'a2', uploadTicket: tickets.uploadTicket, file: file('file2') }, async () => jpeg);
  assert.equal(receipt.state, 'received_unverified');
  assert.equal(receipt.sourceVerified, false);
  await assert.rejects(store.readBlob('req2', 'a2', receipt.objectVersion, tickets.readTicket));
});

test('wrong ticket, attempt, and bound source never publish', async t => {
  const { store } = await fixture(t);
  const tickets = await store.enroll({ requestId: 'req3', attemptId: 'a3', expectedFileId: 'file3' });
  for (const args of [
    { requestId: 'req3', attemptId: 'a3', uploadTicket: 'bad', file: file('file3') },
    { requestId: 'req3', attemptId: 'other', uploadTicket: tickets.uploadTicket, file: file('file3') },
    { requestId: 'req3', attemptId: 'a3', uploadTicket: tickets.uploadTicket, file: file('other') }
  ]) await assert.rejects(store.receive(args, async () => png));
  assert.equal((await store.status('req3', 'a3', tickets.readTicket)).state, 'pending');
});

test('changed duplicate and conflicting concurrent uploads cannot overwrite', async t => {
  const { store } = await fixture(t);
  const tickets = await store.enroll({ requestId: 'req4', attemptId: 'a4', expectedFileId: 'file4' });
  const args = { requestId: 'req4', attemptId: 'a4', uploadTicket: tickets.uploadTicket, file: file('file4') };
  const first = await store.receive(args, async () => png);
  await assert.rejects(store.receive(args, async () => jpeg));
  assert.deepEqual(await store.readBlob('req4', 'a4', first.objectVersion, tickets.readTicket), png);
  const values = await Promise.allSettled([store.receive(args, async () => png), store.receive(args, async () => jpeg)]);
  assert.equal(values.filter(x => x.status === 'rejected').length, 1);
});

test('cancel during source fetch prevents publication and revokes tickets', async t => {
  const { store } = await fixture(t);
  const tickets = await store.enroll({ requestId: 'req5', attemptId: 'a5', expectedFileId: 'file5' });
  let resume;
  const source = new Promise(resolve => { resume = resolve; });
  const task = store.receive({ requestId: 'req5', attemptId: 'a5', uploadTicket: tickets.uploadTicket, file: file('file5') }, async () => source);
  await store.cancel('req5', 'a5');
  resume(png);
  await assert.rejects(task);
  await assert.rejects(store.status('req5', 'a5', tickets.readTicket));
});

test('a second writer cannot open the same root', async t => {
  const { root } = await fixture(t);
  await assert.rejects(ResultStore.open(root));
});

test('cancel after receipt revokes a result before the foreground monitor adopts it', async t => {
  const { store } = await fixture(t);
  const tickets = await store.enroll({ requestId: 'req6', attemptId: 'a6', expectedFileId: 'file6' });
  const receipt = await store.receive({ requestId: 'req6', attemptId: 'a6', uploadTicket: tickets.uploadTicket, file: file('file6') }, async () => png);
  await store.cancel('req6', 'a6');
  await assert.rejects(store.status('req6', 'a6', tickets.readTicket));
  await assert.rejects(store.readBlob('req6', 'a6', receipt.objectVersion, tickets.readTicket));
});

test('new attempt fences an older Ready result for the same request', async t => {
  const { store } = await fixture(t);
  const old = await store.enroll({ requestId: 'same', attemptId: 'old', expectedFileId: 'old-file' });
  const oldReceipt = await store.receive({ requestId: 'same', attemptId: 'old', uploadTicket: old.uploadTicket, file: file('old-file') }, async () => png);
  const next = await store.enroll({ requestId: 'same', attemptId: 'new', expectedFileId: 'new-file' });
  await assert.rejects(store.status('same', 'old', old.readTicket));
  await assert.rejects(store.readBlob('same', 'old', oldReceipt.objectVersion, old.readTicket));
  await assert.rejects(store.receive({ requestId: 'same', attemptId: 'old', uploadTicket: old.uploadTicket, file: file('old-file') }, async () => png));
  const receipt = await store.receive({ requestId: 'same', attemptId: 'new', uploadTicket: next.uploadTicket, file: file('new-file') }, async () => jpeg);
  assert.equal(receipt.state, 'Ready');
});

test('store refuses a junction storage root and a linked persisted blob', async t => {
  const outer = await mkdtemp(join(tmpdir(), 'pf-mcp-root-'));
  t.after(() => rm(outer, { recursive: true, force: true }));
  const real = join(outer, 'real');
  const store = await ResultStore.open(real);
  const tickets = await store.enroll({ requestId: 'linked', attemptId: 'a', expectedFileId: 'file' });
  const receipt = await store.receive({ requestId: 'linked', attemptId: 'a', uploadTicket: tickets.uploadTicket, file: file('file') }, async () => png);
  await store.close();
  const alias = join(outer, 'alias');
  await symlink(real, alias, 'junction');
  await assert.rejects(ResultStore.open(alias));
  await rm(alias);
  const blob = join(real, 'blobs', receipt.objectId);
  const outside = join(outer, 'outside.png');
  await writeFile(outside, png);
  await rm(blob);
  await link(outside, blob);
  const reopened = await ResultStore.open(real);
  t.after(() => reopened.close());
  await assert.rejects(reopened.readBlob('linked', 'a', receipt.objectVersion, tickets.readTicket));
});
