import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import sharp from 'sharp';
import { Client, StreamableHTTPClientTransport } from '@modelcontextprotocol/client';
import { ResultStore } from '../src/store.mjs';
import { startServer } from '../src/server.mjs';

const png = await sharp({ create: { width: 2, height: 2, channels: 3, background: 'red' } }).png().toBuffer();
const file = { download_url: 'https://files.example.test/files/f1', file_id: 'sediment://file_synthetic' };
test('real MCP client lists file parameter contract and uploads only with writer auth', async t => {
  const root = await mkdtemp(join(tmpdir(), 'pf-mcp-http-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const store = await ResultStore.open(root);
  const server = await startServer({ store, writerBearer: 'writer-secret', controlBearer: 'control-secret', readBearer: 'reader-secret', fileFetcher: async () => png, allowedSources: [{ origin: 'https://files.example.test', pathPrefix: '/files/' }], port: 0 });
  t.after(async () => { await server.close(); await store.close(); });
  const address = `http://127.0.0.1:${server.port}`;
  const noAuth = await fetch(`${address}/mcp`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: '{}' });
  assert.equal(noAuth.status, 401);
  const badHost = await fetch(`${address}/mcp`, { method: 'POST', headers: { host: 'evil.test', authorization: 'Bearer writer-secret', 'content-type': 'application/json' }, body: '{}' });
  assert.equal(badHost.status, 400);
  const enrolled = await fetch(`${address}/control/attempts`, { method: 'POST', headers: { authorization: 'Bearer control-secret', 'content-type': 'application/json' }, body: JSON.stringify({ requestId: 'r1', attemptId: 'a1', expectedFileId: file.file_id }) });
  assert.equal(enrolled.status, 201);
  const tickets = await enrolled.json();
  const client = new Client({ name: 'local-contract-test', version: '1.0.0' });
  const transport = new StreamableHTTPClientTransport(new URL(`${address}/mcp`), { requestInit: { headers: { authorization: 'Bearer writer-secret' } } });
  await client.connect(transport);
  t.after(() => client.close());
  const listed = await client.listTools();
  const tool = listed.tools.find(x => x.name === 'save_image_result');
  assert.deepEqual(tool._meta['openai/fileParams'], ['file']);
  assert.deepEqual(tool.inputSchema.required.sort(), ['attempt_id', 'file', 'request_id', 'upload_ticket']);
  assert.deepEqual(tool.inputSchema.properties.file.required.sort(), ['download_url', 'file_id']);
  assert.equal(tool.annotations.readOnlyHint, false);
  assert.equal(tool.annotations.destructiveHint, false);
  assert.equal(tool.annotations.openWorldHint, true);
  assert.ok(tool.outputSchema.properties.objectVersion);
  const reply = await client.callTool({ name: 'save_image_result', arguments: { request_id: 'r1', attempt_id: 'a1', upload_ticket: tickets.uploadTicket, file } });
  assert.equal(reply.isError, undefined);
  assert.equal(reply.structuredContent.state, 'Ready');
  const status = await fetch(`${address}/results/r1/a1`, { headers: { authorization: 'Bearer reader-secret', 'x-read-ticket': tickets.readTicket } });
  assert.equal(status.status, 200);
  assert.equal((await status.json()).objectVersion, reply.structuredContent.objectVersion);
  const forbidden = await fetch(`${address}/results/r1/a1`, { headers: { authorization: 'Bearer writer-secret', 'x-read-ticket': tickets.readTicket } });
  assert.equal(forbidden.status, 401);
});

test('example placeholder bearer values cannot start the local listener', async () => {
  let server;
  try { server = await startServer({
    store: null,
    writerBearer: 'replace-with-a-distinct-random-32-byte-secret',
    controlBearer: 'replace-with-a-different-random-32-byte-secret',
    readBearer: 'replace-with-a-third-random-32-byte-secret',
    port: 0
  }); } catch { return; }
  await server.close();
  assert.fail('example credentials started a listener');
});
