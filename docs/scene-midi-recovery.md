# Persistent Scene MIDI recovery and batch undo

Release 0.8.0 adds private recovery records, receipt import, explicit resume and explicit batch undo for existing-block Scene MIDI plans. FM9, FM3 and both Axe-Fx III capacities use the same operation. [Validation](scene-midi-recovery-validation.json) distinguishes synthetic interruption tests from physical device evidence. Ordinary read-only commands remain read-only.

## Retained records and authorization

`SceneMidiRecoveryStore` retains each addressed original capture, its exact wire SHA-256, complete requested messages, model/capacity/firmware, original request hash, last attempted operation, and verified saved/reloaded result bytes and hash. It rebuilds the deterministic plan from those original bytes on import. A completed receipt requires identical saved/reloaded wire bytes and complete planned-body/header/padding preservation; a status label or hash alone cannot create it.

The record contains complete private presets. Keep it under a private/ignored directory; do not distribute it with the toolkit. New executions through `ApplySceneMidi` create a `recovery` directory automatically. Existing 0.7.0 journals are not silently upgraded or claimed as 0.8.0 recovery records.

Mutable import requires the caller's exact current `state.private.json` SHA-256 and the same contained protocol version. Release 0.29.0 added explicit offline migration of compatible settled schema-1 records into a new current-release directory; release 0.30.0 also accepts never-attempted `pending` items alongside settled ones. `editing` and `undoing` items remain bound to their original release. The separate selective planner accepts canonical schema-1 release labels from 0.8.0 onward through read-only revalidation of completed items. Incompatible records are refused. An internal payload hash detects damage; neither hash proves who authorized an operation. Inspection and migration open no MIDI ports. Resume and undo require separate explicit device authorization. A plan/receipt does not authorize itself. Target identities and message parameters are caller-supplied; no preset, message or port defaults are added.

One exclusive file lease prevents two toolkit callers from updating the same record. Every operation checkpoint is flushed to disk before the corresponding device mutation. A complete replacement record is flushed and atomically renamed in the same directory; incomplete `state.pending` files are ignored. A failed record update stops further device work. Generation counters and exact head hashes reject stale supplied records. These mechanisms do not establish power-loss durability of arbitrary filesystems, prevent an external application from changing the unit, or authenticate deliberately rewritten records.

## Migrate a compatible older record offline

`fractal recovery-migrate --recovery PRIOR_DIRECTORY --state-hash EXACT_CURRENT_SHA256 --destination NEW_DIRECTORY` validates the outer and inner hashes, original captures, rebuilt plans and complete saved/reloaded receipts. Every item must be `edited`, `unchanged`, terminal `undone`, or `pending` with `last_attempt: "none"` and no result evidence. A pending item is eligible only because the recovery state machine checkpoints an attempt before its device mutation. `editing`, `undoing` and any pending item with an attempt or result are refused. The new directory contains a current-release `state.private.json`, a new exact state SHA-256 and `migration.json` identifying the source release/hash and counts including `pending_untouched_count`. The source record remains unchanged. `fractal_migrate_recovery` provides the same offline operation for a private MCP artifact.

Migration copies retained evidence; it does not read a device or certify its current state. A migrated pending item needs the new head hash, fresh exact saved/live reconciliation and separate operator authorization before it can be applied. A later whole-backup undo has the same freshness and authorization requirements. The [offline validation record](scene-midi-recovery-migration-validation.json) covers current-schema synthetic records relabeled with older releases, four locally retained 0.11.0 pending synthetic records across all model/capacity definitions, and one privately retained actual 0.8.0 FM9 12.00 record containing two terminal undone items. These migrated to 0.30.0 with identical item evidence and unchanged sources; offline inspection reopened them without MIDI. Other historical releases and interrupted states remain untested. Incompatible records are rejected rather than converted. Keep both private directories until the intended continuation is verified.

## Resume after an interruption

