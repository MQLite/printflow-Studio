import { AttachmentAssembler, validateEnvelope } from './protocol.js';

const conversationGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export class WorkSubmission {
  #current;
  constructor(page) { this.page = page; }

  disconnect() { this.#current = undefined; }

  async receive(raw, post) {
    const message = validateEnvelope(raw);
    const send = (kind, payload) => post({
      protocolVersion: 1, messageId: crypto.randomUUID(), requestId: message.requestId,
      attemptId: message.attemptId, stateVersion: message.stateVersion, token: message.token, kind, payload
    });
    const request = this.#current;
    if (message.kind === 'Request') {
      if (request) return;
      await this.#stage(message, send);
      return;
    }

    if (!request || message.requestId !== request.requestId ||
        message.attemptId !== request.attemptId || message.token !== request.token) return;

    if (message.kind === 'Failure') {
      if (['SubmitInProgress', 'SubmitPermitMismatch', 'SubmitReceiptInvalid'].includes(message.payload.code)) return;
      this.disconnect();
      return;
    }

    if (message.kind === 'Ack' && ['binding', 'uploaded'].includes(request.phase) &&
        message.stateVersion === request.version + 1 && message.payload.stateVersion === message.stateVersion) {
      request.version = message.stateVersion;
      if (request.attachmentIndex < request.attachments.length) {
        request.phase = 'attachments';
        request.assembler = new AttachmentAssembler(request.attachments[request.attachmentIndex], message);
      } else {
        request.phase = 'permit';
        send('Progress', { stage: 'SubmitReady', tabId: String(request.tab), conversationId: request.conversationId });
      }
      return;
    }

    if (request.phase === 'attachments' && ['AttachmentChunk', 'AttachmentComplete'].includes(message.kind)) {
      try {
        if (message.kind === 'AttachmentChunk') {
          request.assembler.push(message);
          return;
        }
        const attachment = request.attachments[request.attachmentIndex];
        const file = await request.assembler.complete(message);
        if (this.#current !== request) return;
        const uploaded = await this.page.upload(request.tab, request.conversationId, file);
        if (this.#current !== request) return;
        if (!uploaded?.ok) throw new Error('WorkUploadFailed');
        request.attachmentIndex++;
        request.assembler = undefined;
        request.phase = 'uploaded';
        send('UploadReceipt', { attachmentId: attachment.id, conversationId: request.conversationId,
          evidenceKey: 'ready:' + attachment.uploadName });
      } catch {
        if (this.#current !== request) return;
        this.disconnect();
        send('Failure', { code: 'WorkUploadFailed' });
      }
      return;
    }

    if (message.kind === 'Ack' && request.phase === 'receipt' && message.payload.stage === 'Submitted' &&
        message.stateVersion === request.version + 1 && message.payload.stateVersion === message.stateVersion) {
      this.disconnect();
      return;
    }

    const permit = message.payload;
    if (message.kind !== 'SubmitPermit' || request.phase !== 'permit' ||
        message.stateVersion !== request.version + 1 || typeof permit.permitId !== 'string' ||
        !Number.isSafeInteger(permit.expiresAt) || Date.now() >= permit.expiresAt ||
        permit.tabId !== String(request.tab) || permit.conversationId !== request.conversationId) return;

    request.phase = 'receipt';
    request.version = message.stateVersion;
    let result;
    try {
      result = await this.page.run(request.tab, {
        kind: 'submit', identity: request.conversationId, permitId: permit.permitId, expiresAt: permit.expiresAt
      });
    } catch {
      result = { ok: false };
    }
    if (this.#current !== request) return;
    const confirmed = result?.ok === true &&
      (!request.followUpConversationId || result.conversation?.toLowerCase() === request.followUpConversationId);
    send('SubmitReceipt', {
      permitId: permit.permitId, tabId: String(request.tab), conversationId: request.conversationId,
      confirmed, workConversationId: confirmed ? result.conversation : undefined
    });
  }

  async #stage(message, send) {
    const { promptText, attachments, followUpConversationId } = message.payload;
    if (!Array.isArray(attachments) || attachments.length > 8 ||
        attachments.some(a => !a || typeof a.uploadName !== 'string') ||
        new Set(attachments.map(a => a.uploadName)).size !== attachments.length) {
      send('Failure', { code: 'WorkUploadFailed' });
      return;
    }
    if (followUpConversationId != null && (typeof followUpConversationId !== 'string' ||
        !conversationGuid.test(followUpConversationId) || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(followUpConversationId) ||
        attachments.length !== 0)) {
      send('Failure', { code: 'WorkPageUnknown' });
      return;
    }
    if (typeof promptText !== 'string' || !promptText.trim()) {
      send('Failure', { code: 'WorkPromptRejected' });
      return;
    }
    const request = {
      requestId: message.requestId, attemptId: message.attemptId, token: message.token,
      version: message.stateVersion, conversationId: crypto.randomUUID(), phase: 'staging',
      attachments, attachmentIndex: 0, followUpConversationId: followUpConversationId?.toLowerCase()
    };
    this.#current = request;
    try {
      request.tab = await this.page.create(request.followUpConversationId);
      if (!Number.isInteger(request.tab)) throw new Error('No owned Work tab');
      const staged = await this.page.run(request.tab, { kind: 'stage', identity: request.conversationId, prompt: promptText,
        attachmentNames: attachments.map(a => a.uploadName), followUpConversationId: request.followUpConversationId });
      if (this.#current !== request) return;
      if (!staged?.ok) {
        this.disconnect();
        send('Failure', { code: staged?.reason ?? 'WorkPageUnknown' });
        return;
      }
      request.phase = 'binding';
      send('Progress', { stage: 'UploadStarted', tabId: String(request.tab), conversationId: request.conversationId });
    } catch {
      if (this.#current !== request) return;
      this.disconnect();
      send('Failure', { code: 'WorkPageUnknown' });
    }
  }
}
