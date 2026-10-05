import { validateEnvelope } from "./protocol.js";
const emptyId = "00000000-0000-0000-0000-000000000000";
export class NativeBridgeClient {
  constructor(runtime, report) { this.runtime = runtime; this.report = report; this.port = null; this.serial = Promise.resolve(); }
  connect() {
    if (this.port) return;
    try {
      const port = this.runtime.connectNative("com.printflow.ai_design"); this.port = port;
      this.report({ available: false, reason: "BridgeConnecting" });
      port.onDisconnect.addListener(() => { void this.runtime.lastError; this.port = null; this.report({ available: false, reason: "BridgeDisconnected" }); });
      port.onMessage.addListener(message => {
        this.serial = this.serial.then(() => this.receive(message)).catch(() => { this.report({ available: false, reason: "NativeBoundaryRejected" }); });
      });
      port.postMessage({ protocolVersion: 1, messageId: crypto.randomUUID(), requestId: emptyId, attemptId: emptyId, stateVersion: 0, token: "", kind: "Hello",
        payload: { hostVersion: "1.0.0", workspaceIdentity: "", workContractHash: "" } });
    } catch { this.port = null; this.report({ available: false, reason: "BridgeNotConfigured" }); }
  }
  receive(message) {
    validateEnvelope(message);
    if (message.kind === "Ready") {
      if (message.payload.protocolVersion !== 1 || message.payload.hostVersion !== "1.0.0") throw new Error("BridgeVersionMismatch");
      // T02/T07 have not established a Work page contract. Native readiness cannot supply it.
      this.report({ available: false, reason: "WorkContractUnverified" }); return;
    }
    if (["Request", "AttachmentChunk", "AttachmentComplete", "SubmitPermit"].includes(message.kind)) {
      this.port?.postMessage({ ...message, messageId: crypto.randomUUID(), kind: "Failure", payload: { code: "WorkContractUnverified" } });
      this.report({ available: false, reason: "WorkContractUnverified" });
    }
  }
}
