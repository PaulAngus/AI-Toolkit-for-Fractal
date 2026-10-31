# Isolated block removal and insertion undo

Release 0.12.0 implements removal of one exact block instance per preset, independent of block family and AI provider. FM9, FM3 and both Axe-Fx III capacities share the planner, runtime guards and MCP contracts. [Validation](block-removal-validation.json) distinguishes synthetic coverage from measured hardware results.

Removal currently requires an isolated cell: zero outgoing route flags and, for a cell beyond the first column, zero route flags throughout the preceding column. Connected blocks are refused because clearing or reconnecting an audio path requires a separate routing policy. An absent instance is an unchanged planning result. Ambiguous IDs, incomplete grids and unidentified instance records are refused.

The ordinary workflow is `fractal_capture_presets`, `fractal_plan_block_removal` with explicit slots and an exact model-qualified `effect_id`, then `fractal_prepare_removal`. Planning and preparation open no MIDI ports. Preparation regenerates removal actions from the integrity-checked retained snapshot; model-written cells or stale base hashes cannot replace that evidence. `fractal_execute_review` requires a fresh exact host approval. Poll `fractal_inspect_attempt`; acceptance does not establish completion.

Before navigation, the runtime requires complete equality of the selected preset's loaded and saved images and stable selection. It rechecks the target's saved hash, loads once, checks the saved default scene, selects the exact cell and sends Clear once. Complete loaded verification identifies the removed instance's actual record interval and requires every other body byte and grid cell to match the baseline. Raw header and padding preservation, strict transfer integrity, names, routing, all other block settings, Scene MIDI and default scene are checked. Immediately before Save it checks again. One Save is followed by complete live/saved equality, a repeated saved read and one Reload with further verification.

## Undo an insertion

Use `fractal_prepare_insertion_undo` with the `execution_artifact` of a **verified-complete insertion**, then approve and execute its new review. This uses the same removal executor rather than uploading the entire original preset. It binds the consumed insertion review, terminal attempt, verified result captures and original backups. Partial, damaged, mismatched or stale evidence is refused. Removal must match the entire original raw/body image **before Save**, followed by repeated saved/reload verification. Later saved edits or any unsaved work block execution rather than being overwritten.

This explicit undo removes only the added block. It does not undo later configuration edits. After additional changes, plan ordinary removal against a fresh backup if that is the authorized intention; exact insertion undo is deliberately unavailable. Already absent blocks need no second mutation. A consumed execution approval cannot be reused.

## Contained runtime

`BlockRemovalPlan`, `BlockRemovalScope` and `ReadSession.PrepareBlockRemovalAsync` implement the shared operation. `EditorPreservation.VerifyBlockRemoval` uses the inverse insertion projection, preserving the remaining bytes independently of device acknowledgements. The opted-in checker is `--hardware --remove-blocks`, with the same explicit model/firmware/backup/hash/port/destination options as [insertion](block-insertion-execution.md). Each request target supplies `slot`, `row`, `column`, `effect_id`, `saved_scene` and `sysex_sha256`. Insertion undo additionally supplies `undo_file` and `undo_sha256` with a complete `--undo-backup` directory. `--validate-only true` verifies these without ports. The ordinary CLI remains read-only.

Cancellation, rejection or timeouts stop processing and poison the session. Failed verification prevents Save. There is no automatic replay, rollback, upload or resume. Retain the private original and current captures, reconcile live/saved state, and use separately authorized [full restoration](preset-restore.md) if needed. Sequential inventories cannot exclude another writer's transient changes; one coordinated MIDI owner is required.

The retained editor-action implementation locators, including `buildClearBlockCompanion` and `buildSelectCell`, are in the [reference map](../knowledge-base/reference-map.md) and [editor evidence](editing-pilot.md); source attribution is distinct from hardware qualification. Arbitrary families, connected routing and other-model hardware remain separately qualified.

Isolation now checks the target incoming mask and outgoing references in the following column. Unrelated routes no longer prevent removal. See [destination-mask checks and synthetic evidence](automatic-placement.md).
