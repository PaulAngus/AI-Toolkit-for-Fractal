# Execute Scene MIDI plans in populated presets

Release 0.7.0 adds a common existing-block executor for FM9, FM3 and Axe-Fx III. It consumes complete schema-3 saved data and schema-2 Scene MIDI plans. Exact matching messages are unchanged; an occupied conflicting row, absent block, ambiguous record, insertion action or incomplete before-values stops preflight. The ordinary CLI remains read-only. Actual hardware coverage is retained in [the qualification record](scene-midi-execution-validation.json).

## Plan and authorization

The caller chooses the device/firmware, exact saved targets, scenes, message slot, PC or CC, MIDI channel and encoded value, plus the controller for CC. Scenes/message slots/channels are one-based; preset slots and PC values retain their documented encoded addressing. There are no personal preset, port or message defaults. The existing Python planner still grants no execution authorization; its complete output is a reviewable input to the separately opted-in checker.

`SceneMidiEditPlan.Create` copies and strictly decodes an addressed saved capture, checks its wire hash, existing Scene MIDI instance prefix/shape, complete table and saved scene, and qualifies its backup upload form before editing. It refuses conflicting active messages. Off rows can receive PC/CC settings, with their before-fields retained. PC's unused controller word is preserved even if outside the active controller range. Already-matching rows produce no field commands.

`SceneMidiEditPlan.FromPlanner` additionally checks model/firmware/capacity, source/plan schema, target raw-image hash, status/conflicts, exact requested scenes/message, actions and every raw before-field against the actual saved capture. Missing blocks require a separate insertion operation and fresh export/replanning; new-block retained settings must not be treated as empty defaults. Sparse historical snapshots remain planning inputs but are not execution inputs.

## Execution and preservation

1. Validate all requested plans, capture manifests and hashes before opening ports. Keep one coordinated native MIDI owner for the explicit changed-target list. A failed item stops the rest; completed items are retained.
2. Read the currently selected preset's complete saved and live images and require equality and stable selection before navigation. Require the target's fresh saved wire hash to match its backup. Load it once and verify complete equality plus its saved default scene.
3. Bind one `SceneMidiEditScope` to one session. Permit only the exact next planned field, one target Save and verified load/Reload. Block insertion, Clear, relocation, scene selection, arbitrary fields and replacement of occupied rows are forbidden.
4. Prepare each Off row's channel, CC controller when applicable, and value before activating its type last. Write only fields that differ. Each field is attempted once. Require full edit-buffer preservation and an independently queried live table before advancing to the next field. Retain the live table's opaque extra word.
5. Verify the full body against the baseline with only those exact 16-bit table fields substituted. Names, routing, sound settings, channel/bypass data, other MIDI rows/scenes, inactive values, saved default scene and opaque body data remain byte-identical. Check the raw header and padding through the shared representation comparison.
6. Recheck complete preservation, unchanged saved baseline and selection immediately before one Save. Verify complete saved/live equality, repeated saved wire equality, Reload, and full saved/live/table equality again. Only completed persistence/reload verification produces an undo receipt.

CRC, compressed length and compressed-body bytes represent the changed body. They are strictly decoded/integrity-checked rather than required to remain byte-identical. Every other raw header field and padding outside the union of compressed regions stays exact; no default-scene normalization is allowed.

`SceneMidiEditBatchScope` permits distinct reviewed targets and a scope transition only after saved/reloaded completion. Already-correct targets receive a fresh saved-base check without navigation or mutation. Timeouts, cancellation and rejected transfers poison the session; no uncertain write is replayed. Request serialization and state bracketing do not replace one exclusive coordinating caller, or detect a brief external change and return.

## Explicit backup-based undo

`SceneMidiEditReceipt` contains the immutable original upload and exact verified result wire hash. `PrepareSceneMidiUndoAsync` requires the authorized restore scope to use that exact receipt upload and firmware, verifies the fresh saved result hash, and applies the existing full-preset restore preflight requiring identical saved/live contents. A later saved edit, unrelated unsaved work, wrong target or changed scene is a conflict.

Upload once to temporary memory; verify the entire original raw image/body before Save. Recheck both the saved result hash and complete original live buffer immediately before the undo Save. Repeat saved reads and verify Reload against the original. This is deliberately requested full-backup undo of a completed operation. It does not merge later edits, remove a pre-existing block, roll back an uncertain partial edit, or run automatically on failure.

The original 0.7.0 receipt is an in-process completion capability. The checker retains original/result hashes, saved/reloaded captures and private journals. Release 0.8.0 adds [persistent recovery and receipt import](scene-midi-recovery.md) with separate qualification. A later independently authorized restoration must use the retained original source and verified result as the expected current base through the separately documented [restore operation](preset-restore.md), with its own fresh checks.

FM9 12.00 passed one populated-preset PC edit followed by explicit whole-backup undo. The edit preserved all unrelated body/header/padding data and all 511 non-target saved slots. Undo restored the complete original loaded preset and all 512 original saved slots exactly. Later-edit refusal was tested synthetically, not by injecting a hardware conflict. Populated CC and multi-target message execution remain physically untested; the earlier empty-preset CC pilot has separate evidence.

## Opt-in checker

Build `tests/Fractal.HardwareChecks/Fractal.HardwareChecks.csproj`. Use `--hardware --apply-scene-midi` with explicit `--model`, optional Axe-Fx III `--capacity`, `--firmware`, `--backup` capture directory, `--plan` Python planner JSON, `--expected-hash` request SHA-256 and `--undo-after true|false`. Live execution also requires `--input`, `--output` and a new `--destination` private directory.

`--validate-only true` validates the request, full backup and executable before/after semantics without enumerating or opening ports. It requires no port or destination options. It reports changed/unchanged counts and exact field-command count; this is not evidence of fresh hardware state.

Save planner JSON as UTF-8 and hash those exact file bytes. The validation mode rejects a changed request hash, mismatched capture/manifest, conflicting message, forged before-fields or unsupported action before opening ports.

Live qualification captures full saved inventories before and after edits, proving every non-target slot unchanged. `--undo-after true` is restricted to one changed target: it completes the edit verification, opens a fresh restore scope, explicitly undoes it, and compares the entire saved inventory to the original. This option authorizes successful-operation undo only; a failed edit stops with retained evidence and requires fresh reconciliation. The device remains on the tested target's saved default scene. Inventories are sequential reads rather than an atomic lock.

## Sources and limits

The [reading map](../knowledge-base/reference-map.md) and [Scene MIDI field reference](scene-midi-reading.md) retain the manufacturer Blocks Guide section, pinned per-model `MIDIBLOCK` / `CMD_*_BEGIN` banks, saved first-word offset, live table transfer, and `setParam.ts` builder. These sources distinguish manufacturer message behavior, open implementation encodings, synthetic checks and toolkit hardware evidence. Identical Scene MIDI banks do not justify copying unrelated parameter ordinals between devices.

FM3/Axe-Fx III saved offsets, default-scene interpretation and editor behavior remain provisional until physically tested. No combined insertion/configuration transactions, occupied-message replacement, selective later-edit merge, block removal, automatic rollback/resume, downstream receiver behavior or deliberate interrupted-transfer/power-loss recovery is qualified by this operation. PC/CC physical coverage is reported separately rather than inferred from the common builder.
