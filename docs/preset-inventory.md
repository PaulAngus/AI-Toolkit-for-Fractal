# Resumable saved preset inventories

Release 0.15.0 adds a shared `PresetInventory` journal and read-only CLI scan for FM9, FM3 and both established Axe-Fx III capacities. It reuses the contained saved-read protocol, integrity decoder and snapshot exporter. The new orchestration has synthetic validation only; it adds no hardware qualification.

## Commands

Build the CLI as described in [runtime](runtime.md). The following placeholders must be replaced with caller-selected ports, model, recorded firmware, zero-based slots and private destinations. Live commands require a separately requested device read and coordinated ownership of its MIDI ports.

```text
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll inventory --input INPUT --output OUTPUT --model MODEL --firmware VERSION --preset SLOT --destination private/inventory-001
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll inventory --input INPUT --output OUTPUT --resume private/inventory-001
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll inventory-inspect --inventory private/inventory-001
dotnet src/Fractal.Cli/bin/Debug/net10.0/fractal.dll inventory-export --inventory private/inventory-001 --destination private/snapshot-001
```

Repeat `--preset` for selected slots or replace it with `--all` for the established capacity. Axe-Fx III needs explicit `--capacity 512` or `--capacity 1024`; no firmware-based capacity inference occurs. New scans require the expected firmware in `major.minor` form with two minor digits. Identification must match before any saved read. `--timeout` bounds each exchange to 1–120 seconds, default 10. Resume takes its exact model, firmware, capacity and slot set from the checkpoint; new-scan options are rejected. Neither command changes preset selection, edits, uploads nor saves.

Inspection and export open no MIDI ports and reject port arguments. Inspection returns JSON with every selected slot, capture hashes/timestamps, decoded presets/scenes/Amp IDs where available, decoding errors and all attempt outcomes. A selected subset can be complete for `selected-slots`; only a scan whose requested slots cover the entire established range has `full-capacity` scope. No name-based empty-preset optimization is used: every selected slot receives a complete saved read. The existing decoder's `populated` field is a content convention, not a device empty flag.

## Checkpoints and resume

On Windows, a reader can briefly prevent the atomic checkpoint rename and cause
`UnauthorizedAccessException` (HRESULT `0x80070005`). Inventory writes retry only
the rename of the same already-flushed temporary file: at most six attempts with
25, 50, 100, 200 and 400 ms waits (775 ms total waiting). Sharing/lock violations
receive the same bound. This does not replay any device query. Read-only files,
directory collisions and immutable capture collisions remain refusals; attributes
are never cleared and writes never fall back to truncating an existing checkpoint.
Links are checked again before each rename attempt. A persistent failure reports
its attempt count and native HRESULT, retains the previous checkpoint, and removes
the staged file when the filesystem permits cleanup.

`InventoryFileChecks` covers atomic create/replace, immutable collisions, a real
Windows reader lock that is released during retry, a persistent lock, a read-only
checkpoint and a directory collision. These checks use private files only.
Windows sharing and replacement rules are documented by Microsoft:
[FileShare](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-10.0),
[MoveFileExW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw).

Each new private directory contains `inventory.json`, a process lock and immutable capture files whose names bind slot and wire hash. The checkpoint binds schema, protocol release, model, firmware, capacity evidence and the original slot set. Every retained capture is checked for addressed-slot bounds, framing/checksums, raw CRC, bounded decompression and wire/raw/body hashes when opened. Capture filenames are derived from validated fields; no checkpoint-controlled path is followed. Direct symbolic links for the inventory directory, lock, checkpoint and captures are refused.

Each attempt and its current phase/slot is checkpointed before querying. Every successful capture is flushed and retained before the checkpoint references it. Checkpoint replacement uses a flushed temporary file and a same-directory rename. One process owns the journal at a time, including offline inspection/export. An unexpected process exit leaves a `started` attempt; its outstanding capture slot is reported as `interrupted`, while other missing slots remain `unattempted`. Timeouts and ordinary failures retain a `failed` attempt; cancellation retains `cancelled`. The session stops at the first failure with no retry. A deliberate resume opens a new session. OS/driver crashes and power-loss durability have not been physically qualified.

Resume validates local captures, checks freshly identified context, and rereads **every retained slot** before reading missing ones. Raw images must exactly equal retained data; a mismatch stops the attempt without replacing the original. Start a new scan to represent changed saved contents. Capture times remain their original times; the successful attempt records how many retained captures were revalidated. An orphan capture from a crash before checkpointing is not accepted as progress: a new read must match it exactly before it is referenced.

This deliberately favors consistency over scan speed. Resume may reread many slots, and observations across either a new scan or resume are not atomic. Bracketed current-preset/scene queries detect observed selection drift, but cannot rule out a change and return between queries. Equal content and context do not identify a physical unit; cloned devices remain indistinguishable. No cached name becomes a fresh read or physical identity claim.

## Completion and export

`capture_complete` requires every requested capture plus a completed attempt with equal before/after selection. Selection drift leaves the journal partial even if all files were received; fresh reconciliation can finish it. `decode_complete` additionally requires the ordinary saved-body decoder to succeed for every capture. It does not claim every opaque field or unsupported Scene MIDI layout is understood. Unknown bodies retain complete captures and explicit errors.

