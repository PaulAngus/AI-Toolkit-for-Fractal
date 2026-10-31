# Scene MIDI reading and planning, release 0.3.0

Saved snapshots now decode all eight message slots in each of eight scenes. Each slot is Off, PC or CC; active slots include channel and value, and CC slots include controller. Raw fields are retained, including inactive fields in Off slots and the unused controller field in PC slots. Unknown types, invalid active values, missing records or ambiguous records remain an explicit unread-table conflict.

The planner supports PC and CC messages, recognizes already configured targets, fills only unused slots and reports conflicting active messages. Existing Scene MIDI blocks can be anywhere on the grid. When a block is absent, insertion uses the caller's chosen empty cell through the [general block planner](../skills/fractal-fm9/references/block-insertion.md). Each changed slot includes its original message and raw fields, and the plan retains source/base hashes. The planner has no apply command. Later release 0.4.0 adds a separately opted-in [empty-preset editing pilot](editing-pilot.md); the ordinary CLI stays read-only.

## Saved table and model definitions

The FM9 12.00 record has columns 257, rows 1, and 560 total bytes. Its 256 message fields start at record word 17, byte 34. They overlap the last six words of the generic 23-word record header. Reading them from the generic parameter-array start shifts every field. The remaining seven words of the record are retained opaquely, without assigning message meanings to them.

For scene index `s` and message index `m`, both zero-based, `i = s * 8 + m`:

| Field | Table index | Normalized interpretation |
|---|---|---|
| Type | `i` | 0 Off, 1 CC, 2 PC; other values unsupported |
| Channel | `64 + i` | Active raw 0–15 becomes channel 1–16 |
| Controller | `128 + i` | CC controller 0–127; inactive for PC/Off |
| Value | `192 + i` | Active PC/CC setting 0–127 |

The four bank starts were checked independently in the pinned editor catalogues for [FM9](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/fm9/params.ts), [FM3](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/fm3/params.ts) and [Axe-Fx III](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/axe-fx-iii/params.ts). Search for the `MIDIBLOCK` family and `CMD_*_BEGIN`. These are open implementation evidence, with no catalogue firmware version established here. The manufacturer [Blocks Guide, printed p88 / PDF p89](https://www.fractalaudio.com/downloads/manuals/fas-guides/Fractal-Audio-Blocks-Guide.pdf#page=89) documents the eight rows and their user-level PC/CC semantics.

Each device definition supplies its own Scene MIDI layout. FM9 12.00 saved/live offsets and the existing PC example are hardware-qualified. The FM3/Axe-Fx III saved-offset/type profiles and live transfer shapes are provisional and synthetically tested; their matching catalogue bank starts do not establish physical compatibility. There is no FM9-only gate. Do not generalize these matching Scene MIDI banks to unrelated parameters: [cross-model parameter ordinals can differ](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/docs/research/primitives/_negative/gen3-paramid-reuse-across-model-bytes.md).

## Read the current live table

```powershell
dotnet build src/Fractal.Cli/Fractal.Cli.csproj
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll ports
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll scene-midi --input INPUT --output OUTPUT --model MODEL --destination private/scene-read-001
```

Replace INPUT, OUTPUT and MODEL with verified values from the listed ports and actual device/capacity. The command reads the current saved preset, queries its live table, rereads the saved content and checks selection before/after. It never navigates or changes scenes. Output includes the saved snapshot/manifest, exact `scene-midi-live.syx` reply and `scene-midi-comparison.json`. Live fields stay separate from the saved snapshot; a mismatch may reflect unsaved edits. When the saved block is absent, the live query is skipped and explicitly labelled; that does not prove the edit buffer lacks an unsaved block. Scene names in the snapshot remain saved names.

The query is function `1F`, effect ID 190 as low/high septets `3E 01`. The qualified reply chain is:

| Function | Length | Content |
|---|---|---|
| `74` | 12 bytes | Effect ID 190, column marker 257 |
| `75`, section 0 | 778 bytes | 256 unsigned 16-bit words, three low-first septets each, starting at byte 8 |
| `75`, section 1 | 13 bytes | One extra word retained opaquely |
| `76` | 8 bytes | End of transfer |

Byte 7 of each data section is retained as opaque metadata; it is not assumed constant. Check model, checksum, exact sizes, order, repeated/interleaved headers and 16-bit bounds. Addressless data frames require one serialized request owner. An incomplete/corrupt exchange poisons the session; do not use late frames in another request or replay automatically. The allowlist exposes only the Scene MIDI block query, with no generic editor send.

## Offline planning

Replace message, scene, placement and target placeholders with the caller's requested values.

```powershell
python scripts/plan_scene_midi.py --snapshot private/read-001/snapshot.json --all-populated --channel CHANNEL --value VALUE --block-row ROW --block-column COLUMN
python scripts/plan_scene_midi.py --snapshot private/read-001/snapshot.json --preset ZERO_BASED_SLOT --scenes SCENE_A SCENE_B --message-slot MESSAGE_SLOT --type CC --controller CONTROLLER --channel CHANNEL --value VALUE --block-row ROW --block-column COLUMN
```

`--block-row` and `--block-column` specify the fallback location for a new block. Missing placement or incomplete grid evidence produces a conflict for an absent block; existing blocks need neither. Scene MIDI plan schema 2 uses the common `insert-block` action. `--type` defaults to PC. CC requires `--controller`; PC rejects it. Version-3 snapshots require all eight slots and consistent raw/normalized fields for decoded tables; version-1/2 sparse snapshots remain compatible. Off slots have null semantic fields and retained raw values. The planner compares only the fields relevant to the requested type and keeps unrelated content untouched. See the [snapshot contract](../skills/fractal-fm9/references/snapshot-contract.md).

## Validation and remaining scope

All 512 retained FM9 12.00 captures decoded with no errors: 254 complete Scene MIDI tables and 258 absent blocks. They contain 2,032 PC slots and 14,224 Off slots. Planning the already-present PC settings across all populated presets yields **254 unchanged, zero conflicts and zero actions**. The original capture files remain byte-identical. This was offline redecoding of the earlier physical full scan, not a new full-device scan.

The current FM9 12.00 live table matched all 256 independently read saved words. Repeat live bytes, saved rereads, selection checks and port reopening passed ten physical assertions, with 14 allowlisted requests. No navigation or edits were sent. The [redacted evidence](scene-midi-validation.json) records hashes and actual scope. The editor-protocol transfer is independent of the saved-body parser; no front-panel or editor UI comparison was performed. Active CC and varied channel/value hardware cases were unavailable in these captures and remain synthetically tested.

Run the opt-in hardware comparison with a current preset that already has a saved Scene MIDI block:

```powershell
dotnet run --project tests/Fractal.HardwareChecks/Fractal.HardwareChecks.csproj -- --hardware --scene-midi --input INPUT --output OUTPUT --model MODEL --destination private/scene-hardware-001
```

FM3/Axe-Fx III physical validation, saved-default-scene semantics, restore streams and persistent writes remain separate work. The complete saved bytes and opaque headers are preserved; this milestone does not qualify a writer or restore operation.
