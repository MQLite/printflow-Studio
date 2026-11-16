import { open, readFile, rm } from 'node:fs/promises';
import { isAbsolute, resolve } from 'node:path';
import { ResultStore } from './store.mjs';
import { startServer } from './server.mjs';
import { startStudioPipe } from './studio-pipe.mjs';
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
    method, redirect: 'error', signal: AbortSignal.timeout(10_000),
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
    const probe = config.sourceProbe;
    if (probe && (!probe.requestId || !probe.attemptId || !isAbsolute(probe.output ?? '') ||
        !probe.output.endsWith('.local.json'))) throw new Error('invalid source probe');
    const observeSource = probe && (async facts => {
      if (facts.requestId !== probe.requestId || facts.attemptId !== probe.attemptId) return false;
      const output = await open(probe.output, 'wx', 0o600);
      try { await output.writeFile(JSON.stringify(facts)); await output.sync(); }
      finally { await output.close(); }
      return true;
    });
    const store = await ResultStore.open(config.storageRoot);
    const audit = config.sourceAudit;
    const reportSourceFailure = audit && (facts => {
      if (facts.requestId === audit.requestId && facts.attemptId === audit.attemptId)
        process.stderr.write(`Work result: ${facts.code}\n`);
    });
    const server = await startServer({ store, ...config, observeSource, reportSourceFailure });
    const studio = config.studioPipe ? await startStudioPipe({ ...config, store, pipeName: config.studioPipe }).catch(async error => {
      await server.close();
      await store.close();
      throw error;
    }) : null;
    process.stdout.write(`Local MCP receiver listening on 127.0.0.1:${server.port}/mcp\n`);
    await new Promise(resolve => { process.once('SIGINT', resolve); process.once('SIGTERM', resolve); });
    await studio?.close();
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
  if (action === 'bind-source') {
    if (!values.evidence?.endsWith('.local.json') || !isAbsolute(values.evidence))
      throw new Error('absolute --evidence *.local.json required');
    const input = await open(values.evidence, 'r');
    let evidence;
    try {
      if ((await input.stat()).size > 4096) throw new Error('source evidence too large');
      evidence = JSON.parse(await input.readFile('utf8'));
    } finally { await input.close(); }
    const binding = await control(baseUrl, config.controlBearer, '/control/source-binding', 'POST', evidence);
    process.stdout.write('Independent source bound; receiver state: ' + binding.state + '\n');
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
  throw new Error('usage: serve|enroll|bind-source|cancel|monitor --config ABSOLUTE_PATH [options]');
}

if (process.argv[1] && import.meta.url === new URL(`file:///${process.argv[1].replaceAll('\\', '/')}`).href) {
  main(process.argv.slice(2)).catch(error => { process.stderr.write(`Receiver command failed: ${error.message}\n`); process.exitCode = 1; });
}
