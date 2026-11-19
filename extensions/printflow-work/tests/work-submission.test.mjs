import assert from 'node:assert/strict';
import test from 'node:test';
import { createHash } from 'node:crypto';
import { WorkSubmission } from '../work-submission.js';

test('a native permit for the bound tab is the only route to a page click', async () => {
  const actions = [], messages = [];
  const flow = new WorkSubmission({
    async create() { return 42; },
    async run(tab, command) {
      actions.push([tab, command.kind]);
      return command.kind === 'submit' ? { ok: true, conversation: 'a9999999-0000-4000-8000-000000000000' } : { ok: true };
    }
  });
  const request = { protocolVersion: 1, messageId: crypto.randomUUID(), requestId: crypto.randomUUID(),
    attemptId: crypto.randomUUID(), stateVersion: 4, token: 'known', kind: 'Request',
    payload: { promptText: 'Create a teal circle.', attachments: [] } };
  const post = m => messages.push(m);
  await flow.receive(request, post);
  assert.deepEqual(actions, [[42, 'stage']]);
  const { conversationId } = messages.at(-1).payload;
  const frame = (kind, stateVersion, payload) => ({ ...request, messageId: crypto.randomUUID(), kind, stateVersion, payload });
  await flow.receive(frame('Ack', 5, { stateVersion: 5 }), post);
  assert.equal(messages.at(-1).payload.stage, 'SubmitReady');
  await flow.receive(frame('Failure', 5, { code: 'SubmitInProgress' }), post);
  const permit = { permitId: crypto.randomUUID(), tabId: '42', conversationId, expiresAt: Date.now() + 5000 };
  await flow.receive(frame('SubmitPermit', 6, { ...permit, tabId: '43' }), post);
  await flow.receive(frame('SubmitPermit', 6, { ...permit, expiresAt: Date.now() - 1 }), post);
  assert.deepEqual(actions, [[42, 'stage']]);
  await flow.receive(frame('SubmitPermit', 6, permit), post);
  await flow.receive(frame('SubmitPermit', 6, permit), post);
  assert.deepEqual(actions, [[42, 'stage'], [42, 'submit']]);
  assert.equal(messages.filter(m => m.kind === 'SubmitReceipt').length, 1);
  assert.equal(messages.at(-1).payload.confirmed, true);
  assert.equal(messages.at(-1).payload.workConversationId, 'a9999999-0000-4000-8000-000000000000');
});

test('a verified file keeps its request-specific name before the native submission permit is requested', async () => {
  const bytes = Buffer.from('reference contents');
  const attachment = { id: crypto.randomUUID(), order: 1, uploadName: 'attachment-01-0123456789abcdef0123456789abcdef.txt',
    mediaType: 'text/plain', byteLength: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex').toUpperCase() };
  const uploaded = [];
  const flow = new WorkSubmission({
    async create() { return 42; },
    async run() { return { ok: true }; },
    async upload(tab, identity, file) {
      assert.equal(tab, 42);
      uploaded.push([identity, file.name, await file.text()]);
      return { ok: true };
    }
  });
  const request = { protocolVersion: 1, messageId: crypto.randomUUID(), requestId: crypto.randomUUID(),
    attemptId: crypto.randomUUID(), stateVersion: 4, token: 'known', kind: 'Request',
    payload: { promptText: 'Use the file.', attachments: [attachment] } };
  const messages = [];
  const post = m => messages.push(m);
  const frame = (kind, stateVersion, payload) => ({ ...request, messageId: crypto.randomUUID(), kind, stateVersion, payload });
  await flow.receive(request, post);
  const { conversationId, stage } = messages.at(-1).payload;
  assert.equal(stage, 'UploadStarted');
  await flow.receive(frame('Ack', 5, { stateVersion: 5 }), post);
  const identity = { attachmentId: attachment.id, order: 1, chunkCount: 1, totalLength: bytes.length, sha256: attachment.sha256 };
  await flow.receive(frame('AttachmentChunk', 5, { ...identity, chunkIndex: 0, rawLength: bytes.length, data: bytes.toString('base64') }), post);
  assert.deepEqual(uploaded, []);
  await flow.receive(frame('AttachmentComplete', 5, identity), post);
  assert.deepEqual(uploaded, [[conversationId, attachment.uploadName, bytes.toString()]]);
  assert.equal(messages.at(-1).kind, 'UploadReceipt');
  assert.equal(messages.at(-1).payload.attachmentId, attachment.id);
  assert.equal(messages.some(m => m.payload.stage === 'SubmitReady'), false);
  await flow.receive(frame('Ack', 6, { stateVersion: 6 }), post);
  assert.equal(messages.at(-1).payload.stage, 'SubmitReady');
});
