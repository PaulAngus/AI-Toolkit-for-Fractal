# Configure Scene MIDI across preset scenes

## Goal and current capability

Plan a requested PC or CC message in the Scene MIDI table of chosen FM9, FM3 or Axe-Fx III presets/scenes. If the block is absent, choose an appropriate empty grid cell through the [general insertion operation](block-insertion.md). Historical hardware evidence is limited to the recorded model, firmware and message coverage; placement is supplied by the caller.

One Scene MIDI block belongs to a preset grid; it contains the per-scene settings. It does not require an audio connection to function. The toolkit implements a common offline planner and read-only saved export. The live editor procedure below is historical evidence, not an executable device tool in this bundle.

## Recorded hardware coverage

Recorded FM9 12.00 observations on 2 October 2026 reported saving/reloading a pilot and verifying the specified PC entry across all eight scenes. Saved-state checks also covered a limited set of populated presets. The evidence does not establish a completed all-device bulk edit or a production writer in this toolkit.

Saving was observed to change a preset's default scene. Preserving/restoring the original default scene removed the unrelated normalized body difference. Treat the saved default scene as an invariant of any edit that does not explicitly request changing it.

This evidence covers a new block in an empty cell and the specific PC configuration. It does not validate overwriting existing MIDI messages, relocating an existing block, other message types, other firmware, or restoring a preset by sending a received dump back to the device.

## Task sequence for a maintained runtime

1. Resolve exact preset slots, scenes, message slot, PC value, MIDI channel, placement, and receiving equipment. Distinguish display numbers from device slots. Reuse the user's existing authorization for those operations; clarify only missing consequential choices.
2. Identify model and firmware through valid replies. Establish which component owns the ports. Check the live edit-buffer context before loading another preset.
3. Read saved presets and write integrity-checked backups and hashes. Determine populated targets from actual content, not a blank scene name alone.
4. Inspect grid placement and existing Scene MIDI tables. Produce a reviewable plan with intended changes, already configured targets, and conflicts. Preserve existing messages. The initial planner reports occupied cells and message-slot conflicts instead of replacing them.
5. Recheck the current saved hash against the plan's base hash. Configure one pilot through verified target-specific operations. Verify all intended live fields before saving.
6. Preserve the original saved default scene, names, routing, other blocks, and unrelated MIDI messages. Save to the exact slot and read the saved preset back. Compare intended changes and preserved content independently. Reload only within authorized live navigation scope.
7. Extend to a bounded batch after the pilot passes. Stop on unresolved verification errors. Record completion only after saved-state verification, with enough original/result data for safe reconciliation and resume.

The snapshot/plan contract is in [snapshot-contract.md](snapshot-contract.md). The read-only runtime exports complete supported Scene MIDI tables and explicitly marks unsupported layouts unread. Physical encoding qualification remains model-specific. A later [experimental editor pilot](editor-operations.md) qualifies owned empty-preset operations on FM9 12.00; populated-preset writing and full-dump restore remain pending.

## Recovered editor field mapping

These are **recorded implementation and device observations**, scoped to FM9 12.00. They are not a published manufacturer API. A future writer must validate them against retained non-personal fixtures and a new pilot before exposing them as a general capability.

| Field | Recorded value / layout |
|---|---|
| Scene MIDI effect ID | 190 decimal (`BE` hex) |
| Message type PC | Internal table value 2 |
| MIDI channel encoding | Task channel minus one; observed raw range 0–15 |
| PC value encoding | Raw range 0–127; display numbering is a separate convention |
| Type parameter, scene index `s`, message index `m` | `s * 8 + m` |
| MIDI channel parameter | `64 + s * 8 + m` |
| Value parameter | `192 + s * 8 + m` |
| Table read | Function `1F`, effect ID as low/high septets; observed reply chain `74`/`75`/`76`, 256 table values |
| Editor action envelope | Function `01` with a 15-byte payload; sub-action at payload index 0, effect at 2/3, parameter at 4/5, argument at 6/7; multibyte fields low septet first |
| Historical parameter write | Sub-action `09`; IEEE-754 float bits packed into five septets at complete-frame bytes 12–16; recalculate checksum |
| Historical select/edit/insert/save sub-actions | `27` select preset, `30` select cell, `32` insert effect, `26` save to destination slot |

The action names summarize the captured runner's use; their full semantics and reserved fields need transport-specific tests. Parameter IDs alone are not sufficient to invent an editor request.

The historical runner used eight scenes and eight message slots, with a 256-value block read. New-block defaults can contain active settings; read them before planning edits. Its known PC example does not establish general PC display/wire offsets: keep the requested value as a device setting and have a verified adapter handle encoding.

In the observed saved-body format, the grid began at `104` hex with 84 four-byte cells. Inserting this block increased the decompressed body by 560 bytes in the tested presets. These offsets and sizes are comparison evidence, not universal authoring rules. A production writer should parse the verified format and compare meaningfully rather than scan for an arbitrary matching byte sequence or hard-code insertion length as proof of preservation.

## Receiver verification

Saving correct FM9 settings and obtaining the intended downstream behavior are separate outcomes. Establish the receiver's channel and response to PC messages and check the actual outgoing MIDI path. Preset readback does not establish receiver compatibility or behavior.

## Complete saved and live reading, release 0.3.0

The FM9 12.00 saved Scene MIDI record has columns 257, rows 1 and 560 bytes. Its 256 table fields start at record word 17 (byte 34), overlapping six generic header words. The four 64-field banks are type, channel, controller and value. Controller starts at 128; each model's own pinned MIDIBLOCK catalogue confirms bank starts 0/64/128/192. Do not borrow unrelated parameter IDs across models.

Type 0 is Off, 1 CC, 2 PC; active raw channels 0–15 normalize to 1–16. Active value and CC controller ranges are 0–127. Keep inactive fields as raw unsigned words, including nonzero defaults. Preserve all remaining record words without invented semantics.

The current-table query is function 1F, effect 190 as low/high septets. Observed reply lengths are a 12-byte 74 header, 778-byte 75 section 0 (256 packed words starting at byte 8), 13-byte 75 section 1 (one opaque extra word), and 8-byte 76 footer. Data byte 7 is opaque metadata. Validate model/checksum/length/order and reject repeated/interleaved transfers. Addressless sections require serialized ownership and a poisoned session after ambiguous failures.

All 256 current FM9 12.00 live fields matched independently read saved words. All 512 retained saved captures decoded, including 254 complete tables; planning the already-present PC settings across populated presets yielded 254 unchanged and no conflicts/actions. This qualifies FM9 saved/live reading for observed PC/Off data. Active CC hardware behavior and writes remain untested. FM3/Axe-Fx III profiles remain provisional and synthetically tested. The portable planner supports PC/CC requests and version-3 complete slots; use the snapshot contract for required raw fields.

## Subsequent editing qualification, release 0.4.0

The [editor operations reference](editor-operations.md) records the later FM9 12.00 PC/CC write pilot, retained instance settings, default-scene semantics, select/clear sequence and exact original empty-preset recovery. The 0.3.0 read-only evidence above retains its original scope. Other-model hardware tests and full-preset restore remain pending.
