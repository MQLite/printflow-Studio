// Chrome injects this function by value into the extension's isolated world.
export async function runWorkPageCommand(command, testPage) {
  const doc = testPage?.doc ?? globalThis.document;
  const url = testPage?.url ?? globalThis.location;
  const state = testPage?.state ?? (globalThis.__printflowWorkPage ??= {});
  const visible = testPage?.visible ?? (element => element.getClientRects().length > 0 &&
    getComputedStyle(element).visibility === 'visible');
  const find = (parent, selector, match = () => true) => {
    const matches = Array.from(parent.querySelectorAll(selector)).filter(e => visible(e) && match(e));
    return matches.length === 1 ? matches[0] : null;
  };
  const fail = reason => ({ ok: false, reason });
  const workToggle = () => find(doc, 'button[aria-pressed]', e => e.textContent.trim() === 'Work');
  const chatEditor = () => find(doc, '[role="textbox"][contenteditable="true"][aria-label="Ask ChatGPT"]');
  const editor = () => find(doc, '[role="textbox"][contenteditable="true"][aria-label="Work with ChatGPT"]');
  const editableText = input => input.innerText.replace(/\r\n/g, '\n');
  const composerText = input => {
    const paragraphs = Array.from(input.childNodes ?? []);
    if (!paragraphs.length || !paragraphs.every(node => node.tagName === 'P')) return editableText(input);
    // The editor uses one paragraph per line. Root innerText adds display spacing between paragraphs.
    return paragraphs.map(paragraph => {
      const child = paragraph.firstChild;
      if (paragraph.childNodes.length === 1 && child.nodeName === 'BR' &&
          child.classList.contains('ProseMirror-trailingBreak')) return '';
      return editableText(paragraph);
    }).join('\n');
  };
  const uploadName = /^attachment-\d+(?:-[0-9a-f]{32})?\.(png|jpe?g|webp|pdf|txt|md|csv|docx|xlsx|pptx)$/;
  const removes = form => Array.from(form.querySelectorAll('button[aria-label^="Remove "]')).filter(visible);
  const readyFile = (form, name) => find(form, 'button[aria-label], [role="button"][aria-label]',
    e => e.getAttribute('aria-label') === name && !e.disabled && e.getAttribute('aria-disabled') !== 'true') &&
    removes(form).some(e => e.getAttribute('aria-label') === 'Remove ' + name);
  const matchesPath = path => url.pathname.toLowerCase() === path;
  const isGenerating = () => Array.from(doc.querySelectorAll(
    'button[data-testid="stop-button"], button[aria-label="Stop generating"]')).some(visible);
  const poll = async (read, milliseconds) => {
    const deadline = Date.now() + milliseconds;
    do {
      const value = read();
      if (value) return value;
      if (testPage) {
        if (!testPage.waitForUpdate) return null;
        await testPage.waitForUpdate();
      } else {
        await new Promise(resolve => setTimeout(resolve, 100));
      }
    } while (Date.now() < deadline);
    return null;
  };
  if (url.origin !== 'https://chatgpt.com' || typeof command.identity !== 'string' || !command.identity)
    return fail('WorkPageUnknown');

  if (command.kind === 'stage') {
    const names = command.attachmentNames ?? [];
    const followUp = command.followUpConversationId;
    const path = followUp ? '/c/' + followUp : '/';
    if ((followUp && (!/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(followUp) || names.length)) ||
        !matchesPath(path) || state.identity || typeof command.prompt !== 'string' ||
        !command.prompt.trim() || command.prompt.length > 100000 || !Array.isArray(names) || names.length > 8 ||
        names.some(name => typeof name !== 'string' || !uploadName.test(name)) || new Set(names).size !== names.length)
      return fail('WorkPageUnknown');
    const toggle = followUp ? null : await poll(workToggle, 5000);
    if (!matchesPath(path) || (!followUp && !toggle)) return fail('WorkModeUnavailable');
    if (toggle?.getAttribute('aria-pressed') === 'false') {
      if (state.modeSwitchRequested || toggle.disabled || toggle.getAttribute('aria-disabled') === 'true')
        return fail('WorkModeUnavailable');
      const chat = await poll(chatEditor, 5000);
      const chatForm = chat?.closest('form');
      const currentToggle = workToggle();
      if (!chatForm || url.pathname !== '/' || !currentToggle || currentToggle.disabled ||
          currentToggle.getAttribute('aria-disabled') === 'true')
        return fail('WorkModeUnavailable');
      if (composerText(chat).trim() || removes(chatForm).length ||
          Array.from(chatForm.querySelectorAll('[role="progressbar"]')).some(visible))
        return fail('WorkComposerChanged');
      if (currentToggle.getAttribute('aria-pressed') === 'false') {
        state.modeSwitchRequested = true;
        currentToggle.click();
      }
    }
    const input = await poll(() => matchesPath(path) &&
      (followUp || workToggle()?.getAttribute('aria-pressed') === 'true') && editor(), 5000);
    if (!input) return fail('WorkModeUnavailable');
    const form = input?.closest('form');
    const prompt = command.prompt.replace(/\r\n/g, '\n');
    const existing = input ? composerText(input) : '';
    if (!form || (existing.trim() && existing !== prompt) || (followUp && isGenerating()) ||
        find(form, '[role="progressbar"]') ||
        Array.from(form.querySelectorAll('button[aria-label^="Remove "]')).some(visible))
      return fail('WorkComposerChanged');
    state.identity = command.identity;
    state.path = path;
    state.followUp = followUp;
    state.attachmentNames = names;
    state.uploaded = [];
    state.prompt = prompt;
    if (existing !== prompt) {
      input.focus();
      doc.execCommand('insertText', false, prompt);
    }
    // Work can rebuild the editor after insertion. Verify its live content before enabling uploads.
    const stagedForm = await poll(() => {
      const current = editor();
      return matchesPath(path) && current && composerText(current) === prompt && current.closest('form');
    }, 2500);
    if (!stagedForm) return fail('WorkPromptRejected');
    if (!await poll(() => find(stagedForm, 'button[type="submit"][aria-label="Send"]', e => !e.disabled), 2500))
      return fail('WorkSendUnavailable');
    return { ok: true };
  }

  if (command.kind === 'file-chunk' || command.kind === 'upload') {
    const name = state.attachmentNames?.[state.uploaded?.length];
    if (state.identity !== command.identity || state.clicked || url.pathname !== '/' ||
        !name || name !== command.name || workToggle()?.getAttribute('aria-pressed') !== 'true')
      return fail('WorkComposerChanged');
    if (command.kind === 'file-chunk') {
      if (typeof command.data !== 'string' || !/^[A-Za-z0-9+/]+={0,2}$/.test(command.data) ||
          command.data.length > 349528) return fail('WorkUploadFailed');
      const bytes = Uint8Array.from(atob(command.data), c => c.charCodeAt(0));
      const pending = state.pending ??= { size: 0, parts: [] };
      pending.size += bytes.length;
      if (pending.size > 40 * 1024 * 1024) return fail('WorkUploadFailed');
      pending.parts.push(bytes);
      return { ok: true };
    }
    const pending = state.pending;
    state.pending = undefined;
    const input = editor();
    const form = input?.closest('form');
    const pickers = form?.querySelectorAll('input[type="file"][aria-label="Attach files"]');
    if (!pending || pending.size !== command.size || typeof command.mediaType !== 'string' ||
        !pickers || pickers.length !== 1 || pickers[0].disabled || composerText(input) !== state.prompt ||
        removes(form).length !== state.uploaded.length || !state.uploaded.every(file => readyFile(form, file)))
      return fail('WorkComposerChanged');
    const transfer = new DataTransfer();
    transfer.items.add(new File(pending.parts, name, { type: command.mediaType }));
    pickers[0].files = transfer.files;
    pickers[0].dispatchEvent(new Event('change', { bubbles: true }));
    if (!await poll(() => readyFile(form, name) && removes(form).length === state.uploaded.length + 1 &&
        !Array.from(form.querySelectorAll('[role="progressbar"]')).some(visible) &&
        find(form, 'button[type="submit"][aria-label="Send"]', e => !e.disabled), 45000))
      return fail('WorkUploadFailed');
    state.uploaded.push(name);
    return { ok: true };
  }

  if (command.kind !== 'submit' || state.identity !== command.identity || state.clicked ||
      typeof command.permitId !== 'string' || !command.permitId ||
      !Number.isSafeInteger(command.expiresAt) || Date.now() >= command.expiresAt || !matchesPath(state.path))
    return fail('WorkPermitMismatch');
  if (!state.followUp && workToggle()?.getAttribute('aria-pressed') !== 'true') return fail('WorkModeUnavailable');

  const input = editor();
  const form = input?.closest('form');
  const send = form && find(form, 'button[type="submit"][aria-label="Send"]', e => !e.disabled);
  if (!send || composerText(input) !== state.prompt || (state.followUp && isGenerating()) ||
      state.uploaded.length !== state.attachmentNames.length || removes(form).length !== state.attachmentNames.length ||
      !state.attachmentNames.every(name => readyFile(form, name)) ||
      Array.from(form.querySelectorAll('[role="progressbar"]')).some(visible))
    return fail('WorkComposerChanged');

  const matchingUserMessages = () => Array.from(doc.querySelectorAll('[data-user-message-bubble] .whitespace-pre-wrap'))
    .filter(bubble => editableText(bubble) === state.prompt).length;
  const before = matchingUserMessages();
  // Reserve the page-side click before interacting with the site, including uncertain outcomes.
  state.clicked = true;
  send.click();
  const conversation = await poll(() => {
    const match = /^\/c\/([0-9a-f-]{36})$/i.exec(url.pathname);
    return match && (!state.followUp || matchesPath(state.path)) && matchingUserMessages() > before && match[1];
  }, 7000);
  return conversation ? { ok: true, conversation } : fail('WorkSubmitUnconfirmed');
}
