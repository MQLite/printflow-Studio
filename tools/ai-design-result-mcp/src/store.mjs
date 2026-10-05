import { lstat, mkdir, open, readFile, realpath, rename, rm } from 'node:fs/promises';
import { createHash, randomBytes, randomUUID, timingSafeEqual } from 'node:crypto';
import { isAbsolute, join, resolve } from 'node:path';
import { inspectImage } from './image-validation.mjs';

const idPattern = /^[A-Za-z0-9_-]{1,100}$/;
const digest = value => createHash('sha256').update(value).digest('hex');
const ticket = () => randomBytes(32).toString('base64url');
const validId = value => { if (typeof value !== 'string' || !idPattern.test(value)) throw new Error('invalid identity'); return value; };
const keyOf = (requestId, attemptId) => digest(`${validId(requestId)}\0${validId(attemptId)}`);
const requestKey = requestId => `request-${digest(validId(requestId))}`;
function matchTicket(secretHash, value) {
  if (typeof value !== 'string') return false;
  return timingSafeEqual(Buffer.from(secretHash, 'hex'), Buffer.from(digest(value), 'hex'));
}
async function atomicJson(path, value) {
  const temp = `${path}.${randomUUID()}.tmp`;
  try { await createFlushedFile(temp, JSON.stringify(value)); await rename(temp, path); }
  finally { await rm(temp, { force: true }); }
}
async function createFlushedFile(path, bytes) {
  const handle = await open(path, 'wx', 0o600);
  try { await handle.writeFile(bytes); await handle.sync(); }
  finally { await handle.close(); }
}
async function ordinaryDirectory(path) {
  const facts = await lstat(path);
  if (!facts.isDirectory() || facts.isSymbolicLink() || resolve(await realpath(path)).toLowerCase() !== resolve(path).toLowerCase()) throw new Error('managed directory is not ordinary');
}
async function ordinaryFile(path) {
  const facts = await lstat(path);
  if (!facts.isFile() || facts.isSymbolicLink() || facts.nlink !== 1) throw new Error('managed file is not ordinary');
  return readFile(path);
}
function alive(pid) {
  try { process.kill(pid, 0); return true; }
  catch (e) { if (e.code === 'ESRCH') return false; return true; }
}

export class ResultStore {
  static async open(root) {
    if (!isAbsolute(root)) throw new Error('absolute storage root required');
    root = resolve(root);
    await mkdir(root, { recursive: true, mode: 0o700 });
    await ordinaryDirectory(root);
    const lock = join(root, '.writer.lock');
    let handle;
    try { handle = await open(lock, 'wx', 0o600); }
    catch (e) {
      if (e.code !== 'EEXIST') throw e;
      const recovery = join(root, '.writer.recovery');
      let guard;
      try { guard = await open(recovery, 'wx', 0o600); }
      catch { throw new Error('store owned or lock recovery busy'); }
      try {
        const prior = JSON.parse((await ordinaryFile(lock)).toString('utf8'));
        if (!Number.isInteger(prior.pid) || alive(prior.pid)) throw new Error('store owned or owner uncertain');
        await rm(lock);
        handle = await open(lock, 'wx', 0o600);
      } finally { await guard.close(); await rm(recovery, { force: true }); }
    }
    try {
      await handle.writeFile(JSON.stringify({ pid: process.pid, owner: randomUUID() }));
      await handle.sync();
      await mkdir(join(root, 'attempts'), { recursive: true, mode: 0o700 });
      await mkdir(join(root, 'blobs'), { recursive: true, mode: 0o700 });
      await ordinaryDirectory(join(root, 'attempts'));
      await ordinaryDirectory(join(root, 'blobs'));
      return new ResultStore(root, lock, handle);
    } catch (e) { await handle.close(); await rm(lock, { force: true }); throw e; }
  }

