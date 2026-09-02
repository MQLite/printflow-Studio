# PrintFlow Studio — Production Mode Runbook

For the operator and for whoever supports the workstation. It covers three things: starting
production work, what to do when production is refused, and how to go back to Fake.

There is no override in this document, because there is none in the product. If the workstation
does not verify, production work does not run. That is the control working, not a fault in it.

---

## 1. What decides whether production runs

Two separate things, and it helps to keep them apart.

| | What it is | Where it is set | When it changes |
|---|---|---|---|
| **Adapter mode** | whether PrintFlow drives the real Photoshop and Meitu, or deterministic stand-ins | `appsettings.json` → `Adapters.Mode` | only by editing the file and restarting |
| **Workstation verification** | whether *this machine* is the accepted production workstation, right now | nowhere — it is observed | on every production step |

`Adapters.Mode = Production` means PrintFlow *may* use the real applications. It never means it
*will*. Every production step asks the workstation gate first, and a workstation that does not
verify is refused however the file is configured.

The shipped configuration is:

```json
"Adapters": {
  "Mode": "Production"
}
```

---

## 2. Before starting production work

1. Start PrintFlow.
2. From Home, open **Production Readiness**.
3. Production must show **Ready**.
4. **Advisories may remain visible.** They are reported on purpose and do not block anything. An
   advisory is an observation the accepted preset does not make a condition — the read-only file
   attribute note is the usual one, and SHA-256 remains the authority.
5. **If any accepted file has been replaced or updated since PrintFlow started, restart PrintFlow
   before using production mode.** That means: the accepted workstation record, the Photoshop
   installation, the Meitu installation, or the `PrintFlow DTF` Action file.

Point 5 is the one that surprises people, so it is worth saying plainly. Those four things are
read once, when PrintFlow first needs them, and the answer is kept for the life of the process.
Replacing one of them on disk does not change what a running PrintFlow accepts — in either
direction. A restart is what makes a replacement take effect, and it is also what stops a
replacement taking effect silently.

---

## 3. If production is refused

**Do not look for a way past it.** There isn't one, and adding one would defeat the point.

1. Open **Production Readiness**.
2. Read the checks marked as blocking. Each says what was expected and what was found.
3. Repair the workstation condition itself.
4. Then use the right button:

| The check that failed is about… | What to press | Why |
|---|---|---|
| the screen being locked, a remote session, the display or resolution, the Windows display language, the working folder | **Check again** | these are re-observed every time; fix it and PrintFlow sees the fix immediately |
| the accepted workstation record, the Photoshop or Meitu installation, or the Action file | **Restart PrintFlow** | these were read once at startup; a new reading needs a new process |

Common cases:

- **Screen was locked / workstation was left on a lock screen.** Unlock it, press **Check again**.
  Production reopens without restarting.
- **A second monitor was attached, or scaling changed.** Put the display back to the accepted
  configuration, press **Check again**.
- **Photoshop or Meitu was updated.** The installation no longer matches the accepted baseline.
  This is not something to work around at the machine: the workstation preset has to be
  revalidated and reissued before production automation resumes. Until then, Fake work is still
  available and PrintFlow still runs normally for everything else.
- **The Action file was edited or replaced.** Same answer. The canonical `PrintFlow DTF` Action is
  part of what was accepted; a changed one is a different Action.

While production is refused, PrintFlow still starts, still opens, and still runs every non-production
step. Verification failure closes production work; it does not close the application.

---

## 4. Rolling back to Fake

Rollback is a configuration edit and a restart. Nothing is migrated, and no data is rewritten.

1. Close PrintFlow.
2. Open `appsettings.json`.
3. Change the adapter mode back:

   ```json
   "Adapters": {
     "Mode": "Fake"
   }
   ```

4. Save the file.
5. Start PrintFlow.

To confirm the rollback took:

- PrintFlow starts normally.
- A step that would drive Meitu or Photoshop completes without either application appearing.
- Neither Photoshop nor Meitu is launched merely by starting PrintFlow — that is true in both
  modes.

What rollback does **not** touch: existing sessions, revisions, attempts, approved outputs, or the
database. The mode decides which adapters are composed when the process starts, and nothing else.
Rolling forward again is the same edit in reverse.

> Change only the `Mode` value. The `Preset` block — its id, version, path and expected digest —
> identifies the workstation this installation was accepted against, and editing it is not part of
> switching modes.

---

## 5. Things this runbook will not ask you to do

Listed explicitly, because each is a thing somebody eventually suggests on a bad afternoon:

- run production work on a workstation that is not showing **Ready**;
- edit the preset, the expected digest, or an accepted file to make a check pass;
- answer a Photoshop or Meitu dialog on PrintFlow's behalf during a run — PrintFlow does not
  answer prompts it did not raise, and neither should you while a step is running;
- switch to Fake to "get the job out" and then treat the result as a production file. Fake output
  is labelled as fake in the attempt record for exactly this reason.

If a job is urgent and the workstation will not verify, the honest escalation is to fix the
workstation or to reissue the preset — not to route around the gate.
