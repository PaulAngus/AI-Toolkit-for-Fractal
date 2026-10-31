# Fractal and MIDI knowledge base

[Editor export and System backup format research](../docs/setlists-system-format-research.md) identifies `.fas-songs` / `.fas-setlist` and partial XML export structure in all three official editors. Actual file samples, numbering/sentinels, System framing and recovery remain unqualified; no runtime or hardware support is added.

Release 0.38.0 adds [offline songs and setlist proposals](../docs/setlists-songs.md) against retained complete or partial inventories through shared CLI/MCP. DEFAULT stays symbolic and missing dependencies are reported. Editor interchange and live system execution are deferred pending complete codec, preservation and recovery evidence; no hardware qualification is added.

Use [automatic audio paths](../docs/automatic-audio-paths.md) for explicit waypoint routing and the [controller listener](../docs/controller-listener.md) for bounded input-only note/CC observations. Their runtime support and synthetic tests are separate from hardware qualification.

Release 0.17.0 extends [general preset editing and connected routing](../docs/preset-editing.md): shared deterministic planning, explicit templates, complete-image execution, optional Save/Reload, undo and reconciliation across all model/capacity definitions. Physical qualification is separate from executable support.

Reusable procedures for working with Fractal Audio devices through MIDI, saved preset data and supporting documentation. The knowledge applies independently of the controlling application or AI provider. Every device-specific claim retains its model, firmware and evidence limits.

| Task | Procedure |
|---|---|
| Plan songs, sections and ordered sets offline | [Setlists and songs](workflows/setlists-songs.md) |
| Rename, copy, move, swap or reorder saved presets | [Preset organization](workflows/preset-organization.md) |
| Set/query tempo, enable/disable tuner or control a present looper | [Performance controls](workflows/performance-controls.md) |
| Reuse private complete block/channel settings or swap two channels | [Preset templates and swaps](workflows/preset-templates.md) |
| Identify a device and read saved presets/scenes | [Device reads and compatibility](workflows/device-reads.md) |
| Recall a preset/scene and control block state | [Recall and control](workflows/recall-and-control.md) |
| Add a block instance at a chosen grid cell | [Block insertion](workflows/block-insertion.md) |
| Remove an isolated block or undo an insertion | [Block removal](workflows/block-removal.md) |
| Identify grid blocks by name or numeric ID | [Block identification](workflows/block-identification.md) |
| Inspect saved audio parameters and grid routing offline | [Editing readiness](workflows/editing-readiness.md) |
| Add outgoing messages across preset scenes | [Scene MIDI editing](workflows/scene-midi.md) |
| Preserve presets and verify persistent changes | [Backups and verification](workflows/backups-verification.md) |
| Diagnose routing, missing replies and network transport | [MIDI transactions and reliability](workflows/midi-reliability.md) |
| Build an offline inventory and compare changes | [Preset inventory and identity](workflows/preset-inventory.md) |
| Discover amp IDs, variants and real amplifier families | [Amp catalogues](workflows/amp-catalogues.md) |
| Integrate footswitch commands, labels and state feedback | [Controller integration](workflows/controllers.md) |

[capabilities.json](catalog/capabilities.json) provides generalized tasks, prerequisites, outputs and implementation limits. The [capability audit](../docs/capability-audit.md) checks coverage against earlier work. Start documentation retrieval with the [reading map](reference-map.md); the [source register](catalog/sources.md) preserves authority and access limits. The [FM9 12.00 roster](catalog/fm9-12-amp-roster.json) contains 336 recorded ID/name pairs; [reviewed identities](catalog/fm9-12-amp-identities.json) add 162 family/original groups and qualified variants.

```powershell
python scripts/search_knowledge.py "Scene MIDI default scene"
python scripts/search_knowledge.py "missing replies" --json
python scripts/query_amp.py --query "block crunch" --json
python scripts/validate_knowledge.py
```

The toolkit provides a [read-only runtime and exporter](../docs/runtime.md), general block placement, common offline Scene MIDI PC/CC planning, amp lookup and knowledge retrieval. Release 0.4.0 also provides [experimental editor operations and an empty-preset pilot](../docs/editing-pilot.md). FM9 12.00 passed saved PC/CC editing and byte-identical empty-preset recovery; existing-block message execution and explicit backup undo now have a separate [0.7.0 operation](../docs/scene-midi-execution.md). Full-image restoration and isolated insertion have separate qualified implementations. FM3/Axe-Fx III remain synthetic-only. Historical observations retain their own scope.

Release 0.4.2 adds [single-attempt saving of an already-loaded preset](../docs/save-current.md), with complete repeated saved-image verification against an expected backup. Physical FM9 12.00 equality passed; full-file upload remains a separate qualification.

## Knowledge admission rule

Keep an item when another developer or user can apply it to a Fractal/MIDI task without knowing the application or conversation that produced it. Explain the operation, prerequisites, units, method, expected result, sources and limits. A device/firmware-specific observation is useful when that boundary is explicit.

Generalize a discovered failure into a transferable rule: for example, preserve the saved default scene during preset edits, correlate replies before accepting them, or isolate diagnostic logs from simulated tests. Preserve the evidence behind that rule without importing the original bug report and application fix.

Application screen layouts, profile-management choices, release versions, publishing destinations, model recommendations and per-chat request histories belong in their source project. They are excluded from this toolkit. See [scope and curation](../docs/knowledge-scope.md).

For full-preset restore, start with [the contained operation](../docs/preset-restore.md), [physical scope](../docs/preset-restore-validation.json), and pinned `forgefx-upload-driver` / `forgefx-upload-helper` sources. These preserve known transfer evidence without repeating broad searches.

For inserting a block into existing presets, use [the isolated executor](../docs/block-insertion-execution.md) and [its qualified results](../docs/block-insertion-validation.json). It takes explicit instances/cells and verifies complete preservation before Save; setting Scene MIDI messages is a separate operation.

For interrupted Scene MIDI batches or later explicit undo, use [persistent recovery](../docs/scene-midi-recovery.md) and [its qualification](../docs/scene-midi-recovery-validation.json). Reopen the exact current private record, reconcile original/result hashes and preserve unsaved work. A saved receipt or offline inspection never authorizes a device mutation.

For selected-slot or full-capacity saved scans, use [resumable inventories](../docs/preset-inventory.md). Each capture and failed attempt is retained privately; resume revalidates saved contents before reading missing slots, and offline export requires completion.

For typed AI-client access, use [the local MCP workflow](workflows/mcp-client.md) and [installation guide](../docs/mcp.md). The contained stdio adapter retrieves retained knowledge, prepares canonical plans and exposes existing guarded execution through exact operator-approved reviews. Private device data stays outside public resources.

The [live tools](../docs/live-tools.md) provide bounded roster reads, absolute controls and finite state samples. [Automatic isolated placement](../docs/automatic-placement.md) is an explicit shared planner policy. Both retain separate hardware qualification.
