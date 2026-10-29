import { lookup } from 'node:dns/promises';
import { request } from 'node:https';
import { BlockList, isIP } from 'node:net';
import { MAX_BYTES } from './image-validation.mjs';

const blocked = new BlockList();
for (const [address, prefix] of [
  ['0.0.0.0', 8], ['10.0.0.0', 8], ['100.64.0.0', 10], ['127.0.0.0', 8],
  ['169.254.0.0', 16], ['172.16.0.0', 12], ['192.0.0.0', 24], ['192.0.2.0', 24],
  ['192.88.99.0', 24], ['192.168.0.0', 16], ['198.18.0.0', 15], ['198.51.100.0', 24],
  ['203.0.113.0', 24], ['224.0.0.0', 4], ['240.0.0.0', 4]
]) blocked.addSubnet(address, prefix, 'ipv4');
for (const [address, prefix] of [
  ['::', 128], ['::1', 128], ['fc00::', 7], ['fe80::', 10],
  ['ff00::', 8], ['2001:db8::', 32]
]) blocked.addSubnet(address, prefix, 'ipv6');

export function choosePublicAddress(records) {
  if (!records.length || records.some(x => ![4, 6].includes(x.family) || !isIP(x.address) || (x.family === 6 && x.address.toLowerCase().startsWith('::ffff:')) || blocked.check(x.address, x.family === 4 ? 'ipv4' : 'ipv6'))) throw new Error('source address denied');
  return records.find(x => x.family === 4) ?? records[0];
}

export function pinnedLookup(selected) {
  return (_host, options, callback) => {
    if (options?.all === true) callback(null, [{ address: selected.address, family: selected.family }]);
    else callback(null, selected.address, selected.family);
  };
}

export function validateSourceUrl(file, allowedSources) {
  if (!file || typeof file.download_url !== 'string' || typeof file.file_id !== 'string') throw new Error('invalid source');
  let url;
  try { url = new URL(file.download_url); } catch { throw new Error('invalid source URL'); }
  if (url.protocol !== 'https:' || url.username || url.password || url.hash || isIP(url.hostname)) throw new Error('source URL denied');
  const match = allowedSources?.find(x => url.origin === x.origin && url.pathname.startsWith(x.pathPrefix));
  if (!match || !match.pathPrefix?.startsWith('/') || url.pathname.includes('..') || /%(?:2f|5c|2e)/i.test(url.pathname)) throw new Error('source URL denied');
  return url;
}

export function validateSourceResponse(status, headers, receivedLength) {
  if (status !== 200) throw new Error('source response denied');
  const encoding = headers['content-encoding'];
  if (encoding !== undefined && (typeof encoding !== 'string' || encoding.trim().toLowerCase() !== 'identity')) throw new Error('source encoding denied');
  const declared = headers['content-length'];
  if (declared !== undefined) {
    if (typeof declared !== 'string' || !/^\d+$/.test(declared) || Number(declared) > MAX_BYTES) throw new Error('source length denied');
    if (receivedLength !== undefined && Number(declared) !== receivedLength) throw new Error('source length mismatch');
  }
  return true;
}

export async function fetchSourceFile(file, config) {
  const url = validateSourceUrl(file, config.allowedSources);
  const deadline = AbortSignal.timeout(120_000);
  const records = await Promise.race([
    lookup(url.hostname, { all: true, verbatim: true }),
    new Promise((_, reject) => deadline.addEventListener('abort', () => reject(new Error('source timeout')), { once: true }))
  ]);
  const selected = choosePublicAddress(records);
  return new Promise((resolve, reject) => {
    const req = request(url, {
      method: 'GET',
      lookup: pinnedLookup(selected),
      signal: deadline,
      headers: { accept: 'image/png, image/jpeg', 'accept-encoding': 'identity' },
      timeout: 120_000
    }, res => {
      try { validateSourceResponse(res.statusCode, res.headers); }
      catch (e) { res.resume(); reject(e); return; }
      const chunks = [];
      let length = 0;
      res.on('data', chunk => {
        length += chunk.length;
        if (length > MAX_BYTES) req.destroy(new Error('source byte limit'));
        else chunks.push(chunk);
      });
      res.on('end', () => {
        try { validateSourceResponse(res.statusCode, res.headers, length); resolve(Buffer.concat(chunks)); }
        catch (e) { reject(e); }
      });
      res.on('aborted', () => reject(new Error('source incomplete')));
      res.on('error', reject);
    });
    req.on('timeout', () => req.destroy(new Error('source timeout')));
    req.on('error', reject);
    req.end();
  });
}
