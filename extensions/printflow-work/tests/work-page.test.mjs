import assert from 'node:assert/strict';
import test from 'node:test';
import { runWorkPageCommand } from '../work-page.js';

const originalConversation = 'a9999999-0000-4000-8000-000000000000';

function fixture({ work = true, switchWork = false, attachments = 0, renderParagraphs = false, existingPrompt = '',
  settleAfterInsert = false, posts = true, messages = [] } = {}) {
  const docUrl = new URL('https://chatgpt.com/');
  const editor = { innerText: existingPrompt, focus() {} };
  let modeClicks = 0;
  const toggle = {
    textContent: 'Work',
    getAttribute(name) { return name === 'aria-pressed' ? String(work) : null; },
    click() { modeClicks++; if (switchWork) work = true; }
  };
  let clicks = 0;
  let enteredText = existingPrompt;
  let insertions = 0;
  const send = { disabled: false, click() {
    clicks++;
    docUrl.pathname = '/c/' + originalConversation;
    if (posts) messages.push(enteredText);
  } };
  const stop = {};
  const chips = Array.from({ length: attachments }, () => ({}));
  const form = { querySelectorAll(selector) {
    if (selector.includes('Send')) return [send];
    if (selector.includes('Stop')) return clicks ? [stop] : [];
    if (selector.includes('Remove')) return chips;
    return [];
  } };
  editor.closest = () => form;
  const doc = {
    querySelectorAll(selector) {
      if (selector.includes('aria-pressed')) return [toggle];
      if (selector.includes('Work with ChatGPT')) return work ? [editor] : [];
      if (selector.includes('Ask ChatGPT')) return work ? [] : [editor];
      if (selector.includes('data-user-message-bubble')) return messages.map(innerText => ({ innerText }));
      return [];
    },
    execCommand(command, _, text) {
      assert.equal(command, 'insertText');
      assert.equal(work, true, 'the Work mode must be active before entering the prompt');
      insertions++;
      enteredText = text;
      if (settleAfterInsert) {
        editor.innerText = text.replace('\n\n', '\n');
        return false;
      }
      editor.innerText = text;
      if (renderParagraphs) {
        const lines = text.split('\n');
        editor.innerText = lines.join('\n\n');
        editor.childNodes = lines.map(line => {
          const trailingBreak = { nodeName: 'BR', classList: { contains: value => value === 'ProseMirror-trailingBreak' } };
          return { tagName: 'P', innerText: line || '\n', childNodes: line ? [] : [trailingBreak], firstChild: line ? null : trailingBreak };
        });
      }
      return true;
    }
  };
  return { doc, url: docUrl, state: {}, visible: () => true,
    waitForUpdate: settleAfterInsert ? async () => { editor.innerText = enteredText; } : undefined,
    get clicks() { return clicks; }, get insertions() { return insertions; }, get modeClicks() { return modeClicks; } };
}

const identity = 'distinct-request/attempt';
const prompt = '一张白底青绿色圆形图片。';

test('a follow-up is staged and sent in the original conversation and requires a new message', async () => {
  for (const posts of [true, false]) {
    const page = fixture({ posts, messages: [prompt] });
    page.url.pathname = '/c/' + originalConversation;
    const stage = { kind: 'stage', identity, prompt, followUpConversationId: originalConversation };
    assert.equal((await runWorkPageCommand(stage, page)).ok, true);
    assert.equal(page.clicks, 0);
    const permit = { kind: 'submit', identity, permitId: 'permit-1', expiresAt: Date.now() + 5000 };
    const receipt = await runWorkPageCommand(permit, page);
    assert.deepEqual(receipt, posts ? { ok: true, conversation: originalConversation } :
      { ok: false, reason: 'WorkSubmitUnconfirmed' });
    assert.equal(page.clicks, 1);
    assert.equal(page.modeClicks, 0);
  }
});

test('a follow-up refuses a different conversation before staging and before the permitted click', async () => {
  const page = fixture();
  const stage = { kind: 'stage', identity, prompt, followUpConversationId: originalConversation };
  assert.equal((await runWorkPageCommand(stage, page)).ok, false);
  assert.equal(page.insertions, 0);
  page.url.pathname = '/c/' + originalConversation;
  assert.equal((await runWorkPageCommand(stage, page)).ok, true);
  page.url.pathname = '/c/c9999999-0000-4000-8000-000000000000';
  const permit = { kind: 'submit', identity, permitId: 'permit-1', expiresAt: Date.now() + 5000 };
  assert.equal((await runWorkPageCommand(permit, page)).ok, false);
  assert.equal(page.clicks, 0);
});

