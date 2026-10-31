# Local MCP interface

Release 0.38.0 adds [offline songs and setlist proposals](setlists-songs.md) against retained complete or partial inventories through shared CLI/MCP. DEFAULT stays symbolic and missing dependencies are reported. Editor interchange and live system execution are deferred pending complete codec, preservation and recovery evidence; no hardware qualification is added.

Release 0.20.0 adds `fractal_plan_audio_path` for ordinary executable path bundles and `fractal_listen_controller` for bounded input-only controller observations. See [path planning](automatic-audio-paths.md) and [listener contracts](controller-listener.md). Existing live-access configuration, exact host approval and hardware-evidence boundaries remain in force.

Release 0.12.0 exposes 26 typed tools and retained references through a local standard-input/output MCP server. FM9, FM3 and both Axe-Fx III capacities use the same adapter. It calls the contained planners and .NET runtime; the server introduces no new MIDI or preset encoding. [Measured validation](mcp-validation.json) distinguishes adapter tests from earlier physical device qualification.

Use `fractal_plan_block_removal` and `fractal_prepare_removal` for exact isolated instances, or `fractal_prepare_insertion_undo` for a verified insertion receipt. [Removal and undo](block-removal-execution.md) preserve all other settings and require fresh exact approval; connected routing is refused.

For a completed saved general-edit receipt containing only existing Scene MIDI message edits, `fractal_plan_selective_scene_midi_undo` accepts `execution_artifact`, `backup_artifact`, `slot` and optional batch `item_index`. It produces a new ordinary edit bundle that preserves unrelated later settings; use `fractal_prepare_preset_edit` for its execution review. [Selective undo](selective-scene-midi-undo.md) explains exact row ownership and conflicts. Planning opens no MIDI ports.

For a completed item in the persistent Scene MIDI recovery store, `fractal_plan_selective_recovery_undo` accepts `recovery_artifact`, the exact current `state_sha256`, `backup_artifact` and `slot`. It revalidates the receipt read-only and produces the same standard edit bundle. Pending or uncertain items are not converted into completed receipts. This does not mark the old store undone; its later exact-state continuation will conflict after a separate selective edit.

`fractal_migrate_recovery` accepts a private prior-release `recovery_artifact` and its exact `state_sha256`. It copies revalidated schema-1 settled items and never-attempted pending items into a new current-release artifact without MIDI; interrupted items are refused. The source remains unchanged. A later recovery action needs a fresh exact review and operator approval; [migration limits](scene-midi-recovery.md) include other historical releases and states beyond the one retained terminal FM9 record.

## Install and start

For Claude Desktop, build the configurable extension with `python scripts/package_claude.py --destination dist/ai-toolkit-for-fractal-0.12.0.mcpb`. In Claude Desktop, use **Settings > Extensions > Advanced settings > Install Extension...**, choose the file, and complete **Configure**. Keep one active toolkit connection so tool selection and device ownership are unambiguous. No manually edited client JSON is required. Claude manages Python through MCPB's UV runtime; .NET 10 is required for contained helpers.

Configure exposes **Enable live MIDI reads** and **Enable MIDI write execution**. Both start enabled in this live-capable package. Set the device model, preset capacity and recorded firmware. **MIDI connection (optional)** defaults to **Automatic**: connect the Fractal device by USB and leave this alone. The toolkit lists driver connection names without opening ports, finds the unique receive/send pair for the selected model, and retains the resolved indices internally. Users do not enter port numbers.

If several connections match, setup refuses to guess. Enter the intended connection name shown in the error. For a separate MIDI interface, enter its shared connection name; common direction suffixes such as In/Out are removed for matching. Duplicate names remain ambiguous; make them distinct or disconnect the unused device. Automatic matching checks names for FM9, FM3 or Axe-Fx III, and rejects mismatched receive/send names. This name matching establishes a route, not device identity: live operations still verify identity/firmware through the existing runtime. Restart/reconfigure after changing USB connections so driver indices are resolved again. Live access needs a matching connected route; when setup needs attention, the server stays connected with device access disabled and knowledge tools available. Offline startup performs no enumeration.

