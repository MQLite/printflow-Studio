import test from 'node:test';
import assert from 'node:assert/strict';
import sharp from 'sharp';
import { createServer } from 'node:net';
import { request as httpsRequest } from 'node:https';
import { inspectImage, MAX_BYTES } from '../src/image-validation.mjs';
import { validateSourceUrl, choosePublicAddress, validateSourceResponse, pinnedLookup } from '../src/source-fetch.mjs';

const allowed = [{ origin: 'https://files.example.test', pathPrefix: '/files/' }];
test('image receiver decodes genuine JPEG and rejects empty, corrupt and oversized bytes', async () => {
  const jpeg = await sharp({ create: { width: 4, height: 5, channels: 3, background: 'green' } }).jpeg().toBuffer();
  const facts = await inspectImage(jpeg);
  assert.equal(facts.mediaType, 'image/jpeg');
  assert.equal(facts.width, 4);
  assert.equal(facts.height, 5);
  for (const invalid of [Buffer.alloc(0), jpeg.subarray(0, 20), Buffer.alloc(MAX_BYTES + 1), Buffer.from('<svg/>')]) {
    await assert.rejects(inspectImage(invalid));
  }
});

test('source URL must stay inside approved HTTPS file path with no credentials or local target', () => {
  const valid = { file_id: 'f1', download_url: 'https://files.example.test/files/f1?sig=opaque' };
  assert.equal(validateSourceUrl(valid, allowed).hostname, 'files.example.test');
  for (const url of [
    'http://files.example.test/files/f1',
    'https://127.0.0.1/files/f1',
    'https://user:password@files.example.test/files/f1',
    'https://files.example.test/admin/f1',
    'https://files.example.test/files/%2fadmin'
  ]) assert.throws(() => validateSourceUrl({ file_id: 'f1', download_url: url }, allowed));
});

test('DNS selection permits public dual stack and rejects mixed private answers', () => {
  assert.deepEqual(choosePublicAddress([{ address: '93.184.215.14', family: 4 }, { address: '2606:2800:220:1:248:1893:25c8:1946', family: 6 }]), { address: '93.184.215.14', family: 4 });
  assert.throws(() => choosePublicAddress([{ address: '93.184.215.14', family: 4 }, { address: '127.0.0.1', family: 4 }]));
  assert.throws(() => choosePublicAddress([{ address: '::1', family: 6 }]));
  assert.throws(() => choosePublicAddress([{ address: '192.0.2.44', family: 4 }]));
  assert.throws(() => choosePublicAddress([{ address: '198.51.100.8', family: 4 }]));
  assert.throws(() => choosePublicAddress([{ address: '203.0.113.5', family: 4 }]));
});

test('source response requires identity bytes, complete length and no redirects', () => {
  assert.equal(validateSourceResponse(200, { 'content-length': '4', 'content-encoding': 'identity' }, 4), true);
  assert.throws(() => validateSourceResponse(302, { location: 'https://other.test' }, 0));
  assert.throws(() => validateSourceResponse(200, { 'content-encoding': 'gzip' }, 4));
  assert.throws(() => validateSourceResponse(200, { 'content-length': '5' }, 4));
});

test('verified-address lookup interoperates with native HTTPS auto family selection', async t => {
  let accepted = false;
  const target = createServer(socket => { accepted = true; socket.destroy(); });
  await new Promise(resolve => target.listen(0, '127.0.0.1', resolve));
  t.after(() => target.close());
  await new Promise(resolve => {
    const req = httpsRequest(`https://file-fixture.invalid:${target.address().port}/`, {
      lookup: pinnedLookup({ address: '127.0.0.1', family: 4 }),
      rejectUnauthorized: false, timeout: 1000
    });
    req.on('error', resolve);
    req.end();
  });
  assert.equal(accepted, true);
});
