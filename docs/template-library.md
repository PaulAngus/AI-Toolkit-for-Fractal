# Private template library and channel swaps

The shared offline library retains complete block/channel donors from integrity-checked saved captures. Entries carry names/tags, model/capacity, caller-declared firmware and provenance, exact instance/shape, source slot, capture hash and selected-settings hash. Names are private labels, never instructions or defaults. The library does not require an editor installation or exhaustive parameter maps.

FM9, FM3 and both Axe-Fx III capacities use the same core contracts. [TemplateLibrary](../src/Fractal.Core/TemplateLibrary.cs) validates entries and lowers applications to ordinary [edit plans](preset-editing.md). [Swap planning](../src/Fractal.Core/PresetEditPlan.cs) uses the same writer, executor, review, Save/Reload, reconciliation and exact-result undo. [Validation](template-library-validation.json) is synthetic; no physical qualification is added.

## Import, search and apply

Choose a private library directory outside distributed source/package folders. The complete donor capture is retained inside each immutable schema-1 JSON entry, even for a single channel; it can contain unrelated private preset data. The entry filename is an opaque ID. Import creates a new entry without replacing another; interrupted `.pending` files are not entries. Library entries are independent of the protocol release, while execution bundles retain their existing release rules.

CLI metadata below is synthetic. Supply the real complete capture SHA-256, source context, exact instance and provenance; channel templates require A–D on a four-row instance, while block templates omit `channel`.

```json
{
  "schema_version": 1,
  "model": "FM9",
  "capacity": 512,
  "firmware": "99.07",
  "effect_id": 66,
  "kind": "channel",
  "channel": "C",
  "name": "Example settings",
  "tags": ["example"],
  "provenance": "Caller-supplied donor; firmware declaration requires independent evidence",
  "capture_sha256": "<SHA-256 of the complete donor capture>"
}
```

```text
fractal template-import --library PRIVATE_LIBRARY --request METADATA.json --capture DONOR.syx
fractal template-list --library PRIVATE_LIBRARY --query example --offset 0 --limit 20
fractal template-inspect --library PRIVATE_LIBRARY --entry ENTRY_ID --expected-hash ENTRY_SHA256
fractal plan-template --entry-file PRIVATE_ENTRY.json --entry-hash ENTRY_SHA256 --request APPLY.json --capture BASELINE.syx --destination NEW_BUNDLE
```

Search matches names/tags case-insensitively; an optional `--tag` requires an exact tag match. Results and inspection omit donor bytes and return the current exact entry hash. Entries are bounded to 200,000 bytes, names to 80 characters, provenance to 512, and at most 16 unique tags of 32 characters. Listing has bounded paging and a 5,000-entry limit. Linked paths, duplicate JSON fields, damaged captures, inconsistent shapes/settings hashes and stale supplied entry hashes are refused. Hashes establish integrity, not hardware origin or authorization.

`APPLY.json` is a schema-1 request with `model`, `capacity`, `firmware`, `slot`, `persistence` (`save` or `edit-buffer`), `base_sha256`, exact `effect_id` and `kind`:

| Kind | Required entry and additional input | Preserved scope |
| --- | --- | --- |
| `copy-channel` | Channel entry and destination `channel` | Copies its complete selected row; other rows, scene assignments and shared modifiers stay exact. |
| `copy-block-settings` | Block entry | Copies the complete instance interval including scene assignments; grid and shared modifiers stay exact. |
| `insert-block` | Block entry, explicit `row`/`column`, optional `policy: replace-shunt` | Uses existing complete-instance initialization and placement rules; availability/DSP/defaults are not inferred. |

Model, capacity, declared firmware, exact instance and target record shape must match. Cross-instance and cross-model remapping are outside this version. The ordinary bundle embeds the complete donor and rederives the authored result when reopened; moving/changing the library later cannot silently change a frozen review.

MCP uses `fractal_import_template` with `backup_artifact`, `slot`, `effect_id`, `kind`, `name`, `provenance`, optional `tags` and channel. The library lives under the existing private `data_root/templates`. `fractal_list_templates` and `fractal_inspect_template` return metadata/hashes without exposing host paths. `fractal_plan_template` takes the exact `entry_id`/`entry_sha256`, target backup/slot/persistence, exact instance and apply fields. It produces a normal `plan_artifact`; use `fractal_prepare_preset_edit` and the existing trusted-host approval/execution contract. Source/target artifacts are revalidated against manifests. Library and planning tools work with live access disabled.

## Channel swaps

Use an ordinary `plan-edit` request or MCP `fractal_plan_preset_edit` containing:

```json
{
  "kind": "swap-channels",
  "effect_id": 66,
  "channel": "A",
  "other_channel": "C",
  "policy": "follow-channel-settings"
}
```

Both complete rows come from one immutable baseline. `preserve-scene-references` leaves scene channel selectors unchanged; scenes selecting the swapped channels then use the exchanged settings. `follow-channel-settings` exchanges only the relevant scene channel selectors alongside the two rows. Bypass words, scene names, MIDI tables, routing, shared modifiers and all other body bytes are preserved. This is a guarantee about these stored fields, not identical sound under other channel-dependent behavior.

The preview records row hashes, affected scenes, before/after scene references and the non-idempotent intent. Only four-row uniquely placed exact instances and channel references A–D are accepted. Distinct channels and an explicit policy are required. Same-instance edits and all shared-modifier edit composition are refused; unrelated edits can compose.

Identical rows produce a no-op under either policy, retaining all original scene references and wire bytes. A completed nontrivial swap cannot be replayed against its old baseline: ordinary fresh saved/live comparison rejects that plan, and execution instances/approvals are single-attempt. An intentional second swap requires a fresh current baseline/review. Incomplete attempts use query-only reconciliation; completed saved receipts use exact-result undo, which refuses later saved/unsaved changes.

## Evidence

Manufacturer feature descriptions are retained in [implementation-plan sources E1/E2](preset-authoring-plan.md#manufacturer-sources-and-limits). Instance intervals, channel payloads and scene selectors reuse the pinned `gen3-body` interpretation documented in [editing profiles](editing-read-profiles.md). Template capture hashes do not establish firmware labels or factory defaults. [Independent expected-body and transport checks](../tests/Fractal.Checks/TemplateLibraryChecks.cs) cover all definitions; editor-native library files, physical swaps, audible equivalence and other instance layouts remain unqualified.
