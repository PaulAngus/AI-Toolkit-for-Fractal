# FM9 protocol reference

Scope: recorded FM9 observations, primarily firmware 12.00. See [sources.md](sources.md) for technical attribution. The initial toolkit has no transport or codec implementation.

## Numbering and frame envelope

Device preset slots are **0–511**. User display offsets are independent of protocol addresses. Device scenes use indices **0–7**; task inputs use **1–8**. MIDI channels at the task boundary use **1–16**. Effect channels A–D are distinct from MIDI channels.

The Fractal manufacturer header is `F0 00 01 74`. The FM9 model byte is `12` hex. A normal frame consists of start, manufacturer, model, function, payload, checksum, and `F7`. Data bytes are seven-bit. The checksum is XOR of all preceding frame bytes, including `F0`, masked by `7F`. Validate the whole envelope, expected function/length/model, available address fields, and checksum.

## Recorded queries

| Operation | Function / payload | Evidence and interpretation |
|---|---|---|
| Device identity | Broadcast `F0 00 01 74 7F 00 7A F7` | Captured successful editor exchange; FM9 response `F0 00 01 74 12 64 00 00 73 F7` |
| Firmware | `08`, no payload | FM9 request `F0 00 01 74 12 08 1F F7`; initially source-verified and synthetically tested, require actual reply |
| Preset name by slot | `0D`, `[slot & 127, slot >> 7]` | FM9 validated; reply identifies slot and contains 32 ASCII name bytes |
| Current preset | `0D`, `[7F, 7F]` | Reads selection without selecting a preset |
| Scene name | `0E`, `[scene index]` | Reply identifies scene only, then 32 ASCII bytes; it does not identify preset |
| Current scene | `0C`, `[7F]` | Read selection; a scene-select command uses a different payload and changes live state |
| Stored preset | `03`, `[slot >> 7, slot & 127, 00]` | **High septet first**, unlike the preset-name query |

Identity requires a supported model, function `64`, echoed function `00`, status `00`, checksum, and exact captured envelope. A port name does not identify the device. Model identity does not establish firmware, physical-unit identity, or Axe-Fx III hardware revision.

The guessed `46` identity query was rejected by the FM9 and removed. Do not reconstruct private editor transactions from a parameter ID alone. Device-name reading uses a captured full `01` envelope; the detailed original transaction remains in the source document.

## Saved-preset decoding

The tested stored response comprises a 13-byte `77` header, eight 3082-byte `78` data frames, and an 11-byte `79` footer. Assemble only the expected slot and validate each frame.

Each data chunk has 3072 packed data bytes, representing 1024 16-bit words at three septets per word. Validate packed word bounds, raw-image magic, CRC-16/CCITT with initial value `AA55` and zeroed CRC field, and footer word XOR. The tested raw image is 16384 bytes. Compressed sizes are at raw offsets `48`/`4A` hex; the Huffman stream begins at `4C`. Reject incomplete, corrupt, oversized, or unsupported data before decoding.

Eight saved 32-byte scene names begin at decompressed body offset 4. Empty names are valid names. Saved dumps describe **saved state**; live queries can reflect unsaved edits. Do not join live scene replies to an old preset merely because their scene indices match.

The credited existing core supplies framing, assembly and decoding. Assess it for versioned independent packaging during runtime integration; applications should share one verified implementation.

## Control and interpretation limits

The FM9 Scene Select CC assignment is configurable. An example using CC 34 must match device configuration; it is not a universal mandatory CC number. Values 0–7 select scenes 1–8. Preset navigation uses bank and program change with the configured MIDI channel and mapping.

Amp rosters and private parameter IDs are firmware-specific. The captured FM9 12.00 amp roster contains 336 observed names; neither that count nor its request adapter establishes another firmware or model's roster. Unknown IDs should remain unknown.

Read-only request bytes cannot change their classification into a write simply because a read failed. Equally, successful saved reads do not establish audible transparency or unconditional edit-buffer preservation.

## Toolkit physical qualification, 8 October 2026

The contained reader passed all 512 saved FM9 12.00 slots plus repeated boundary reads and state/failure-recovery checks. Recorded chunk leading fields repeat `00 08`: treat them as opaque rather than unique sequence numbers. FM9 terminal metadata marked 350×1 occupies 736 bytes and has a different layout from normal effect records; preserve it whole until its semantics are qualified. Do not copy this terminal layout into another model without evidence. These read observations do not qualify a restore stream or writes. A later read-only Scene MIDI comparison qualified saved offsets against the current editor-protocol table; see [scene-midi.md](scene-midi.md).
