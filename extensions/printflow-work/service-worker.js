import { NativeBridgeClient } from "./bridge-client.js";
const bridge = new NativeBridgeClient(chrome.runtime, status => {
  chrome.action.setBadgeText({ text: status.available ? "ON" : "OFF" });
  chrome.action.setTitle({ title: `PrintFlow bridge: ${status.reason ?? "available"}` });
});
chrome.action.onClicked.addListener(() => bridge.connect());
