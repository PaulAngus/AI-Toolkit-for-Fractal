# Isolated block insertion in saved presets

Release 0.6.0 adds a common runtime that can insert one caller-resolved block instance into a caller-chosen isolated cell of an existing preset. This operation is distinct from routing, moving/removing existing blocks, configuring parameters and applying Scene MIDI message plans. FM9, FM3 and Axe-Fx III use the same plan, scope and coordinator with their explicit model definitions. Physical evidence is in [the validation record](block-insertion-validation.json).

`BlockInsertionPlan.Create` consumes an addressed, integrity-checked saved capture and its exact wire hash, model, row, column, exact instance ID and caller-supplied saved scene. It retains private copies rather than mutable caller bytes. The saved scene must match the retained body field. That field's physical meaning is qualified on FM9 12.00; other models remain provisional until tested.

The requested instance must be absent. Existing instances are reused by the offline planner; this insertion executor does not relocate, replace or implicitly edit them. The cell must be empty with zero route flags. For columns after the first, the entire preceding column must have zero route flags: this conservative rule rejects uncertain incoming routing rather than infer unknown route bits. The caller supplies placement explicitly.

## Execution and preservation

1. Validate every requested target, capture and hash before opening MIDI ports. A hash-bound request lists distinct slots, cells, instance IDs, saved scenes and source hashes. Capacity, firmware, ports and evidence destination are caller inputs.
2. Read the selected preset's complete live and saved images. Require equality and stable selection before loading a destination, so navigation cannot discard unknown unsaved work. Check the requested destination's fresh saved hash against its backup.
3. Load that exact destination once and require its full loaded raw/body image to equal the baseline, with the expected saved scene. Bind one `BlockInsertionScope` to one session. Only its target, cell, instance, Save and verified Reload are permitted; Clear, scene changes and parameter writes are forbidden.
4. Select the cell, then insert once. Read the complete loaded image before Save. Identify the inserted record by its actual instance prefix, remove its actual interval, and project only the chosen grid cell back to the original. Every remaining body byte must be identical. This includes routing, every existing block/channel/bypass/parameter, Scene MIDI active/inactive fields, names, saved default scene and opaque terminal data.
5. Preserve the raw preset header and padding. CRC, expanded/compressed length fields and the compressed-body region necessarily reflect the changed body; these representation fields are checked through strict transfer integrity and complete decoded preservation rather than required to remain byte-identical. No other raw header field is excluded.
6. Recheck complete loaded preservation, unchanged saved baseline and selected target immediately before one Save. Verify complete live/saved equality, repeat the saved read, reload once and verify again. A status query is supporting evidence rather than sufficient preservation proof.

The device supplies the newly inserted block's retained/default settings. The executor does not assume initialized defaults, write its parameters or connect it to audio. Device refusal, unavailable instances, DSP exhaustion, unexpected layouts, changed bases or unexplained differences stop processing. Only measured success qualifies a specific insertion case; the numeric range accepted by a typed builder is not an availability catalogue.

Cancellation, timeout and rejection poison the session. There is no automatic retry, blind replay, cleanup or rollback. Preserve private evidence and reconcile the fresh live/saved state first. [Full-preset restoration](preset-restore.md) is a separately qualified operation; it is not an implicit failure handler.

## Explicit multi-target checker

Build `tests/Fractal.HardwareChecks/Fractal.HardwareChecks.csproj`. Invoke `--hardware --insert-blocks` with `--input`, `--output`, `--model`, optional explicit Axe-Fx III `--capacity`, exact `--firmware`, `--backup` capture directory, `--plan` JSON file, `--expected-hash` request-file SHA-256 and a new `--destination`. The ordinary CLI remains read-only.

The request is an array of objects containing `slot`, `row`, `column`, `effect_id`, `saved_scene` and `sysex_sha256`. Use caller-resolved values; no device configuration is shipped. All captures must match the supplied manifest and request hashes. One native MIDI connection remains open for the request. Device identity/firmware are established once; per-item sessions borrow that connection and release their subscriptions without closing its ports. `BlockInsertionBatchScope` allows only reviewed items and yields to a new item after complete saved/reload verification, with no scope reuse. A 10 ms query delay is toolkit policy, not a manufacturer timing requirement.

Each item retains immutable saved/reloaded exports and a result; the private progress record reports completed items. A failure stops the remaining items, retaining the active stage and target. Already completed items are not automatically undone. Rerunning the same request does not silently duplicate an insertion or resume it; reconciliation is a separate step.

Run one scratch-copy pilot before a larger explicitly authorized set. Capture complete saved inventories before and after to verify every non-target slot as well as each target's projected preservation. Inventories are sequential reads, not an atomic device lock. One coordinated owner must control the device throughout; state checks cannot detect a brief change and return or protect against another writer between checks.

## Sources and limits

Use the retained pinned `gen3-body` implementation's `EFFECT_BASES` / `effectName` for exact instance research, and the manufacturer MIDI effect-ID appendix for its own documented scope. The [Blocks Guide](https://www.fractalaudio.com/downloads/manuals/fas-guides/Fractal-Audio-Blocks-Guide.pdf), block table and relevant family section, establishes user-level availability/behavior; it does not specify private insertion encoding. The pinned `setParam.ts` `buildSelectCell` / `buildInsertBlock` locators are retained in [editing-pilot.md](editing-pilot.md). Do not generalize parameter ordinals across models.

The [reading map](../knowledge-base/reference-map.md) locates these documents and code symbols before another broad search. Physical coverage is limited to the recorded model/firmware and one caller-selected non-Scene-MIDI audio block type. It does not qualify arbitrary block families, connected audio routing, new defaults, automatic undo, resumable batches, external receivers or other-model hardware. The original Scene MIDI editing pilot remains separately scoped.

FM9 12.00 passed 52 complete saved/reloaded insertions, with every existing body byte preserved, 51 existing MIDI tables unchanged and all 460 non-target saved slots byte-identical. Two pre-edit identity-read timeouts stopped their requests without editing the next target. A continuation requires fresh baseline reconciliation and one coordinated owner. Root cause was not established; these observations do not qualify recovery after an uncertain write. Full metrics, build versions, negative evidence and neutral private-artifact hashes are in [the record](block-insertion-validation.json).

Release 0.9.0 adds `--validate-only true` to inspect the exact insertion request/backups without ports or a destination. [The local MCP adapter](mcp.md) uses this same preflight before creating an execution review; it introduces no new insertion encoding or physical qualification.

Automatic planning can supply concrete cells through the explicit [first-isolated-empty policy](automatic-placement.md). The isolated execution guard now checks the target incoming mask and references to its row in the following column; unrelated routes remain allowed. This guard correction has synthetic coverage and adds no physical qualification.
