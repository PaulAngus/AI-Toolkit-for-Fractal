# Toolkit implementation plan

Baseline: **contained release 0.38.0**, reconciled on **14 October 2026** against the [release manifest](protocol-release.json), [capability audit](capability-audit.md), current core services and regression checks.

Status: **The supported editing core is implemented. Named templates/channel swaps and bounded performance controls are implemented. Bounded preset organization is implemented. Setlists/songs investigation and toolkit-native offline planning are delivered in 0.38.0. Editor interchange and live system execution remain deferred behind explicit evidence gates.** Exhaustive parameter/default mapping and whole-preset specification/orchestration are optional backlog items. Existing supported mappings, calibration witnesses, templates and compound edits remain available. Add mappings when a concrete workflow requires them. The existing filename is retained for link compatibility.

The delivery order is: **(1) named template library and channel swaps (implemented in 0.35.0); (2) tempo, tuner and looper controls (implemented in 0.36.0, with explicit evidence limits); (3) preset organization (implemented in 0.37.0); (4) focused setlists/songs investigation and offline planning (delivered in 0.38.0), with editor interchange and live execution deferred until complete storage, preservation and recovery evidence exists.** The plan does not authorize hardware access.

The [general editing design](general-editing-and-routing-design.md) retains the original design intent. Use the [current editing contracts](preset-editing.md), [capture-backed authoring](calibrated-authoring.md) and [built-in scales](builtin-scales.md) for implemented behavior. Earlier milestones describing basic writers, connected routing or CLI/MCP as future work are superseded by the coverage map here.

## Active delivery sequence

All milestones reuse the existing deterministic core, coordinated transport, integrity-checked backups, CLI/MCP reviews and verification. Keep common contracts for FM9, FM3 and both Axe-Fx III capacities, with independently established model/firmware behavior. Software support and physical qualification remain separate.

### 1 Named template library and channel swaps — implemented in 0.35.0

**Software implementation delivered:** [private library and swap contracts](template-library.md), [independent checks](../tests/Fractal.Checks/TemplateLibraryChecks.cs), [validation record](template-library-validation.json). Physical qualification remains separate. Milestone 2 is delivered with its separate performance evidence limits; preset organization is delivered; setlists/songs offline investigation is delivered in 0.38.0.

The manufacturer manuals describe libraries of complete blocks/channels and channel swapping (sources E1/E2 below). Existing `copy-channel`, `copy-block-settings` and template insertion supply the editing foundation.

- Define a versioned private library entry with label/tags, exact model/firmware, instance/record shape, selected channel where applicable, source capture hash and provenance. Reuse complete donor captures; a template does not claim factory defaults.
- Add offline import, list/search, inspection and apply planning. Show the actual footprint: channel copying preserves scene assignments; whole-block copying includes its documented assignments. Editor-native library file interchange needs its own format evidence and is not required for the first toolkit library.
- Add explicit channel swaps derived from both original rows in one immutable baseline. Let the caller choose whether scene channel references stay unchanged or follow the original settings. State this limited guarantee without promising identical sound where other channel-dependent behavior is unresolved.
- Start with supported exact instances and record shapes. Cross-instance remapping needs separate identity/shared-modifier evidence. Keep private captures and user labels outside distributable artifacts.
- Expose the same core services through CLI/MCP and ordinary review, execution and undo.

Acceptance: reject tampered/mismatched entries; verify complete copied rows and preservation of unrelated channels, modifiers, names, routing, Scene MIDI and saved default scene. Test both scene-reference policies across all definitions. Swaps are non-idempotent: prevent replay of a completed receipt, recognize identical-row no-ops, and require fresh exact review for a newly requested swap. Physical qualification remains separately scoped.

### 2 Tempo, tuner and looper controls — bounded implementation in 0.36.0

**Software implementation delivered:** [contracts and source qualifications](performance-controls.md), [fake-transport checks](../tests/Fractal.Checks/PerformanceControlChecks.cs), [validation record](performance-controls-validation.json). CLI/MCP capture, exact preparation and execution use the shared typed scope. Tempo queries require explicit review, tuner and button events remain sent-unverified, and unmapped loaded tempo changes stop for reconciliation. FM3/FM9 command adaptation and all physical behavior remain unqualified. Preset organization is delivered in 0.37.0; setlists/songs offline investigation is delivered in 0.38.0.

