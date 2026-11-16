import { validateEnvelope } from './protocol.js';
const emptyId = '00000000-0000-0000-0000-000000000000';

export class NativeBridgeClient {
  constructor(runtime, report, work = null, contractHash = '') {
    this.runtime = runtime;
    this.report = report;
    this.work = work;
    this.contractHash = contractHash;
    this.port = null;
    this.ready = false;
    this.serial = Promise.resolve();
  }

  connect(launchNonce = '') {
    if (this.port && this.launchNonce === launchNonce) return;
    if (this.port) {
      this.port.disconnect();
      this.port = null;
      this.ready = false;
      this.work?.disconnect();
      setTimeout(() => this.connect(launchNonce), 300);
      return;
    }
    this.launchNonce = launchNonce;
    try {
      const port = this.runtime.connectNative('com.printflow.ai_design');
      this.port = port;
      this.report({ available: false, reason: 'BridgeConnecting' });
      port.onDisconnect.addListener(() => {
        void this.runtime.lastError;
        if (this.port !== port) return;
        this.port = null;
        this.ready = false;
        this.work?.disconnect();
        this.report({ available: false, reason: 'BridgeDisconnected' });
      });
      port.onMessage.addListener(message => {
        this.serial = this.serial.then(() => this.port === port && this.receive(message)).catch(() => {
          this.report({ available: false, reason: 'NativeBoundaryRejected' });
        });
      });
      port.postMessage({
        protocolVersion: 1, messageId: crypto.randomUUID(), requestId: emptyId, attemptId: emptyId,
        stateVersion: 0, token: '', kind: 'Hello',
        payload: { hostVersion: '1.0.0', workspaceIdentity: '', workContractHash: this.contractHash, launchNonce }
      });
    } catch {
      this.port = null;
      this.ready = false;
      this.report({ available: false, reason: 'BridgeNotConfigured' });
    }
  }

  async receive(message) {
    validateEnvelope(message);
    if (message.kind === 'Ready') {
      if (message.payload.protocolVersion !== 1 || message.payload.hostVersion !== '1.0.0')
        throw new Error('BridgeVersionMismatch');
      this.ready = message.payload.available === true && !!this.work && !!this.contractHash;
      this.report(this.ready ? { available: true } : { available: false, reason: 'WorkContractUnverified' });
      return;
    }
    if (this.ready) {
      const port = this.port;
      await this.work.receive(message, outgoing => { if (this.port === port) port?.postMessage(outgoing); });
      return;
    }
    if (['Request', 'AttachmentChunk', 'AttachmentComplete', 'SubmitPermit'].includes(message.kind)) {
      this.port?.postMessage({ ...message, messageId: crypto.randomUUID(), kind: 'Failure', payload: { code: 'WorkContractUnverified' } });
      this.report({ available: false, reason: 'WorkContractUnverified' });
    }
  }
}
