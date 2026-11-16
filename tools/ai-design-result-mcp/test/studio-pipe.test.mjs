import test from 'node:test';
import assert from 'node:assert/strict';
import { connect } from 'node:net';
import { randomUUID } from 'node:crypto';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { startStudioPipe } from '../src/studio-pipe.mjs';
import { ResultStore } from '../src/store.mjs';

function exchange(pipeName, message) {
  return new Promise((resolve, reject) => {
    const socket = connect('\\\\.\\pipe\\' + pipeName);
    const chunks = [];
    socket.once('connect', () => socket.write(JSON.stringify(message) + '\n'));
    socket.on('data', chunk => chunks.push(chunk));
    socket.once('error', reject);
    socket.setTimeout(5000, () => socket.destroy(new Error('pipe timed out')));
    socket.once('end', () => {
      try {
        const result = Buffer.concat(chunks);
        assert.equal(result.readInt32LE(0), result.length - 4);
        resolve(result.length === 4 ? null : JSON.parse(result.subarray(4).toString('utf8')));
      } catch (error) { reject(error); }
    });
  });
}

test('Studio pipe separates enrollment and read roles without adding a product HTTP client',
  { skip: process.platform !== 'win32' }, async t => {
    const root = await mkdtemp(join(tmpdir(), 'pf-pipe-'));
    const store = await ResultStore.open(root);
    const pipeName = 'PrintFlow-' + randomUUID();
    const pipe = await startStudioPipe({ pipeName, store, controlBearer: 'control-secret', readBearer: 'reader-secret' });
    t.after(async () => { await pipe.close(); await store.close(); await rm(root, { recursive: true, force: true }); });

    const ids = { requestId: randomUUID(), attemptId: randomUUID() };
    const enroll = { ...ids, operation: 'enroll', referenceSha256: [] };
    assert.equal(await exchange(pipeName, { ...enroll, bearer: 'reader-secret' }), null);
    const tickets = await exchange(pipeName, { ...enroll, bearer: 'control-secret' });
    const status = { ...ids, operation: 'status', readTicket: tickets.readTicket };
    assert.equal(await exchange(pipeName, { ...status, bearer: 'control-secret' }), null);
    assert.equal(await exchange(pipeName, { ...status, bearer: 'reader-secret', readTicket: tickets.uploadTicket }), null);
    assert.deepEqual(await exchange(pipeName, { ...status, bearer: 'reader-secret' }), { ...ids, schemaVersion: 1, state: 'pending' });
    assert.equal(await exchange(pipeName, { ...status, operation: 'read', bearer: 'reader-secret', objectVersion: randomUUID() }), null);
    assert.equal(await exchange(pipeName, { ...ids, operation: 'cancel', bearer: 'reader-secret' }), null);
    assert.deepEqual(await exchange(pipeName, { ...ids, operation: 'cancel', bearer: 'control-secret' }), { state: 'cancelled' });
    assert.equal(await exchange(pipeName, { ...status, bearer: 'reader-secret' }), null);
  });
