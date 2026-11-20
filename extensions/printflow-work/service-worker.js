import { NativeBridgeClient } from './bridge-client.js';
import { workTextTrialContract } from './work-contract.js';
import { runWorkPageCommand } from './work-page.js';
import { WorkSubmission } from './work-submission.js';
import { MAX_RAW_CHUNK } from './protocol.js';

const work = new WorkSubmission({
  async create(followUpConversationId) {
    const path = followUpConversationId ? '/c/' + followUpConversationId : '/';
    const created = await chrome.tabs.create({ url: 'https://chatgpt.com' + path });
    const deadline = Date.now() + 8000;
    while (Date.now() < deadline) {
      const tab = await chrome.tabs.get(created.id);
      if (tab.status === 'complete') {
        const url = new URL(tab.url);
        if (url.origin !== 'https://chatgpt.com' || url.pathname.toLowerCase() !== path)
          throw new Error('Unexpected Work page');
        return created.id;
      }
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error('Work tab load timed out');
  },
  async run(tabId, command) {
    const [frame] = await chrome.scripting.executeScript({
      target: { tabId }, func: runWorkPageCommand, args: [command]
    });
    return frame?.result;
  },
  async upload(tab, identity, file) {
    for (let offset = 0; offset < file.size; offset += MAX_RAW_CHUNK) {
      const bytes = new Uint8Array(await file.slice(offset, offset + MAX_RAW_CHUNK).arrayBuffer());
      const data = btoa(Array.from(bytes, byte => String.fromCharCode(byte)).join(''));
      const staged = await this.run(tab, { kind: 'file-chunk', identity, name: file.name, data });
      if (!staged?.ok) return staged;
    }
    return this.run(tab, { kind: 'upload', identity, name: file.name, size: file.size, mediaType: file.type });
  }
});

const bridge = new NativeBridgeClient(chrome.runtime, status => {
  void chrome.action.setBadgeText({ text: status.available ? 'ON' : 'OFF' });
  void chrome.action.setTitle({ title: 'PrintFlow bridge: ' + (status.reason ?? 'available') });
}, work, workTextTrialContract);

chrome.action.onClicked.addListener(() => bridge.connect());
chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (message?.kind !== 'OpenPrintFlowBridge' || sender.id !== chrome.runtime.id ||
      sender.url?.split('#')[0] !== chrome.runtime.getURL('connect.html') || !/^[A-F0-9]{32}$/.test(message.launchNonce)) return;
  bridge.connect(message.launchNonce);
  respond(true);
});
