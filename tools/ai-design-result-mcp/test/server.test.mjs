import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import sharp from 'sharp';
import { Client, StreamableHTTPClientTransport } from '@modelcontextprotocol/client';
import { ResultStore } from '../src/store.mjs';
import { startServer } from '../src/server.mjs';

const png = await sharp({ create: { width: 2, height: 2, channels: 3, background: 'red' } }).png().toBuffer();
const pdf = await readFile(new URL('../../../tests/PrintFlow.Tests/Fixtures/Pdf/single.pdf', import.meta.url));
const file = { download_url: 'https://files.example.test/files/f1', file_id: 'sediment://file_synthetic' };
test('real MCP client lists file parameter contract and uploads only with writer auth', async t => {
  const root = await mkdtemp(join(tmpdir(), 'pf-mcp-http-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const store = await ResultStore.open(root);
  const server = await startServer({ store, writerBearer: 'writer-secret', controlBearer: 'control-secret', readBearer: 'reader-secret',
    fileFetcher: async source => source.file_id === 'final-pdf' ? pdf : png,
    allowedSources: [{ origin: 'https://files.example.test', pathPrefix: '/files/' }], port: 0 });
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

  const finalTool = listed.tools.find(x => x.name === 'save_final_pdf');
  assert.deepEqual(finalTool._meta['openai/fileParams'], ['file']);
  const pdfTickets = await store.enroll({ requestId: 'pdf', attemptId: 'a1', requireWorkConversation: true,
    returnMediaType: 'application/pdf' });
  const pdfReply = await client.callTool({ name: 'save_final_pdf', arguments: {
    request_id: 'pdf', attempt_id: 'a1', upload_ticket: pdfTickets.uploadTicket,
    file: { ...file, file_id: 'final-pdf', mime_type: 'application/pdf' }
  } });
  assert.equal(pdfReply.isError, undefined);
  assert.equal(pdfReply.structuredContent.mediaType, 'application/pdf');
  assert.equal(pdfReply.structuredContent.width, null);
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

test('source observation requires an enrolled upload ticket and never fetches or records a signed URL', async t => {
  const root = await mkdtemp(join(tmpdir(), 'pf-mcp-observe-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const store = await ResultStore.open(root);
  const tickets = await store.enroll({ requestId: 'observed', attemptId: 'attempt' });
  const seen = [];
  const server = await startServer({ store, writerBearer: 'writer-secret', controlBearer: 'control-secret',
    readBearer: 'reader-secret', port: 0, observeSource: facts => seen.push(facts),
    fileFetcher: async () => assert.fail('A diagnostic request must not fetch the image') });
  t.after(async () => { await server.close(); await store.close(); });
  const client = new Client({ name: 'local-source-test', version: '1.0.0' });
  await client.connect(new StreamableHTTPClientTransport(new URL(`http://127.0.0.1:${server.port}/mcp`), {
    requestInit: { headers: { authorization: 'Bearer writer-secret' } }
  }));
  t.after(() => client.close());
  const args = { request_id: 'observed', attempt_id: 'attempt', file: { ...file, download_url: file.download_url + '?signature=private' } };
  const send = upload_ticket => client.callTool({ name: 'save_image_result', arguments: { ...args, upload_ticket } });
  assert.equal((await send('wrong')).isError, true);
  assert.deepEqual(seen, []);
  assert.equal((await send(tickets.uploadTicket)).isError, true);
  assert.deepEqual(seen, [{ requestId: 'observed', attemptId: 'attempt', sourceFileId: file.file_id,
    origin: 'https://files.example.test', path: '/files/f1' }]);
  assert.equal((await store.status('observed', 'attempt', tickets.readTicket)).state, 'pending');
});
