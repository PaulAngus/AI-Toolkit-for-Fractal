# General preset editing and connected routing

Release 0.35.0 adds [private named template libraries and channel swaps](template-library.md). `swap-channels` exchanges two complete channel rows with an explicit preserve/follow scene-reference policy, using ordinary edit execution and undo. Named donors lower to existing channel/block copy or insertion operations; firmware/provenance declarations and physical qualification remain separate.

Release 0.17.0 implements one shared offline planner and complete-image executor for FM9, FM3 and Axe-Fx III (512 or 1024 slots). CLI and MCP use the same deterministic core. Execution availability is independent of physical qualification: these operations are enabled for every model definition, with no firmware whitelist. Synthetic checks cover all model definitions; selected operations have separate FM9 12.00 hardware evidence below.

## Operations

| Operation | Explicit inputs and behavior |
| --- | --- |
| `rename-scene` | `scene` 1–8 and `name`, at most 31 printable ASCII characters. Other names and the saved default scene remain unchanged. |
| `set-parameter` | Exact `effect_id`, `channel` A–D, semantic `key`, finite `value` and catalogue `unit`. Uses the model's saved-field descriptor. |
| `set-block-type` | Same fields; key must end in `.type`. Numeric IDs require `policy: preserve-other-fields`, retaining other stored settings rather than guessing type defaults. |
| `copy-block-settings` | Same-model/firmware exact-instance template with the same record shape. Copies the whole block interval, including its scene assignments. Supports single-channel Return settings; grid and shared modifier records remain unchanged. |
| `set-scene-midi` | `scene` and `message_slot` 1–8, `message_type` Off/PC/CC. PC/CC takes `midi_channel` 1–16, integer `value` 0–127 and CC `controller` 0–127. Occupied replacement needs `policy: replace`. Off requires exact active `expected_raw` words `[type, channel, controller, value]` and clears only type, preserving inactive words. Can follow insertion in the same transaction. |
| `copy-channel` | Exact instance and destination channel, plus a complete same-model/firmware template capture and explicit `template_channel`. Copies the entire channel row, including settings without named field mappings. Shared modifiers and scene assignments remain unchanged. |
| `set-scene-block-state` | Exact instance, scene, absolute `channel` and/or `bypassed`. Edits persistent assignments on four-channel records; live effective controller behavior is not inferred. |
| `connect`, `disconnect` | One-based `source_row`, `source_column`, `destination_row`, `destination_column`. Occupied endpoints in adjacent forward columns; explicit splits, merges and parallel paths. |
| `insert-block` | Exact instance, chosen `row`/`column`, and complete same-model/firmware template containing that instance. Empty cell or explicit `policy: replace-shunt`; preserves its connections. Subsequent parameter/state operations can configure the inserted instance in the same plan. |
| `remove-block` | Exact instance and policy `disconnect`, `reconnect` with explicit replacement connect operations, or `replace-with-shunt` with an explicit shunt ID in `value`. Removes its complete record interval. |
| `insert-shunt`, `remove-shunt` | Chosen row/column. Insert supplies exact saved shunt identity 1024–1029; removal requires `policy: disconnect`. |

Named saved parameters currently cover Amp type on all models; drive, bass, mid, treble, master volume, depth, sag, presence and negative feedback on FM9/FM3; Cab mode and applicable DynaCab/mic numeric IDs; and Reverb, Delay, Drive, Chorus, Compressor, Filter, Flanger, Phaser, Tremolo, Wah, Enhancer and Rotary type IDs. Query `edit-catalogue` / `fractal_query_parameters` for exact keys, units, record shapes and model scope. Editor parameter IDs alone are not saved offsets. Unmapped named fields return a specific conflict; complete channel copying remains available for template-based settings.

Normalized values use the retained candidate scale `raw * 10 / 65535`. The review includes requested and effective saved values, raw values and affected scenes. Numeric selections are integers 0–65535; the encoding range does not establish firmware roster membership. Cab mode conditions must hold before mode-specific fields are edited. Type changes retain existing channel settings explicitly; template insertion supplies complete initialization. Offline catalogue counts and byte readback do not establish DSP capacity or audible behavior.

Routing reviews include complete before/after nodes and edges, changed grid cells and masks. The special routing report adds the two documented fixed Send/Return links, feedback components and model-qualified I/O endpoints. Output-to-Input grid cables are supported for external effects loops. Scene-dependent potential paths account for Input bypass (external source versus grid through) and Output bypass (mute versus tap and grid through). These are structural paths, not audibility predictions: Return level, other bypass modes, global digital sources and external cables remain separate. Creating or rewiring a feedback loop requires request-level `feedback_policy: allow`; the default `preserve` permits existing loops and their removal. No Return level is changed automatically. USB identity 41 is source-qualified for Axe-Fx III only; the FM9 candidate is reported as unresolved and FM3 has no documented USB Input block. [Automatic isolated placement](automatic-placement.md) is available through shared block/Scene MIDI planning; automatic audio-path design remains an extension. Existing opaque settings and unrelated unknown route bits are preserved; unknown bits in a word being changed produce a localized conflict.