Enter `FM9`, `FM3` or `Axe-Fx III` as the model; setup accepts uppercase/lowercase and common spacing/hyphen variants, then retains the canonical model name; FM9/FM3 capacity is 512, and Axe-Fx III capacity is explicitly 512 or 1024. Firmware accepts `major.minor`, for example `12.0` or `12.00`, with optional surrounding spaces or a leading `v`. The minor version is a version component: `7.2` becomes `7.02` internally. No firmware version is guessed, and beta suffixes remain unsupported. For read-only use, leave reads enabled and turn write execution off. For offline use, turn both switches off. Writes enabled with reads disabled is refused. Explicit numeric port overrides remain available to trusted command-line operators and synthetic packaging checks; they are absent from Claude Configure.

Claude's per-tool permissions control which calls the client sends. They do not replace the toolkit's operator-only approval: hardware execution still requires the exact local review approval described below. Starting the server or enabling either switch opens no ports and sends no hardware commands. Start a fresh chat and call `fractal_status` to check flags before a deliberately authorized device operation. The extension reports its own version in MCP initialization and status `release`/`extension_version`; status `toolkit_release` separately identifies the contained protocol/toolkit version used for immutable reviews and recovery. Generic MCP clients continue to report the shared toolkit release.

The wrapper stores the validated host settings in `${HOME}/.fractal-ai-toolkit/claude-desktop/server.json`, with private artifacts beside it. Use that exact file as `PRIVATE_CONFIG` for the approval/import CLI. Invalid settings do not replace the last valid file or reuse its live access. `fractal_status` reports `setup.ready: false`, a corrective message, requested access and effective disabled flags; knowledge/reference tools stay usable until Configure is corrected and the extension restarted. An extension update uses its own contained runtime while preserving artifacts. Reconfigure/restart the extension after changing access settings; an already-started call is not revoked mid-flight by a switch change. Artifacts belonging to a separate extension identity are not silently migrated.

