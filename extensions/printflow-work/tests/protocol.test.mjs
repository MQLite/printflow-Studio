import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { AttachmentAssembler, validateEnvelope, MAX_RAW_CHUNK } from "../protocol.js";

const fence = { requestId: "11111111-1111-1111-1111-111111111111", attemptId: "22222222-2222-2222-2222-222222222222", stateVersion: 3, token: "token" };
const attachmentId = "33333333-3333-3333-3333-333333333333";
function fixture(length = MAX_RAW_CHUNK + 3) {
  const data = Buffer.alloc(length, 7);
  const attachment = { id: attachmentId, order: 1, uploadName: "attachment-01.png", mediaType: "image/png", byteLength: length, sha256: createHash("sha256").update(data).digest("hex").toUpperCase() };
  const envelope = (kind, payload) => ({ protocolVersion: 1, messageId: "44444444-4444-4444-4444-444444444444", ...fence, kind, payload });
  const chunks = [];
  for (let offset = 0, index = 0; offset < length; offset += MAX_RAW_CHUNK, index++) {
    const bytes = data.subarray(offset, offset + MAX_RAW_CHUNK);
    chunks.push(envelope("AttachmentChunk", { attachmentId, order: 1, chunkIndex: index, chunkCount: Math.ceil(length / MAX_RAW_CHUNK), rawLength: bytes.length, totalLength: length, sha256: attachment.sha256, data: bytes.toString("base64") }));
  }
  return { attachment, chunks, data, done: envelope("AttachmentComplete", { attachmentId, order: 1, chunkCount: chunks.length, totalLength: length, sha256: attachment.sha256 }) };
}
test("complete original bytes and filename survive standard JSON transport", async () => {
  const f = fixture(); const assembler = new AttachmentAssembler(f.attachment, fence);
  for (const chunk of f.chunks) assembler.push(JSON.parse(JSON.stringify(chunk)));
  const file = await assembler.complete(f.done);
  assert.equal(file.name, "attachment-01.png"); assert.equal(file.size, f.data.length);
  assert.deepEqual(Buffer.from(await file.arrayBuffer()), f.data);
});
test("acknowledged version updates apply between attachments and never during a chunk stream", async () => {
  const f = fixture(); const a = new AttachmentAssembler(f.attachment, fence);
  const ack = { ...f.done, kind: "Ack", stateVersion: 4, payload: { stateVersion: 4 } };
  a.rebindFence(ack);
  assert.throws(() => a.push(f.chunks[0]));
  a.push({ ...f.chunks[0], stateVersion: 4 });
  assert.doesNotThrow(() => a.rebindFence(ack)); // delayed same-fence handler Ack is idempotent
  assert.throws(() => a.rebindFence({ ...ack, stateVersion: 5, payload: { stateVersion: 5 } }));
  a.push({ ...f.chunks[1], stateVersion: 4 });
  assert.equal((await a.complete({ ...f.done, stateVersion: 4 })).size, f.data.length);
});
test("256 KiB raw chunks pass; one byte extra fails before accepting any data", () => {
  const f = fixture(MAX_RAW_CHUNK); const a = new AttachmentAssembler(f.attachment, fence); a.push(f.chunks[0]);
  const bad = fixture(MAX_RAW_CHUNK + 1);
  bad.chunks[0].payload.rawLength = MAX_RAW_CHUNK + 1;
  bad.chunks[0].payload.data = bad.data.toString("base64");
  assert.throws(() => new AttachmentAssembler(bad.attachment, fence).push(bad.chunks[0]));
});
test("ordered chunks reject reorder, duplicates, missing blocks and hash corruption", async () => {
  const f = fixture(); const a = new AttachmentAssembler(f.attachment, fence);
  assert.throws(() => a.push(f.chunks[1])); a.push(f.chunks[0]); assert.throws(() => a.push(f.chunks[0]));
  await assert.rejects(() => a.complete(f.done));
  const b = new AttachmentAssembler(f.attachment, fence); const altered = structuredClone(f.chunks[0]);
  altered.payload.data = Buffer.alloc(MAX_RAW_CHUNK, 9).toString("base64"); b.push(altered); b.push(f.chunks[1]);
  await assert.rejects(() => b.complete(f.done));
});
test("fence, attachment identity, protocol, raw length and arbitrary commands are rejected", () => {
  const f = fixture();
  for (const patch of [{ token: "old" }, { attemptId: fence.requestId }, { requestId: fence.attemptId }, { stateVersion: 2 }, { protocolVersion: 2 }])
    assert.throws(() => new AttachmentAssembler(f.attachment, fence).push({ ...f.chunks[0], ...patch }));
  assert.throws(() => validateEnvelope({ ...f.done, kind: "ReadArbitraryFile" }));
  assert.throws(() => new AttachmentAssembler(f.attachment, fence).push({ ...f.chunks[0], payload: { ...f.chunks[0].payload, attachmentId: fence.requestId } }));
  assert.throws(() => new AttachmentAssembler(f.attachment, fence).push({ ...f.chunks[0], payload: { ...f.chunks[0].payload, rawLength: 1 } }));
});
