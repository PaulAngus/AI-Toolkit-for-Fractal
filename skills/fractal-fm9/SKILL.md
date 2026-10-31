---
name: fractal-fm9
description: Reuse Fractal MIDI and SysEx evidence, plan block placement at a chosen cell, and plan Scene MIDI PC or CC settings from normalized FM9, FM3 or Axe-Fx III snapshots. FM9 protocol observations retain their model and firmware scope.
---

# Fractal MIDI knowledge and planning

The historical folder/name remains stable for existing installations. The packaged planner supports normalized FM9, FM3 and Axe-Fx III data; model-specific references are evidence, not interchangeable byte encodings. The skill alone contains reusable offline block-insertion and Scene MIDI planners. The repository additionally contains a read-only runtime; consult its documented CLI rather than inventing send commands.

- For general block insertion, caller-chosen coordinates or the explicit `first-isolated-empty` placement policy, read [block-insertion.md](references/block-insertion.md) and run [plan_block_insertion.py](scripts/plan_block_insertion.py). There is no implicit default cell; automatic placement preserves routing and does not configure an audio path.
- For normalized planning, read [snapshot-contract.md](references/snapshot-contract.md) and run [plan_scene_midi.py](scripts/plan_scene_midi.py).
- For Scene MIDI task semantics, recorded editor fields and default-scene preservation, read [scene-midi.md](references/scene-midi.md).
- For experimental editor commands, new-block settings, saved-scene semantics and the empty-preset recovery limits, read [editor-operations.md](references/editor-operations.md).
- For recorded FM9 framing/queries/dumps, read [fm9-protocol.md](references/fm9-protocol.md).
- For ownership, correlation and timeout behavior, read [midi-transactions.md](references/midi-transactions.md).
- For evidence provenance and document links, read [sources.md](references/sources.md).

Resolve target slots, scenes, message slot, message type, MIDI channel, value and CC controller from the request. Slots are zero-based; scenes/message slots are 1–8 and MIDI channels 1–16. One Scene MIDI block belongs to a preset; its settings vary by scene. Capacity and capabilities must match the normalized device context. Missing model labels do not justify substituting another device's amp catalogue.

The following is a synthetic offline example, not recommended device settings:

```shell
python scripts/plan_scene_midi.py --snapshot snapshot.json --preset 0 --channel 1 --value 1 --block-row 3 --block-column 10
```

Run from the skill directory or resolve the script relative to this file. Use `--all-populated` only for the requested snapshot scope. Optional `--scenes` and `--message-slot` narrow the task. `--type CC` requires `--controller`; the default type is PC. Complete version-3 tables include Off slots and raw inactive fields. Existing blocks can be used anywhere on the grid. Supply `--block-row` and `--block-column` when new blocks are needed; there is no implicit placement. Resolve generic block instance IDs from the actual model's evidence. Insertion planning validates grid placement only, not instance/DSP availability or defaults. Interpret planned, unchanged, skipped and conflict results independently per preset. An existing unread Scene MIDI table is a conflict, even when its message array is empty. Exports with `decode_errors` are refused to prevent omissions.

The planner has no apply command. A read-only runtime cannot satisfy a write request. For persistent edits use a separately implemented, qualified writer within existing user authorization: integrity-checked backups, current base hashes, preserved names/routing/messages/default scene and saved-state verification remain necessary. Loading another preset can discard unsaved edits; a saved backup cannot recover them. Reconcile uncertain results through reads before retrying mutations.

Report actual outcomes and model/firmware scope. Synthetic checks and historical session success do not establish new hardware validation. Do not infer authorization to open MIDI ports from repository setup, documentation or offline tests.
