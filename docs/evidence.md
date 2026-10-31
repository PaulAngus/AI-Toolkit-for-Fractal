# Evidence scope

Imported knowledge dates to **7 October 2026**. No hardware was contacted during toolkit initialization or knowledge curation. Technical source files, hashes and revisions are recorded in [provenance.json](provenance.json); source attribution does not make the originating application's behavior part of this toolkit.

| Category | Meaning |
|---|---|
| Manufacturer documentation | Published specification, within its stated device scope |
| Open implementation | Inspectable code or author research; independent validation still matters |
| Recorded hardware observation | Prior device results, distinguished from retained replayable captures |
| Inference | Interpretation supported by evidence but not directly established |
| Unknown / proposed | Insufficient validation or an unimplemented method |

## Recorded device reads

FM9 firmware 12.00 observations support identity/firmware queries, preset/scene names, complete saved-preset reads, decoded Amp channels/scene assignments and a 336-entry amp roster. Recorded validation includes the 512-slot address range and eight scenes in six navigation presets.

A saved-read run observed an unexplained active-scene change while transmitting read requests. A later run preserved selection. The reusable conclusion is to bracket reads with state checks and retain failures, rather than guarantee state preservation. These observations do not establish audio quality, DSP performance or unconditional coexistence with other MIDI writers.

Live and stored state are distinct. One monitored saved read produced no reported audible change; that does not validate every operation or a full scan's audio continuity. Scene Ignore and unsaved edits can qualify comparisons with an editor.

FM3, Axe-Fx III variants and other FM9 firmware remain unverified by these imported observations. Candidate format implementations and synthetic fixtures do not extend physical compatibility. Capacity probing must distinguish positive exact-address evidence from a timeout.

## Recorded persistent editing

The 2 October 2026 FM9 session reported saving/reloading a new Scene MIDI block and verifying one PC channel/value combination in all eight scenes. Evidence covers new-block placement in an empty cell and a limited set of presets, not a completed all-device bulk edit. The temporary writer and personal backup collection were not retained in this toolkit, so these results are historical reports rather than executable replay fixtures.

Preservation verification found that saving could change the preset's default scene. Restoring the original default scene removed the unrelated normalized difference. This supports a general preservation requirement for preset editing; this is a general preservation constraint.

Other message types, existing-message replacement, block relocation and automatic restore streams require separate validation. The absence of a rejection reply is not proof of persistence. Saved readback and downstream receiver behavior are separate verification steps.

## Implementation limits

The toolkit implements common normalized planning, contained read-only runtime/export, amp lookup and knowledge search. Release 0.2.1 has a [FM9 12.00 read-only physical pass](hardware-validation.md). FM3/Axe-Fx III remain synthetic-only. Guarded editing, full-preset restoration and the local MCP adapter are implemented separately; consult their operation-specific records rather than extending this read-only qualification. Planning a parameter value is not proof of hardware encoding support.

Pacing, retry limits, queue bounds and logging policies are transport implementation choices. A historical 10 ms query interval was not a manufacturer requirement or a proven remedy. Preserve correlation constraints and measure target-specific behavior before choosing policies.

Sources are in the [curated register](../knowledge-base/catalog/sources.md) and [portable skill references](../skills/fractal-fm9/references/sources.md). Personal transcripts, application documentation copies, credentials, preset backups and private names are excluded.

## Toolkit physical reads, 8 October 2026

The new Windows runtime read and decoded all 512 saved FM9 12.00 slots (254 populated / 258 empty), with state checks after each 32 slots and nine byte-identical repeat samples. Its 586 transmitted requests were all read-allowlisted. Receive timeout/cancellation used actual replies deliberately withheld from the coordinator; late replies were rejected and ports reopened successfully. Independent export hash checks passed. See [hardware-validation.md](hardware-validation.md) for the exact scope and retained negative evidence. Raw captures are retained privately; the public report carries hashes and redacted observations. No toolkit writes, navigation, restore, physical cable removal or independent front-panel semantic comparisons were performed.

## Toolkit Scene MIDI reads, 8 October 2026

Release 0.3.0 compared the current FM9 12.00 editor-protocol table with all 256 directly read saved words. Ten physical assertions passed with 14 allowlisted requests; no navigation or editing occurred. Offline redecoding of all 512 retained captures produced 254 complete tables and 258 absent blocks. Planning the already-present PC settings yielded 254 unchanged with no conflicts/actions. This is new reading evidence, not proof that the toolkit performed historical writes. Active CC/varied-channel hardware cases and FM3/Axe-Fx III physical behavior remain untested. See [Scene MIDI reading](scene-midi-reading.md) and its redacted evidence.

## Toolkit empty-preset editing, 10 October 2026

Release 0.4.0 physically qualified FM9 12.00 Scene MIDI insertion/removal, active PC/CC settings, all eight scenes, varied channels/values, save/reload and original saved default-scene preservation. The complete pilot passed 62 assertions with 106 sends and six verified exports; the original saved raw image/body and selection were restored exactly. Three failed attempts and two explicit recoveries remain qualified in [redacted evidence](editor-pilot-validation.json). No full-dump restore, populated-preset edit, other-model physical compatibility, downstream receiver observation or independent editor display is established. See [commands and recovery boundaries](editing-pilot.md).
