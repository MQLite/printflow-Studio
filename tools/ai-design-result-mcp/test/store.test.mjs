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
const pdf = await readFile(new URL('../../../tests/PrintFlow.Tests/Fixtures/Pdf/single.pdf', import.meta.url));
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

test('a final PDF attempt rejects intermediate images without occupying the result slot', async t => {
  const { store } = await fixture(t);
  const ids = { requestId: 'final-pdf', attemptId: 'first' };
  const conversation = '6afb6da3-9354-83ec-979d-3ed4f321fdcc';
  const tickets = await store.enroll({ ...ids, requireWorkConversation: true, returnMediaType: 'application/pdf' });
  assert.equal(tickets.returnMediaType, 'application/pdf');
  const upload = (sourceId, bytes, returnMediaType) => store.receive({
    ...ids, uploadTicket: tickets.uploadTicket, file: file(sourceId), returnMediaType
  }, async () => bytes);
  await assert.rejects(upload('intermediate', png));
  await assert.rejects(upload('fake-pdf', png, 'application/pdf'));
  assert.equal((await store.status(ids.requestId, ids.attemptId, tickets.readTicket, conversation)).state, 'pending');

  const receipt = await upload('final-one', pdf, 'application/pdf');
  assert.equal(receipt.mediaType, 'application/pdf');
  assert.equal(receipt.width, null);
  assert.equal(receipt.height, null);
  assert.equal(receipt.sha256, sha(pdf));
  const ready = await store.status(ids.requestId, ids.attemptId, tickets.readTicket, conversation);
  assert.equal(ready.state, 'Ready');
  assert.equal(ready.workReturnAccepted, true);
  assert.deepEqual(await store.readBlob(ids.requestId, ids.attemptId, ready.objectVersion, tickets.readTicket), pdf);
  assert.deepEqual(await upload('final-one', pdf, 'application/pdf'), ready);
  await assert.rejects(upload('another-final', pdf, 'application/pdf'));
});

test('the current Studio attempt can accept its returned original without an independent source lookup', async t => {
  const { store } = await fixture(t);
  const ids = { requestId: 'studio-return', attemptId: 'first' };
  const conversation = '6afb6da3-9354-83ec-979d-3ed4f321fdcc';
  const tickets = await store.enroll({ ...ids, requireWorkConversation: true, referenceSha256: [sha(jpeg)] });
  const status = (ticket, id) => store.status(ids.requestId, ids.attemptId, ticket, id);
  assert.equal((await status(tickets.readTicket, conversation)).state, 'pending');
  await assert.rejects(store.receive({ ...ids, uploadTicket: tickets.uploadTicket, file: file('reference') }, async () => jpeg));
  const upload = await store.receive({ ...ids, uploadTicket: tickets.uploadTicket, file: file('new-original') }, async () => png);
  assert.equal((await status(tickets.readTicket)).state, 'received_unverified');
  await assert.rejects(status(tickets.uploadTicket, conversation));
  await assert.rejects(status(tickets.readTicket, 'invalid'));

  const accepted = await status(tickets.readTicket, conversation);
  assert.equal(accepted.state, 'Ready');
  assert.equal(accepted.workConversationId, conversation);
  assert.equal(accepted.workReturnAccepted, true);
  assert.equal(accepted.sourceVerified, false);
  assert.equal(accepted.objectVersion, upload.objectVersion);
  assert.deepEqual(await store.readBlob(ids.requestId, ids.attemptId, accepted.objectVersion, tickets.readTicket), png);
  assert.deepEqual(await status(tickets.readTicket, conversation), accepted);
  await assert.rejects(status(tickets.readTicket, 'f56bda1a-8be7-40b2-9c61-4beca8aa729d'));
});

test('binding a pending observation requires the independently verified original digest', async t => {
  const { store } = await fixture(t);
  const ids = { requestId: 'observe1', attemptId: 'a1' };
  const tickets = await store.enroll(ids);
  const original = { ...ids, expectedFileId: 'chosen', expectedSha256: sha(png), expectedByteLength: png.length };
  await store.bindObservedSource(original);
  await assert.rejects(store.bindObservedSource({ ...original, expectedFileId: 'different' }));
  const upload = { ...ids, uploadTicket: tickets.uploadTicket, file: file('chosen') };
  await assert.rejects(store.receive(upload, async () => jpeg));
  assert.equal((await store.status(ids.requestId, ids.attemptId, tickets.readTicket)).state, 'pending');
  const reissued = { ...upload, file: file('reissued') };
  await assert.rejects(store.receive(reissued, async () => png));
  await store.bindObservedSource({ ...original, allowReissuedFileId: true });
  await assert.rejects(store.receive(reissued, async () => jpeg));
  assert.equal((await store.receive(reissued, async () => png)).state, 'Ready');
});

