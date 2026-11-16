const status = document.querySelector('p');
try {
  const launchNonce = location.hash.slice(1);
  if (!/^[A-F0-9]{32}$/.test(launchNonce)) throw new Error("Invalid PrintFlow launch");
  const accepted = await chrome.runtime.sendMessage({ kind: 'OpenPrintFlowBridge', launchNonce });
  status.textContent = accepted ? 'PrintFlow Studio is waiting for the local bridge.' : 'PrintFlow bridge unavailable.';
} catch {
  status.textContent = 'PrintFlow bridge unavailable.';
}