## Offline CLI

Create a request from an integrity-checked, addressed saved capture. All example values below are synthetic; choose the actual slot, firmware, hash and operations from caller inputs.

```json
{
  "schema_version": 1,
  "model": "FM9",
  "capacity": 512,
  "firmware": "99.07",
  "slot": 0,
  "persistence": "edit-buffer",
  "base_sha256": "<SHA-256 of complete baseline.syx>",
  "operations": [
    {"kind": "rename-scene", "scene": 2, "name": "Example"},
    {"kind": "set-scene-block-state", "effect_id": 58, "scene": 2, "channel": "B", "bypassed": false}
  ]
}
```

```text
dotnet run --project src/Fractal.Cli -- plan-edit --request request.json --capture baseline.syx --destination edit-bundle
dotnet run --project src/Fractal.Cli -- inspect-edit-plan --bundle edit-bundle --expected-hash <plan_sha256>
dotnet run --project tests/Fractal.HardwareChecks -- --hardware --apply-preset-edit --bundle edit-bundle --expected-hash <plan_sha256> --validate-only true
```

`plan-edit` returns the report and its exact SHA-256. Conflicts produce exit code 2 and no executable bundle. The bundle contains request, original capture, authored capture and plan. Reopening rederives the result from the original and request, checking every hash. Equivalent operation order produces the same plan; repeated desired state is a no-op and retains the original wire bytes. Templates in CLI requests use `template_capture` (base64 complete capture), `template_sha256` and `template_firmware`; the public report includes hashes, not template bytes.

To execute, the trusted operator invokes the same helper without validate-only, supplying explicit `--input`, `--output` and a new private `--destination`. This is a live operation and must match the user's authorized scope. Select the target preset and its saved default scene first. The helper refuses unsaved changes and stale baselines. Use `persistence: save` only when saving is intended; `edit-buffer` uploads and verifies while checking the saved preset remains unchanged.

## Execution and recovery

The body writer encodes a deterministic Huffman stream into the contained model-specific container, updates CRC/transfer checksums and validates the result with the existing decoder. It preserves opaque header bytes and padding outside the replacement compressed region. Body edits touch only specified fields/grid words or explicit inserted/removed record intervals; unrelated block data, scenes, Scene MIDI, names, controllers and opaque suffixes are retained.

Execution checks context and complete saved/live baseline, repeats preflight immediately before upload, uploads once, and independently reads the complete loaded image. Renamed scenes also receive live scene-name queries with stable-selection bracketing. Scene MIDI edits receive independent complete live-table verification before Save and after Reload. A clear operation requires a fresh complete baseline and exact message words; it is not automatic historical-receipt merging. Save execution rechecks the full live image and unchanged saved baseline immediately before one Save; it then compares two saved reads, Reloads and verifies the complete result again. Acknowledgement or silence alone never establishes completion. No-op execution sends queries only.

Each attempt retains the immutable plan and original/result captures in a private evidence directory. Flushed phase checkpoints precede upload, Save and Reload. During general editing, a device-recompressed image is accepted only when its independently decoded body and all opaque raw header/trailer bytes match the plan; only the CRC and compressed stream may differ. Full-file restoration retains exact raw verification. Completed saved receipts record the actual observed saved wire hash, check repeated saved reads and Reload against those same bytes, and bind later undo to that observed hash. Failures stop with `stopped-uncertain`; neither uploads nor Save are replayed automatically. `--mode reconcile` with a plan bundle sends queries only and retains fresh saved/loaded images plus classifications `original`, `result`, `result-equivalent` or `other`. Mixed or later state requires a newly scoped action.

`--mode undo --receipt <execution-directory> --receipt-hash <attempt-json-sha256>` prepares exact original-image restoration from a completed saved receipt. It requires the freshly observed saved and loaded result to match, so later edits cannot be silently overwritten. Undo uses the same complete verification and one-save path. Edit-buffer-only or incomplete attempts use reconciliation; they are not misrepresented as completed saved receipts.

For completed saved receipts containing only Scene MIDI message edits, `plan-selective-scene-midi-undo` can create a new guarded bundle against a caller-supplied current saved capture. Release 0.27.0 also accepts completed items from the persistent recovery store through `plan-selective-recovery-undo`. Both restore exact receipt-owned raw rows, including inactive Off-row words, while preserving later unrelated settings; conflicting owned rows are refused. See [selective Scene MIDI undo](selective-scene-midi-undo.md).

## MCP