1. Reopen the retained record with its current exact hash; validate all addressed captures, plans and completed receipts before ports are opened.
2. Identify the exact model/capacity/firmware. Require stable current selection and complete saved/live equality before navigation. Reconcile every target before the first write, then reread each target immediately before its operation.
3. A pending/interrupted target still matching its complete original may be applied through a fresh existing executor scope, after fresh saved/live checks. This is a deliberately requested new execution against known state; previous uncertain fields are never blindly resent.
4. A complete saved image matching the intended full result is independently verified through guarded target load/Reload, complete loaded/saved comparison, repeated saved reads and the live Scene MIDI table. No fields, Save or upload are sent. Only successful verification retains a completed receipt.
5. A partial or unrelated saved image is a conflict. A partial unsaved edit also stops recovery before navigation. Completed result hashes must remain exact; an externally restored original is not silently treated as a new task. A target marked undone is terminal and cannot be reapplied by resume.

An interruption after Save but before the receipt checkpoint can therefore be reconciled without repeating Save. A rejected transfer, cancelled request, cable removal or uncertain upload is not automatically replayed. If saved/live contents differ, preserve and resolve the unsaved buffer explicitly; this recovery operation does not discard it or finish an uncertain partial edit.

## Explicit batch undo

Undo requires completed receipts (or an interrupted explicit undo), not merely requested plans. No-op entries are left alone. All targets are checked for conflicts before the first upload, and every target is freshly guarded again immediately before its operation. One coordinated native port owner spans the whole request; new session scopes do not reopen the native connection.

Safely load and independently verify each exact recorded result, then use its original upload through the qualified full-preset restore operation. Require exact saved result and no unsaved work before upload; verify the entire original live buffer before Save; recheck the saved result and original live buffer immediately before the once-only Save. Verify repeated saved original bytes and Reload before marking the item undone. Later edits stop the batch rather than being overwritten. Earlier completed items remain recorded; there is no automatic all-or-nothing rollback.

If an undo Save succeeded before interruption, explicit undo can verify the exact saved original with reads and guarded loads only, without repeating upload/Save. If an upload left the original only in temporary memory while the saved result remains changed, saved/live inequality stops continuation. Resume cannot reapply an item whose explicit undo is in progress. Repeat undo of a verified terminal original performs saved reads only.

## Opt-in checker

Build `tests/Fractal.HardwareChecks/Fractal.HardwareChecks.csproj`. Use:

```text
--hardware --scene-midi-recovery --mode inspect|resume|undo
  --recovery PRIVATE_RECOVERY_DIRECTORY --expected-hash CURRENT_STATE_SHA256
```

`inspect` is always offline. `--validate-only true` also validates resume/undo inputs without opening ports or claiming fresh device state. Live resume/undo additionally require explicit `--input`, `--output` and a new private `--destination`. The checker captures full inventories before/after, compares every non-target saved preset and verifies final target hashes. It records updated head hashes at checkpoints and in its final private report. After an abrupt process termination, obtain the actual committed file's SHA-256; a prior report can contain a stale head. Never select `state.pending` as the current record.

`--undo-after true` on the original execution checker remains its one-target qualification option. For a reviewed multi-target request, use `--undo-after false`, retain the successful recovery record, then separately opt into `--scene-midi-recovery --mode undo`. The [separate offline selective planner](selective-scene-midi-undo.md) accepts one completed recovery item and a caller-supplied later saved capture. It creates an ordinary general-edit bundle; it does not update this recovery state machine or accept unfinished/uncertain items. Combined insertion/configuration transactions and general block removal remain outside this recovery operation.

## Retained sources and limits

Recovery introduces no new SysEx encoding. It composes the existing [message executor](scene-midi-execution.md), [full-preset restore](preset-restore.md) and their exact manufacturer/open-implementation locators in the [reading map](../knowledge-base/reference-map.md). `SceneMidiRecovery.cs`, `SceneMidiRecoveryRunner.cs` and `SceneMidiReconcileSession.cs` define the common file/state/execution contract; `RecoverSceneMidi.cs` is the opt-in Windows adapter.

Synthetic tests qualify state-machine and interruption handling in software. Physical PC/CC, batch, receipt-import and interruption claims must come from separately recorded hardware trials; previous single-preset PC and empty-preset CC evidence does not establish this new milestone's hardware behavior.

FM9 12.00 passed a two-target populated PC/CC batch, fresh-process receipt import and result-only verification, then explicit original-backup undo. All 510 non-target saved slots were unchanged and all 512 original saved slots matched exactly after undo. One retained preflight inventory and three fresh after-operation inventories supplied the complete comparison; copied baselines are not counted as fresh reads. Deliberate hardware interruption and other-model physical behavior remain untested.
