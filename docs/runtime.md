# Runtime, release 0.15.0

Release 0.38.0 adds [offline songs and setlist proposals](setlists-songs.md) against retained complete or partial inventories through shared CLI/MCP. DEFAULT stays symbolic and missing dependencies are reported. Editor interchange and live system execution are deferred pending complete codec, preservation and recovery evidence; no hardware qualification is added.

Release 0.20.0 adds offline `plan-audio-path` and receive-only `listen-midi`; see [automatic paths](automatic-audio-paths.md) and [controller listening](controller-listener.md). Both use shared model-independent task code and retain separate hardware qualification.

Release 0.19.0 adds offline `create-edit-batch` and `route-midi`, configured bank/PC mapping in `prepare-load`, and shared execution for [general saved-edit batches](batch-editing.md) and [configured MIDI controls](configured-midi-routing.md). The ordinary CLI still sends queries only; mutations use the explicit helper or exact MCP review.

Release 0.16.0 provides [general preset editing and connected routing](preset-editing.md): shared deterministic planning, explicit templates, complete-image execution, optional Save/Reload, undo and reconciliation across all model/capacity definitions. Physical qualification is separate from executable support.

Release 0.26.0 adds [selective Scene MIDI receipt undo](selective-scene-midi-undo.md) through the general saved editor. Offline planning merges exact owned rows into a caller-supplied current saved capture; ordinary complete-image execution still requires fresh preflight and review. Hardware qualification remains separate.

Release 0.27.0 extends that planner to completed persistent Scene MIDI recovery-store items and restores all four original raw words per owned row from a hash-bound original backup. Off-row inactive words are preserved exactly; partial recovery items remain conflicts. CLI/MCP planning is offline and the existing execution review remains required.

Release 0.29.0 added offline `recovery-migrate` for compatible settled older schema-1 records; release 0.30.0 also accepts never-attempted pending items. It writes a separate current-release record and migration provenance without reading a device; interrupted records are refused. [Recovery rules](scene-midi-recovery.md) still require fresh exact reconciliation and approval before any later mutation.

Release 0.15.0 adds `inventory`, `inventory-inspect` and `inventory-export`; see [resumable saved inventories](preset-inventory.md) for checkpoints, resume preconditions and completion semantics.

Release 0.14.0 extends offline-only `edit-capabilities`, `edit-catalogue` and `decode-edit`; see [editing read profiles](editing-read-profiles.md). These provide model-specific audio catalogues and a separate partial saved-parameter/routing projection and do not add writes or change existing snapshot exports. The earlier runtime commands below retain their contracts.

The toolkit contains its C# protocol code, shared-package Windows/macOS MIDI adapters, CLI and synthetic regression fixtures. Build with the .NET 10 SDK. Pinned MIDI and transitive NuGet packages are contained in `dependencies/midi`. No sibling checkout, AI service or remote package feed is required. See [shared MIDI integration](shared-midi.md). Offline decoding runs wherever .NET 10 runs; local live MIDI uses the shared legacy Windows or standard macOS CoreMIDI backend.

## Commands

Run commands from the repository root. Build once, then run the resulting CLI:

```powershell
dotnet build src/Fractal.Cli/Fractal.Cli.csproj
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll ports
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll inspect --input INPUT --output OUTPUT --model MODEL
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll snapshot --input INPUT --output OUTPUT --model MODEL --preset ZERO_BASED_SLOT --destination private/read-001
python scripts/plan_scene_midi.py --snapshot private/read-001/snapshot.json --all-populated --channel CHANNEL --value VALUE --block-row ROW --block-column COLUMN
```

Replace INPUT, OUTPUT, MODEL and ZERO_BASED_SLOT with verified caller choices; select the actual input and output from `ports`. Listing ports does not start listening or send MIDI; macOS enumeration uses the shared package's transient device wrappers. Close other applications that own the ports before `inspect`, `snapshot` or `scene-midi`. These commands open a device connection only when deliberately invoked. A hosted integration should supply its application's coordinated `IMidiTransport` instead of opening another connection.

Use `--model FM3` for an FM3. Use `--model "Axe-Fx III" --capacity 512` or `--capacity 1024` for the appropriate Axe-Fx III hardware. Identity cannot distinguish those capacities; no version-based guess is made. FM3/FM9 definitions establish 512 slots. An optional expected model is checked against the identity reply before further queries.

Repeat `--preset` to read multiple zero-based slots; `--all` reads the established capacity. Scenes and message slots use 1–8; MIDI channels use 1–16. No request changes the selected preset/scene. Snapshot reads bracket the saved transfers with current-state queries and report observed drift. This observation cannot rule out a brief change and return between queries. Multiple saved reads are not an atomic device-wide snapshot.

