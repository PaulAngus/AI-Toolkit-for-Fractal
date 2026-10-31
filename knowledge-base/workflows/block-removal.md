# Remove a block or undo its insertion

Resolve the exact block instance from the actual model's [block catalogue](block-identification.md). Read complete saved backups and plan its removal from explicit presets. Removal is independent of Scene MIDI or any other block family. Existing names, other parameters, messages, routing and saved default scene must be preserved.

Use [the removal operation](../../docs/block-removal-execution.md) for an isolated block. The deterministic [offline planner](../../scripts/plan_block_removal.py) returns planned, unchanged or conflict outcomes. Connected or uncertain routing requires a separately qualified routing policy and is refused. Unknown IDs or records are not guessed.

To undo a verified insertion, retain its exact original backup and verified inserted result. `fractal_prepare_insertion_undo` binds those bytes into a new removal review. The complete original image must be restored before saving. Later edits invalidate this exact undo; ordinary removal against fresh evidence is a separate caller decision. A repeated plan with the block already absent needs no edit.

Follow [backup and preservation checks](backups-verification.md), keep one MIDI owner and preserve unsaved work before navigation. Save once only after complete loaded verification, then verify repeated saved reads and Reload. Inspect [qualified results](../../docs/block-removal-validation.json) for model/firmware and family limits. A silence or acknowledgement is not persistence evidence.
