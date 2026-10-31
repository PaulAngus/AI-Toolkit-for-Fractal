# Read device identity, saved presets and scenes

General task: establish what a connected device supports, then obtain validated data without confusing its saved state with the live edit buffer. The runtime target includes FM9, Axe-Fx III and FM3 through common operations and explicit model definitions; see [device support](../../docs/device-support.md).

Use the [FM9 protocol reference](../../skills/fractal-fm9/references/fm9-protocol.md) for recorded query formats, frame validation and saved-dump assembly. Published Axe-Fx III documentation and an open implementation are starting evidence; physical FM9 validation establishes local applicability. FM3/Axe-Fx III and other firmware need their own validation.

1. Select the intended input/output pair and establish bidirectional communication. Port names and enumeration do not prove an end-to-end connection.
2. Validate manufacturer identity, model and firmware from supported replies. A friendly device name is editable and is not a unique hardware identifier.
3. Record current preset/scene before a saved read. Assemble the entire bounded transaction, validate checksums/CRC, then decompress and decode.
4. Read preset names, all saved scene names, placed Amp blocks, channel models and saved scene assignments. Treat Amp identity and presence independently of grid location.
5. Compare representative varied channels/scenes against an independent editor/device display. A missing model label can be a catalogue gap rather than a decoder failure.
6. Recheck current selection afterward. For live scene snapshots, bracket reads with a stable preset check because scene-name replies do not identify the preset.

Saved programmed values can differ from unsaved live edits. Scene Ignore can make effective state conditional. Restoring selection cannot recover discarded unsaved edits. State-preserving reads are also not proof of audio continuity; monitor audio separately when that claim matters.

## Interpret read results

An addressed saved snapshot's slot identifies the preset whose saved bytes were read; it does not identify the currently selected preset. Report current preset and scene only from explicit current-state fields. If those values were not supplied, say they are unknown even if before/after equality was reported. Apply only the caller's explicit display offset when translating slots into screen numbers.

Saved grid, names, amp models and scene assignments remain saved-state facts. A matching live Scene MIDI table establishes equality of that table alone; it does not establish the live amp channel, bypass state, grid or absence of unrelated unsaved changes.

Offline lookup, decoding and planning send no MIDI. Live read-only operations send MIDI/SysEx queries; read-only means no intentional selection, editing or saving, not silence on the MIDI wire. Reading stored or live Scene MIDI configuration does not demonstrate transmission or receipt of its configured PC/CC messages. Receiver routing, channels, program maps and display offsets require separate evidence.

Equal before/after selection establishes no observed lasting drift between the sampled endpoints. It cannot exclude a transient change and return. Continuous notifications or a wire capture are additional observations, not proof that all internal selection changes are observable. Such a claim requires model/firmware-qualified coverage and loss detection; no complete monitor is implemented here.

## Discover preset capacity

A checksum-valid, exact-address reply at zero-based slot 512 demonstrates that the first address beyond 512 exists. Within a supported 512/1024 family definition, this supports the larger range. An empty name is still positive address evidence.

A missing, corrupt, wrong-model or wrapped-address response establishes nothing. A timeout cannot prove the smaller capacity. Use a verified negative response or an explicit capacity choice when uncertainty remains. Persist the established range and use it consistently for scans, send bounds, progress and completeness. This boundary method has not been physically validated on Axe-Fx III by the imported evidence.

## Validate compatibility

Use simulated tests for logic, captured-response replay for formats and opt-in physical tests for drivers/device behavior. Capture model, firmware, port path, selected addresses, timings, hashes and failures. Exercise bank boundaries, the last configured slot, cancellation, late replies, repeat reads and catalogue coverage.

Keep navigation tests separate: loading presets/scenes changes live state and may discard edits. Verify readback and restoration, without resending uncertain navigation commands automatically. Missing hardware is unverified; it is never a hardware pass.

Recorded FM9 12.00 evidence includes full 512-slot reads, 336-name roster discovery and navigation through 48 scene selections in six presets. A read-only scan also observed an unexplained scene change; a later run passed selection preservation. The applicable lesson is to measure and report preservation, rather than promise it unconditionally. See [evidence scope](../../docs/evidence.md).

## Live changes and refresh

Investigate the model, output path, notifications and editor traffic before promising automatic change detection. The published Axe-Fx III specification's push messages cover tempo/tuner on MIDI OUT and distinguish them from USB; they do not document a comprehensive saved-preset change feed. Private editor behavior needs captured evidence of its own.

Combine supported notifications with bounded, coordinated state queries and targeted invalidation. A changed selection/live parameter does not establish that a preset was saved. Keep unsolicited messages distinct from solicited replies and preserve saved-versus-live provenance. A [finite query-based monitor](../../docs/live-tools.md) now samples current selection and effective block status. Complete private notification decoding remains unimplemented; sampling does not establish complete internal-change coverage. See the [reading map](../reference-map.md).

## Contained implementation

The [read-only runtime](../../docs/runtime.md) supplies identity, firmware, current state, live scene names and saved reads for all three model definitions, with common correlation/timeouts/export. Synthetic checks cover each model. [FM9 12.00 physical reads](../../docs/hardware-validation.md) cover all 512 saved slots, repeated boundaries, state preservation, cancellation/timeout and port recovery. FM3/Axe-Fx III physical tests remain unrecorded. Axe-Fx III capacity is explicit in this release; a positive-capacity probe is still a future operation.

## Saved transfer and terminal-layout observations

FM9 12.00 actual chunk leading fields repeat `00 08`; do not assume uniqueness or derive a sequence from them. Complete transfer length, footer XOR, raw CRC and decompression validate assembled content. Its observed 350×1-marked 736-byte terminal metadata is distinct from ordinary effect records and is retained opaquely. Both findings are qualified to the tested model/firmware in the [physical report](../../docs/hardware-validation.md); unknown layouts must remain explicit.

## Complete Scene MIDI tables

Use the contained [Scene MIDI reader](../../docs/scene-midi-reading.md) to decode all 64 saved slots and compare the current live table without changing selection. Live edit-buffer data remains separate from saved snapshots. FM9 12.00 offsets have a saved/live physical comparison; FM3/Axe-Fx III profiles are provisional. Unknown shapes or active values remain explicit errors/conflicts.

The [passive notification capture](../../docs/network-notifications.md) decodes published tempo/tuner pushes and known status formats, retains unknown or malformed complete frames and incomplete raw bytes, and reports refresh hints. It sends no MIDI queries or edits. Context is caller-supplied; reply formats do not establish unsolicited origin or saved persistence. Existing query monitoring remains available on both native and network transports.