Decode existing single-preset captures without MIDI:

```powershell
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll decode --model MODEL --firmware VERSION --capture exported-preset.syx --destination private/offline-001
```

Repeat `--capture` for more files. Replace MODEL and VERSION with the captured device context. The supplied firmware is contextual evidence; the decoder cannot discover firmware from a preset export. Bank files must first be separated into individual complete `77/78/79` transfers. No bank splitter is included in this release.

Inspect one single-preset backup without requiring a saved-slot address or inferred firmware:

```powershell
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll check-backup --model FM9 --capture backup.syx
```

Use the actual model and an explicit Axe-Fx III capacity. This command opens no MIDI ports and leaves the file unchanged. It verifies strict framing, the model's exact chunk count, frame/footer checksums, raw CRC and bounded decompression. Its JSON contains sizes, hashes and the complete opaque five-byte header; preset and scene names are omitted. `PresetFile.Inspect` exposes the same contained-library operation.

An independently restored FM9 file contained header payload `7F 00 00 40 00`, while an addressed saved read uses the requested slot in its first two fields. The backup's destination/save semantics are unqualified. `decode` and live saved reads retain their strict slot bounds; an opaque backup cannot be relabelled as an addressed snapshot. File integrity is distinct from restore validation. See [the qualification record](restore-qualification.md).

Exit codes: **0** completed; **2** invalid input/read/transport failure; **3** inspected state changed; **4** integrity-checked captures retained but one or more bodies could not be decoded; **130** cancelled. A timeout or cancellation poisons the session; reconnect deliberately before retrying. There is no automatic replay.

## Export contents and interpretation

Each new destination contains exact saved-transfer `.syx` files, `snapshot.json` and a final `manifest.json`. Existing destinations are refused. The manifest records wire/raw/body SHA-256 values and the snapshot-file hash. Frame XOR, footer word XOR, raw CRC and bounded Huffman decoding must pass before export. A complete manifest must parse and its hashes must match; its filename alone is insufficient evidence of completion. These files are immutable by convention and never overwritten by this tool; the filesystem does not make them tamperproof.

The [snapshot contract](../skills/fractal-fm9/references/snapshot-contract.md) includes model, firmware, capacity evidence and relevant capabilities. Decoded content includes preset and saved scene names, the model's routing grid, numeric Amp models for A–D, scene channel/bypass selections, opaque body-header words, terminal metadata and block-record words. Unknown numeric amp IDs remain visible without borrowing another device's catalogue. Complete bytes remain available in the capture even when a semantic field is not understood.

An unfamiliar Amp/body shape produces `decode_errors`; the complete integrity-checked capture is still retained. The planner rejects an export containing such errors so it cannot silently omit undecoded presets. Version-3 snapshots decode complete Scene MIDI tables, including Off slots and raw inactive fields. Missing/ambiguous records or unsupported active values use `scene_midi_decode_status: "unsupported-saved-table-layout"`; empty arrays then mean unknown and produce conflicts. See [Scene MIDI reading](scene-midi-reading.md) for model qualifications, the live `scene-midi` command and PC/CC planning.

The ordinary CLI does not translate arbitrary block parameters into named settings, restore captures, author preset data, save hardware or expose MCP tools. The separately opted-in library/harness can [save an already-loaded preset](save-current.md) and [restore a complete preset](preset-restore.md) through an immutable temporary-buffer plan, verified before Save and Reload. A received dump alone grants no write permission.

## Transaction and transport contract

`ReadSession` serializes requests, subscribes before sending, assembles fragmented SysEx, bounds queues/frame sizes and matches model/function/length/checksum plus address fields where available. A closed request allowlist is enforced both in the session and shared-package adapter: broadcast identity, firmware, preset/current-scene/scene-name queries, addressed saved reads and the current Scene MIDI block query (`1F`, effect 190 only). The library additionally permits the exact edit-buffer dump sentinel through its closed read allowlist. It returns a distinct, strictly integrity-checked live capture. No arbitrary send is exposed; typed writes require a separate explicit scope.

The shared 0.3.6 package owns native I/O and buffer lifetimes. The toolkit adapter copies receive data into a bounded queue, serializes sends and retains `MidiSendAuthority` checks. Overflow, receive errors and failed/cancelled/timed-out sends permanently fault the adapter; nothing is replayed. Synchronous sends run outside callbacks. An uncertain send retains port ownership until it finishes and cleanup succeeds. Cleanup waits are bounded; a hung driver can leave cleanup pending until process exit.

