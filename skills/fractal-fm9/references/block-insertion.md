# Plan a block insertion

The general operation ensures that a caller-resolved **block instance** exists in a preset. It supports normalized FM9, FM3 and Axe-Fx III snapshots and uses a chosen cell when that instance is absent. Scene MIDI message planning calls this same placement operation with effect ID 190. Placement is explicit: supply coordinates or `--placement-policy first-isolated-empty`. The policy scans columns then rows, reuses existing instances, and selects only an empty cell with no incoming or outgoing connections. It never replaces a shunt, creates routing or invents block defaults. Coordinates and policy are mutually exclusive.

The command below is an illustrative offline example; resolve the instance and placement for the actual task.

```shell
python scripts/plan_block_insertion.py --snapshot snapshot.json --preset 0 --effect-id 190 --row 3 --column 10
```

Effect IDs identify instances, not just effect families. Resolve the ID from evidence for the actual model and firmware. The accepted integer range 1–1000 is a structural block range, **not** a catalogue of supported effects. The planner does not establish that a requested instance exists on the device, that DSP resources are available, or that its default settings suit the task. Those requirements remain explicit in the plan for a future qualified executor. No writer is included.

## Inputs and addressing

Snapshots use schema versions 1–3. The common contract requires a recorded model/firmware, established preset capacity, preset slot, populated flag and saved content hash. Version-1 FM9 snapshots retain the legacy implicit capacity of 512. Other contexts require explicit capacity. Generic insertion does not require Scene MIDI capabilities, scenes or messages.

The grid is an array of cells with `row`, `column`, `effect_id` and `route_flag`, plus any retained opaque fields. Rows and columns are one-based. A complete grid contains every cell exactly once:

| Model | Rows | Columns |
|---|---|---|
| FM9 | 6 | 14 |
| FM3 | 4 | 12 |
| Axe-Fx III | 6 | 14 |

These separate definitions match the toolkit's contained device definitions. Physical qualification remains model-specific. Older snapshots with only a `top_left_block` summary cannot establish grid occupancy or instance uniqueness; insertion returns `unknown-grid` until a complete grid is provided.

Choose repeatable `--preset SLOT` or `--all-populated`. Explicit selection can add a block to an empty preset; `--all-populated` selects only populated presets. Scene MIDI message planning keeps its separate policy of skipping empty presets.

## Results and preservation

- An existing exact instance anywhere in the grid is `unchanged`, with its `existing_cell`; a requested location is a fallback for insertion and does not request relocation.
- An absent instance needs both `--row` and `--column`, or the explicit automatic policy. Missing placement gives `missing-block-placement`; no suitable automatic candidate gives `no-isolated-empty-cell`.
- An occupied chosen cell gives `occupied-grid-cell`. All nonzero effect IDs count as occupied, including shunts. No replacement, rewiring or movement is proposed.
- Missing or incomplete grids give `unknown-grid`; multiple occurrences of the same instance give `ambiguous-block-instance`. Malformed cells, duplicate coordinates or out-of-bounds arguments are rejected.
- An empty chosen cell produces one `insert-block` action with the exact instance ID and location, a deep copy of `before_cell`, and `preserve_route_flag`. Other cells and routing flags receive no changes. Explicit coordinates preserve opaque flags. Automatic isolation interprets destination masks: the candidate's incoming mask is zero, and no following-column mask references its row; unknown following-column bits prevent proving isolation. Unrelated routes do not prevent placement.

The plan has `schema_version: 1`, `mode: "offline-plan-only"`, `operation: "ensure-block-instance"`, `validation_scope: "grid-placement-only"`, source/base hashes, requested placement, per-preset outcomes and executor requirements. A future executor must verify these requirements and the actual current content. Inserting an audio-processing block also needs a task-specific signal-flow and settings plan; grid placement alone does not establish sound behavior.

Reapplying the same operation to a snapshot containing the exact instance yields no insertion. A conflict gives no actions for that preset. Other selected presets have independent results. Exit code 0 means a plan was produced; inspect conflicts before using it. No command opens MIDI or alters preset bytes.

## Scene MIDI integration

```shell
python scripts/plan_scene_midi.py --snapshot snapshot.json --all-populated --channel 1 --value 1 --block-row 3 --block-column 10
```

The location is needed only for presets that lack a Scene MIDI block. Existing blocks are used where they are, even if the fallback cell is occupied. All selected scene settings belong to one preset-level block. Message conflicts suppress insertion as well as message actions. See the [snapshot and plan contract](snapshot-contract.md).

Scene MIDI plan schema 2 uses the common `insert-block` action with caller-supplied placement. Snapshot schema versions are unchanged.

## Subsequent execution evidence

The repository now contains common typed insert/select operations and a separately opted-in empty-preset Scene MIDI pilot; see [editor-operations.md](editor-operations.md). Scene MIDI insertion/removal on an empty FM9 12.00 preset has a scoped physical qualification. General isolated insertion of one audio block type has a separate repository qualification; neither establishes arbitrary-family availability or compatibility. This offline planner does not apply edits or establish other block availability/DSP/defaults.
