import { AttachmentAssembler, validateEnvelope } from "../protocol.js";
let buffered = Buffer.alloc(0), assembler, completed, frames = 0;
try {
  for await (const chunk of process.stdin) {
    buffered = Buffer.concat([buffered, chunk]);
    while (buffered.length >= 4) {
      const length = buffered.readUInt32LE();
      if (!length || length > 1024 * 1024) throw new Error("NativeFrameInvalid");
      if (buffered.length < length + 4) break;
      const message = validateEnvelope(JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(buffered.subarray(4, length + 4))));
      buffered = buffered.subarray(length + 4); frames++;
      if (message.kind === "Request") assembler = new AttachmentAssembler(message.payload.attachments[0], message);
      if (message.kind === "Ack") assembler?.rebindFence(message);
      if (message.kind === "AttachmentChunk") assembler.push(message);
      if (message.kind === "AttachmentComplete") completed = await assembler.complete(message);
    }
  }
  if (buffered.length || !completed) throw new Error("NativeTransferIncomplete");
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", await completed.arrayBuffer()));
  const hash = Array.from(digest, b => b.toString(16).padStart(2, "0")).join("").toUpperCase();
  process.stdout.write(JSON.stringify({ byteLength: completed.size, uploadName: completed.name, sha256: hash, frameCount: frames }));
} catch { process.stderr.write("NativeTransferRejected"); process.exitCode = 2; }