Source E4 publishes commands for Axe-Fx III. [DeviceNotifications](../src/Fractal.Core/DeviceNotifications.cs) already decodes related state messages; [LiveControl](../src/Fractal.Core/LiveControl.cs) and [EditorCommand](../src/Fractal.Core/EditorCommand.cs) provide the typed execution pattern. Active controls and bounded correlated queries are implemented; source/firmware and physical limits remain explicit.

- Add bounded tempo read/set and explicit tuner enable/disable, then looper state queries and scoped actions. Treat tap tempo as an intentional event, separate from setting a BPM value.
- Establish each model's command/response forms, valid values and verification capability. An Axe-Fx III document alone does not qualify FM9/FM3; a missing profile blocks only that operation.
- Distinguish absolute states from button events. Looper Record/Play/Undo/Once/Reverse/Half-speed and tap events cannot be blindly retried after uncertainty. Query before/after where supported; otherwise retain explicit verification limits.
- Use existing ownership and authorization contracts without arbitrary MIDI/SysEx entry points. Define transient versus preset-stored tempo behavior explicitly; live control does not implicitly Save.
- Respect the documented MIDI Out transport limits for tuner/tempo push streams rather than assuming USB observation.

Acceptance: fake-transport checks cover reply correlation, timeouts, cancellation, stale state, once-only events and absolute-state no-ops. A sent event is not reported as a verified state change without evidence. Publish per-model software support and separate physical results.

### 3 Preset organization — bounded implementation in 0.37.0

**Software implementation delivered:** [contracts](preset-organization.md), [independent fake-transport checks](../tests/Fractal.Checks/PresetOrganizationChecks.cs), [validation record](preset-organization-validation.json). Immutable rename/copy/import, explicit replacement-based move, swaps and closed permutations lower to existing guarded batches. Sources/destinations are retained and rechecked; copied default scenes are preserved, progress is non-atomic, and receipt-based undo accepts only verified eligible targets. Hardware relocation, external reference updates, cross-model conversion, bank-file interchange and uncertain-write resume remain outside this bounded delivery. Setlists/songs offline investigation is delivered in 0.38.0.

Source E5 describes previewed preset copying/import/export/reordering; E2 documents naming and storing at another location.

- Add rename and explicit source-to-destination copy first, then move/swap and reviewed multi-target reordering. Bind exact source/destination captures and hashes, device context and overwrite intent.
- Reuse inventories, comparison, complete-image upload/verification and durable batch patterns. Extend source/destination addressing explicitly; existing same-target edit/restore receipts do not authorize relocation.
- Retain every source and destination original before writing. Derive overlapping moves/swaps from immutable captures. Define what occupies a moved-from slot before exposing move; do not invent an empty-preset image.
- Preview final locations, names, overwritten data and unresolved external references. Moving a preset does not automatically rewrite songs, footswitch assignments or MIDI mappings; reference updates need their own supported scope.
- Retain per-target progress and fresh-state reconciliation. Multi-preset organization is not atomic: distinguish completed, uncertain and untouched destinations. Undo must refuse later changes and restore only eligible recorded targets.
- Stage same-model preset imports through ordinary integrity and destination checks. Cross-model conversion and additional bank-file formats remain separate extensions.

Acceptance: test naming limits, destination conflicts, overlapping permutations, no-ops, stale bases and interruption at each target boundary. Independently verify destination contents and untargeted saved slots. Qualify rename/relocation and recovery under separately authorized hardware scope.

### 4 Setlists and songs — investigation and offline planning delivered in 0.38.0

**Format follow-up, 15 October 2026:** [official-editor export and System backup investigation](setlists-system-format-research.md), with [hashed evidence](setlists-system-format-research.json). All three own-model editor packages provide `.fas-songs` / `.fas-setlist` identifiers and matching partial XML construction. No actual export or complete System backup was available; numbering/sentinels, import compatibility, System framing/integrity and recovery remain unqualified. Next obtain the small provenance-complete sample set in that assessment and compare it offline. No codec or live access is authorized by this follow-up, and no broad parameter-mapping campaign is needed.

**Delivered:** [requirement-to-source/code map and native proposal contract](setlists-songs.md), [independent checks](../tests/Fractal.Checks/SetlistSongChecks.cs), [validation record](setlists-songs-validation.json). Offline songs/sections/ordered sets share CLI/MCP validation against complete or partial inventories. DEFAULT stays symbolic, repeated songs are retained, missing dependencies are explicit and applicable firmware bounds are checked. **No-go for live system execution and editor interchange:** complete codecs, addressing, unrelated-system preservation and verified recovery are missing. The bounded evidence gate is recorded in the assessment; exhaustive mapping remains optional.

