export const MAX_RAW_CHUNK = 256 * 1024;
const kinds = new Set(["Hello", "Ready", "Request", "AttachmentChunk", "AttachmentComplete", "UploadReceipt", "SubmitPermit", "SubmitReceipt", "Progress", "DownloadRegistered", "DownloadComplete", "Stop", "Failure", "Ack"]);
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function validateEnvelope(message) {
  if (!message || message.protocolVersion !== 1 || !guid.test(message.messageId) || !guid.test(message.requestId) || !guid.test(message.attemptId)
    || !Number.isSafeInteger(message.stateVersion) || message.stateVersion < 0 || typeof message.token !== "string" || !kinds.has(message.kind)
    || !message.payload || typeof message.payload !== "object" || Array.isArray(message.payload)
    || new TextEncoder().encode(JSON.stringify(message)).length > 1024 * 1024) throw new Error("NativeEnvelopeInvalid");
  return message;
}
export class AttachmentAssembler {
  constructor(attachment, fence) {
    if (!guid.test(attachment.id) || !Number.isSafeInteger(attachment.byteLength) || attachment.byteLength <= 0 || attachment.byteLength > 40 * 1024 * 1024
      || !Number.isSafeInteger(attachment.order) || attachment.order <= 0 || !/^attachment-\d+\.(png|jpe?g|webp|pdf|txt|md|csv|docx|xlsx|pptx)$/.test(attachment.uploadName)
      || !/^[0-9A-F]{64}$/.test(attachment.sha256)) throw new Error("AttachmentManifestInvalid");
    this.attachment = structuredClone(attachment); this.fence = { ...fence }; this.chunks = []; this.length = 0;
  }
  identity(message, kind) {
    validateEnvelope(message);
    if (message.kind !== kind || ["requestId", "attemptId", "stateVersion", "token"].some(key => message[key] !== this.fence[key])) throw new Error("AttachmentFenceMismatch");
    const p = message.payload, a = this.attachment;
    if (p.attachmentId !== a.id || p.order !== a.order || p.totalLength !== a.byteLength || p.sha256 !== a.sha256
      || p.chunkCount !== Math.ceil(a.byteLength / MAX_RAW_CHUNK)) throw new Error("AttachmentIdentityMismatch");
    return p;
  }
  rebindFence(ack) {
    validateEnvelope(ack);
    if (ack.kind !== "Ack" || ["requestId", "attemptId", "token"].some(key => ack[key] !== this.fence[key])
      || ack.stateVersion < this.fence.stateVersion || ack.payload.stateVersion !== ack.stateVersion) throw new Error("AttachmentFenceBoundaryInvalid");
    if (ack.stateVersion === this.fence.stateVersion) return;
    if (this.chunks.length !== 0 || this.length !== 0) throw new Error("AttachmentFenceBoundaryInvalid");
    this.fence = { requestId: ack.requestId, attemptId: ack.attemptId, stateVersion: ack.stateVersion, token: ack.token };
  }
  push(message) {
    const p = this.identity(message, "AttachmentChunk");
    if (p.chunkIndex !== this.chunks.length || !Number.isSafeInteger(p.rawLength) || p.rawLength <= 0 || p.rawLength > MAX_RAW_CHUNK
      || typeof p.data !== "string" || p.data.length % 4 !== 0 || !/^[A-Za-z0-9+/]*={0,2}$/.test(p.data)) throw new Error("AttachmentChunkInvalid");
    const decoded = atob(p.data), bytes = Uint8Array.from(decoded, c => c.charCodeAt(0));
    const expected = Math.min(MAX_RAW_CHUNK, this.attachment.byteLength - this.length);
    if (bytes.length !== p.rawLength || bytes.length !== expected) throw new Error("AttachmentLengthMismatch");
    this.chunks.push(bytes); this.length += bytes.length;
  }
  async complete(message) {
    const p = this.identity(message, "AttachmentComplete");
    if (this.chunks.length !== p.chunkCount || this.length !== p.totalLength) throw new Error("AttachmentIncomplete");
    const bytes = new Uint8Array(this.length); let offset = 0;
    for (const chunk of this.chunks) { bytes.set(chunk, offset); offset += chunk.length; }
    const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));
    const hash = Array.from(digest, b => b.toString(16).padStart(2, "0")).join("").toUpperCase();
    if (hash !== this.attachment.sha256) throw new Error("AttachmentHashMismatch");
    this.chunks = []; return new File([bytes], this.attachment.uploadName, { type: this.attachment.mediaType });
  }
}
