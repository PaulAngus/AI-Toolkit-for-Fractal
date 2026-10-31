# General saved-edit batches

Release 0.19.0 composes existing general edit plans into one ordered, reviewed batch across FM9, FM3 and both Axe-Fx III capacities. Each item can combine mapped parameters, templates, scene settings, Scene MIDI and connected routing. Supported operations remain enabled; physical qualification is separate.

## Planning and execution

Create integrity-checked per-preset bundles with `plan-edit`, choosing `persistence: save`. Capture the initial current selection, saved image, complete temporary buffer and block status with `capture-control`. Build a schema-1 request with `context` containing that context object, `plans` containing ordered `{ "bundle": "relative-directory", "sha256": "plan-json-hash" }` references, and optional Boolean `discard_changes` (default false).

`create-edit-batch --request REQUEST_JSON --destination NEW_DIRECTORY` freezes a self-contained batch. It accepts 1–32 distinct target slots and at most 256 total operations in one explicit model/capacity/firmware context. Conflicts, non-executable plans and edit-buffer-only persistence are refused: moving to another preset would discard a temporary result. Unchanged plans are valid and require no navigation or writes.

The helper command is `--hardware --edit-batch --bundle BATCH_DIRECTORY --expected-hash BATCH_JSON_SHA256 --mode apply`. Add `--validate-only true` for offline validation. Deliberate execution additionally takes `--input`, `--output` and a new `--destination`; it uses one coordinated port connection. A host can instead supply its existing `IPresetRestoreTransport` to `PresetEditBatch.RunAsync`.

Execution freshly checks the complete initial context and every target's saved baseline before the first mutation. Each changed target is loaded using a fresh capture-bound load scope, then executed by the existing single-preset upload/complete-readback/Save/Reload verifier. A device may retain or reset its selected scene on recall. The reviewed load scope checks both saved images and the complete loaded body after recall. It sends one absolute scene selection only when readback shows a scene mismatch and no other decoded or opaque change; an already-correct scene is a verified no-op. The post-Save Reload verifier applies the same narrow check against the saved result before aligning a changed scene. Complete loaded content and the target's saved default scene must then verify. This also accommodates verified device recompression while preserving exact current and target state guards. Saved default scenes and unrelated content are preserved. Later items do not run after an uncertain result. Final saved reads check each item's actual observed result hash and the initial saved preset when it was not targeted. These serialized observations are not an atomic device-wide snapshot and do not claim preservation of unobserved slots.

Unknown unsaved work is preserved by default. Explicit `discard_changes: true` permits discarding only the retained initial loaded differences when navigation is required; that exact buffer stays in the frozen request. The final selection is the last changed target at its saved default scene. An entirely unchanged batch leaves selection and loaded contents alone.

## Interruption, reconciliation and undo

The execution directory retains the frozen batch, flushed/atomically replaced `progress.json` and each attempted item's independent transaction directory. Completed receipts survive later failures. Cancellation, timeouts, stale state and readback failure stop without automatic retry or rollback. A sent command alone never counts as a completed item.

`--mode reconcile` queries fresh target images and current saved/loaded state. It classifies each target as `original`, `result-observed`, `result-equivalent` for integrity-checked device recompression, or `other`; it sends no navigation or edits and does not manufacture a completion receipt from matching bytes. After reconciliation, capture fresh context and explicitly prepare a new batch containing only the intended remaining original-state plans. Mixed or later state requires a new deliberate plan. Never replay the interrupted batch.

A completed saved item supports existing exact-result undo using its `item-NNN/attempt.json` receipt. Select that target and its saved scene explicitly before executing undo. Undo refuses later saved or unsaved changes. There is no automatic whole-batch rollback.

FM9 12.00 physically passed one two-target saved-edit batch with verified Save/Reload for both items and exact receipt-bound undo in reverse order. One item combined a scene rename with a connected grid cable; the other renamed a scene. Fresh independent reads matched both original saved targets and the original selection, loaded/saved context and block status after undo. Two earlier trials stopped at scene selection during target recall and post-Save Reload respectively; each was reconciled and explicitly restored before a fresh attempt. These negative results and the exact physical limits are retained in [the editing validation record](preset-editing-validation.json). Other models, firmware, operations and interruption during upload or Save remain unqualified.

## MCP

Use `fractal_plan_preset_edit` for each target, then `fractal_capture_live_control_context` and `fractal_prepare_edit_batch(context_artifact, plan_artifacts, discard_changes?)`. Preparation freezes every source and validates through the real helper offline. The normal trusted-host exact approval and `fractal_execute_review` execute the ordered batch once.

Use `fractal_prepare_batch_reconciliation(batch_artifact)` with the original batch review ID, then the existing review execution flow. For a completed item, `fractal_prepare_preset_edit_undo(execution_artifact, item_index)` selects its zero-based receipt index. `fractal_read_artifact` can inspect `execution/progress.json` after interruption.

See [synthetic validation and evidence limits](batch-midi-validation.json). No hardware ports were opened to implement or validate this release.

## Organization source guards

[Preset organization](preset-organization.md) compiles to the same saved-edit batch. Optional schema-1 `guards` retain complete addressed source/destination captures and hashes (1–64 distinct slots); old requests omit this field. Guards are checked before the first mutation, each target transition/upload and final verification. Previously completed targets use their observed verified hashes. Progress now binds the immutable batch hash and current source release. Organization undo accepts only deeply verified completed changed items from that prefix, with fresh result/original captures; unknown active and untouched pending items are excluded.
