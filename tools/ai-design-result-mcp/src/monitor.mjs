import { createHash, randomUUID, timingSafeEqual } from 'node:crypto';
import { lstat, mkdir, open, readFile, realpath, rename, rm, rmdir } from 'node:fs/promises';
import { isAbsolute, join, resolve } from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';
import { inspectImage, MAX_BYTES } from './image-validation.mjs';

function exactLoopback(baseUrl) {
  const url = new URL(baseUrl);
  if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1' || url.username || url.password || url.pathname !== '/' || url.search || url.hash) throw new Error('loopback base URL required');
  return url.origin;
}
async function get(url, headers, signal) {
  const response = await fetch(url, { headers, signal, redirect: 'error' });
  if (response.status === 403) return null;
  if (!response.ok) throw new Error(`read failed: ${response.status}`);
  return response;
}
async function boundedBytes(response) {
  const chunks = [];
  let size = 0;
  for await (const chunk of response.body) {
    size += chunk.length;
    if (size > MAX_BYTES) throw new Error('monitor byte limit');
    chunks.push(chunk);
  }
  return Buffer.concat(chunks);
}
function equalHash(a, b) {
  if (!/^[a-f0-9]{64}$/.test(a) || !/^[a-f0-9]{64}$/.test(b)) return false;
  return timingSafeEqual(Buffer.from(a, 'hex'), Buffer.from(b, 'hex'));
}
async function ordinaryDirectory(path) {
  const facts = await lstat(path);
  if (!facts.isDirectory() || facts.isSymbolicLink() || resolve(await realpath(path)).toLowerCase() !== resolve(path).toLowerCase()) throw new Error('inbox directory changed');
}
async function ordinaryFile(path) {
  const facts = await lstat(path);
  if (!facts.isFile() || facts.isSymbolicLink() || facts.nlink !== 1) throw new Error('inbox file changed');
  return readFile(path);
}

export async function monitorOnce({ baseUrl, requestId, attemptId, readBearer, readTicket, inboxRoot, signal, timeoutMs = 120_000 }) {
  const origin = exactLoopback(baseUrl);
  if (!Number.isInteger(timeoutMs) || timeoutMs < 1) throw new Error('monitor deadline invalid');
  const boundedSignal = signal ? AbortSignal.any([signal, AbortSignal.timeout(timeoutMs)]) : AbortSignal.timeout(timeoutMs);
  if (!isAbsolute(inboxRoot) || !/^[A-Za-z0-9_-]{1,100}$/.test(requestId) || !/^[A-Za-z0-9_-]{1,100}$/.test(attemptId)) throw new Error('monitor scope invalid');
  const headers = { authorization: `Bearer ${readBearer}`, 'x-read-ticket': readTicket };
  const statusResponse = await get(`${origin}/results/${requestId}/${attemptId}`, headers, boundedSignal);
  if (!statusResponse) return null;
  const receipt = await statusResponse.json();
  if (receipt.state !== 'Ready' || receipt.sourceVerified !== true) return null;
  if (receipt.requestId !== requestId || receipt.attemptId !== attemptId || !/^[A-Za-z0-9_-]+$/.test(receipt.objectVersion)) throw new Error('receipt scope mismatch');
  const blobResponse = await get(`${origin}/results/${requestId}/${attemptId}/blob/${receipt.objectVersion}`, headers, boundedSignal);
  if (!blobResponse) throw new Error('blob unavailable');
  const bytes = await boundedBytes(blobResponse);
  const facts = await inspectImage(bytes);
  if (boundedSignal.aborted) throw boundedSignal.reason;
  if (!equalHash(facts.sha256, receipt.sha256) || facts.byteLength !== receipt.byteLength || facts.mediaType !== receipt.mediaType || facts.width !== receipt.width || facts.height !== receipt.height) throw new Error('remote receipt mismatch');
  const root = resolve(inboxRoot);
  await mkdir(root, { recursive: true, mode: 0o700 });
  await ordinaryDirectory(root);
  const stem = createHash('sha256').update(`${requestId}\0${attemptId}\0${receipt.objectId}\0${receipt.objectVersion}`).digest('hex');
  const imageFile = `image.${facts.mediaType === 'image/png' ? 'png' : 'jpg'}`;
  const finalDir = join(root, stem);
  const imagePath = join(finalDir, imageFile);
  const receiptPath = join(finalDir, 'receipt.json');
  const current = async () => {
    const response = await get(`${origin}/results/${requestId}/${attemptId}`, headers, boundedSignal);
    if (!response || JSON.stringify(await response.json()) !== JSON.stringify(receipt)) throw new Error('receipt revoked or changed');
  };
  const verifiedExisting = async () => {
    let existing;
    try { await ordinaryDirectory(finalDir); existing = JSON.parse((await ordinaryFile(receiptPath)).toString('utf8')); }
    catch (e) { if (e.code === 'ENOENT') return false; throw e; }
    if (JSON.stringify(existing.remote) !== JSON.stringify(receipt)) throw new Error('inbox receipt changed');
    const local = await ordinaryFile(imagePath);
    let localFacts;
    try { localFacts = await inspectImage(local); }
    catch { throw new Error('inbox image changed'); }
    if (!equalHash(localFacts.sha256, receipt.sha256) || localFacts.byteLength !== receipt.byteLength) throw new Error('inbox image changed');
    await current();
    return true;
  };
  if (await verifiedExisting()) return { ...receipt, imagePath, receiptPath };
  const tempDir = join(root, `${stem}.${randomUUID()}.pending`);
  await mkdir(tempDir, { mode: 0o700 });
  const tempImage = join(tempDir, imageFile), tempReceipt = join(tempDir, 'receipt.json');
  try {
    const imageHandle = await open(tempImage, 'wx', 0o600);
    try { await imageHandle.writeFile(bytes); await imageHandle.sync(); }
    finally { await imageHandle.close(); }
    const manifest = { schemaVersion: 1, remote: receipt, imageFile, independentlyVerifiedSha256: facts.sha256 };
    const receiptHandle = await open(tempReceipt, 'wx', 0o600);
    try { await receiptHandle.writeFile(JSON.stringify(manifest)); await receiptHandle.sync(); }
    finally { await receiptHandle.close(); }
    await current();
    await rename(tempDir, finalDir); // A populated existing directory cannot be replaced.
  } catch (e) {
    await rm(tempImage, { force: true });
    await rm(tempReceipt, { force: true });
    await rmdir(tempDir).catch(() => {});
    if (['EEXIST', 'ENOTEMPTY', 'EPERM'].includes(e.code) && await verifiedExisting()) return { ...receipt, imagePath, receiptPath };
    throw e;
  }
  return { ...receipt, imagePath, receiptPath };
}

export async function pollUntilReady(options, { intervalMs = 1000, timeoutMs = 120_000, signal } = {}) {
  const deadline = Date.now() + timeoutMs;
  const boundedSignal = signal ? AbortSignal.any([signal, AbortSignal.timeout(timeoutMs)]) : AbortSignal.timeout(timeoutMs);
  while (Date.now() < deadline) {
    if (boundedSignal.aborted) throw boundedSignal.reason;
    const result = await monitorOnce({ ...options, signal: boundedSignal });
    if (result) return result;
    await sleep(Math.min(intervalMs, Math.max(1, deadline - Date.now())), undefined, { signal: boundedSignal });
  }
  return null;
}