1. Query `fractal_query_edit_capabilities` and `fractal_query_parameters` for model-specific support.
2. Call `fractal_plan_preset_edit` with `backup_artifact`, `slot`, `persistence`, `operations` and optional `feedback_policy`. Templates use `template_artifact`, `template_slot`, and optionally `template_channel` rather than paths or raw SysEx.
3. Call `fractal_prepare_preset_edit` with the returned `plan_artifact`. The frozen review contains field changes, routing graph, exact hashes and persistence intent.
4. Apply the existing [trusted-host exact review authorization](mcp.md) and `fractal_execute_review` contract.
5. Use `fractal_prepare_preset_edit_undo` with `execution_artifact` for exact whole-image undo, or `fractal_plan_selective_scene_midi_undo` with a completed Scene MIDI receipt and current saved backup artifact before an ordinary `fractal_prepare_preset_edit` review. Use `fractal_prepare_edit_reconciliation` with `plan_artifact` for query-only recovery.

These tools introduce no provider dependency, arbitrary shell command, arbitrary SysEx call or firmware-wide disable switch. Existing operator access configuration and exact review authorization still apply.

## Evidence and remaining coverage

The saved field/grid profiles retain their [source attribution](editing-read-profiles.md). General scene-state and channel-row layout also reference pinned `presetBody.ts`, revision `59047175cfc4f23e092931b54a7c54f2bffde3ea`, SHA-256 `4619a595b9bf336e7f49b6efe4fbe07c8951a8ca7fedf581bdb42baa6a7c559d`: instance prefix is shape minus 12; four-channel data begins at shape plus 34. This is open implementation evidence, not manufacturer or hardware validation. The codec is the inverse of the contained decoder; normal builds fetch nothing.

[Synthetic checks](../tests/Fractal.Checks/PresetEditChecks.cs) independently assemble expected complete bodies, connected graphs, template/channel intervals and transport responses across all four definitions. They cover preservation, conflicting assignments, repeated requests, invalid fields, tampered bundles, edit-buffer/save behavior, equivalent compression, vacated compressed-stream bytes, actual-result receipts and undo, stale-state refusal, cancellation and failure before Save. The [validation record](preset-editing-validation.json) separates these results from physical qualification. FM9 12.00 combined scene-name/connected-cable editing, a two-target batch and a mapped Amp Drive saved-word edit passed Save/Reload and exact receipt-bound undo. A later compound Amp Bass/Mid edit and existing modifier-source edit also passed the same complete-image path and exact undo. Independent decoding found only the intended body bytes changed in each parameter/modifier trial. The first modifier upload stopped before Save when FM9 zeroed a byte vacated by a shorter compressed stream; read-only reconciliation and guarded Reload restored the original buffer. The corrected writer clears only that vacated interval, and a fresh modifier attempt passed. A final 512-slot saved inventory and context read matched the verified pre-series backup and starting state exactly. These trials do not establish other fields, routes, models, firmware, effective modifier control or audible behavior.

Remaining extensions include named mappings and physical calibration for additional fields, semantic roster/default catalogues, modifier layouts beyond the supported authoring fields, global I/O and external-cable configuration, automatic effect/tone selection and cross-model conversion. [Absolute live scene/channel/bypass controls](live-tools.md), [configured MIDI routing](configured-midi-routing.md), [automatic grid paths](automatic-audio-paths.md) and [saved-edit batches](batch-editing.md) are separately implemented. These limits do not disable the implemented saved-image operations.

The [mapping research record](parameter-mapping-research.json) retains 462 candidate fields across 18 families, including duplicate word indices, weak labels and absent display scales. Those hints do not establish safe saved setters. [SpecialRoutingChecks](../tests/Fractal.Checks/SpecialRoutingChecks.cs) and [CompoundMidiChecks](../tests/Fractal.Checks/CompoundMidiChecks.cs) add literal expected-body, special-path, independent live-table, occupied-row and selective-clear checks on all definitions. I/O and Send/Return behavior follows the manufacturer Blocks Guide, June 2023, pp4–6 and 89–90; numeric identities retain their separate model evidence.

[Release 0.23 authoring](calibrated-authoring.md) adds `set-calibrated-parameter`, `set-scene-ignore`, `set-modifier-source` and `copy-modifier`. Exact caller-labelled calibration witnesses and explicit templates are retained in private plans and revalidated before ordinary execution; hardware qualification stays separate.

Release 0.24.0 [built-in scales and named modifier fields](builtin-scales.md) add exact-profile physical ranges and quantization, preserving all other channels and opaque settings. A later source-scoped extension adds saved-normalized modifier Scale/Offset/Attack/Release and exact Damping Type word copy from a same-binding donor, without inferred display conversions or selector labels. Shared Save/Reload and receipt undo apply unchanged.

## Whole-image organization operations

`rename-preset` and `copy-preset` are sole-operation complete-image plans used by the [organization compiler](preset-organization.md). A preset rename owns only its raw 32-byte name field and integrity values. A copy carries a complete same-context donor, optional explicit rename and the donor saved default scene, while the original destination scene remains the pre-upload requirement. They cannot compose with parameter/grid operations. Organization adapters expose intent-level operations and guard the immutable source pool; ordinary batch verification, receipt integrity and exact trusted-host review remain authoritative. No whole-preset authoring specification/compiler or exhaustive parameter mapping is added.
