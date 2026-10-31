# AI Toolkit for Fractal

Current source release: [0.9.0 â€” late beta](docs/releases/0.9.0.md).

<img src="assets/branding/logo.png" alt="AI Toolkit for Fractal logo: an open toolkit with an effects routing grid" width="360">

Reusable Fractal/MIDI knowledge and deterministic automation for **FM9, Axe-Fx III and FM3**, independent of any AI provider. This repository is self-contained; no other checkout is needed to build or use it.

An independent community project, not affiliated with or endorsed by Fractal Audio. Fractal Audio and its product names belong to their respective trademark owners.

## Available now

- [Offline songs and setlist proposals](docs/setlists-songs.md), with symbolic defaults and explicit missing dependencies; editor interchange and live system execution remain deferred.
- [General preset editing and connected routing](docs/preset-editing.md), [saved inventories](docs/preset-inventory.md), [block identification](knowledge-base/workflows/block-identification.md) and [built-in saved scales and modifier curves](docs/builtin-scales.md).
- [Network transport and device notifications](docs/network-notifications.md), with physical interoperability qualified separately.

- [Private named block/channel templates and channel swaps](docs/template-library.md), with exact donor hashes, explicit scene-reference policies and ordinary verified edit execution.
- [Tempo, tuner and looper controls](docs/performance-controls.md), with exact review, finite events/queries, no Save/replay and explicit verification limits.
- [Preset organization](docs/preset-organization.md): immutable rename/copy/import, explicit replacement-based moves, swaps/reordering, guarded batches and eligible completed-target undo.

- [Capture-backed parameter setters, Scene Ignore and modifier authoring](docs/calibrated-authoring.md) with exact evidence and shared execution.

- [Mapped live block reads and private notifications](docs/live-parameters.md), with own-model editor names, raw values and separate hardware qualification.

- [Automatic audio paths](docs/automatic-audio-paths.md) through explicit grid waypoints, with ordinary edit execution and preservation checks.
- A bounded [standalone controller input listener](docs/controller-listener.md) that retains note/CC events and mapped intents without sending MIDI.

- [General saved-edit batches](docs/batch-editing.md) with per-target verification, durable progress, reconciliation and completed-item undo across all model definitions.
- [Configured MIDI routing](docs/configured-midi-routing.md): explicit bank/PC recall, assigned CC controls and host-fed note/CC mapping, with shared CLI/MCP contracts and separate hardware qualification.

- A contained **read-only .NET runtime and CLI**: identity, firmware, current preset/scene, live scene names, addressed saved-preset reads and offline capture decoding.
- Offline single-preset **backup integrity checking**, including opaque manufacturer-style headers, without assuming a restore destination.
- Normalized snapshots with saved names, routing grid, numeric Amp models, scene channel/bypass settings, complete Scene MIDI slots, retained raw records and integrity/hash manifests.
- A general **offline block-insertion planner** with caller-chosen cells, existing-instance reuse, routing preservation and model-specific grid bounds; see the [block contract](skills/fractal-fm9/references/block-insertion.md).
- A common **offline Scene MIDI PC/CC planner** for all three models, with capacity/capability validation, preservation and conflict reporting.
- A common [existing-block Scene MIDI executor](docs/scene-midi-execution.md), with exact planner/base checks, complete preservation, Save/Reload verification and explicit backup undo that refuses later edits.
- [Persistent Scene MIDI recovery](docs/scene-midi-recovery.md): private reloadable receipts, fresh-state resume and explicit batch undo, with later-edit protection and no blind replay.
- A common [isolated block-insertion executor](docs/block-insertion-execution.md) for explicit saved targets, preserving all existing data before Save and after Reload; current model/block evidence remains qualified.
- Typed experimental editor operations and an [empty-preset Scene MIDI pilot](docs/editing-pilot.md), with FM9 12.00 saved verification and exact recovery. The ordinary CLI stays read-only.
- An opt-in [full-preset restore path](docs/preset-restore.md): immutable temporary-buffer upload, exact source/base hashes, complete verification before Save, repeated saved readback and Reload. Hardware scope is kept in its [qualification record](docs/preset-restore-validation.json).
- [Isolated block removal and exact insertion undo](docs/block-removal-execution.md), with preservation and later-edit protection.
- [Explicit preset loading](docs/preset-load.md), including reviewed handling of unsaved differences, and [guarded saving](docs/save-current.md) of an already-loaded preset.
- [Source-attributed block-name lookup](knowledge-base/workflows/block-identification.md) and named grid exports, qualified by model and dated evidence.
- A [local MCP interface](docs/mcp.md) with typed tools, private artifacts, exact reviews, one-use host approval and execution polling/cancellation.
- A searchable [knowledge base](knowledge-base/README.md), [documentation reading map](knowledge-base/reference-map.md), [capability audit](docs/capability-audit.md) and portable [skill](skills/fractal-fm9/SKILL.md).
- A qualified amp lookup covering 336 recorded **FM9 12.00** models in 162 family/original groups. This catalogue is not reused for another model or firmware.

