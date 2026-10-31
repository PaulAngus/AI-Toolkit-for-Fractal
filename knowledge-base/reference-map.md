# Documentation reading map

For `.fas-songs`, `.fas-setlist` and complete System backup format research, start with [the 15 October static assessment](../docs/setlists-system-format-research.md) and [hashed artifacts/locators](../docs/setlists-system-format-research.json). Reviewed export paths exist independently in FM9-Edit 1.03.22, FM3-Edit 1.07.17 and Axe-Edit III 1.14.34. Partial XML construction is evidence; numeric sentinels, actual interchange and System recovery remain unqualified.

Release 0.38.0 adds [offline songs and setlist proposals](../docs/setlists-songs.md) against retained complete or partial inventories through shared CLI/MCP. DEFAULT stays symbolic and missing dependencies are reported. Editor interchange and live system execution are deferred pending complete codec, preservation and recovery evidence; no hardware qualification is added.

For private named donors and channel swaps, use [the template workflow](workflows/preset-templates.md), [TemplateLibrary.cs](../src/Fractal.Core/TemplateLibrary.cs) and [independent checks](../tests/Fractal.Checks/TemplateLibraryChecks.cs). Applications lower to existing copy/insertion plans; `swap-channels` owns two complete rows and optional scene channel selectors, with shared modifier composition refused. Software evidence is separate from hardware qualification.

[Selective Scene MIDI receipt undo](../docs/selective-scene-midi-undo.md) maps completed saved general-edit receipts and completed persistent recovery-store items to exact owned rows and a new current saved capture. Implementation: [SceneMidiSelectiveUndo.cs](../src/Fractal.Core/SceneMidiSelectiveUndo.cs), [selective synthetic checks](../tests/Fractal.Checks/SelectiveUndoChecks.cs). The recovery store retains separate exact-state continuation rules for unfinished items.

Release 0.17.0 extends [general preset editing and connected routing](../docs/preset-editing.md): additional block types, whole-block templates, compound Scene MIDI edits, Send/Return links and I/O-conditioned paths. [Inventory analysis](../docs/preset-inventory.md) adds comparisons, saved amp usage, qualified absence, migration and MCP access. Physical qualification is separate from executable support.

Start with the local procedure and its evidence limits, then use the locator below. This map retains useful deep references from prior research so a client need not repeat broad searches. It links documentation rather than redistributing manuals or guide prose. Authority, historical access, shared lineage and link checks are in [sources.json](catalog/sources.json).

The target runtime covers FM9, Axe-Fx III and FM3; [device support](../docs/device-support.md) records their common interface, model-specific adaptations and current implementation gaps. Scene MIDI references for FM3 are printed pp56/134 (PDF pp62/140); Axe-Fx III's Sending MIDI section is printed p102. These sections were inspected on 8 October 2026, and confirm the feature independently of the FM9 editing observations.

