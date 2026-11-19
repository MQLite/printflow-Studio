import { createServer } from 'node:net';
import { timingSafeEqual } from 'node:crypto';

const prefix = '\\\\.\\pipe\\';
const maxRequestBytes = 4096;
const maxClients = 8;
const responseTimeoutMs = 120_000;

function matches(secret, supplied) {
  if (typeof secret !== 'string' || typeof supplied !== 'string') return false;
  const a = Buffer.from(secret), b = Buffer.from(supplied);
  return a.length === b.length && timingSafeEqual(a, b);
}

export async function startStudioPipe({ pipeName, store, controlBearer, readBearer }) {
  if (process.platform !== 'win32' || typeof pipeName !== 'string' ||
      !/^PrintFlow-[A-Za-z0-9-]{2,70}$/.test(pipeName)) throw new Error('invalid Studio pipe name');
  const clients = new Set();
  const server = createServer(socket => {
    socket.on('error', () => socket.destroy());
    if (clients.size >= maxClients) { socket.destroy(); return; }
    clients.add(socket);
    socket.once('close', () => clients.delete(socket));
    socket.setTimeout(responseTimeoutMs, () => socket.destroy());
    let input = Buffer.alloc(0);
    socket.on('data', chunk => {
      if (input.length + chunk.length > maxRequestBytes) { socket.destroy(); return; }
      input = Buffer.concat([input, chunk]);
      if (input.at(-1) !== 10) return;
      socket.pause();
      void (async () => {
        let payload;
        try {
          const request = JSON.parse(input.toString('utf8'));
          const { operation, bearer, requestId, attemptId, readTicket, objectVersion, referenceSha256, workConversationId, returnMediaType } = request;
          if (operation === 'enroll' && matches(controlBearer, bearer)) {
            const value = await store.enroll({ requestId, attemptId, referenceSha256, requireWorkConversation: true,
              returnMediaType: returnMediaType ?? undefined });
            payload = Buffer.from(JSON.stringify(value));
          } else if (operation === 'cancel' && matches(controlBearer, bearer)) {
            payload = Buffer.from(JSON.stringify(await store.cancel(requestId, attemptId)));
          } else if (operation === 'status' && matches(readBearer, bearer)) {
            payload = Buffer.from(JSON.stringify(await store.status(requestId, attemptId, readTicket, workConversationId ?? undefined)));
          } else if (operation === 'read' && matches(readBearer, bearer)) {
            payload = await store.readBlob(requestId, attemptId, objectVersion, readTicket);
          }
        } catch { /* Do not expose errors or credentials to local callers. */ }
        if (socket.destroyed) return;
        const header = Buffer.alloc(4);
        header.writeInt32LE(payload?.length ?? 0);
        socket.write(header);
        socket.end(payload);
      })();
    });
  });

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(prefix + pipeName, () => { server.off('error', reject); resolve(); });
  });
  return {
    close: () => new Promise((resolve, reject) => {
      for (const client of clients) client.destroy();
      server.close(error => error ? reject(error) : resolve());
    })
  };
}