  constructor(root, lock, handle) { this.root = root; this.lock = lock; this.handle = handle; this.queues = new Map(); this.closed = false; }
  async close() {
    if (this.closed) return;
    this.closed = true;
    await Promise.allSettled([...this.queues.values()]);
    await this.handle.close();
    await rm(this.lock, { force: true });
  }
  #path(r, a) { return join(this.root, 'attempts', `${keyOf(r, a)}.json`); }
  #activePath(r) { return join(this.root, 'attempts', `${requestKey(r)}.json`); }
  async #checkDirs() {
    await ordinaryDirectory(this.root);
    await ordinaryDirectory(join(this.root, 'attempts'));
    await ordinaryDirectory(join(this.root, 'blobs'));
  }
  async #requireCurrent(r, a) {
    await this.#checkDirs();
    let active;
    try { active = JSON.parse((await ordinaryFile(this.#activePath(r))).toString('utf8')); }
    catch { throw new Error('attempt not current'); }
    if (active.requestId !== r || active.attemptId !== a) throw new Error('attempt not current');
  }
  async #read(r, a) {
    this.#checkOpen();
    await this.#checkDirs();
    let record;
    try { record = JSON.parse((await ordinaryFile(this.#path(r, a))).toString('utf8')); }
    catch (e) { if (e.code === 'ENOENT') throw new Error('unknown attempt'); throw e; }
    if (record.requestId !== r || record.attemptId !== a) throw new Error('attempt record identity mismatch');
    return record;
  }
  #checkOpen() { if (this.closed) throw new Error('store closed'); }
  #serialized(key, fn) {
    const prior = this.queues.get(key) ?? Promise.resolve();
    const current = prior.catch(() => {}).then(fn);
    this.queues.set(key, current);
    current.finally(() => { if (this.queues.get(key) === current) this.queues.delete(key); }).catch(() => {});
    return current;
  }
  async enroll({ requestId, attemptId, expectedFileId }) {
    const key = requestKey(requestId);
    if (expectedFileId !== undefined) validId(expectedFileId);
    return this.#serialized(key, async () => {
      this.#checkOpen();
      try { await this.#read(requestId, attemptId); throw new Error('attempt already enrolled'); }
      catch (e) { if (e.message !== 'unknown attempt') throw e; }
      const uploadTicket = ticket(), readTicket = ticket();
      await atomicJson(this.#path(requestId, attemptId), {
        requestId, attemptId, expectedFileId: expectedFileId ?? null,
        uploadHash: digest(uploadTicket), readHash: digest(readTicket), state: 'pending'
      });
      await atomicJson(this.#activePath(requestId), { requestId, attemptId });
      return { requestId, attemptId, uploadTicket, readTicket };
    });
  }
  async cancel(requestId, attemptId) {
    return this.#serialized(requestKey(requestId), async () => {
      await this.#requireCurrent(requestId, attemptId);
      const row = await this.#read(requestId, attemptId);
      row.state = 'cancelled';
      await atomicJson(this.#path(requestId, attemptId), row);
      return { state: 'cancelled' };
    });
  }
  async receive({ requestId, attemptId, uploadTicket, file }, fetchFile) {
    const key = requestKey(requestId);
    await this.#requireCurrent(requestId, attemptId);
    const before = await this.#read(requestId, attemptId);
    if (!matchTicket(before.uploadHash, uploadTicket) || before.state === 'cancelled') throw new Error('upload unauthorized');
    if (!file || typeof file.download_url !== 'string' || !validId(file.file_id)) throw new Error('invalid file');
    if (before.expectedFileId && before.expectedFileId !== file.file_id) throw new Error('source mismatch');
    const bytes = await fetchFile(file);
    const facts = await inspectImage(bytes);
    return this.#serialized(key, async () => {
      await this.#requireCurrent(requestId, attemptId);
      const row = await this.#read(requestId, attemptId);
      if (row.state === 'cancelled' || !matchTicket(row.uploadHash, uploadTicket)) throw new Error('upload revoked');
      if (row.receipt) {
        if (row.receipt.sourceFileId === file.file_id && row.receipt.sha256 === facts.sha256 && row.receipt.byteLength === facts.byteLength) return row.receipt;
        throw new Error('conflicting upload');
      }
      // Claim source globally before publishing so one file cannot satisfy another attempt.
      const claim = join(this.root, 'attempts', `source-${digest(file.file_id)}.json`);
      let claimed = false;
      try { await createFlushedFile(claim, JSON.stringify({ requestId, attemptId })); claimed = true; }
      catch (e) {
        if (e.code !== 'EEXIST') throw e;
        const owner = JSON.parse((await ordinaryFile(claim)).toString('utf8'));
        if (owner.requestId !== requestId || owner.attemptId !== attemptId) throw new Error('source already used');
      }
      const objectId = randomUUID();
      const objectVersion = randomUUID();
      const blobPath = join(this.root, 'blobs', objectId);
      try {
        await createFlushedFile(blobPath, bytes);
        const receipt = {
          schemaVersion: 1, requestId, attemptId, sourceFileId: file.file_id,
          objectId, objectVersion, ...facts,
          sourceVerified: Boolean(row.expectedFileId),
          state: row.expectedFileId ? 'Ready' : 'received_unverified'
        };
        row.state = receipt.state;
        row.receipt = receipt;
        await atomicJson(this.#path(requestId, attemptId), row);
        return receipt;
      } catch (e) {
        await rm(blobPath, { force: true });
        if (claimed) await rm(claim, { force: true });
        throw e;
      }
    });
  }
  async status(requestId, attemptId, readTicket) {
    return this.#serialized(requestKey(requestId), async () => {
      await this.#requireCurrent(requestId, attemptId);
      const row = await this.#read(requestId, attemptId);
      if (row.state === 'cancelled' || !matchTicket(row.readHash, readTicket)) throw new Error('read unauthorized');
      return row.receipt ?? { schemaVersion: 1, requestId, attemptId, state: row.state };
    });
  }
  async readBlob(requestId, attemptId, version, readTicket) {
    return this.#serialized(requestKey(requestId), async () => {
      await this.#requireCurrent(requestId, attemptId);
      const row = await this.#read(requestId, attemptId);
      if (row.state !== 'Ready' || !matchTicket(row.readHash, readTicket) || row.receipt.objectVersion !== version) throw new Error('blob unauthorized');
      const bytes = await ordinaryFile(join(this.root, 'blobs', row.receipt.objectId));
      const facts = await inspectImage(bytes);
      if (facts.sha256 !== row.receipt.sha256 || facts.byteLength !== row.receipt.byteLength) throw new Error('stored blob changed');
      return bytes;
    });
  }
}
