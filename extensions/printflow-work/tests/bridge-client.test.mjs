import test from "node:test";
import assert from "node:assert/strict";
import { NativeBridgeClient } from "../bridge-client.js";
test("unverified extension reports unavailable even if native Ready claims available", async () => {
  const sent = [], reports = []; let receive, disconnect;
  const port = { postMessage: m => sent.push(m), onMessage: { addListener: f => receive = f }, onDisconnect: { addListener: f => disconnect = f } };
  const runtime = { connectNative: name => { assert.equal(name, "com.printflow.ai_design"); return port; } };
  const client = new NativeBridgeClient(runtime, status => reports.push(status)); client.connect();
  assert.equal(sent[0].kind, "Hello"); assert.equal(sent[0].payload.workContractHash, "");
  const base = { protocolVersion: 1, messageId: crypto.randomUUID(), requestId: crypto.randomUUID(), attemptId: crypto.randomUUID(), stateVersion: 1, token: "synthetic" };
  receive({ ...base, kind: "Ready", payload: { available: true, protocolVersion: 1, hostVersion: "1.0.0" } });
  receive({ ...base, kind: "Request", payload: { promptText: "manual prompt", attachments: [] } });
  await client.serial;
  assert.equal(reports.at(-1).available, false); assert.equal(sent.at(-1).kind, "Failure");
  assert.equal(sent.at(-1).payload.code, "WorkContractUnverified");
  disconnect(); assert.equal(reports.at(-1).reason, "BridgeDisconnected");
});
test("missing native host is a structured unavailable status", () => {
  const reports = []; new NativeBridgeClient({ connectNative() { throw new Error("unregistered"); } }, status => reports.push(status)).connect();
  assert.deepEqual(reports.at(-1), { available: false, reason: "BridgeNotConfigured" });
});
