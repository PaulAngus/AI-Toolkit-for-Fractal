# Offline editing profiles and semantic reads

Release 0.14.0 extends [general editing milestone 1](general-editing-and-routing-design.md) with model-specific audio parameter catalogues, bounded saved audio reads and directed grid-edge decoding. That read-only foundation is now consumed by the separate [0.16.0 editing planner and executor](preset-editing.md). All new reads are source-derived candidates; synthetic tests do not establish exact-firmware or hardware compatibility.

## Commands

Build the contained CLI, then inspect capabilities/catalogues or an existing saved capture:

```text
dotnet build src/Fractal.Cli/Fractal.Cli.csproj
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll edit-capabilities --model MODEL --firmware VERSION
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll edit-catalogue --model MODEL --firmware VERSION --family Amp
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll decode-edit --model MODEL --firmware VERSION --capture CAPTURE.syx
```

Use the capture's model and recorded firmware in `major.minor` format with two minor digits. Axe-Fx III requires explicit `--capacity 512` or `--capacity 1024`. Firmware is caller context and is never inferred from a preset file. Commands return before MIDI enumeration or transport creation and reject input/output port arguments. They only read files and print JSON; redirect output to a new private file if it should be retained. The capture is unchanged.

`decode-edit` accepts one complete addressed saved capture, rechecking framing, model, checksums, CRC, bounded decompression and preset bounds. Opaque backup headers remain inspectable through `check-backup` but are not relabelled as saved-slot captures. Invalid inputs or failed integrity return exit 2. A valid, explicitly partial semantic report returns exit 0; consumers must inspect `status`, `issues` and each field's support status. Unsupported route/parameter semantics never become an empty path or zero value.

## Current coverage

| Read | FM9 | FM3 | Axe-Fx III with either capacity |
| --- | --- | --- | --- |
| Scene names | Raw 32-byte fields plus printable ASCII candidate text | Separate provisional profile | Separate provisional profile |
| Grid and instance records | Exact prefix/grid association; ambiguity reported | Same association algorithm, model-specific layout | Same association algorithm, model-specific layout |
| Amp type per channel | Model-specific numeric ID field | Different model-specific offset/stride | Different first-channel and subsequent-channel offsets |
| Amp scene channel/bypass | Eight candidate assignments | Provisional model profile | Provisional model profile |
| Editor audio parameter mappings | 1,577 entries | 1,581 entries | 1,651 entries |
| Saved Amp knobs | Nine controls, four channels, candidate 0–10 scaling | Own-model offsets, nine controls | Unsupported; no FM9/FM3 offsets borrowed |
| Saved Cab fields | Mode and conditional DynaCab/mic IDs, 101-column shape | 101/106-column shapes | 105-column shape |
| Saved effect types | Reverb, Delay, Drive, Chorus plus Compressor, Filter, Flanger, Phaser, Tremolo, Wah, Enhancer and Rotary; numeric IDs | Own-model shapes/offsets | Own-model shapes/offsets |
| Directed grid edges | Six incoming row bits | Four incoming row bits | Six incoming row bits |
| Live reads or writes through these APIs | None | None | None |

The [embedded catalogue](../knowledge-base/catalog/edit-read-profiles.json) is versioned and hashed. It retains source IDs, revision/hash attribution and model scope. Existing FM9 12.00 layout observations are recorded as historical evidence only; the new projection is synthetically tested, not newly physically qualified. Other firmware remains provisional. Structural matching does not establish compatibility, DSP availability or edit authorization. Unknown numeric Amp IDs are not resolved against the FM9 roster for another device.

## Contract and preservation

`EditReadCatalogue.Capabilities` and `Parameters` expose the same core data used by the CLI. `PresetEditProjection.Decode` returns schema 1 with protocol/profile versions, catalogue hash, caller context, capture/raw/body hashes and complete `body_hex`. Original captures remain the source of full transfer bytes. Existing schema-3 snapshot exports and planners are unchanged; re-decode the original capture to obtain this additional projection.

The shared structural reader locates the retained block-record shape. Instance identity comes from the prefix 12 bytes before that shape, matched to exactly one grid cell and exactly one record. Duplicate cells, duplicate prefixes, missing records and mismatched saved-field shapes are explicit failures for that block's semantic projection. The new reader does not pair sorted Amp IDs with same-shaped records. Other block records retain numeric IDs even when their family is unknown; no parameter interpretation is inferred from their dimensions. System/unplaced records remain in the record inventory.

Name results retain all bytes, including terminators and padding, alongside candidate display text. Non-printable/non-ASCII name content produces null text and an issue rather than substitution. Invalid channel/bypass fields retain raw values with null semantic values; known fields remain readable. Scene Ignore, modifiers, reset behavior and live effective state are not inferred from saved channel/bypass fields.