test('a quarantined upload is promoted only by matching independent evidence', async t => {
  const { store } = await fixture(t);
  const ids = { requestId: 'late-bind', attemptId: 'a1' };
  const tickets = await store.enroll({ ...ids, requireWorkConversation: true });
  const upload = { ...ids, uploadTicket: tickets.uploadTicket, file: file('reissued') };
  const quarantined = await store.receive(upload, async () => png);
  const workConversationId = '193d972b-e554-4cc2-8a66-915b7a30cb69';
  const evidence = { ...ids, expectedFileId: 'original', expectedSha256: sha(png),
    expectedByteLength: png.length, workConversationId, allowReissuedFileId: true };
  await assert.rejects(store.bindObservedSource({ ...evidence, workConversationId: undefined }));
  await assert.rejects(store.bindObservedSource({ ...evidence, expectedSha256: sha(jpeg) }));
  await assert.rejects(store.bindObservedSource({ ...evidence, allowReissuedFileId: false }));
  assert.equal((await store.status(ids.requestId, ids.attemptId, tickets.readTicket)).state, 'received_unverified');
  await store.bindObservedSource(evidence);
  const ready = await store.status(ids.requestId, ids.attemptId, tickets.readTicket);
  assert.deepEqual(ready, { ...quarantined, state: 'Ready', sourceVerified: true, workConversationId });
  assert.deepEqual(await store.readBlob(ids.requestId, ids.attemptId, ready.objectVersion, tickets.readTicket), png);
  await store.bindObservedSource(evidence);
  await assert.rejects(store.bindObservedSource({ ...evidence, workConversationId: '20fd1697-52d9-4305-a4b5-46d5beac2388' }));
  await store.cancel(ids.requestId, ids.attemptId);
  await assert.rejects(store.bindObservedSource(evidence));
});

test('attachment bytes cannot be accepted as an attempt result, even with a fresh file ID', async t => {
  const { store } = await fixture(t);
  const ids = { requestId: 'reference', attemptId: 'a1' };
  const tickets = await store.enroll({ ...ids, referenceSha256: [sha(png).toUpperCase()] });
  const upload = { ...ids, uploadTicket: tickets.uploadTicket, file: file('new-id') };
  await assert.rejects(store.receive(upload, async () => png));
  assert.equal((await store.status(ids.requestId, ids.attemptId, tickets.readTicket)).state, 'pending');
});

test('unverified file IDs cannot block the independently bound owner', async t => {
  const { store } = await fixture(t);
  const first = await store.enroll({ requestId: 'unverified-owner', attemptId: 'a1' });
  const owner = await store.enroll({ requestId: 'verified-owner', attemptId: 'a1', expectedFileId: 'shared' });
  const upload = { attemptId: 'a1', file: file('shared') };
  assert.equal((await store.receive({ ...upload, requestId: first.requestId, uploadTicket: first.uploadTicket }, async () => png)).state,
    'received_unverified');
  assert.equal((await store.receive({ ...upload, requestId: owner.requestId, uploadTicket: owner.uploadTicket }, async () => png)).state,
    'Ready');
  await assert.rejects(store.bindObservedSource({ requestId: first.requestId, attemptId: 'a1',
    expectedFileId: 'shared', expectedSha256: sha(png), expectedByteLength: png.length }));
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

test('an independently bound opaque file URI can publish, replay, and cannot cross attempts', async t => {
  const { store } = await fixture(t);
  const sourceId = 'sediment://file_0123abcd'; // Synthetic shape; no real connector identity.
  const source = { download_url: 'https://files.example.test/files/signed-object', file_id: sourceId };
  const first = await store.enroll({ requestId: 'opaque', attemptId: 'a1', expectedFileId: sourceId });
  await assert.rejects(store.receive({ requestId: 'opaque', attemptId: 'a1', uploadTicket: first.uploadTicket, file: { ...source, file_id: 'sediment://file_other' } }, async () => png));
  const receipt = await store.receive({ requestId: 'opaque', attemptId: 'a1', uploadTicket: first.uploadTicket, file: source }, async () => png);
  assert.equal(receipt.state, 'Ready');
  assert.equal(receipt.sourceFileId, sourceId);
  assert.deepEqual(await store.receive({ requestId: 'opaque', attemptId: 'a1', uploadTicket: first.uploadTicket, file: source }, async () => png), receipt);
  const second = await store.enroll({ requestId: 'other', attemptId: 'a1', expectedFileId: sourceId });
  await assert.rejects(store.receive({ requestId: 'other', attemptId: 'a1', uploadTicket: second.uploadTicket, file: source }, async () => png));
  await assert.rejects(store.enroll({ requestId: 'bad', attemptId: 'a1', expectedFileId: 'sediment://file_\u0000bad' }));
  await assert.rejects(store.enroll({ requestId: 'bad', attemptId: 'a2', expectedFileId: 'sediment://file_\u0085bad' }));
});