Numeric port selection retains the WinMM catalogue. The shared legacy input uses WinRT: exact unique name mapping is required, and ambiguous/unmatched endpoints are refused. Same-toolkit processes retain the existing named ownership semaphores. Other applications still require coordination. Shared input events have no driver timestamp; listener reports identify host monotonic receipt time explicitly. macOS uses the shared standard CoreMIDI backend with exclusive toolkit file leases; see [shared MIDI integration](shared-midi.md). This migration has offline coverage only; previous WinMM hardware evidence does not qualify the new backend.

## Evidence and validation

All three model definitions and both Axe-Fx III capacities have synthetic checks and an offline CLI capture→decode→plan check. Release 0.2.1 also has [FM9 12.00 physical read validation](hardware-validation.md): all 512 saved slots, repeat samples, state preservation and failure recovery passed. The [validation record](runtime-validation.json) records local checks. FM3/Axe-Fx III physical coverage remains unrecorded; Linux CI is configured but was not run locally. New device/firmware success is not inferred from one FM9.

The FM9 definition recognizes an exact 350×1-marked, 736-byte terminal tail seen in these saved images and preserves it as opaque bytes. It does not apply that model-specific layout to another device by analogy. The decoder retains the first two words as `body_header_words`. The later [0.4.0 pilot](editing-pilot.md) qualified the first word as the zero-based saved default scene on FM9 12.00; the second word and other-model semantics remain opaque/unqualified. Release 0.3.0 established saved/live PC/Off table offsets; the later pilot adds active PC/CC and varied channels. FM3/Axe-Fx III physical comparisons remain untested.

Protocol lineage, origin hashes, modifications and local source hashes are in [protocol-release.json](protocol-release.json). The initial C# framing, assembly and stored decoder were adapted from Apache-2.0 PresetMaestro source; model/body research is credited in [NOTICE](../NOTICE). No PresetMaestro UI, settings, integration lifecycle or application repair code is included.

Format research is pinned to [presetDump.ts](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-gen3/src/presetDump.ts) (`77/78/79`, packed words, model transfer lengths) and [presetBody.ts](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-gen3/src/presetBody.ts) (grid, record walk, Amp offsets). These are open implementation evidence. Their upstream `fractal-syx-codec` link returned 404 on 8 October 2026; the retained implementations are usable fallbacks, not independent manufacturer confirmation.

Windows backend references: [MIDIHDR](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/ns-mmeapi-midihdr), [midiInAddBuffer](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-midiinaddbuffer), [midiInReset](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-midiinreset), [midiOutLongMsg](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-midioutlongmsg). Buffer ownership and asynchronous completion follow these documented contracts; eight input buffers and request deadlines are toolkit policy.

```powershell
python scripts/check_cli_pipeline.py
python -m unittest discover -s tests -v
python scripts/protocol_release.py
python scripts/validate_knowledge.py
```

The CLI pipeline runs .NET regression checks, then exports/decodes/plans FM9, FM3 and 512/1024-slot Axe-Fx III synthetic presets. CI runs these checks on Windows and Linux. No hardware work belongs in ordinary CI.

Opt-in physical tests are documented in [hardware-validation.md](hardware-validation.md). They build from contained source and are excluded from ordinary CI. Private captures, names and traces stay in ignored local evidence; the public summary contains redacted results and artifact hashes.

## Experimental editor path, release 0.4.0

The ordinary CLI and default WinMM transport remain read-only. The library additionally exposes typed, scoped editor operations and strict block-status reads for the separately opted-in [empty-preset pilot](editing-pilot.md). Delivery returns sent-unverified and must be followed by readback. Isolated insertion into populated presets has a separate operation; existing-block Scene MIDI execution and explicit undo have a separate [0.7.0 scope](scene-midi-execution.md). Complete preset restoration has its own [0.5.0 scope](preset-restore.md).

Release 0.4.2 adds a distinct [single-attempt save scope](save-current.md) for an already-loaded preset, without applying empty-preset ownership rules to a populated preset. Full saved readback against the expected backup passed on FM9 12.00; other model definitions have synthetic scope coverage only.

## Isolated insertion into populated presets

[The insertion operation](block-insertion-execution.md) uses `BlockInsertionPlan`, `BlockInsertionScope`, complete live/saved preflight and `EditorPreservation.VerifyBlockInsertion`. Its opted-in checker processes explicit hash-bound targets, preserving existing routing, block parameters, names, saved scene and Scene MIDI data before saving. A failed item stops the remaining targets; no automatic retry or rollback is sent. Actual hardware scope is retained in [the separate record](block-insertion-validation.json). General routing, parameter configuration, automatic undo and resumable batch orchestration remain outside this operation.

## Existing-block Scene MIDI execution and undo

