# Full-preset restoration qualification

Full-preset restoration must recreate the complete saved image: routing, block parameters, all scenes, names, saved default scene and opaque data. Single-preset restoration, bank/system/cab restoration and saving an already-loaded buffer are separate operations. The target slot, model, capacity, scenes, ports and files are caller inputs.

## Evidence retained

A real FM9 single-preset file passed strict framing, model chunk count, frame XOR, footer word XOR, raw CRC16 and bounded Huffman checks. Its opaque header payload was `7F 00 00 40 00`. Interpreting the first two fields as a saved-read address would exceed supported capacities. Preserve those bytes without assuming an edit-buffer marker, upload destination or save behavior. The common [offline inspector](runtime.md) accepts this format while addressed saved reads remain strict. See [redacted integrity evidence](backup-file-validation.json).

Read-only FM9 12.00 evidence established that current block status and stable saved captures can describe different contents. A controlled changed saved baseline was verified while unrelated body data, names, routing and saved default scene remained unchanged. A matching channel/status query cannot establish complete live-buffer equality or persistent recovery. Record the observed distinction without attributing a save failure to an unobserved action.

Release 0.4.2's [save-current operation](save-current.md) verified persistence of an already-loaded original on FM9 12.00. It checked the exact authorized target, current selection, retained saved-base hash, expected scene names and caller-supplied channel expectations; retained a fresh base; and sent one Save. Two complete saved reads matched the expected backup's raw image and decoded body exactly, without excluded or normalized fields. Selection was preserved. The run passed 18 checks with 22 reads and one Save. No backup file contents, edits or reloads were transmitted. See [redacted persistence evidence](save-current-validation.json).

This qualifies persistence of an already-loaded buffer on the tested model/firmware. It does not establish how the original reached that buffer, arbitrary file upload, reload behavior or preservation of other slots. FM3 and Axe-Fx III have synthetic common-scope coverage; physical qualification remains model/firmware-specific.

## General qualification procedure

1. Inspect the exact expected backup and bind it to a hash. Reject bank, system, firmware and cab transfers from a single-preset operation. Preserve unknown header semantics.
2. Establish the model, firmware, capacity and authorized target. Capture a fresh saved baseline and distinguish it from unsaved live work. Never apply an empty-preset cleanup procedure to a populated target.
3. Establish host-to-device transfer framing, addressing, temporary-buffer and persistence requirements from verified evidence. A received dump is not automatically a verified upload stream. Retain the pinned [format implementation](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-gen3/src/presetDump.ts) as research evidence; its backup serializer is not qualified host-to-device transfer evidence.
4. Exercise wrong model/address, corruption, cancellation and ambiguous replies offline before a live transfer. Use one MIDI-port owner and bounded requests; reconcile uncertain outcomes through reads rather than blind replay.
5. Run an explicitly authorized scratch-preset trial with a verified changed baseline. Restore, persist, compare complete saved contents, then reload and compare again when reload is within scope. Check other slots against a fresh inventory before qualifying broader preservation or batches. Stop after unexplained differences.

For a manually loaded preset, follow the relevant manufacturer's complete save sequence before claiming persistence. The [FM9 Owner's Manual](https://www.fractalaudio.com/downloads/manuals/FM9/FM9-Owners-Manual.pdf#page=53), printed page 47, describes STORE, destination review, ENTER, ENTER and the “SAVED!” indication. Consult the appropriate manual for another model. Preserve the loaded buffer while reconciling a live/saved difference.

A unit-wide manufacturer backup is a precaution; the individual preset backup is the restoration input. Keep personal captures and operation logs outside distributable artifacts. The [backup workflow](../knowledge-base/workflows/backups-verification.md), [reading map](../knowledge-base/reference-map.md) and [editor pilot](editing-pilot.md) retain general invariants, protocol locators and qualification limits.

## Contained full-file operation

Release 0.5.0 implements [single-preset upload, verification, Save and Reload](preset-restore.md). Its immutable plan retargets only the upload header to temporary memory and preserves every payload byte. The original opaque backup address never grants permission to overwrite a saved slot. The [current qualification record](preset-restore-validation.json) supersedes earlier upload gaps only within its measured model/firmware scope. Other models, batches and interrupted-transfer recovery require separate qualification.