Live MIDI uses the **shared MightBeUseful.Midi 0.3.6 Windows legacy or macOS CoreMIDI backend** or the shared platform-neutral **Apple RTP-MIDI** initiator for an explicit network endpoint. Offline .NET decoding is portable; Python tools require only Python 3.10 or newer. The .NET 10 SDK and operating-system MIDI drivers are prerequisites. Pinned NuGet dependencies are included in `dependencies/midi`; restore uses only that local feed, with no build-time source downloads. See [shared MIDI integration](docs/shared-midi.md).

## Build and use

```powershell
dotnet build src/Fractal.Cli/Fractal.Cli.csproj
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll help
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll ports
```

Select the actual port indices from the listing, close other port owners, then deliberately invoke a read:

```powershell
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll snapshot --input INPUT --output OUTPUT --model MODEL --preset ZERO_BASED_SLOT --destination private/read-001
python scripts/plan_scene_midi.py --snapshot private/read-001/snapshot.json --all-populated --channel 1 --value 1 --block-row 3 --block-column 10
```

Port indices and preset slot are examples. Use `--model FM3`, or `--model "Axe-Fx III" --capacity 512|1024` with the actual capacity. Repeat `--preset` or deliberately choose `--all`. Commands never navigate, edit, save or restore. See the [runtime guide](docs/runtime.md) for inspection, offline decoding, export contents, errors and transaction behavior.

Try planning without a device:

```powershell
python scripts/plan_scene_midi.py --snapshot examples/fm9-snapshot.json --all-populated --channel 1 --value 1 --block-row 4 --block-column 10
python scripts/plan_block_insertion.py --snapshot examples/fm9-snapshot.json --preset 0 --effect-id 190 --row 3 --column 10
python scripts/search_knowledge.py "Scene MIDI default scene"
python scripts/query_amp.py --query "PortaBass" --json
```

The example is synthetic. Scene MIDI plans use the general block operation when insertion is needed, then propose settings across chosen scenes. Supply the chosen fallback row and column explicitly. Generic planning takes an exact caller-resolved instance ID and qualifies grid placement. The separate isolated insertion executor verifies actual device results; availability/default/DSP catalogues remain unimplemented. Plans contain no protocol bytes and grant no write authorization. The [snapshot contract](skills/fractal-fm9/references/snapshot-contract.md) defines addressing, source hashes and conflict policy.

## Evidence and limits

All three model paths and both Axe-Fx III capacities have synthetic regression and CLI pipeline checks. **FM9 firmware 12.00 has historical physical read-only validation**, covering saved slots, repeat/state checks and timeout/cancellation recovery. See the [hardware report](docs/hardware-validation.md). FM3 and Axe-Fx III remain physically untested. Historical write evidence and retained failures are recorded separately for each operation; see [device support](docs/device-support.md) and [evidence](docs/evidence.md). These records retain their original dates, versions and hashes and do not qualify the new 0.9.0 beta.

Signed native distribution instructions: [release packaging](docs/github-releases.md).

## AI clients and maintenance

Give any client [AGENTS.md](AGENTS.md), the relevant knowledge recipe and the [skill](skills/fractal-fm9/SKILL.md). The historical skill folder name retains compatibility; its normalized planner supports all three models and its FM9 references retain their own evidence scope. Clients can invoke the same CLI/Python tools without a ChatGPT account. The [local MCP adapter](docs/mcp.md) exposes the deterministic operations with private reviews and exact host approval; provider-specific packaging and remote/cloud transport remain separate.

Build a complete movable MCP bundle with `python scripts/package_mcp.py --destination dist/mcp`. For Claude Desktop, build the extension with `python scripts/package_claude.py --destination dist/ai-toolkit-for-fractal-0.9.0.mcpb`. Install through Extensions settings, then configure the device and read/write switches. Startup opens no MIDI ports. The extension contains its own toolkit/references and uses managed Python; .NET 10 is required for contained helpers. [Setup](docs/mcp.md) and [packaging validation](docs/claude-desktop-validation.json) retain the measured scope.

The [protocol release manifest](docs/protocol-release.json) records source lineage, modifications and contained source hashes. Update complete source releases deliberately and independently in each consumer; normal builds do not fetch another repository's code.

```powershell
python scripts/check_cli_pipeline.py
python -m unittest discover -s tests -v
python scripts/protocol_release.py
python scripts/validate_knowledge.py
```

Licensed under [Apache-2.0](LICENSE); see [NOTICE](NOTICE). Fractal Audio product names are descriptive. This project is not affiliated with Fractal Audio Systems.

## Application Test Lab

Portable dashboard integration and build instructions: [Test Lab adapter](docs/test-lab.md).

## Offline Windows Test Lab profile

Build an audited bundle without MIDI access:

```text
python packaging/build_test_lab.py --rid win-x64 --config packaging/test-lab-offline-windows.json --output artifacts/offline-windows
```

Every bundle uses fresh build outputs and records its selected test-source hashes in
`offline-policy.json`. All covers only the profile's offline Functional stages.
Native and interactive Desktop requests report unavailable; no hardware or MIDI
routing qualification is implied. No native, virtual, TCP or UDP MIDI connections
are opened or created. In-memory logic fixtures remain included. The normal
development profile and its broader transport tests remain separate.