[The 0.7.0 operation](scene-midi-execution.md) consumes complete schema-3 backups and schema-2 PC/CC plans. It fills only Off rows, reuses identical messages, preserves every unrelated body byte and raw header/padding, verifies the independent live table, and requires complete saved/reload verification. Exact-result receipts permit explicitly requested whole-backup undo while refusing later saved/unsaved changes. Port-free plan validation is available; ordinary CLI commands remain read-only. See [actual physical scope](scene-midi-execution-validation.json).

## Persistent Scene MIDI recovery

[Release 0.8.0](scene-midi-recovery.md) adds `SceneMidiRecoveryStore`, `SceneMidiRecoveryAuthority`, `SceneMidiRecoveryRunner.RunAsync` and guarded `SceneMidiReconcileScope` sessions. The opted-in `--scene-midi-recovery` checker supports port-free inspect/validation and explicitly requested resume/undo. Original/result hashes, complete captures, atomic pre-attempt checkpoints and fresh saved/live checks protect against blind replay and later edits. Physical scope remains separate in [the record](scene-midi-recovery-validation.json).

## Explicit preset navigation

[PresetLoadScope](preset-load.md) permits one caller-requested load, binding retained current saved/loaded and target bytes. Loaded/saved differences require explicit discard authorization; both saved images remain unchanged. This is separate from automatic recovery navigation, which keeps its unsaved-work refusal. The opt-in checker has port-free validation.

## Local MCP adapter

[Release 0.9.0](mcp.md) exposes the existing read/planning/scoped execution operations through local stdio, with concrete immutable reviews and one-use operator approval. `prepare-load` is a read-only CLI addition retaining current saved/loaded and target captures; it sends no Load. The insertion checker now supports port-free `--validate-only true`. Normal CLI read-only behavior is preserved. [Adapter qualification](mcp-validation.json) is software-only and does not add device hardware claims.

## Named grids and offline block lookup

Release 0.10.0 adds `name` and `kind` to grid cells without changing their numeric IDs, positions or routing. `device.block_catalogue` records the model, source revisions and limitations. Unknown/model-unqualified IDs have null names; empty cells and shunts are distinct grid elements. The embedded catalogue and core resolver are self-contained. `dotnet runtime/fractal.dll blocks --model FM3 --query Reverb` performs a port-free lookup; use `--effect-id` for a numeric identity and optional `--firmware` for recorded context. [Procedure and scope](../knowledge-base/workflows/block-identification.md), [offline validation](block-catalogue-validation.json).

Release 0.18.0 adds `amp-roster`, `capture-control` and finite `live-monitor` queries, plus a separately opted-in absolute live-control helper. See [live tools](live-tools.md) for exact contracts, partial results and qualification limits.

[Network transport and notifications](network-notifications.md): device commands and authorized helpers accept `--network IPv4:CONTROL_PORT` in place of native ports. `listen-notifications` adds bounded passive capture. Supported operations share their existing scopes, freshness checks and readback verification. Network session packets are distinct from MIDI queries/writes.


[Private block parameter reads](live-parameters.md): `read-block-parameters` uses the existing native/network transport with an explicit present instance, bounded transfer, own-model mappings, endpoint checks and private partial evidence. Read-only FM9 12.00 Amp and seven additional family instances have separately scoped physical evidence; other models, firmware and parameter writes remain unqualified.

`calibrate-parameter --request REQUEST_JSON --destination NEW_DIRECTORY` validates retained calibration captures offline. Its output feeds ordinary `plan-edit` for exact observed block, Scene Ignore and modifier values. [Modifier and calibration contracts](calibrated-authoring.md).

[Built-in scales and named modifier curves](builtin-scales.md) are discoverable through `edit-catalogue` and authored through ordinary `plan-edit` operations `set-scaled-parameter` and `set-modifier-parameter`. Signed values use `physical_value`; exact saved-normalized modifier fractions require that declared unit. A same-binding witness can supply the raw Damping Type word through `copy-modifier-selector`. Existing `value` contracts remain unchanged.

Release 0.36.0 adds [performance controls](performance-controls.md). `capture-performance` queries a present looper and captures complete current saved/loaded context without querying tempo. Tempo read/set, tuner and looper execution use the opt-in hash-bound performance helper and existing exact reviews; ordinary read authorization does not grant tempo queries.

Release 0.37.0 adds offline `plan-organization`, `inspect-organization`, `organization-batch` and `plan-organization-undo`. [Contracts](preset-organization.md) cover immutable saved-slot assignments, qualified individual imports, source/destination guards and eligible completed-target restoration through existing exact batch reviews. No hardware relocation qualification is implied.
