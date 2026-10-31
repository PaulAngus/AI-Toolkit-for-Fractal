# Inventory saved sounds and compare changes

General task: maintain a searchable snapshot of presets, scenes and Amp models, and determine what later reads actually establish.

| Operation | Evidence produced |
|---|---|
| Name sweep | Fresh returned names; incomplete addresses remain explicit |
| Sampled content check | Correspondence for the sampled presets and saved scenes |
| Full content check | Correspondence across the established address range |
| Full saved scan | A new decoded inventory with per-slot integrity, firmware and completeness |

Names alone do not establish Amp usage or unchanged preset contents. A sampled match is not a full-device match or a probability of physical identity. An editable name, port pair or local UUID does not identify a unique unit; cloned units can share all saved contents.

Use model/firmware-qualified capacity and retain populated, empty, failed and unattempted slots. A fresh `<EMPTY>` convention may provide an optimization, but it is not a dedicated protocol empty flag. Blank/unknown names must not be assumed empty. “No presets use this model” requires complete compatible data; missing reads imply unknown usage.

Separate the last complete snapshot from a partial scan. Bind checkpoints and resume to device context and source data; never silently mark cached names as fresh or commit an incomplete inventory as complete. Reuse successful reads where applicable without conflating live and saved caches.

## Contained resumable inventory

Use [the inventory runtime](../../docs/preset-inventory.md) for selected-slot or full-capacity scans. It retains each integrity-checked capture, explicitly records failed/cancelled/unattempted slots, and requires fresh equality of all retained raw images before continuing missing reads. Completed scans are immutable. Offline inspection exposes capture and decode completeness separately; only completed journals export to the shared snapshot contract. Model/firmware/capacity and source-release bindings are enforced. FM9 12.00 observations include two matching full-capacity read-only scans that also matched an integrity-checked earlier full saved backup in all wire, raw and decoded-body hashes, plus a selected-slot resume after process termination with three retained captures revalidated. Other-model compatibility and unique physical identity remain unqualified. Direct external reads of a live checkpoint can interfere with Windows atomic replacement; use the owned inspection operation after the scan stops.

## Changes and identity

A whole-image hash establishes exact equality, not sound identity. A mismatch cannot reconstruct which opaque old parameter changed when only the old hash remains. Comparisons need explicit rules for meaningful bytes, names, compression representation, bypass and firmware re-encoding.

For rename detection, validate a name-independent fingerprint using before/after saved captures. For moves/swaps, compare the whole inventory and require a unique supported correspondence. Copies and identical templates are ambiguous; do not assign identity from a matching name or address alone.

For scene swaps, identify every scene-indexed field moved by the editor, including channels, bypass, levels, controllers and undecoded data. Amp channel/bypass arrays alone may be insufficient. The contained same-slot comparator tests raw scene names, every located placed-block channel/bypass word and complete Scene MIDI banks; it returns a unique map only when permuting those fields reconstructs the entire decoded body byte for byte. Unsupported layouts, indistinguishable scenes and any unexplained byte difference remain unresolved. A positive result proves this saved-byte correspondence, not all editor scene semantics or sound identity.

User annotations and numeric recall shortcuts are separate from device facts. When identity is uncertain, retain the prior association for review. Never silently redirect a recall target based on an unverified inferred move. Any continuity policy should be explicit and reversible; it is not a device-provided identity guarantee.

Name-independent full-content comparison is implemented as qualified correspondence, with duplicate ambiguity retained. The exact known-field scene permutation check is available for paired same-slot captures; complete scene identity when additional unlocated fields move remains a research requirement. Existing hashes cannot retroactively provide missing old scene data. See [backups and verification](backups-verification.md) and [evidence categories](../../docs/evidence.md).

Use [the contained comparison, amp-usage and migration commands](../../docs/preset-inventory.md#comparison-amp-usage-and-migration) for saved inventory analysis. Unique full-content matches are possible moves; duplicate matches and incomplete captures remain ambiguous. No automatic scene reassignment is performed.
