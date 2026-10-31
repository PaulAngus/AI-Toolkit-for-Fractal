# Offline Scene MIDI snapshot and plan contract

Version 3 supports complete normalized **FM9, FM3 and Axe-Fx III** Scene MIDI tables. Planning validates normalized settings, capacity and capabilities; it does not impose a firmware whitelist or prove a hardware encoding. A firmware context must be recorded. Version-1/2 sparse message tables remain compatible; block insertion requires a complete grid. Version-1 FM9 examples have an implicit 512-slot capacity; other models require explicit capacity even in version 1.

## Snapshot

New exports have `schema_version: 3`, `device`, `source`, `decode_errors` and `presets`. The device has `model`, `firmware`, `preset_capacity` (512 for FM9/FM3; 512 or 1024 for Axe-Fx III), `capacity_evidence`, `protocol_release` and evidence qualifications. Required planning capabilities are `scene_midi: true`, `scene_count: 8`, `message_slots: 8`. Firmware obtained live is queried; an offline decode's supplied firmware is contextual evidence, not extracted from the dump.

`source` identifies live-saved-read/offline-saved-capture provenance, exact capture hashes and observed live state before/after where available. Saved reads are independently addressed, not a device-wide atomic snapshot. `decode_errors` identifies any integrity-valid capture with an unsupported body; the planner refuses such an export rather than silently excluding those presets. Manifest-file hashes are separate from the planner's canonical JSON hash. Each decoded preset has:

| Field | Contract |
|---|---|
| `slot` | Unique integer 0–capacity minus 1; device slot, without display offset |
| `content_sha256` | SHA-256 of the integrity-checked saved raw image, 64 hexadecimal characters |
| `populated` | Boolean established from saved content |
| `grid` | Complete array of cells with one-based row/column, effect ID and opaque route flag; required for insertion, with model-specific bounds in the [block contract](block-insertion.md) |
| `top_left_block` | Optional descriptive legacy summary; no longer used to decide insertion or placement |
| `scene_midi_present` | Boolean; true also when the existing block is elsewhere in the grid |
| `body_header_words` | Two opaque saved body words at byte offsets 0 and 2; no semantic default-scene interpretation is claimed |
| `opaque_tail_offset`, `opaque_tail_hex` | Optional explicitly qualified terminal metadata, preserved byte-for-byte |
| `scene_midi_decode_status` | Version 2/3 requires `absent`, `decoded` or `unsupported-saved-table-layout`; status must agree with block presence |
| `scenes` | Exactly eight objects, with distinct `number` values 1–8 and a `messages` array |

In version 3, each decoded scene has exactly eight distinct slots 1–8. A slot's `type` is `Off`, `PC` or `CC`. PC/CC have `channel` 1–16 and `value` 0–127; CC also has `controller` 0–127. PC's controller is null. Off has null channel/value/controller. Every slot retains `raw` type/channel/controller/value as unsigned 16-bit integers. Raw type is 0/1/2 for Off/CC/PC; active raw channel plus one and raw value must match normalized settings, and CC raw controller must match. Inactive values can be nonzero or outside active ranges and remain preserved. Version 1/2 permits sparse arrays that omit unused slots. Message values represent device settings; the contract does not establish outgoing-wire PC display offsets.

If no Scene MIDI block is present, all message lists must be empty. If a table is unread, its status is `unsupported-saved-table-layout` and the empty lists mean **unknown**, not unused: every selected populated preset with this status receives a conflict and no actions. The contained decoder emits complete tables for supported record shapes and valid fields; unsupported data keeps raw records and a limitation. FM9 12.00 offsets are physically qualified; FM3/Axe-Fx III saved profiles remain provisional with synthetic coverage. A `"Scene MIDI"` top-left cell requires `scene_midi_present: true`. Additional descriptive fields are permitted; core fields are validated. JSON booleans are not accepted where integers are required.

The example uses fictitious hashes and normalized settings. Real adapters must hash actual verified saved data; the planner cannot establish that an input describes a real device.

## Scope and conflict policy

Choose `--preset SLOT` (repeatable) or `--all-populated`. Scenes default to 1–8; `--scenes 1 2` narrows them. `--message-slot` defaults to 1. `--channel` and `--value` are required. `--type` defaults to PC; CC requires `--controller` 0–127, and PC rejects that option.

- Skip requested empty presets. `--all-populated` excludes empty presets from selection.
- A new block uses the chosen `--block-row` and `--block-column`, with no implicit location. Missing placement, incomplete grid evidence or an occupied target produces a conflict. The [general insertion planner](block-insertion.md) supplies the common operation.
- An existing Scene MIDI block is used at its current grid position, without relocation.
- An unread existing table is a conflict; no messages are assumed empty.
- A selected message slot with another message is a conflict; it is not overwritten.
- An Off slot is available and its inactive raw fields are retained as before-values.
- A slot already equal to the desired message produces no action; PC ignores its inactive controller field.
- Other message slots and unselected scenes remain untouched.
- A conflict suppresses all actions for that preset. Other presets still receive independent results.

## Plan

The result includes `schema_version: 2`, `mode: "offline-plan-only"`, `device`, the requested `message`, `message_slot`, `scenes`, `source_snapshot_sha256`, `summary`, and `presets` results.

Each preset result includes its `slot`, `base_content_sha256`, `status`, `reason`, `actions`, and `conflicts`. Status is `planned`, `unchanged`, `skipped`, or `conflict`. Planned actions use `insert-block` with effect ID 190 at the chosen row/column, retaining `before_cell` and its opaque route flag, or set a message at the selected scene/message slot. This replaces the schema-1 `insert-scene-midi-block` action; snapshot versions are unchanged. The plan records requested fallback placement and grid-only insertion qualification; a future executor must establish availability, defaults and DSP resources independently. A set action includes `before_message` (the original Off slot and raw fields, or null for a missing legacy slot) and the desired `message`. Conflicts retain the existing message. The source snapshot hash covers canonical JSON for the whole parsed input, including extra fields, independently of whitespace/key order.

Exit code 0 means the plan was produced; inspect `summary.conflict` and each result before proceeding. Invalid inputs or arguments return a nonzero exit code. An executor must authenticate its device/session, verify original content hashes and backups, check conflicts and scope, resolve live unsaved edits, and independently validate operation capabilities. An offline plan neither authorizes nor verifies a hardware write.