`edit-catalogue` separates `parameters` (saved-field descriptors) from `editor_parameters.entries` (editor IDs). Each editor catalogue carries its own model, pinned source revision/hash, contained data hash and evidence limitations. `--family` is case-insensitive and uses friendly names such as Amp, Cab, Reverb or Delay. Omit it to list all retained audio families. Legacy symbols sharing an ID remain separate entries. `addressable` means only that the number fits the 14-bit editor address field; it does not qualify a command, firmware or value scale. Labels, units and display bounds ending in `_hint` retain upstream inference and must not drive conversion or writes.

Saved descriptors specify their exact instance IDs, record shape, four channel offsets and offset origin. Descriptor minimum/maximum/resolution apply to `range_domain: raw-u16`. Reads include `raw_value`, `value`, `scale` and `status`. FM9/FM3 Amp drive, bass, mid, treble, master volume, depth, sag, presence and negative feedback use the retained candidate conversion `raw * 10 / 65535`; precision is not rounded away. Amp output level and all Axe-Fx III Amp knob conversions remain unsupported. General effect settings have editor IDs but only the listed saved type fields have readers. Unresolved type, Cab and mic identifiers stay numeric, including out-of-roster values.

Cab mode is always retained. DynaCab 1/2 and mic fields are interpreted only when that channel's mode equals 1. Other modes retain their raw words with null `value` and `not-applicable` status. Unknown numeric mode IDs are not coerced to a supported mode. Nothing here resolves user IR identity or claims Cab defaults.

Routing words belong to the destination cell: bit 0 names source row 1 in the previous column, bit 1 names source row 2, and so on. For example, a mask of 5 at row 2, column 3 represents edges from rows 1 and 3 in column 2, provided those cells and the destination are occupied. The decoder supports straight and cross-row edges, fan-in, fan-out and shunts. Cell keys use one-based `cell:row:column` coordinates.

Routing reports retain all nodes, words, known masks and unknown bits. Valid edges survive alongside partial issues. An incoming bit in column 1 is an unresolved boundary; an empty source or destination produces an issue and no invented edge. Unknown bits remain opaque. `edges: []` means no recognized grid edges; when `status` is partial it does not prove disconnection. `edges: null` is reserved for an unsupported body layout. `decoded-candidate` means all grid mask bits were accounted for under this candidate encoding, not that the complete signal path or audible result is known. The additive `special_routing` report now represents fixed Send/Return links, feedback components, model-qualified I/O and per-scene I/O-conditioned potential paths. These remain separate from saved grid-mask edges. Global source configuration, external cables and actual audibility are not inferred. Release 0.16.0 adds a saved-word connect/disconnect encoder in the separate [edit planner](preset-editing.md). `raw_grid_hex` and complete `body_hex` preserve original bytes; structural failures retain complete body bytes and readable names.

The read projection itself sets `ports_opened` and `execution_supported` to false. Capability and saved-parameter catalogue queries now advertise implemented write/execution support separately from `hardware_qualified: false`. Read projection output alone grants no write authorization; the separate edit planner resolves an exact result and the executor verifies it.

## Validation and next work

[EditReadChecks](../tests/Fractal.Checks/EditReadChecks.cs) independently assembles synthetic model layouts and expected values rather than using the production profile resolver to generate expectations. Fixtures reverse Amp record order, include a same-shaped Cab decoy, unknown instances, shunts, unknown routing bits, all scenes/channels and unrelated tail bytes. Negative cases cover missing/duplicate prefixes and grid instances, invalid scene fields/text, unsupported shapes, corrupt/incomplete/wrong-model captures, opaque headers and invalid firmware. The CLI pipeline exercises all three commands for FM9, FM3 and both Axe-Fx III capacities without opening ports. The original foundation results remain in [the 0.13 validation record](editing-read-profiles-validation.json). [AudioRoutingChecks](../tests/Fractal.Checks/AudioRoutingChecks.cs) adds independently assembled values and expected edges for all definitions, mode conditions, model address differences and invalid routing endpoints. Current results are recorded in [audio/routing validation](audio-routing-validation.json).

Release 0.16.0 adds [saved-value writers, compound plans, connected routing and CLI/MCP execution](preset-editing.md), including scene-name editing and explicit template initialization. Wider named fields and calibrated scales, enum names, global I/O configuration, Scene Ignore, modifier editing and reset/default catalogues remain extensions. Physical validation requires separate model/firmware-specific authorization.

[Capture-backed authoring](calibrated-authoring.md) adds exact-value witnesses without expanding the built-in scale catalogue by inference. `decode-edit` now also exposes modifier slot bindings and raw settings; named modifier curves remain qualified.

[Built-in saved scales](builtin-scales.md) are a separate contained catalogue with exact model/firmware/shape scope. `decode-edit` exposes physical values and unknown raw sentinels, named modifier fields and the catalogue hash. Legacy descriptors and each model's editor IDs retain their independent source bounds.