Completed journals cannot be resumed or refreshed; create a new directory for another scan, preserving the last complete inventory separately from new partial work. Export refuses partial journals and uses `SnapshotExport` to write a new immutable schema-3 snapshot and hash manifest. Export is labelled an offline projection of retained saved captures, not a fresh live scan. The original journal retains acquisition/resume timestamps. Normal snapshot planning remains unchanged and continues to refuse exports with decoding errors.

Scan exit 0 means capture completion; inspect `decode_complete` for decoded coverage. Offline inspection exits 0 for either a valid partial or complete journal. Export exits 4 if complete integrity-checked captures include undecodable bodies. Invalid inputs, changed baselines, drift, integrity or transport failures exit 2; cancellation exits 130. Checkpoint files are not signed or tamperproof, and must remain private along with captures, names and attempt diagnostics.

## Validation and remaining work

[InventoryChecks](../tests/Fractal.Checks/InventoryChecks.cs) covers all four model/capacity definitions with synthetic first/last slots, timeout and cancellation, changed baseline refusal, selection drift and reconciliation, corrupt captures/checkpoints, exclusive ownership, immutable completion, unknown body retention and exact snapshot export. The [CLI pipeline](../scripts/check_cli_pipeline.py) tests offline inspect/export and refusal guards using complete fixture journals. See [the validation record](preset-inventory-validation.json).

Release 0.17.0 adds the analysis and MCP tools below; release 0.25.0 adds the exact known-field scene permutation check. A recorded FM9 12.00 trial completed two independent read-only full-capacity inventories: all 512 saved raw images matched across the scans, and a final live-context read matched the recorded starting context. An integrity-checked full-capacity saved backup acquired before the later editing trials also matched both final inventories in all 512 wire, raw and decoded-body hashes. This proves equal saved endpoints, not that no slot changed transiently. A separate selected-slot trial resumed after deliberate process termination, revalidated three retained captures and completed the other 29; all 32 images matched both full scans. A direct external checkpoint read during an earlier attempt caused a Windows replacement conflict; supported inspection takes journal ownership. Full-capacity resume, power-loss behavior, scene permutations and moves, physical identity, other-model behavior and complete editor scene semantics remain separate work. Integrations can call the shared core with their existing coordinated `ReadSession`; they must not open a second device connection.

## Comparison, amp usage and migration

These commands are offline and reject MIDI port arguments:

```text
dotnet run --project src/Fractal.Cli -- inventory-compare --before private/inventory-001 --after private/inventory-002
dotnet run --project src/Fractal.Cli -- inventory-amp-usage --inventory private/inventory-001 --amp-type 42
dotnet run --project src/Fractal.Cli -- inventory-amp-usage --inventory private/inventory-001 --roster qualified-roster.json
dotnet run --project src/Fractal.Cli -- inventory-migrate --inventory private/older-inventory --destination private/migrated-inventory
```

Comparison distinguishes complete raw equality, changed name bytes, changed representation and content changes. Missing captures remain unknown. Name-independent fingerprints retain every decoded body byte and opaque raw header bytes outside name/CRC/compression metadata. Unique content correspondence can identify a possible move; identical copies remain ambiguous. All-saved equality requires complete established capacity on both sides.

For same-slot content changes, `changes[].scene_permutation` reports a one-based `before_to_after` map only when a unique permutation of raw scene names, every placed block's channel/bypass words and all four complete Scene MIDI message banks reconstructs the entire after body byte for byte. This is an offline saved-byte proof within the located field layout. Identical scenes return `ambiguous-identical-scenes`; unsupported or changed layouts and unrelated body changes retain explicit statuses with no map. A `null` result means the slot had no content change or no paired capture. The result does not establish audible scene identity, all editor scene semantics or automatic reassignment.

[Validation](scene-permutation-validation.json) uses synthetic captures for FM9, FM3 and both Axe-Fx III capacities; it includes Scene MIDI, duplicate-scene ambiguity and an unrelated opaque-byte change. No hardware was accessed.

Amp search returns saved instance/channel IDs and scene assignments, including bypassed and unselected channels. A roster file has exact `model`, `firmware` and integer `ids` fields; it must match the inventory. `absent_ids` is returned only when every slot in the established capacity is captured and all relevant Amp data decoded. Partial inventories return unknown absence. Saved assignments do not establish live controller state or audibility.

Migration explicitly accepts schema-1 checkpoints from releases 0.15.0, 0.16.0, 0.17.0, 0.24.0 or the current release. It validates every capture and copies them into a new directory, retaining acquisition timestamps, attempts and completion state. `migration.json` records the original checkpoint hash. The original is unchanged; migration is not a fresh read. Unsupported schemas/releases still need a specific migration implementation.

MCP provides `fractal_capture_inventory` (explicit `slots` or `all: true`), `fractal_resume_inventory`, `fractal_inspect_inventory`, `fractal_export_inventory`, `fractal_compare_inventories`, `fractal_inventory_amp_usage` and `fractal_migrate_inventory`. Live scans share the configured device owner; no second connection is opened. Interrupted scans retain an artifact identifier for inspection/resume. Offline tools use private artifact references, and export produces a backup artifact for normal planning. Search can take a qualified roster artifact/file. Inspection and amp usage accept `offset`/`limit` (CLI `--offset`/`--limit`); MCP defaults to 10 inventory entries or 100 usage entries and returns `next_offset`. Paging never truncates the completeness or absence calculation. [InventoryAnalysisChecks](../tests/Fractal.Checks/InventoryAnalysisChecks.cs) covers complete synthetic capacity, partial evidence, moves, duplicate ambiguity, name changes, absence and prior-release migration.
