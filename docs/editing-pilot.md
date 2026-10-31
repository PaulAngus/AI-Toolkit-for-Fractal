# Scoped editing pilot, release 0.4.0

The toolkit now contains typed editor commands, a serialized experimental send path, block-status reads and an opt-in empty-preset Scene MIDI pilot. FM9 firmware 12.00 passed physical insertion, PC/CC editing, saving, reloading, default-scene preservation and exact recovery of the original empty preset. See the [public evidence](editor-pilot-validation.json). The ordinary CLI and the default transport remain read-only.

The same command builders and pilot accept FM9, FM3 and Axe-Fx III device contexts. FM3 uses its four-row/twelve-column grid; the other definitions use six rows/fourteen columns. Axe-Fx III capacity stays explicit. Synthetic checks cover all four model/capacity paths. Physical compatibility of FM3/Axe-Fx III editor operations and their provisional saved profiles remains unverified.

## Available operations and evidence

| Operation | Encoding | Current qualification |
|---|---|---|
| Select cell | Function 01, action 30; column-major zero-based position | FM9 12.00 physical sequence; upstream FM3 evidence requires select before insert |
| Insert a block instance | Function 01, action 32; instance ID and position | Common builder; only Scene MIDI instance 190 physically tested here |
| Clear selected cell | Function 01, action 33 after action 30 | FM9 12.00 removal and exact empty-preset recovery |
| Set Scene MIDI field | Function 01, action 09; field ID and five-septet IEEE float32 value | FM9 12.00 PC/CC, channels 1/16, value edges, all eight scenes |
| Select scene | Function 0C; zero-based scene | FM9 12.00 readback; task boundary uses scenes 1–8 |
| Save/reload authorized preset | Function 01, action 26/27; little-endian 14-bit slot | FM9 12.00 saved readback and same-target reload, including slot above 127 |
| Read block status | Function 13; strict ID/flags triples | FM9 12.00 physical read; published Axe-Fx III protocol |

The editor envelopes come from the pinned [Gen-3 implementation, setParam.ts](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/axe-fx-iii/setParam.ts). Retrieve that exact revision and search `buildSelectCell`, `buildInsertBlock`, `buildClearBlockCompanion`, `buildSetParameterFloat`, `buildStorePreset` and `buildSwitchPresetSysEx`. Parameter bank starts are independently retained for all three models in [Scene MIDI reading](scene-midi-reading.md). The manufacturer [MIDI specification, revision 1.4](https://www.fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf) covers status/scene commands and effect IDs; it does not document these private editor/save envelopes.

## What the pilot does

1. Before opening MIDI ports, validate the selected saved backup's frame checksums, footer, CRC, decompression and manifest hashes. Require an empty grid, a valid scene candidate and the explicitly supplied base hash.
2. Identify the device, verify the selected target, compare a fresh saved hash and live scene names, and check the live status. Status includes foot-controller entries 200/201 even for an empty grid; they must not be mistaken for inserted grid blocks.
3. Insert Scene MIDI at the caller's chosen empty cell, read its actual settings, remove it and verify the saved state is unchanged. Reload the same saved target and verify it is empty.
4. Insert again; configure two owned rows per scene using actual defaults, preserving the other six rows and inactive fields. Read back the complete 256-field table and its extra opaque word.
5. Deliberately save with an alternate scene to qualify default-scene semantics, then save with the original default scene. Independently compare names, routing, all unrelated body bytes and the complete intended message table.
6. Reload the same target and verify the persisted table/default scene. Remove the owned block, restore the original default scene, save and reload. Require the saved body and raw image to match the original backup exactly and the original selection to be restored.

The saved body first word tracked the selected scene minus one in this FM9 12.00 experiment. The second word remains opaque. This is scoped hardware evidence, not a universal guarantee for future firmware or the other models.

Newly inserted blocks can expose previously used instance settings. The pilot observed active PC settings immediately after insertion into an empty saved preset; a later insertion retained the trial's PC/CC settings. Do not assume Off defaults or overwrite a table without reading it. For real presets, offline conflict policy still protects existing messages.

Saved shape-record offsets begin 12 bytes after the instance prefix and include the next instance prefix among their final six words in the qualified FM9 case. Preservation must remove the inserted instance's actual byte interval, then compare the unchanged remainder; deleting at the shape offset incorrectly changes the following prefix. `EditorPreservation.VerifySceneMidiInsertion` validates identity, placement, route flags, names, expected saved scene and the complete unrelated-byte projection. It does not author or restore preset bytes. Other-model profiles have synthetic checks only.

## Deliberate hardware invocation

Use only a target the user has authorized for destructive testing and persistent saves. Preset arguments are zero-based device slots; confirm any display offset. Read and back up the target using the normal CLI first. Replace the port, model, target and placement placeholders with the exact authorized values:

```powershell
$pilotHash = (Get-Content private/pilot-baseline/manifest.json | ConvertFrom-Json).captures[0].raw_sha256
dotnet run --project tests/Fractal.HardwareChecks/Fractal.HardwareChecks.csproj -- --hardware --empty-write-pilot --input INPUT --output OUTPUT --model MODEL --preset ZERO_BASED_SLOT --row ROW --column COLUMN --backup private/pilot-baseline --expected-hash $pilotHash --destination private/pilot-run
```

Each destination must be new. Results, exact wire traces, captures and partial failures remain private. The harness checks the selected target before each normal mutation and a fresh saved base before each save. Its authorization scope permits only the selected slot/cell and Scene MIDI fields; it cannot save another slot, insert another effect or change another cell. Generic availability/DSP validation for other blocks is pending.

Every send is performed once. The session monitors short, model/function-correlated error replies, bounds delivery and permanently stops after rejection, timeout, cancellation or transport failure. A 100 ms observation window is a toolkit policy. A successful return means **sent-unverified**; silence or a zero result code never substitutes for independent readback. Keep one coordinated MIDI owner and avoid concurrent manual edits during the pilot.

After failure, inspect the saved state before deciding recovery. `--recover-empty-pilot` requires the saved baseline hash still to match, then reloads that unchanged target once and verifies it. `--restore-empty-pilot` additionally requires `--current-backup` and `--current-hash` from a retained partial trial; it verifies the exact current saved image, sole owned block, complete live table and unrelated-byte preservation before removing the block and saving the original empty state. These are explicit recovery invocations, with fresh readback, not automatic retries. Cancellation can leave a partial live or saved state; the report says so.

## Remaining gates

This qualifies operation-based recovery of an empty preset used by this pilot. It does not qualify uploading an arbitrary full preset dump or restoring existing sound blocks. Received 77/78/79 saved-read frames remain backup evidence, not a proven restore stream. Editing populated presets, existing-message replacement/undo, DSP/availability catalogues, resumable batches and MCP need their own implementation and restore qualification. External MIDI receiver behavior and independent front-panel/editor semantic comparisons were not observed.
