import { createHash } from 'node:crypto';
import sharp from 'sharp';

export const MAX_BYTES = 40 * 1024 * 1024;
export const PDF_MEDIA_TYPE = 'application/pdf';
export const MAX_PIXELS = 64_000_000;
const MAX_DECODED_BYTES = MAX_PIXELS * 4;
sharp.concurrency(1);
sharp.cache(false);
let decodeTail = Promise.resolve();

export async function inspectWorkResult(bytes, returnMediaType) {
  if (returnMediaType !== PDF_MEDIA_TYPE) return inspectImage(bytes);
  if (!Buffer.isBuffer(bytes) || bytes.length === 0 || bytes.length > MAX_BYTES ||
      !/^%PDF-[12]\.[0-9](?:\r|\n)/.test(bytes.subarray(0, 16).toString('latin1')) ||
      !/%%EOF[\x00\x09\x0a\x0c\x0d\x20]*$/.test(bytes.subarray(-1024).toString('latin1')))
    throw new Error('invalid PDF file');
  // The Studio PDF reader checks the actual page count and supported page geometry before import.
  return { byteLength: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex'),
    mediaType: PDF_MEDIA_TYPE, width: null, height: null };
}

export async function inspectImage(bytes) {
  if (!Buffer.isBuffer(bytes) || bytes.length === 0 || bytes.length > MAX_BYTES) throw new Error('image byte limit');
  const work = decodeTail.catch(() => {}).then(async () => {
    const image = sharp(bytes, { limitInputPixels: MAX_PIXELS, failOn: 'error', sequentialRead: true });
    const info = await image.metadata();
    if (!['png', 'jpeg'].includes(info.format) || !info.width || !info.height || info.width * info.height > MAX_PIXELS) throw new Error('unsupported image');
    const expectedOutput = info.width * info.height * 4;
    if (expectedOutput > MAX_DECODED_BYTES) throw new Error('decoded image limit');
    let decoded = 0;
    for await (const chunk of image.toColourspace('srgb').ensureAlpha().raw({ depth: 'uchar' })) {
      decoded += chunk.length;
      if (decoded > expectedOutput) throw new Error('decoded image limit');
    }
    if (decoded !== expectedOutput) throw new Error('incomplete image decode');
    return {
      byteLength: bytes.length,
      sha256: createHash('sha256').update(bytes).digest('hex'),
      mediaType: info.format === 'png' ? 'image/png' : 'image/jpeg',
      width: info.width,
      height: info.height
    };
  });
  decodeTail = work.catch(() => {});
  return work;
}