| Task | Local procedure | Document or implementation locator |
|---|---|---|
| Identity, framing, current preset/scene, live scene names | [FM9 protocol](../skills/fractal-fm9/references/fm9-protocol.md) | [Third-party MIDI specification](https://www.fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf): envelope, commands 0AH–0EH, effect-ID appendix. Published model is Axe-Fx III; FM9 applicability is separately recorded. |
| Firmware and discovery | [Device reads](workflows/device-reads.md) | Pinned [ToneCommand protocol](https://github.com/monzta1/ToneCommand/blob/3c97d6cff34c8b9ca842b79bed21154052411047/fm9/protocol.py) and [reader](https://github.com/monzta1/ToneCommand/blob/3c97d6cff34c8b9ca842b79bed21154052411047/fm9/device.py): firmware function 08H and full-frame offsets; [recorded discovery](https://github.com/PaulAngus/Preset-Maestro/blob/078a1226559751030884aad4d95384dee37c969c/FRACTAL-DEVICE-INFORMATION.md). |
| Bank/PC, scene CC, custom mapping, bypass assignments | [Recall and control](workflows/recall-and-control.md) | [FM9 manual](https://www.fractalaudio.com/downloads/manuals/FM9/FM9-Owners-Manual.pdf): MIDI Reference Tables → MIDI Bank & Program Change; SETUP → MIDI/Remote. [Official downloads](https://www.fractalaudio.com/fm9-downloads/) is the fallback for a moved manual or driver. |
| Saved transfer, scene names and integrity | [Saved reads](workflows/device-reads.md), [backups](workflows/backups-verification.md) | [presetDump.ts](https://raw.githubusercontent.com/TheAndrewStaker/mcp-midi-control/main/packages/fractal-gen3/src/presetDump.ts): `parsePresetDump`, `extractPresetName`, 77H/78H/79H framing and read/write-direction limits. [presetContainer.ts](https://github.com/TheAndrewStaker/mcp-midi-control/blob/main/packages/fractal-midi/src/shared/presetContainer.ts): CRC/container parsing. |
| Grid, blocks, Amp channels, scene assignments, modifiers | [Inventory](workflows/preset-inventory.md) | [presetBody.ts](https://github.com/TheAndrewStaker/mcp-midi-control/blob/main/packages/fractal-gen3/src/presetBody.ts): `decodeGen3Body`/`decodeGen3PresetDump`, placed effects and scene-indexed fields. [gen3BodyTables.ts](https://github.com/TheAndrewStaker/mcp-midi-control/blob/main/packages/fractal-gen3/src/gen3BodyTables.ts): effect/parameter tables. Historical amp tables do not establish current numeric availability. |
| Add a block instance at a chosen cell | [General block insertion](workflows/block-insertion.md) | [Local contract](../skills/fractal-fm9/references/block-insertion.md), `plan_block_insertion.py`: instance identity, explicit placement, grid-only validation; retained MIDI specification effect-ID appendix and model grid definitions above. |
| Scene MIDI and its per-scene PC/CC rows | [Workflow](workflows/scene-midi.md), [recovered fields](../skills/fractal-fm9/references/scene-midi.md) | [Blocks Guide](https://www.fractalaudio.com/downloads/manuals/fas-guides/Fractal-Audio-Blocks-Guide.pdf#page=89): **The Scene MIDI Block**, PDF page 89 in the version opened 7 October 2026. [Pinned editor envelopes](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/axe-fx-iii/setParam.ts): source symbols and local empty-preset qualification in [editing pilot](../docs/editing-pilot.md). |
| Amp IDs and firmware availability | [Amp catalogues](workflows/amp-catalogues.md) | [Recorded roster](catalog/fm9-12-amp-roster.json): 336 FM9 12.00 pairs and capture hash. [FM9 12.00 release ZIP](https://www.fractalaudio.com/downloads/firmware-presets/fm9/12p0/fm9_dsp_rel_12p00.zip): embedded Release Notes PDF, first page, **New Amp Models**. |
| Real amplifier family, variants and uncertainty | [Local identities](catalog/fm9-12-amp-identities.json), `scripts/query_amp.py` | Each model has a Wiki section URL, source IDs and historical locators. [Axe-Fx II manual](https://www.fractalaudio.com/downloads/manuals/axe-fx-2/Axe-Fx-II-Owners-Manual.pdf#page=177): Doc Q7.0 §16.1, printed pp165–168 / PDF pp177–180, historical family basis only. [Fullerton AMPS tables](https://shop.fractalaudio.com/products/icons-fullerton-complete): Volumes 1–4. |
| Conflicting amp revisions and historical references | [Qualifications](catalog/fm9-12-amp-identities.json) | Per-model locators retain [pinned Ampdex data](https://github.com/rinkashimikito/ampdex/blob/1210a8c2110bfdd14d567b6262383a602657d310/src/data/amps.json), specific Fractal administrator posts and historical release references. Ampdex and Wiki share lineage. Unfetched citations and third-party reposts retain those limits. |
| Missing replies, competing clients, endpoints, partial sends | [MIDI reliability](workflows/midi-reliability.md) | [Windows endpoint API](https://microsoft.github.io/MIDI/sdk-reference/MidiEndpointConnection/), [reporting API](https://microsoft.github.io/MIDI/sdk-reference/Reporting/MidiReporting/), [batch-send code](https://raw.githubusercontent.com/microsoft/MIDI/main/src/api/Client/WinRT/t/core/MidiEndpointConnection_SendMultipleMessages.cpp), [SysEx collision issue](https://github.com/microsoft/MIDI/issues/60). Match the installed backend; current issue status is not implied. |
| Network sessions and endpoint ownership | [Network procedure](workflows/midi-reliability.md) | [Apple network MIDI protocol](https://developer.apple.com/library/archive/documentation/Audio/Conceptual/MIDINetworkDriverProtocol/MIDI/MIDI.html): sessions/recovery journal. |
| Controller labels versus state feedback | [Controllers](workflows/controllers.md) | [VOES MX-6](https://voes.be/mx6.html), [MX-6x6](https://www.voes.be/6x6.html), [standard MX commands](https://www.voes.be/commands.html), [downloads](https://www.voes.be/downloads.html). Match hardware, firmware/editor and command family. |
| Editor comparison and other Fractal models | [Compatibility](workflows/device-reads.md) | [FM9-Edit guide](https://www.fractalaudio.com/downloads/manuals/FM9-Edit/FM9-Edit-User-Guide.pdf), [FM3 manual](https://www.fractalaudio.com/downloads/manuals/FM3/FM3-Owners-Manual.pdf), [Axe-Fx III manual](https://www.fractalaudio.com/downloads/manuals/axe-fx-3/Axe-Fx-III-Owners-Manual.pdf), [FM9 capture guide](https://raw.githubusercontent.com/TheAndrewStaker/mcp-midi-control/main/packages/fractal-midi/docs/capture-guides/testing-fm9.md). References and simulated tests do not prove physical compatibility. |
| Live changes and controller status | [Live observations](workflows/device-reads.md#live-changes-and-refresh) | [Third-party MIDI specification](https://www.fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf): STATUS DUMP 13H and **Push Data**. Published push scope is limited; private editor traffic requires separate evidence. |

## Retrieval limits and fallbacks

On 7 October 2026, the MIDI specification, Blocks Guide, Footswitch Functions Guide, FM9 downloads, Gen-3 dump source and body tables were retrievable. Direct FM9 manual fetching timed out; its official listing and indexed bank/PC table were available. Other references retain their historical access status rather than a claim of fresh verification.

Original `fractal-syx-codec` repository links under both `drewmerc302` and `BoodieTraps` returned 404 in this pass. Preserve technical attribution, but start format work with the local summary, credited existing core and accessible Gen-3 sources. Do not depend on an unavailable source.

During the amp review, the Wiki page returned 403 and was inspected through indexed excerpts; its section index was later retrieved through the API. Section links are reading pointers, not independently verified amp identity. Keep stored qualifications when pages cannot be fetched.

For current firmware/controller features, revisit the official source already listed here. Use local material first for known scoped facts. Update a claim, locator, model/firmware scope and qualification together when new evidence changes an interpretation.

## Contained runtime implementation

For automatic routing use [AutomaticRouting.cs](../src/Fractal.Core/AutomaticRouting.cs): `Create` selects a forward path and delegates the exact operations to `PresetEditPlan`. For controller input use [ControllerListener.cs](../src/Fractal.Core/ControllerListener.cs): `Decode`, `CaptureAsync`, and `WinMmTransport.OpenInput`. Microsoft's [MIM_DATA](https://learn.microsoft.com/en-us/windows/win32/multimedia/mim-data) and [MIM_ERROR](https://learn.microsoft.com/en-us/windows/win32/multimedia/mim-error) define packed short input and failures; these platform contracts are not hardware tests.

For special routing, [SpecialRouting.cs](../src/Fractal.Core/SpecialRouting.cs) projects fixed Send/Return links and scene-conditioned I/O paths. The manufacturer Blocks Guide (June 2023) explains I/O through/bypass on printed pp4–6 and fixed Send/Return pairs on pp89–90; numeric identities retain the model-specific block catalogue evidence. For additional type mappings, pinned `presetBody.ts` symbols `SHARED_BLOCK_COLS`, `SHARED_TYPE_LOCS` and `typeFieldByteOffset` supply exact shapes/offsets. [Mapping research](../docs/parameter-mapping-research.json) retains negative findings from the broader correlation-derived tables.

For inventory comparison, usage and qualified roster absence, use [InventoryAnalysis.cs](../src/Fractal.Core/InventoryAnalysis.cs): `Compare`, `AmpUsage`, `ReadRoster`. [PresetInventory.cs](../src/Fractal.Core/PresetInventory.cs) owns immutable capture integrity and explicit migration; comparison does not assign physical or sound identity.

Begin with [runtime.md](../docs/runtime.md) for commands, source hashes, export integrity and limits. Pinned `gen3-body` / `gen3-dump` references above are retained in [sources.json](catalog/sources.json); use their record URLs before searching broadly. Windows backend API contracts are indexed as `winmm-header`, `winmm-input-buffer`, `winmm-input-reset`, `winmm-output-long`. The underlying fractal-syx-codec repository was unavailable (404) on 8 October 2026; the pinned format implementations provide a qualified fallback.

For actual single-preset backup files, use `check-backup` and [PresetFile.cs](../src/Fractal.Core/PresetFile.cs): strict one-transfer framing, opaque header retention and shared payload integrity. [Restore qualification](../docs/restore-qualification.md) distinguishes file integrity, loaded/saved-state differences and verified persistence. In pinned `presetBody.ts`, `walkBlocks` reads scene channels at words `[-1,0..6]`; do not mistake the preceding record's final word for a standalone parameter or discard a changed channel selector.

For block identity during recovery comparisons, read manufacturer MIDI Appendix 1 and pinned `presetBody.ts` `EFFECT_BASES`/`effectName`, then correlate the actual numeric instance with retained status/grid/prefix evidence. The pinned mapping labels 58 as Amp 1, 62 as Cab 1 and 119 as Drive 2. A record's parameter-column count is insufficient to assign a block identity or a channel edit.

| Source file | Relevant symbols |
|---|---|
| [DeviceDefinition.cs](../src/Fractal.Core/DeviceDefinition.cs) | Create, ModelName, ValidateSlot: model/capacity/layout definitions |
| [SysexProtocol.cs](../src/Fractal.Core/SysexProtocol.cs) | Frame, ValidFrame, IsReadRequest: checksum, addressing and closed read allowlist |
| [StoredPresetDecoder.cs](../src/Fractal.Core/StoredPresetDecoder.cs) | AcceptImage, ComputeCrc, Expand: bounded saved transfers and integrity |
| [PresetBody.cs](../src/Fractal.Core/PresetBody.cs) | Decode: model grid/Amp/scene layouts, complete Scene MIDI slots and explicit unsupported status |
| [ReadSession.cs](../src/Fractal.Core/ReadSession.cs) | IdentifyAsync, PresetNameAsync, CurrentAsync, SavedAsync, SnapshotAsync: serialized reads, correlation and drift |
| [SnapshotExport.cs](../src/Fractal.Core/SnapshotExport.cs) | DecodeFile, Write: capture validation, normalized snapshot and manifest |
| [WinMmTransport.cs](../src/Fractal.Core/WinMmTransport.cs) | Ports, SendReadAsync, DisposeAsync: native ownership and bounded transport |

For current FM9 physical read limits and the repeated-chunk / terminal-metadata findings, use [hardware-validation.md](../docs/hardware-validation.md) and [hardware-fm9-12.00.json](../docs/hardware-fm9-12.00.json). Reproduce with [Fractal.HardwareChecks](../tests/Fractal.HardwareChecks/Program.cs) only under explicit connection authorization. The [public summarizer](../scripts/summarize_hardware_validation.py) verifies export hashes while excluding personal names and raw data.

## Scene MIDI reading locators

- [Contained saved/live reader and validation](../docs/scene-midi-reading.md): record word 17, all 256 fields, current-table framing and model/firmware limits.
- [SceneMidiTable.cs](../src/Fractal.Core/SceneMidiTable.cs): SavedFields, Decode and SceneMidiTableDecoder; complete slots, inactive-field preservation and strict ordered transfers.
- Model-specific MIDIBLOCK / CMD_*_BEGIN bank catalogues and the negative result for cross-model parameter reuse are retained in the [source register](catalog/sources.md). Matching Scene MIDI banks do not establish matching unrelated parameter IDs.

## Editor pilot locators

- [Editing pilot and pinned source symbols](../docs/editing-pilot.md): select/insert/clear, typed float32, save/reload, saved default scene, actual new-block settings and explicit recovery.
- [EditorCommand.cs](../src/Fractal.Core/EditorCommand.cs): typed builders, EmptyPresetPilotScope and BlockStatusTable.
- [ReadSession.cs](../src/Fractal.Core/ReadSession.cs): AuthorizeEmptyPresetPilot, SendEditorAsync and BlockStatusAsync; bounded delivery, late rejection and no replay.
- [EditorPreservation.cs](../src/Fractal.Core/EditorPreservation.cs): VerifySceneMidiInsertion; actual instance interval, names, routing, saved scene and unrelated bytes.
- [EmptyWritePilot.cs](../tests/Fractal.HardwareChecks/EmptyWritePilot.cs): preflight, PC/CC live/saved verification, exact empty recovery and partial reconciliation.
- [Redacted physical record](../docs/editor-pilot-validation.json): FM9 12.00 complete pass, failed attempts and recoveries; other-model physical tests remain missing.

## Full-preset restoration locators

- [Restore qualification stages](../docs/restore-qualification.md): actual single-preset backup inspection, transfer/destination semantics, fresh baseline and model-scoped changed-image and original-image trial. Current toolkit upload evidence is recorded separately below.
- [FM9 manual, Saving Changes](https://www.fractalaudio.com/downloads/manuals/FM9/FM9-Owners-Manual.pdf#page=53), printed p47: complete STORE/ENTER/ENTER confirmation, destination and “SAVED!” indication. Use this when current restored status differs from addressed saved readback; retain the live buffer while reconciling.
- [Save an already-loaded preset](../docs/save-current.md): `PresetSaveScope`, `ReadSession.AuthorizePresetSave` and [SaveCurrent.Run](../tests/Fractal.HardwareChecks/SaveCurrent.cs); one save, exact base hashes and complete repeated raw/body comparison. [Physical results](../docs/save-current-validation.json) qualify FM9 12.00 persistence only; the expected backup is not uploaded.

- [Full-preset restoration](../docs/preset-restore.md): pinned ForgeFX `Gen3Driver.loadPresetBytes` and forgefx-midi `retargetPresetDumpToEditBuffer`; exact revisions/hashes in the source register.
- [PresetRestore.cs](../src/Fractal.Core/PresetRestore.cs): `PresetUploadPlan.Create`, `PresetRestoreScope`; immutable temporary addressing, session-bound proofs and strict source validation.
- [ReadSession.cs](../src/Fractal.Core/ReadSession.cs): `EditBufferAsync`, `PreparePresetRestoreAsync`, `UploadPresetAsync`, `VerifyUploadedPresetAsync`, `VerifyRestoredSavedAsync`; distinguish live and saved images, never replay an uncertain write.
- [RestorePreset.cs](../tests/Fractal.HardwareChecks/RestorePreset.cs): opt-in generic trial with complete inventory preservation checks. [Qualification record](../docs/preset-restore-validation.json) separates physical evidence from synthetic coverage.

## Generic insertion execution locators

- [Execution contract](../docs/block-insertion-execution.md): exact caller inputs, isolated routing, safe navigation, record projection and bounded multi-target behavior.
- [BlockInsertion.cs](../src/Fractal.Core/BlockInsertion.cs): `BlockInsertionPlan.Create`, `BlockInsertionScope`; immutable baseline and closed target/cell/instance scope.
- [BlockInsertionSession.cs](../src/Fractal.Core/BlockInsertionSession.cs): `PrepareBlockInsertionAsync`, `VerifyInsertedBufferAsync`, `VerifyInsertedSavedAsync`; whole-buffer preservation before loading/saving, once-only operations and complete persistence checks.
- [EditorPreservation.cs](../src/Fractal.Core/EditorPreservation.cs): `VerifyBlockInsertion`; identify by actual prefix, remove the actual interval, compare all remaining body bytes and raw header/padding.
- [InsertBlocks.cs](../tests/Fractal.HardwareChecks/InsertBlocks.cs): explicitly opted-in caller request, per-item exports, stop on failure; [redacted evidence](../docs/block-insertion-validation.json).

## Existing-block Scene MIDI execution and undo locators

- [Execution contract](../docs/scene-midi-execution.md): complete planner inputs, existing-block conflicts, exact field order, preservation, multi-target stopping and explicit undo boundaries.
- [SceneMidiEdit.cs](../src/Fractal.Core/SceneMidiEdit.cs): `SceneMidiEditPlan.Create`, `FromPlanner`, `ExpectedFields`, `VerifyProgress`, `SceneMidiEditScope`, `SceneMidiEditBatchScope`, `SceneMidiEditReceipt`; immutable saved baseline, complete raw before-fields and exact-result undo authority.
- [SceneMidiEditSession.cs](../src/Fractal.Core/SceneMidiEditSession.cs): `PrepareSceneMidiEditAsync`, `VerifySceneMidiFieldAsync`, `VerifySceneMidiBufferAsync`, `VerifySceneMidiSavedAsync`, `SceneMidiReceipt`, `PrepareSceneMidiUndoAsync`; safe navigation, independent live table, full pre-Save/post-Reload verification and later-edit refusal.
- [EditorPreservation.cs](../src/Fractal.Core/EditorPreservation.cs): `VerifyRawRepresentation`; exact opaque header/padding comparison alongside strict body/transfer integrity.
- [ApplySceneMidi.cs](../tests/Fractal.HardwareChecks/ApplySceneMidi.cs): explicit request hash, manifest preflight, port-free validation, completed-only scopes, full before/edited/restored inventories and deliberately requested one-target undo.
- [Qualification record](../docs/scene-midi-execution-validation.json): actual PC/CC/model/firmware scope. Use the retained manufacturer Blocks Guide, pinned per-model banks and `setParam.ts` locators above before a fresh broad search.

## Persistent Scene MIDI recovery locators

- [Recovery contract](../docs/scene-midi-recovery.md), [execution qualification](../docs/scene-midi-recovery-validation.json) and [migration validation](../docs/scene-midi-recovery-migration-validation.json): exact original/result reconciliation, no blind field/Save/upload replay, partial unsaved conflicts, explicit batch undo, compatible older-record migration and disk/authorization limits.
- [SceneMidiRecovery.cs](../src/Fractal.Core/SceneMidiRecovery.cs): `SceneMidiRecoveryStore.Create`, `Open`, `Plan`, `Receipt`, `Attempt`, `CompleteEdit`, `CompleteUndo`, `Reconcile`; strict private import, lease, atomic replacement and lifecycle decisions.
- [SceneMidiRecoveryRunner.cs](../src/Fractal.Core/SceneMidiRecoveryRunner.cs): `RunAsync`; all-target preflight, immediate per-item checks, one transport owner and fresh scopes for edit/result verification/undo.
- [SceneMidiReconcileSession.cs](../src/Fractal.Core/SceneMidiReconcileSession.cs): `VerifyRecoveredSceneMidiAsync`; safe Load/Reload, full saved/live/table checks, no fields/Save/upload.
- [RecoverSceneMidi.cs](../tests/Fractal.HardwareChecks/RecoverSceneMidi.cs): offline inspection/validation and explicitly opted-in live resume/undo, full inventory preservation and checkpoint heads.
- Reuse the Blocks Guide, per-model banks, pinned `setParam.ts`, upload driver/helper and restore locators above. Recovery adds file/transaction logic rather than another researched wire format.

## Explicit preset load locators

- [Preset-load contract](../docs/preset-load.md), [PresetLoad.cs](../src/Fractal.Core/PresetLoad.cs) and [LoadPreset.cs](../tests/Fractal.HardwareChecks/LoadPreset.cs): one typed target load against retained current/target hashes, explicit discard choice, current-buffer evidence and saved/default-scene checks. Recovery retains its stricter no-discard navigation. Existing pinned Reload builder sources apply.

## Local MCP adapter locators

Start with [installation/contracts](../docs/mcp.md) and [qualified scope](../docs/mcp-validation.json). Pinned negotiated 2025 specification sources `mcp-2025-stdio/lifecycle/tools/resources/cancellation` locate framing, initialize/initialized, schemas/results, URI resources and request-ID cancellation. `mcp-python-sdk` records the independent client test version; normal builds do not fetch it.

- [server.py](../src/fractal_mcp/server.py): `Server.dispatch`, `serve`, `work`; negotiated stdio, bounded framing, typed discovery/results, serial work and readable cancellation.
- [tools.py](../src/fractal_mcp/tools.py): `Toolkit.call`, `prepare`, `validate_review`, `approve`, `execute`; canonical source reuse, artifact discovery, exact review bindings and local once-only approval.
- [storage.py](../src/fractal_mcp/storage.py): `Artifacts`, `confined`, `atomic_json`; private evidence, complete input hashes and path confinement.
- [runtime.py](../src/fractal_mcp/runtime.py): `Runtime.run`, `device_owner`, `load_config`; bounded owned child execution, port-pair leases and operator settings.
- [package_mcp.py](../scripts/package_mcp.py) and [check_mcp_pipeline.py](../scripts/check_mcp_pipeline.py): complete contained bundle and relocated real stdio/model checks. [check_mcp_sdk.py](../scripts/check_mcp_sdk.py) is optional independent interoperability testing.

## Contained block identity lookup

For offline general-editing readiness, use [editing read profiles](../docs/editing-read-profiles.md), [EditReadCatalogue.cs](../src/Fractal.Core/EditReadCatalogue.cs) and [PresetEditProjection.cs](../src/Fractal.Core/PresetEditProjection.cs). The [embedded profile data](catalog/edit-read-profiles.json) retains model-specific offsets and provenance. `PresetBody.ReadLayout` is shared structural parsing; exact prefix association is separate from the legacy Amp projection. The [audio catalogue](catalog/audio-parameter-mappings.json) retains independent editor IDs and display hints per model; these are separate from saved offsets. Profiles also supply bounded saved audio candidates and model-specific incoming row masks. See [AudioParameterMappings.cs](../src/Fractal.Core/AudioParameterMappings.cs) for catalogue selection. No new writers or hardware qualification are implied.

Start with [block identification](workflows/block-identification.md) and the [bundled factual catalogue](catalog/block-identities.json). Manufacturer MIDI revision 1.4 Appendix 1 supplies Axe-Fx III enum IDs; the pinned `gen3-body` `EFFECT_BASES`/`effectName` supplies independent common mappings; Blocks Guide June 2023 printed p3/PDF p4 gives separate model counts, with Input USB distinctions on printed pp4–5. Exact source bytes, hashes and scope are retained in the catalogue. `Fractal.Core.BlockCatalogue` is the shared resolver used by CLI `blocks`, MCP `fractal_query_block` and grid export; no broad search or network retrieval is needed for mapped names. Unknown/model-unqualified identities remain numeric.

## Isolated block removal and insertion undo

Use [the procedure](workflows/block-removal.md), [closed executor](../docs/block-removal-execution.md) and [qualification](../docs/block-removal-validation.json). Retained editor locators are in [the editor evidence](../docs/editing-pilot.md): pinned setParam.ts symbols buildSelectCell and buildClearBlockCompanion. Shared implementation symbols: BlockRemovalPlan.Create, ReadSession.PrepareBlockRemovalAsync and EditorPreservation.VerifyBlockRemoval. MCP insertion undo derives exact inserted/original pairs from verified receipts; it does not introduce another wire encoding.

## Resumable saved inventory

[PresetInventory.cs](../src/Fractal.Core/PresetInventory.cs): `Create`, `Open`, `RunAsync`, `Inspect` and `Export` implement private per-slot checkpoints and full retained-content revalidation through the existing coordinated `ReadSession`. [Runtime contract](../docs/preset-inventory.md), [synthetic checks](../tests/Fractal.Checks/InventoryChecks.cs) and [validation](../docs/preset-inventory-validation.json) separate complete capture scope from decoded coverage and physical identity. No new wire encoding is introduced.

For live amp roster, direct scene/channel/bypass and finite state samples, start with [live tools](../docs/live-tools.md), [validation](../docs/live-tools-validation.json), and `AmpRoster`, `LiveControlScope`, `LiveMonitor` in the contained core.

For explicit automatic isolated placement, start with [policy and validation](../docs/automatic-placement.md), `GridIsolation.IsIsolated`, and `isolated` / `insertion_for` in the shared portable block planner.

Network and notification implementation: [runtime contract](../docs/network-notifications.md), [validation](../docs/network-notifications-validation.json), `RtpMidiCodec`, `RtpMidiTransport`, `MidiSendAuthority`, `DeviceNotifications`; source IDs `apple-network-midi`, `rtp-midi-rfc6295`, `fractal-midi-spec`, `toolkit-network-notifications`.


Private mappings: [contract and pinned-source locators](../docs/live-parameters.md), `BlockParameterDecoder`, `PrivateParameters.DecodeEcho`, `DeviceNotificationObserver`, `ReadSession.BlockParametersAsync`, `BlockParameterRead.CaptureAsync`; [validation](../docs/live-parameters-validation.json).

[Calibrated authoring](../docs/calibrated-authoring.md): `SavedCalibration.Resolve/Apply`, `SavedModifiers.Read/Apply`, and shared `PresetEditPlan` operations. Modifier field locators are pinned `gen3-body` `extractChainModifiers`; exact-value calibration is a contained integrity/context comparison, not a remote calibration feed.

[Built-in scales and curves](../docs/builtin-scales.md): `BuiltinScales.Catalogue/Apply`, `SavedValueScale.Encode/Decode`, `SavedModifiers.ApplyParameter/CopyRawSelector`; [contained data](catalog/saved-value-scales.json), [validation](../docs/builtin-scales-validation.json). Pinned sources `axe32-word-laws`, `axe32-map`, `axe32-modifiers` identify saved-normalized Scale/Offset/Attack/Release words and the raw Damping Type index for Axe-Fx III 32.02. Display conversions and selector labels remain unknown; upstream reported evidence is distinct from toolkit hardware testing.

## Tempo, tuner and looper

[Performance workflow](workflows/performance-controls.md) and [contracts](../docs/performance-controls.md) map to `PerformanceControlScope`, `PerformanceProtocol`, `ReadSession.LooperAsync` and the reviewed tempo exchange. Manufacturer MIDI specification functions 0F/10/11/14 define Axe-Fx III forms; each own-model manual section Tempo defines 20–250 BPM and global/preset coupling. Pinned `setParam.ts` lines 1791–1857 and 2378–2425 provide command forms/model-bound tempo queries. `gen3-tempo-query-limit` retains the qualified negative report; `tonecommand-protocol` lines 134–139 supplies independent FM9 tempo read/set. No qualified loaded-tempo offset or tuner state query is present.

## Saved preset organization

[Procedure](workflows/preset-organization.md), [contracts](../docs/preset-organization.md), `PresetOrganizationPlan`, `PresetBodyWriter.RenamePreset`, `PresetFile.AddressSaved`, `PresetEditPlan.CreateOrganizationImage` and guarded `PresetEditBatch`. Pinned `gen3-body` `rawPatchName` lines 692–697 supplies raw name offset 0x08/32 bytes. Ordinary upload/Save/Reload and strict whole-image verification are reused. Organization progress binds batch/current release; completed-item receipts qualify eligible undo. Different before/result scenes have independent one-shot upload/reload alignment proofs. Hardware relocation, external references/assets and bank-file interchange remain unqualified.
