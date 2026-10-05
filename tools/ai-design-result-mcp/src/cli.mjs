import { open, readFile, rm } from 'node:fs/promises';
import { isAbsolute, resolve } from 'node:path';
import { ResultStore } from './store.mjs';
import { startServer } from './server.mjs';
import { pollUntilReady } from './monitor.mjs';

function args(argv) {
  const [action, ...items] = argv;
  const values = {};
  for (let i = 0; i < items.length; i += 2) {
    if (!items[i]?.startsWith('--') || !items[i + 1]) throw new Error('invalid arguments');
    values[items[i].slice(2)] = items[i + 1];
  }
  return { action, values };
}
async function configuration(path) {
  if (!path || !isAbsolute(path)) throw new Error('absolute --config required');
  const config = JSON.parse(await readFile(path, 'utf8'));
  if (!isAbsolute(config.storageRoot) || !Array.isArray(config.allowedSources)) throw new Error('invalid configuration');
  return config;
}
async function control(baseUrl, bearer, path, method, data) {
  const response = await fetch(`${baseUrl}${path}`, {
    method,
    headers: { authorization: `Bearer ${bearer}`, 'content-type': 'application/json' },
    body: JSON.stringify(data ?? {})
  });
  if (!response.ok) throw new Error(`control request failed: ${response.status}`);
  return response.json();
}

export async function main(argv) {
  const { action, values } = args(argv);
  const config = await configuration(values.config);
  const baseUrl = `http://127.0.0.1:${config.port ?? 32179}`;
  if (action === 'serve') {
    const store = await ResultStore.open(config.storageRoot);
    const server = await startServer({ store, ...config, writerBearer: config.writerBearer, controlBearer: config.controlBearer, readBearer: config.readBearer });
    process.stdout.write(`Local MCP receiver listening on 127.0.0.1:${server.port}/mcp\n`);
    await new Promise(resolve => { process.once('SIGINT', resolve); process.once('SIGTERM', resolve); });
    await server.close();
    await store.close();
    return;
  }
  if (action === 'enroll') {
    if (!values.request || !values.attempt || !values.out?.endsWith('.local.json') || !isAbsolute(values.out)) throw new Error('request, attempt and absolute --out *.local.json required');
    const output = await open(values.out, 'wx', 0o600);
    try {
      const tickets = await control(baseUrl, config.controlBearer, '/control/attempts', 'POST', {
        requestId: values.request, attemptId: values.attempt, ...(values['expected-file-id'] ? { expectedFileId: values['expected-file-id'] } : {})
      });
      await output.writeFile(JSON.stringify({ ...tickets, baseUrl }));
      await output.sync();
    } catch (e) { await output.close(); await rm(values.out, { force: true }); throw e; }
    await output.close();
    process.stdout.write(`Attempt enrolled; private tickets saved to ${resolve(values.out)}\n`);
    return;
  }
  if (action === 'cancel') {
    if (!values.request || !values.attempt) throw new Error('request and attempt required');
    await control(baseUrl, config.controlBearer, `/control/attempts/${encodeURIComponent(values.request)}/${encodeURIComponent(values.attempt)}/cancel`, 'POST');
    process.stdout.write('Attempt cancelled.\n');
    return;
  }
  if (action === 'monitor') {
    if (!values.ticket || !isAbsolute(values.ticket) || !values.inbox || !isAbsolute(values.inbox)) throw new Error('absolute --ticket and --inbox required');
    const tickets = JSON.parse(await readFile(values.ticket, 'utf8'));
    const receipt = await pollUntilReady({ baseUrl, requestId: tickets.requestId, attemptId: tickets.attemptId, readBearer: config.readBearer, readTicket: tickets.readTicket, inboxRoot: values.inbox }, { timeoutMs: 120_000 });
    process.stdout.write(receipt ? `Verified candidate: ${receipt.imagePath}\n` : 'No verified result within the foreground polling interval.\n');
    return;
  }
  throw new Error('usage: serve|enroll|cancel|monitor --config ABSOLUTE_PATH [options]');
}

if (process.argv[1] && import.meta.url === new URL(`file:///${process.argv[1].replaceAll('\\', '/')}`).href) {
  main(process.argv.slice(2)).catch(error => { process.stderr.write(`Receiver command failed: ${error.message}\n`); process.exitCode = 1; });
}