The extension's manifest, wrapper and Python project descriptor live in `adapters/claude_desktop`; its package also contains the complete protocol source/build inputs, references and runtime. Build output excludes private device data. Verify with `python scripts/check_claude_package.py PACKAGE.mcpb`, or add `--uv PATH_TO_UV` to test the manifest's actual launch command. The provider wrapper does not imply additional model/firmware hardware qualification. See [MCPB's manifest specification](https://github.com/modelcontextprotocol/mcpb/blob/main/MANIFEST.md), [managed Python example](https://github.com/modelcontextprotocol/mcpb/tree/main/examples/hello-world-uv) and [Claude's extension installation guide](https://support.claude.com/en/articles/10949351-getting-started-with-local-mcp-servers-on-claude-desktop).

Use Python 3.10 or newer and the .NET 10 SDK to build. From this repository:

```text
python scripts/package_mcp.py --destination dist/mcp
```

The resulting folder contains the runtime binaries, complete protocol/adapter source, build inputs, checks, planners, knowledge base, references, examples, licence and notices. It can be moved independently of this checkout and PresetMaestro. Running it needs Python and the .NET 10 runtime; no Python packages, NuGet feed, AI account or other repository is needed. Existing package destinations are never replaced.

Copy [the offline configuration](../examples/mcp/server.offline.json) to a private location. Set `data_root` to a private artifact directory and `runtime_dir` to the package's `runtime` directory. Relative paths are resolved against the configuration file, independently of the launch directory. Keep that file outside distributed artifacts. Start with:

```text
python ABSOLUTE_BUNDLE_PATH/scripts/fractal_mcp.py --config ABSOLUTE_PRIVATE_CONFIG_PATH
```

Give those executable/argument values to an AI client's local stdio MCP configuration. [The launch example](../examples/mcp/client-launch.json) shows a common container format; the client's exact configuration surface must support launching a local subprocess. Replace both placeholder paths. Build before launching the server: standard output carries only newline-delimited JSON-RPC, never build logs. No server socket, remote tunnel or HTTP endpoint is provided in this milestone. A cloud-only client needs a separately implemented and qualified connection to this computer.

For live reads, the operator sets `allow_live_reads` to `true` and adds a `device` object with explicit `model`, `capacity`, recorded `firmware`, `input` and `output` MIDI port indices. Supported models are `FM9`, `FM3`, `Axe-Fx III`; capacities are 512 for FM9/FM3 and explicitly 512 or 1024 for Axe-Fx III. There are no port, preset or message defaults. `allow_writes` remains `false` until the operator deliberately enables execution. Both flags must be enabled for execution. Close other MIDI port owners before opening a device connection.

## Available tasks

| Task | Tools |
|---|---|
| Discover configured access and evidence | `fractal_status` |
| Import/search/inspect private block/channel library entries and plan their use | `fractal_import_template`, `fractal_list_templates`, `fractal_inspect_template`, `fractal_plan_template`; apply via ordinary preset-edit review |
| Plan/review/reconcile saved preset organization and eligible undo | `fractal_plan_preset_organization`, `fractal_inspect_preset_organization`, `fractal_prepare_preset_organization`, `fractal_prepare_organization_reconciliation`, `fractal_plan_preset_organization_undo`; execute via exact ordinary batch review |
| Retrieve reusable knowledge and qualified amplifier attribution | `fractal_search_knowledge`, `fractal_query_amp` |
| Reconnect to private evidence | `fractal_list_artifacts`, `fractal_read_artifact`, `fractal_inspect_attempt` |
| Identify/read the configured device, without navigation | `fractal_list_ports`, `fractal_inspect_device`, `fractal_capture_presets`, `fractal_read_scene_midi` |
| Retain a later explicit preset-load context, without sending Load | `fractal_capture_load_context` |
| Check or decode existing captures offline | `fractal_check_backup`, `fractal_decode_captures` |
| Plan explicit PC/CC messages or chosen-cell insertion offline | `fractal_plan_scene_midi`, `fractal_plan_block_insertion` |
| Plan isolated removal or undo a verified insertion | `fractal_plan_block_removal`, `fractal_prepare_removal`, `fractal_prepare_insertion_undo` |
| Plan selective undo of a completed saved Scene MIDI general edit | `fractal_plan_selective_scene_midi_undo`, then `fractal_prepare_preset_edit` |
| Plan selective undo of a completed Scene MIDI recovery item | `fractal_plan_selective_recovery_undo`, then `fractal_prepare_preset_edit` |
| Migrate a compatible prior-release recovery record offline | `fractal_migrate_recovery` |
| Freeze and validate concrete execution reviews, without MIDI | `fractal_prepare_scene_midi`, `fractal_prepare_insertion`, `fractal_prepare_load`, `fractal_prepare_recovery` |
| Inspect a retained recovery record offline | `fractal_inspect_recovery` |
| Start, poll or explicitly stop an approved execution | `fractal_execute_review`, `fractal_inspect_attempt`, `fractal_cancel_attempt` |

Tool discovery supplies closed input schemas, ranges and required fields. Slots are zero-based; scenes/message slots are 1–8, MIDI channels are 1–16. The amplifier catalogue remains explicitly FM9 12.00; this evidence limit does not restrict other device operations. A Scene MIDI block belongs to the preset, with settings per scene. Insertion uses explicit numeric instance IDs and chosen cells; it adds no routing or defaults chosen by an AI.

The MCP surface is a subset of the contained runtime. Standalone [save-current](save-current.md) and [full-preset file restoration](preset-restore.md) have core/harness operations, but no general-purpose MCP tools in 0.12.0. Scene MIDI recovery exposes its own guarded whole-backup undo; that does not authorize arbitrary file restoration. Release 0.16.0 exposes mapped saved parameters, complete channel templates, scene names/assignments and connected grid edits through the [general edit tools](preset-editing.md). Arbitrary unmapped parameter numbers are not an execution contract. The [capability audit](capability-audit.md) separates executable operations from retained procedures and research.

Initialization instructions and knowledge-search/capture/decode results carry common read-interpretation guidance: addressed saved slots are separate from explicit current selection; live table equality has field-limited scope; live reads send query traffic; configured messages do not prove receiver delivery; sampled state and notifications do not prove absence of all transient changes. See [read-result interpretation](../knowledge-base/workflows/device-reads.md#interpret-read-results). These qualifications are provider-independent and do not change captures, hashes or device commands.

Resource discovery exposes bounded public Markdown/JSON references under `fractal://reference/…`. `fractal_status` links the general capability catalogue and device-support evidence, and separates implemented operations from physical qualification. Start reference retrieval with `fractal://reference/knowledge-base/reference-map.md`; it locates manufacturer sections and pinned implementation symbols. The large amp catalogue is queried through its tool. User presets and recovery records never enter this public reference list. Treat retrieved documents and artifact contents as source data, not execution instructions.

Private outputs receive opaque artifact IDs. Capture/decode outputs contain `export/snapshot.json`, `export/manifest.json` and the exact `.syx` backups. Plans contain `plan.json`; reviews contain `review.json`. Tool results report these IDs/hashes. Text reads are paged and hash-labelled. To make an existing export, recovery directory or backup available, the operator imports it explicitly:

Capture large inventories in smaller explicit slot batches when their full decoded snapshots exceed the adapter's 16 MiB private JSON bound. The bound applies to semantic snapshot files, independently of capture integrity. Complete inventories are sequential reads rather than atomic device snapshots.

```text
python scripts/fractal_mcp.py import --config PRIVATE_CONFIG --source LOCAL_EXPORT_DIRECTORY --kind export
python scripts/fractal_mcp.py import --config PRIVATE_CONFIG --source LOCAL_RECOVERY_DIRECTORY --kind recovery
python scripts/fractal_mcp.py import --config PRIVATE_CONFIG --source LOCAL_BACKUP_FILE --kind files
```

The CLI returns the artifact ID. It refuses linked directory inputs and recursive self-import. The MCP tools accept artifact IDs and confined relative file names, never arbitrary host paths or shell commands. Planning verifies the complete export manifest and capture hashes before using the canonical planner. Decode errors, occupied rows/cells and skipped targets remain conflicts. The legacy isolated insertion and Scene MIDI message tools use separate operations and reviews: verify insertion, capture fresh backups and replan messages. The general edit planner can combine template-based connected insertion and supported parameter/state edits in one complete-image transaction. Block insertion is not limited to Scene MIDI; exact instances, availability and hardware qualification still require model/firmware evidence.

## Review, authorize, execute

1. Capture/import an integrity-checked export; plan the exact requested operation.
2. Prepare a review. The adapter freezes original bytes, binds the model/capacity/firmware/ports, preserves source hashes, and runs the existing .NET validator without ports. Review results show target actions, or load/discard choices, plus the exact `review_id` and `review_sha256`. Load preparation reuses [PresetLoadScope](preset-load.md); insertion projects the retained saved-scene word and the .NET plan independently rechecks it.
3. The trusted local operator/host authorizes the concrete review within the user's requested scope. With writes deliberately enabled, invoke:

```text
python scripts/fractal_mcp.py approve --config PRIVATE_CONFIG --review-id REVIEW_ID --review-sha256 REVIEW_SHA256
```

4. Invoke `fractal_execute_review` with those exact values. It returns an accepted job promptly; acceptance is not successful completion. Poll `fractal_inspect_attempt` with the same `review_id` and `wait_seconds: 20` until `verified-complete` or `stopped-uncertain`. Never submit the execution again while polling. The server rechecks current configuration, frozen bytes and original source artifacts, acquires one cooperating MIDI lease, consumes the approval once, records the attempt, then calls the existing scoped executor. The actual executor still performs fresh device/base/unsaved-work checks and complete preservation/Save/Reload verification. A valid review cannot bypass those checks.

There is no MCP approval tool, arbitrary SysEx sender or Boolean argument that authorizes a mutation. Approval is a local host action, separate from model-controlled tool calls. Where the user has already explicitly authorized this exact action, a trusted host can invoke the approval CLI within that authorization; an additional user confirmation is not inherently required. An AI client's confirmation UI alone does not create the local approval record unless its trusted integration explicitly performs that action. Tool annotations are descriptive hints, not permission enforcement. Tools creating private artifacts are marked as local writes without destructive device effects; only execution has the destructive hint.

Approvals are consumed before runtime launch, even if later execution is uncertain. A second call cannot repeat the same review. Inspect the attempt/evidence instead. Attempt inspection returns the new artifact and, when present, its `recovery_artifact` and exact current `state_sha256`, including after interruption; callers need not discover the head in a long file listing. A verified terminal attempt includes its complete result. Each continuation operates on a private copy, preserving earlier evidence. Lost replies can be resolved with `fractal_inspect_attempt`, artifact listing and the updated recovery record. Resume/undo need a fresh explicit review/authorization; use [the durable recovery rules](scene-midi-recovery.md). Mutable import requires the same protocol version; compatible older records need explicit migration and are never silently upgraded.

## Cancellation and ownership

MIDI operations remain serialized by the shared ownership lease. Accepted execution jobs run under the local server while short status calls remain responsive. Cancelling a status call stops only that wait. To stop an accepted job at the user's request, call `fractal_cancel_attempt`; it signals the owned runtime and does not claim completion. Closing the server/client connection, output overflow or the two-hour child-runtime bound also stops owned work and retains available logs/checkpoints. A server restart never automatically resumes a job. A stopped write is uncertain until fresh readback reconciles it; no fields, Save, upload or rollback are automatically repeated. Terminating a child can interrupt an in-flight device operation. The original backups and flushed recovery checkpoints supply evidence, not a guarantee of completed restoration.

Cooperating MCP instances share an OS lease for each input/output port pair, including across private data roots. The native runtime owns one connection for its request; preparation opens none. Other applications and differently routed port aliases are external owners. Inventory reads and selection checks do not provide a device-wide lock. Full before/after execution inventories can take minutes. Execution acceptance and bounded polling avoid keeping a single tool request open for that duration without relaxing inventories, preservation checks or cancellation. This is a common toolkit job contract, independent of provider-specific MCP Tasks support.

## Verification and source locators

Normal checks run offline:

```text
python scripts/check_cli_pipeline.py
python scripts/check_mcp_pipeline.py
python -m unittest discover -s tests -v
python scripts/protocol_release.py
python scripts/validate_knowledge.py
```

The MCP pipeline relocates a complete bundle, uses real stdio and .NET helpers across all four model/capacity definitions, validates PC/CC, insertion, isolated removal and receipt-based insertion-undo reviews and recovery import, and proves disabled live execution. `tests/test_mcp.py` covers approval replay, source/port changes, path confinement, malformed frames, cancellation and child-runtime limits. Optional `scripts/check_mcp_sdk.py --config OFFLINE_CONFIG` uses the official Python SDK in a separate test environment; it is not a server/build dependency. The validation record states its actual tested version. Actual Claude hardware qualification is recorded separately in [client validation](claude-desktop-validation.json), including 0.12.0 isolated removal and insertion undo on FM9 12.00. Earlier operation records retain their independent release/model scope.

This adapter deliberately implements negotiated revisions `2025-11-25`, `2025-06-18` and `2025-03-26`, not the newer stateless draft. It advertises only tools and unsubscribed static resources. It has no sampling, elicitation, tasks, prompts or HTTP/authentication implementation. These are bounded implementation choices, not requirements imposed by MCP.

Retained primary sources:

- [Stdio transport](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports): UTF-8 newline framing, JSON-RPC-only stdout and stderr diagnostics.
- [Lifecycle](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle): initialize, supported-version negotiation and initialized notification.
- [Tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools): closed tool schemas, text/structured results, errors and annotation limits.
- [Resources](https://modelcontextprotocol.io/specification/2025-11-25/server/resources): URI discovery/read and capability declaration.
- [Cancellation](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/cancellation): request-ID cancellation and stopped processing.
- [Official Python SDK](https://github.com/modelcontextprotocol/python-sdk): independent client interoperability; its installation is confined to optional testing.

Local symbols: [server.py](../src/fractal_mcp/server.py) `Server.dispatch/serve/work`; [tools.py](../src/fractal_mcp/tools.py) `Toolkit.call/prepare/validate_review/approve/execute`; [storage.py](../src/fractal_mcp/storage.py) `Artifacts/confined/atomic_json`; [runtime.py](../src/fractal_mcp/runtime.py) `Runtime.run/device_owner/load_config`. [The release manifest](protocol-release.json) hashes the contained adapter separately from protocol and planner sources.

## Block names and instance lookup

`fractal_query_block` takes a required model plus `query` or `effect_id`, with optional recorded firmware context. It calls the contained core resolver offline and returns source-attributed identities, dated model instance limits and explicit unknown/unqualified results. The [catalogue](../knowledge-base/catalog/block-identities.json) is also a public resource. New captures/decodes name grid cells; old exports remain immutable and can be decoded again into a new artifact. This is distinct from `fractal_query_amp`, which maps amp model types. See [block identification](../knowledge-base/workflows/block-identification.md); names and June 2023 counts do not qualify parameters, DSP capacity or exact-firmware editing.

Release 0.17.0 adds private resumable inventory capture/resume, offline inspection/export/comparison, amp usage and explicit checkpoint migration; see [inventory tools](preset-inventory.md#comparison-amp-usage-and-migration). General editing adds block settings templates, combined Scene MIDI operations, eight additional type families and explicit feedback policy with special-path review. These use the same runtime and exact review contract.

Live tools: `fractal_read_amp_roster`, `fractal_capture_live_control_context`, `fractal_prepare_live_control` and `fractal_monitor_live_state` use the common runtime and existing exact trusted-host execution flow. See [contracts and evidence](live-tools.md). No new hardware qualification is implied.

Release 0.36.0 adds `fractal_query_performance_capabilities`, `fractal_capture_performance_context` and `fractal_prepare_performance_control` through the same exact review contract. Tempo queries require enabled writes and explicit review because of the qualified legacy query-as-set report. Events and tuner commands remain `sent-unverified` with `completion_verified: false`; their result remains readable. See [performance controls](performance-controls.md). No Save, replay or hardware qualification is implied.

Release 0.19.0 adds `fractal_prepare_edit_batch` and `fractal_prepare_batch_reconciliation`, plus optional `item_index` for completed-item undo. `fractal_capture_load_context` accepts explicit `midi_mapping`; `fractal_prepare_live_control` accepts explicit `midi_assignment`. `fractal_route_midi_events` maps finite note/CC input offline. See [batch contracts](batch-editing.md) and [configured MIDI routing](configured-midi-routing.md). These use the same trusted-host authorization and consumed-review rules; preparation never opens ports.

Block and Scene MIDI planning accept `placement_policy: "first-isolated-empty"` instead of coordinates. Concrete chosen locations are frozen before insertion review; [automatic placement](automatic-placement.md) does not invent an audio path or defaults.

[Network transport and notifications](network-notifications.md): operator configuration can replace device `input`/`output` with `network` (an explicit IPv4/control-port endpoint). Reviews and approvals bind that endpoint. `fractal_listen_device_notifications` captures bounded observations using live-read access, without MIDI queries or automatic edits. Runtime capabilities stay enabled with the existing access settings.


`fractal_read_block_parameters` takes one `effect_id`, uses configured live-read access and retains `parameters/parameters.json` plus raw frames. Passive notification capture now also decodes source-supported private parameter echoes and completed block tables. [Contracts and qualification](live-parameters.md).

`fractal_calibrate_saved_parameter` validates retained export witnesses offline. `fractal_plan_preset_edit` accepts calibration artifacts for exact block, Scene Ignore and modifier values, as well as modifier source changes and templates. Modifier calibration requires a slot and exact binding; it accepts only captured values in matching modifier and target-block context. These use existing exact execution reviews; no arbitrary offset or SysEx tools are exposed. [Contracts](calibrated-authoring.md).

[Built-in scale/curve authoring](builtin-scales.md) adds `builtin_scales` to parameter discovery and accepts `set-scaled-parameter` / `set-modifier-parameter` with signed `physical_value`. Exact Axe-Fx III 32.02 modifier Scale, Offset, Attack and Release use unit `saved-normalized`, not display units. `copy-modifier-selector` accepts a retained template artifact/slot and copies only the same-binding Damping Type raw word; its index labels are unqualified. Modifier edits require `expected_binding`. Preparation and execution reuse the existing exact review; neither discovery nor planning opens device ports.