Source E6 documents songs/sections/set ordering, editor import/export and model-specific firmware applicability. These records are system data; changes take effect without a preset Save.

- Establish contained representations, read/export support, write addressing, limits, immediate persistence and system backup/restore semantics. Editor behavior alone does not establish its file or wire format.
- Define offline song/section/set planning against a supplied inventory. Validate names, ordering, preset/scene references and limits; retain explicit default-scene references and unresolved dependencies.
- Deliver a toolkit-native offline planner first if useful. Do not describe its output as editor-importable or device-executable without format evidence.
- Add interchange or live execution only once that path has complete preservation and qualified system-data recovery. Reuse coordination/review while keeping system persistence distinct from preset Save/Reload.

Acceptance: produce a requirement-to-source/code map and a bounded go/no-go decision for live execution. Test applicable model/firmware contexts and missing references offline. An unresolved storage path leaves an honest offline tool or a documented deferred item, without triggering exhaustive parameter research.

## Practical follow-on candidates

These remain candidates after the active sequence, with no assigned order. They do not block the active milestones.

| Candidate | Evidence gate and implementation boundary |
| --- | --- |
| Scene copy/swap within one preset | E1/E3 document scene operations. Existing [permutation analysis](../src/Fractal.Core/ScenePermutationAnalysis.cs) locates only known fields. Establish all affected scene data, including Scene Controllers/output levels, or a verified native operation; define name/default-scene policy. Never call a partial known-field copy complete. Cross-preset scene copying remains outside scope. |
| Native channel reset | E2/E3 describe reset. Establish the command, type/default dependencies, hidden/shared effects, readback and restoration so the device can supply its own defaults. Template application remains separately named. |
| Performance-page assignments | E3 describes bindings, labels and ordering. Establish assignment layout and target validity using known IDs or exact donor bindings; global and per-preset persistence require separate qualification. |
| User-cab management | E3 describes cab organization/import. Establish asset identity, transfer/address formats and verified asset backup/restore; preset backups are insufficient. |
| Footswitch-layout import/export | E1/E3 describe layout interchange. Establish file/system format, assignments and immediate persistence/recovery; keep it distinct from existing host-fed note/CC routing. |
| Global Blocks | E2 describes shared block operations on Axe-Fx III. Establish system storage and scope every affected linked preset. Do not extrapolate support to FM9/FM3. |

## Manufacturer sources and limits

Primary-source web text was reviewed on 14 October 2026. The initial candidate survey produced no new raw manual archive/hash or hardware validation. The later setlist/song investigation retrieved and hashed E6; no manual archive is redistributed and no hardware validation was added. Owner manuals retain their historical firmware scope; current editor/support pages supplement them. Attempted standalone *-Edit guide links were unavailable. These sources motivate generalized operations; editor UI and private file formats are not toolkit contracts.