test('a staged Work prompt is clicked once and a replay cannot click again', async () => {
  const page = fixture();
  assert.equal((await runWorkPageCommand({ kind: 'stage', identity, prompt }, page)).ok, true);
  assert.equal(page.clicks, 0);
  const command = { kind: 'submit', identity, permitId: 'permit-1', expiresAt: Date.now() + 5000 };
  assert.equal((await runWorkPageCommand({ ...command, identity: 'old-attempt' }, page)).ok, false);
  assert.equal(page.clicks, 0);
  assert.equal((await runWorkPageCommand(command, page)).ok, true);
  assert.equal((await runWorkPageCommand(command, page)).ok, false);
  assert.equal(page.clicks, 1);
  assert.equal(page.modeClicks, 0);
});

test('a fresh empty Chat tab switches to Work before accepting the prompt or attachments', async () => {
  const page = fixture({ work: false, switchWork: true });
  assert.deepEqual(await runWorkPageCommand({ kind: 'stage', identity, prompt,
    attachmentNames: ['attachment-01.png'] }, page), { ok: true });
  assert.equal(page.modeClicks, 1);
  assert.equal(page.insertions, 1);
  assert.equal(page.clicks, 0);
});

test('unconfirmed Work mode refuses input and upload; existing Chat drafts are left alone', async () => {
  const unavailable = fixture({ work: false });
  const occupied = fixture({ work: false, switchWork: true, existingPrompt: 'Unsent draft' });
  for (const page of [unavailable, occupied]) {
    assert.equal((await runWorkPageCommand({ kind: 'stage', identity, prompt,
      attachmentNames: ['attachment-01.png'] }, page)).ok, false);
    assert.equal((await runWorkPageCommand({ kind: 'file-chunk', identity,
      name: 'attachment-01.png', data: 'AA==' }, page)).ok, false);
    assert.equal(page.insertions, 0);
    assert.equal(page.clicks, 0);
  }
  assert.equal(unavailable.modeClicks, 1);
  assert.equal(occupied.modeClicks, 0);
});

test('non-Work composer and an unexpected attachment cannot stage a pure-text submission', async () => {
  for (const page of [fixture({ work: false }), fixture({ attachments: 1 })]) {
    assert.equal((await runWorkPageCommand({ kind: 'stage', identity, prompt }, page)).ok, false);
    assert.equal((await runWorkPageCommand({ kind: 'submit', identity, permitId: 'permit-1', expiresAt: Date.now() + 5000 }, page)).ok, false);
    assert.equal(page.clicks, 0);
  }
});

test('paragraph display spacing does not change a multiline prompt or its intentional blank line', async () => {
  const page = fixture({ renderParagraphs: true });
  const multiline = '参考图片：attachment-01.png\r\n\r\n生成一张白底蓝色三角形图片。';
  assert.equal((await runWorkPageCommand({ kind: 'stage', identity, prompt: multiline }, page)).ok, true);
  const permit = { kind: 'submit', identity, permitId: 'permit-1', expiresAt: Date.now() + 5000 };
  const paragraphs = page.doc.querySelectorAll('[aria-label="Work with ChatGPT"]')[0].childNodes;
  paragraphs[0].innerText = '参考图片：attachment-02.png';
  assert.equal((await runWorkPageCommand(permit, page)).ok, false);
  assert.equal(page.clicks, 0);
  paragraphs[0].innerText = '参考图片：attachment-01.png';
  assert.equal((await runWorkPageCommand(permit, page)).ok, true);
  assert.equal(page.clicks, 1);
});

test('a retry accepts its exact restored prompt without overwriting another draft', async () => {
  const matching = fixture({ existingPrompt: prompt });
  assert.equal((await runWorkPageCommand({ kind: 'stage', identity, prompt }, matching)).ok, true);
  assert.equal(matching.insertions, 0);
  const different = fixture({ existingPrompt: 'Other draft' });
  const result = await runWorkPageCommand({ kind: 'stage', identity, prompt }, different);
  assert.deepEqual(result, { ok: false, reason: 'WorkComposerChanged' });
  assert.equal(different.insertions, 0);
});

test('staging waits for the exact multiline editor content after one insert attempt', async () => {
  const page = fixture({ settleAfterInsert: true });
  const multiline = '使用附件中的参考图。\n\n请生成红色三角形。';
  const result = await runWorkPageCommand({ kind: 'stage', identity, prompt: multiline,
    attachmentNames: ['attachment-01.png'] }, page);
  assert.deepEqual(result, { ok: true });
  assert.equal(page.insertions, 1);
  assert.equal(page.clicks, 0);
});

test('staging accepts two request-specific upload names', async () => {
  const page = fixture();
  const names = ['attachment-01-0123456789abcdef0123456789abcdef.png',
    'attachment-02-0123456789abcdef0123456789abcdef.png'];
  const result = await runWorkPageCommand({ kind: 'stage', identity, prompt: names.join('\n'),
    attachmentNames: names }, page);
  assert.deepEqual(result, { ok: true });
  assert.equal(page.clicks, 0);
});