| ID | Source and locator | Scope |
| --- | --- | --- |
| E1 | [FM3 Owner's Manual](https://www.fractalaudio.com/downloads/manuals/FM3/FM3-Owners-Manual.pdf), July 2023 / firmware 7.x; printed pp53–54, 90 | Channel libraries/copying, scene copy/swap and layout interchange. |
| E2 | [Axe-Fx III Owner's Manual](https://www.fractalaudio.com/downloads/manuals/axe-fx-3/Axe-Fx-III-Owners-Manual.pdf), July 2022 / firmware 20; printed pp41, 44–45, 94–95 | Naming/location, reset, channel/library operations, scene copy and Global Blocks. |
| E3 | [FM9 Owner's Manual](https://www.fractalaudio.com/downloads/manuals/FM9/FM9-Owners-Manual.pdf), August 2022 / firmware 3.x; printed pp41, 50–51, 84, 108, 114–115 | Reset, copying, layout interchange, cabs and performance assignments. |
| E4 | [Axe-Fx III MIDI for Third-Party Devices](https://www.fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf), PDF pp2–4 | Commands 0F/10/11/14 and push-stream transport limits. Published Axe-Fx III scope. |
| E5 | [Installing Factory Presets](https://support.fractalaudio.com/en-US/installing-factory-presets-1410465), “Using Manage Presets to Relocate Favorites” | Previewed copy/import/export/reordering. |
| E6 | [Setlists & Songs Mini Manual](https://www.fractalaudio.com/downloads/manuals/fas-guides/Fractal-Audio-Setlists-Songs-Mini-Manual.pdf), version 1.04, 30 October 2023 | Structure, firmware applicability, editor interchange and system persistence. |

## Optional backlog

**Exhaustive mapping is deferred.** An all-block/all-setting/default catalogue across models and firmware is optional research, not a completion criterion. Retain existing supported mappings and add individual fields only for concrete tasks with independent evidence. Broad modifier display laws, type/default rosters and additional layouts remain qualified gaps in the audit.

**Whole-preset authoring is deferred.** The concept, contracts and authoring sequence below preserve the earlier design for possible future scheduling. They are not dependencies of the active milestones. Video extraction, automatic tone selection, cross-model conversion, firmware updates, global topology optimization and general selective audio undo also remain optional extensions.

### Optional authoring concept

The whole-preset concept in this section is retained for possible future use. It is not the first active delivery or a dependency of the practical milestones above.

Start with one preset and a small connected signal path containing a parallel reverb branch. The caller supplies the baseline, exact block instances, placement, reverb type/channel/settings, scene assignments, destination and persistence intent.

Use complete same-model/firmware templates for new instances and unmapped channel settings. An explicit template is an initialization source, not a claim of factory defaults. Scoped edits preserve unspecified values. Reconstruction requires complete supplied settings/templates or qualified defaults; unresolved required values remain non-executable. Broad default catalogues are not a prerequisite for a fully supplied, template-based first delivery.

Verify the complete live result and, when saving is requested, saved and reloaded state. Qualify actual signal flow separately from structural route readback. The first delivery covers only its published operation/profile matrix, not arbitrary presets or sound matching.

Excluded from this first authoring delivery are video downloading/OCR, audio tone matching, cross-model conversion, firmware updates, automatic IR installation, multi-preset specification generation and a graphical editor. Existing saved-edit batches remain available. General selective audio undo after later edits is excluded; the existing selective Scene MIDI-only undo remains supported. Required external assets are explicit dependencies.

## Coverage map at 0.37.0

All common operations use FM9, FM3 and Axe-Fx III definitions with explicit 512/1024 Axe-Fx III capacity. Synthetic coverage and physical qualification are separate. A missing field/profile blocks that operation, not unrelated supported operations on a model.

| Requirement and disposition | Existing code and regression coverage | Reuse / optional authoring evidence boundary |
| --- | --- | --- |
| Saved preset organization: implemented | [PresetOrganizationPlan](../src/Fractal.Core/PresetOrganization.cs), raw-name writer, guarded ordinary batches and completed-item receipts; [checks](../tests/Fractal.Checks/PresetOrganizationChecks.cs) | Rename/copy/import, explicit replacement-based move, swap/permutation, source/destination preservation and eligible partial/completed undo. Different default scenes have separate guarded pre-Save/post-Reload alignment. All hardware organization remains unqualified. [Validation](preset-organization-validation.json). |
| Bounded tempo/tuner/looper: implemented | [PerformanceControlScope/PerformanceProtocol](../src/Fractal.Core/PerformanceControl.cs), typed commands and common session/transport send limits; [checks](../tests/Fractal.Checks/PerformanceControlChecks.cs) | Shared CLI/MCP exact reviews. Tempo queries are explicit, tuner/button effects sent-unverified, and unmapped loaded changes stop for reconciliation. FM3/FM9 adaptations and all hardware behavior remain unqualified. [Validation](performance-controls-validation.json). |
| Model context, correlated reads and backups: reuse | [DeviceDefinition](../src/Fractal.Core/DeviceDefinition.cs), [ReadSession](../src/Fractal.Core/ReadSession.cs), [SnapshotExport](../src/Fractal.Core/SnapshotExport.cs); [base checks](../tests/Fractal.Checks/Program.cs), [inventory checks](../tests/Fractal.Checks/InventoryChecks.cs) | Bind specification to exact model/firmware/capacity and integrity-checked baseline. [Read qualification](hardware-validation.md) does not qualify new editing operations. |
| Saved semantic reads and discovery: extend | [PresetEditProjection.Decode](../src/Fractal.Core/PresetEditProjection.cs), [EditReadCatalogue.Capabilities/Parameters](../src/Fractal.Core/EditReadCatalogue.cs); [read checks](../tests/Fractal.Checks/EditReadChecks.cs) | Reuse named fields, scene state and routing projections; add only missing required semantics. Preserve unknown fields and explicit limitations. [Read-profile evidence](editing-read-profiles-validation.json). |
| Parameter scales, exact witnesses, Scene Ignore and modifiers: extend | [BuiltinScales](../src/Fractal.Core/BuiltinScales.cs), [SavedCalibration](../src/Fractal.Core/ParameterCalibration.cs), [SavedModifiers](../src/Fractal.Core/SavedModifiers.cs); [scale checks](../tests/Fractal.Checks/BuiltinScaleChecks.cs), [calibration checks](../tests/Fractal.Checks/CalibrationModifierChecks.cs) | 59 built-in block fields are scoped to Axe-Fx III 32.02. Exact witnessed values and templates are implemented; broad defaults, display laws and additional model/firmware profiles remain gaps. [Scale](builtin-scales-validation.json), [calibration](calibrated-authoring-validation.json) and [modifier witness](modifier-calibration-validation.json) records retain distinct limits. |
| Compound editing, templates, scene names/channel/bypass and insertion/removal: reuse | [PresetEditRequest/PresetEditPlan.Create](../src/Fractal.Core/PresetEditPlan.cs); [edit checks](../tests/Fractal.Checks/PresetEditChecks.cs) | Compile a specification into existing operations. New-instance initialization already uses complete templates. Type changes use explicit preservation policy rather than assumed reset/default behavior. [Editing qualification](preset-editing-validation.json). |
| Connected grid, splits/merges, shunts and special paths: reuse | [PresetEditPlan](../src/Fractal.Core/PresetEditPlan.cs), [SpecialRouting](../src/Fractal.Core/SpecialRouting.cs), [AutomaticRouting.Create](../src/Fractal.Core/AutomaticRouting.cs); [audio routing](../tests/Fractal.Checks/AudioRoutingChecks.cs), [special routing](../tests/Fractal.Checks/SpecialRoutingChecks.cs), [path checks](../tests/Fractal.Checks/PathAndListenerChecks.cs) | Explicit graphs and automatic forward paths already exist. Compose them without inventing global I/O settings, DSP estimates or audibility. [Routing](audio-routing-validation.json) and [path](path-listener-validation.json) records separate software from hardware. |
| Combined Scene MIDI insertion/configuration and clearing: reuse | [PresetEditPlan](../src/Fractal.Core/PresetEditPlan.cs); [compound MIDI checks](../tests/Fractal.Checks/CompoundMidiChecks.cs) | Reuse exact row ownership, occupied-row policy, live-table verification and preservation. Do not introduce a second Scene MIDI planner. [Editing record](preset-editing-validation.json). |
| Upload, preservation, Save/Reload and complete saved receipts: reuse | [PresetEditExecution.RunAsync](../src/Fractal.Core/PresetEditExecution.cs), [PresetRestore](../src/Fractal.Core/PresetRestore.cs); [edit checks](../tests/Fractal.Checks/PresetEditChecks.cs) | One ordinary compound transaction already exists. Bind authoring metadata to its plan/result; do not add intermediate saves or a replacement executor. [Editing](preset-editing-validation.json) and [restore](preset-restore-validation.json) scopes remain separate. |
| Undo, uncertain-state reconciliation and persistent recovery: reuse with composition checks | [PresetEditExecution.PrepareUndo/ReconcileAsync](../src/Fractal.Core/PresetEditExecution.cs), [SceneMidiRecoveryStore](../src/Fractal.Core/SceneMidiRecovery.cs), [SceneMidiSelectiveUndo](../src/Fractal.Core/SceneMidiSelectiveUndo.cs); [recovery](../tests/Fractal.Checks/SceneMidiRecoveryChecks.cs), [selective undo](../tests/Fractal.Checks/SelectiveUndoChecks.cs), [edit checks](../tests/Fractal.Checks/PresetEditChecks.cs) | General edits already retain checkpoints and support query-only reconciliation and completed-result undo. Scene MIDI recovery migration/selective undo have narrower contracts; do not assume they implement resumable audio authoring. [Recovery](scene-midi-recovery-validation.json), [migration](scene-midi-recovery-migration-validation.json), [selective undo](selective-scene-midi-undo-validation.json) records. |
| Saved-edit batches and inventories: reuse when needed | [PresetEditBatch](../src/Fractal.Core/PresetEditBatch.cs), [PresetInventory](../src/Fractal.Core/PresetInventory.cs), [InventoryAnalysis](../src/Fractal.Core/InventoryAnalysis.cs); [batch](../tests/Fractal.Checks/BatchAndMidiChecks.cs), [inventory analysis](../tests/Fractal.Checks/InventoryAnalysisChecks.cs) | Multi-target execution, progress and inventory comparison already exist. First specification delivery remains single-preset. [Batch](batch-midi-validation.json) and [inventory](preset-inventory-validation.json) qualification. |
| CLI/MCP discovery, planning, review, execution and inspection: extend | [CLI](../src/Fractal.Cli/Program.cs), [MCP tools](../src/fractal_mcp/tools.py); [MCP tests](../tests/test_mcp.py), [CLI pipeline](../scripts/check_cli_pipeline.py), [MCP pipeline](../scripts/check_mcp_pipeline.py) | Add specification-facing endpoints only; existing edit reviews, exact trusted-host approval, execution, undo and reconciliation remain authoritative. [MCP](mcp-validation.json) and [client](claude-desktop-validation.json) evidence. |
| Whole-preset semantic specification/resolution: new | No implemented `PresetSpecification` or authoring compiler was found in the current core | Define the additional schema and deterministic compilation into existing edit plans. Do not create a parallel parameter catalogue, transaction framework or transport. |

Keep Fractal source, fixtures and build inputs contained in this repository. Protocol changes follow explicit versioned source releases with licence, notices, source revision and hashes. Normal builds must not fetch another checkout or protocol feed.

## Optional authoring implementation and evidence gaps

| Gap | Bounded next work |
| --- | --- |
| Whole-preset specification and resolution | Versioned description of desired instances, graph, channel settings, scenes, baseline intent, initialization sources and assets; deterministic lowering into `PresetEditRequest`/`PresetEditPlan`. |
| Initial recipe's unresolved fields/defaults | Identify required reverb settings and source each from existing named descriptors, exact calibration witnesses or complete templates. Add a mapping/default profile only where those paths cannot meet an explicit requirement. |
| Broader model/firmware catalogues | Extend exact scales, type rosters and qualified defaults independently per model/firmware. Unknown enum availability, reset dependencies and DSP cost remain visible. |
| Modifier and Scene Ignore semantics | General Scale/Offset display laws, Attack/Release timing, Damping Type labels, additional layouts and multiword Scene Ignore remain unresolved. Existing exact witnesses, normalized words and donor selector copying stay available. |
| Asset and external-topology dependencies | Represent required IR identity, global I/O assumptions and external cables without inferring identity from a slot number. Upload/global configuration remain separate future operations. |
| Composed verification and physical qualification | Add specification-level independent expected fixtures and failure cases, then qualify the selected complete workflow separately. Existing FM9 passes do not qualify the parallel-reverb workflow or other models. |

Network peer interoperability/journal recovery, complete unsolicited-change semantics, controller feedback programming, automatic effect selection and global topology optimization remain separate audit workstreams, not prerequisites for a fully specified local single-preset authoring request. See the [remaining audit](capability-audit.md#remaining-implementation-and-evidence-work).

## Optional authoring contracts

These are proposed authoring contracts, not current API names. Prefer additional fields/wrappers around existing contracts; settle exact public names and migration rules before implementation.

| Proposed contract | Required additions / existing reuse |
| --- | --- |
| `PresetSpecification` | Schema version; exact device context; baseline hash and create-from-baseline/scoped-edit intent; exact instance keys/cells; directed graph; per-channel type/settings; scene assignments; persistence intent; initialization policy and template/witness references; required assets and unresolved observations. Preserve the baseline saved default scene initially; a requested change needs a separately supported operation. |
| Catalogue resolution | Extend `EditReadCatalogue`, `BuiltinScales` and `SavedCalibration` with required semantic lookup/provenance/default information. Distinguish editor IDs, saved offsets and display scales; do not create a competing `ParameterCatalogue`. |
| `PresetAuthoringPlan` | Specification and resolution-evidence hashes; existing edit plan/bundle identity; resolved operations/effective values; explicit, template-supplied, qualified-default or unresolved value origins; conflicts, preservation and verification requirements. |
| `PresetAuthoringResult` | Existing `PresetEditExecutionResult` and receipt reference; specification/plan binding; independently checked intended semantics; observed saved/result hashes, persistence state and qualification limits. Never emit success for an unresolved or uncertain workflow. |

Use one-based grid coordinates, scenes 1–8 and public channel labels where supported. Keep zero-based device preset slots distinct from display offsets. Reject unknown units, non-finite values, ambiguous aliases, duplicate instances and unsupported channels instead of coercing them.

Defaults are explicit: preserve unspecified values for scoped edits, use a complete caller-selected template for supplied initialization, or resolve a requested default against qualified exact-firmware evidence. Missing values are never implicitly zero. State whether a default refers to block initialization or the selected effect type; a template or type ID alone does not prove factory defaults.

Optional tutorial/frame references stay in private client artifacts. Public examples are neutral synthetic fixtures or explicitly approved data with provenance. A specification, plan, receipt or calibration cannot authorize its own device execution.

## Optional authoring delivery sequence

### 1 Define and validate the authoring specification

If whole-preset authoring is scheduled, use the coverage map above to define only the additional contracts and acceptance fixtures needed by its selected workflow.

- Finalize the minimal schema using existing device, baseline, catalogue, conflict, plan and result contracts.
- Define exact instance/graph/channel/scene references, preserve-versus-initialize behavior, persistence and asset dependencies.
- Inventory only the first recipe's required settings against the coverage map. Choose explicit templates where defaults are unavailable; report any unresolved named field without inventing an encoding.
- Add neutral series and parallel-reverb specification examples for all four model/capacity definitions. Distinguish non-executable drafts from complete executable requests.

Acceptance: offline validation rejects incomplete/ambiguous executable requests, preserves unresolved observations in drafts and opens no MIDI ports. The first recipe has a field-by-field resolution/verification matrix. Any required core refactor is justified by a concrete gap in existing services.

### 2 Close the first recipe's evidence gaps

- Reuse existing saved projections, own-model descriptors, built-in scales, calibration witnesses and complete templates.
- Add only missing required read/write mappings, units, quantization, type/reset semantics or qualified default initialization.
- Use the [reading map](../knowledge-base/reference-map.md) and [mapping research](parameter-mapping-research.json) before new research. Record source revision, hashes, licence, model/firmware and authority.
- Retain opaque fields and unsupported layouts. Do not infer a saved offset from an editor parameter ID or borrow a catalogue across models.

Acceptance: independent expected fixtures check every new mapping; wrong model/firmware/context and unknown sentinels are refused. A fully supplied template-based recipe can proceed without a broad default catalogue. Requests that specifically require unresolved factory defaults cannot execute.

### 3 Compile specifications into the existing planner and expose previews

- Resolve semantic inputs and evidence against the exact baseline, then lower them into existing typed edit operations.
- Reuse connected routing and `AutomaticRouting` only for explicitly requested path policy. Validate collisions, known instance limits and feedback intent; leave unknown DSP cost explicit.
- Order template initialization, type/value edits, graph edits and scene assignments according to their dependencies and qualified side effects. Check what the existing planner supports before extracting any new boundary.
- Produce deterministic text/JSON previews of effective values and their origins, channel-sharing consequences, graph changes, conflicts, assets and preservation.
- Add CLI/MCP specification planning/inspection entry points through the same core compiler. Reuse existing edit preparation/review rather than defer adapter integration until after hardware testing.

Acceptance: independently expected plans across all definitions; repeat-request no-ops, stale-base and occupied-cell conflicts, preserved unrelated data, and no executable output for unresolved assets/operations. Provider-independent equivalent specifications produce identical plans. Reopened bundles revalidate specification/evidence binding as well as the existing edit result.

### 4 Verify composed execution and recovery

- Execute through `PresetEditExecution` and the existing coordinated transport, complete-image verification and one-Save/Reload path. Retain edit-buffer-only mode separately.
- Bind authoring metadata to immutable existing plans/receipts; extend payloads only where the specification cannot otherwise be revalidated. Preserve old readers and narrow prior-release compatibility rules.
- Test complete intended semantics and unaffected raw records, names, scenes, MIDI tables and opaque bytes using independent expectations. Retain existing narrow compression/CRC equivalence rules.
- Exercise failures at upload, Save, Reload and evidence-write boundaries, query-only reconciliation, completed-result undo and later-edit refusal. Stop partial/uncertain state without automatic write replay or rollback.

Acceptance: saved success requires complete saved/reloaded verification; no-op execution does not mutate. Exact authorization and stale-review checks apply through the existing host path. Incomplete/live-only results cannot become completed saved receipts. No new generic transaction or resumable audio-recovery engine is required unless a demonstrated gap makes it necessary.

### 5 Qualify the first complete hardware workflow

Use a separately authorized caller-selected device, target, type/channel/settings, placement, scenes and persistence intent. Confirm a supported restore path and capture an integrity-checked baseline first.

1. Build the small connected path and parallel reverb branch using complete explicit initialization or qualified defaults.
2. Independently inspect settings, scene assignments and routing on the device/editor; use a signal/audio check for claims that the route passes signal.
3. Verify live, saved and reloaded semantics, untouched channel settings, unrelated Scene MIDI and the saved default scene.
4. Repeat the request to establish a no-op, then explicitly restore the original and compare the complete image and all non-target saved slots.
5. Retain every rejection, partial result, failed restore and untested step; publish only generic redacted qualification evidence.

Acceptance: publish the exact model/firmware/operation scope. Existing FM9 12.00 scene-name/cable, Amp parameter, modifier-source and exact-witness modifier passes remain their own evidence. FM3/Axe-Fx III physical execution, the new parallel-reverb workflow and in-flight upload/Save interruption remain unqualified until tested. Synthetic coverage never establishes hardware compatibility.

### 6 Expand coverage after the bounded delivery

- Extend Amp, Cab, Drive, Delay, modifiers and other settings through the same resolution, independent verification and qualification gates. Existing supported operations remain enabled.
- Add broader defaults/rosters and model/firmware display scales when evidence permits; do not require every catalogue to be complete before releasing a bounded operation.
- Add an AI-client recipe for extracting final values, resolving uncertainty, selecting explicit initialization, previewing and inspecting results. Keep video extraction outside the deterministic core.
- Reuse existing saved-edit batches for future multi-preset specifications; track asset upload, global I/O editing and cross-model conversion separately.

Acceptance: publish a supported-operation matrix with software, source and hardware evidence distinguished. No provider SDK/account is required for planning, builds or tests.

## Shared source organization and review

Implement active practical services in `src/Fractal.Core` with CLI/MCP adapters delegating to them. Preserve existing request schemas/planners and immutable bundles; version additional contracts explicitly. Add an authoring specification/resolver/compiler only if that optional backlog item is scheduled.

Keep reusable attributed catalogue data under `knowledge-base/catalog`, schema/procedure documentation under `docs`, and neutral synthetic fixtures/composition checks under `tests/Fractal.Checks`. Retain complete private captures/witnesses only in private artifacts. Extend the opt-in hardware harness separately.

Review unresolved encodings, default/reset side effects and any write/recovery-path changes independently. Review the first combined authoring workflow before physical qualification. Development model/provider choices are not toolkit dependencies.

## Validation and completion gates

Retain regression coverage for reused services; add tests for new semantics and composition boundaries. Cover all model/capacity definitions, invalid/missing values, unit/enum handling, channel sharing, initialization effects, collisions, unsupported profiles, missing assets, repeat runs, stale baselines, unrelated-byte preservation, cancellation, uncertain writes, undo and later edits.

Run checks when their layer changes:

```text
python -m unittest discover -s tests -v
python scripts/check_cli_pipeline.py
python scripts/check_mcp_pipeline.py
python scripts/validate_knowledge.py
python scripts/protocol_release.py
```

Planner changes require Python tests; runtime changes require the CLI pipeline; adapter changes require the MCP pipeline; knowledge changes require knowledge validation. Deliberately update/check the contained protocol release manifest after reviewed source changes using its supported workflow. Verify self-contained builds without fetching another repository. Hardware testing is separate from normal CI.

Each active milestone can ship independently when its bounded contract and checks pass, with remaining physical limits stated. Events, swaps and moves need explicit replay rules rather than a blanket assumption of idempotence. Completing the practical sequence does not require an exhaustive parameter catalogue or a whole-preset authoring compiler.

If optional authoring is scheduled, its first delivery is complete when a fully specified supported single-preset request can be previewed, executed, independently verified after the requested persistence mode, repeated without unnecessary writes and explicitly restored. Conflicts and partial failures remain visible. Wider field/default coverage, other-model physical qualification and client video support are independent follow-up work.
