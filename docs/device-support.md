# Device support and common runtime contract

Release 0.38.0 adds [offline songs and setlist proposals](setlists-songs.md) against retained complete or partial inventories through shared CLI/MCP. DEFAULT stays symbolic and missing dependencies are reported. Editor interchange and live system execution are deferred pending complete codec, preservation and recovery evidence; no hardware qualification is added.

Release 0.16.0 provides [general preset editing and connected routing](preset-editing.md): shared deterministic planning, explicit templates, complete-image execution, optional Save/Reload, undo and reconciliation across all model/capacity definitions. Physical qualification is separate from executable support.

The toolkit targets **FM9, Axe-Fx III and FM3**. These models use the same user operations and CLI/MCP interface, with explicit adapters for real protocol, firmware and capacity differences. The initial FM9 hardware observations must not turn into a permanent restriction on the toolkit.

Release 0.14.0 extends [offline editing read profiles](editing-read-profiles.md) for each model and both Axe-Fx III capacities. These independently select editor audio catalogues, bounded saved audio layouts and incoming grid masks. Unknown fields/bits remain explicit; source-derived reads add no hardware qualification or writers.

## Common operations and model definitions

Use a common contract for identification, firmware/current-state queries, saved reads, preset/scene inventory, snapshot export, backup, planning and verified editing. An operation takes a device context; task code does not assume an FM9, 512 slots, one dump size or one amp roster.

Each model definition/adaptor supplies:

- Protocol model addressing and supported transaction layouts.
- Established preset capacity and relevant hardware/firmware distinctions.
- Grid dimensions, effect limits, channel/scene capabilities and parameter layouts.
- Dump assembly bounds, integrity rules and body decoding.
- Firmware-qualified model catalogues and editor read/write encodings.

Keep the transaction coordinator, fragmentation handling, timeout/cancellation behavior, normalized data and preservation checks common. Parsing should account for the actual model's transfer shape rather than forcing FM9 frame counts. The [Gen-3 implementation](https://raw.githubusercontent.com/TheAndrewStaker/mcp-midi-control/main/packages/fractal-gen3/src/presetDump.ts) is useful format research; it remains independent implementation evidence, not a manufacturer guarantee or toolkit hardware test.

Unknown numeric models can remain visible without a friendly label. A missing catalogue must not prevent compatible preset reads or generic Scene MIDI planning. A truly unavailable operation is reported for that operation; it does not make the entire device unusable. Persistent execution still requires a correctly implemented target-specific operation and its verification procedure.

## Scene MIDI applies across the target models

The FM3 manual documents the Scene MIDI block and per-scene messages in **Transmitting MIDI With Scenes**, printed p56 (PDF p62), and **Sending and Receiving MIDI â†’ The Scene MIDI Block**, printed p134 (PDF p140). The Axe-Fx III manual documents **Sending MIDI â†’ The Scene MIDI Block**, printed p102. Both refer to the common Blocks Guide. This establishes the device feature; it does not establish that the FM9's private editor envelope or saved-body offsets can be reused unchanged.

Sources: [FM3 manual](https://www.fractalaudio.com/downloads/manuals/FM3/FM3-Owners-Manual.pdf), [Axe-Fx III manual](https://www.fractalaudio.com/downloads/manuals/axe-fx-3/Axe-Fx-III-Owners-Manual.pdf), [Blocks Guide](https://www.fractalaudio.com/downloads/manuals/fas-guides/Fractal-Audio-Blocks-Guide.pdf). The FM3 manual retrieved on 8 October 2026 is labelled for firmware 7.x; the Axe-Fx III section was available through indexed official-manual text. Firmware compatibility remains explicit.

## Current implementation and evidence

| Component | Current state | Remaining work |
|---|---|---|
| Runtime and protocol source | Contained release 0.15.0; common reads, backup inspection, guarded load/save/restore, isolated insertion/removal, insertion undo and existing-block Scene MIDI execution/recovery; Windows WinMM backend | FM3/Axe-Fx III physical validation; additional OS backends if needed |
| Capacity/dump layouts | FM9/FM3 512 slots, 8 chunks; Axe-Fx III explicit 512/1024 capacity, 16 chunks | Validate physical hardware/revisions; positive capacity probe remains optional future work |
| Offline Scene MIDI planner | Common version-3 complete slots, PC/CC, no firmware whitelist; legacy sparse input compatible | Existing-block executor consumes complete plans; insertion uses a separate operation with fresh capture/replanning; other-model hardware remains unqualified |
| Saved export | [Resumable inventory](preset-inventory.md), retained-content revalidation, offline completed export; exact captures, integrity/hash manifests, grid/Amp/scenes/Scene MIDI/raw records | FM3/Axe-Fx III saved Scene MIDI offsets provisional; unknown body shapes retained as errors |
| Numeric amp roster and lookup | Captured FM9 12.00 data only | Add separately scoped model/firmware catalogues; keep unknown IDs visible |
| Physical evidence | Toolkit FM9 12.00 reads, Scene MIDI PC/CC edits/batches, recovery, load/save/restore and isolated insertion/removal qualified separately; actual Claude client evidence retained | Add FM3/Axe-Fx III physical validation and independent semantic comparisons |

The historical `skills/fractal-fm9` path stays stable; its normalized planner now supports all three models, while its FM9 byte references retain their own scope. See the [runtime guide](runtime.md).

## Milestone acceptance criteria and result

1. A checkout of this repository contains its protocol source, tests and build inputs and works without PresetMaestro present.
2. The same public operations resolve FM9, Axe-Fx III and FM3 device definitions. No migrated FM9-only gate remains in a supposedly common reader or planner.
3. Offline tests exercise every model path, wrong-model replies, capacity boundaries, differing dump shapes and preservation of unrelated data. Synthetic or external fixtures retain their provenance and do not count as physical tests.
4. A saved read exports a common snapshot with model, firmware, established capacity, relevant capabilities and source integrity evidence. Plans use those facts and preserve addressing conventions.
5. Hardware reports identify the model/firmware and actual operations verified. Missing hardware is an evidence gap; failed or unimplemented functionality is reported separately. No cross-model hardware success is inferred from an FM9 run.

Release 0.3.0 meets the contained-source, common model-path and normalized offline contract criteria. FM9 12.00 saved-read/runtime behavior has a [physical pass](hardware-validation.md), with additional [Scene MIDI saved/live qualification](scene-midi-reading.md). FM3/Axe-Fx III remain physically unverified, and their Scene MIDI saved profiles are provisional. Each model's own editor catalogue confirms its field-bank starts; other parameters must still use their model-specific catalogue. See [architecture](architecture.md) and [capability audit](capability-audit.md).

## Subsequent editor milestone, release 0.4.0

[Common typed editor commands and an empty-preset pilot](editing-pilot.md) are implemented for all model definitions. FM9 12.00 physical PC/CC save/reload/default preservation and original recovery passed. FM3/Axe-Fx III remain physically unverified; no model-wide gate prevents their qualification. Existing-block Scene MIDI execution now has a separate 0.7.0 operation. A single combined insertion/configuration transaction, occupied-row replacement and availability/DSP catalogues remain missing. General block insertion is implemented separately; it is not limited to Scene MIDI. Complete same-model preset restoration and isolated insertion have separate operations and qualification records.

## Complete preset restoration

The [0.5.0 restoration sequence](preset-restore.md) uses common plan, transaction and verification code across FM9, FM3 and Axe-Fx III, with model-sized transfers and explicit capacity. There is no FM9-only functional gate. The [validation record](preset-restore-validation.json) states actual hardware scope. External FM3 audition evidence does not count as toolkit physical testing. Unknown upload word-count/header variants are rejected without weakening file inspection; no cross-model preset conversion is attempted.

## Isolated block insertion

The [common insertion executor](block-insertion-execution.md) accepts explicit models, caller-resolved instance IDs and chosen isolated cells. It retains model-specific grid and dump dimensions, with no FM9-only execution gate. Synthetic paths cover all model/capacity definitions; [physical scope](block-insertion-validation.json) is qualified separately. The saved-scene body field and instance-record projection remain provisional for other hardware until their physical tests pass. Matching protocol builders do not establish device availability, parameter ordinals or DSP resources.

FM9 12.00 passed 52 insertions of one caller-selected audio block type, complete existing-data preservation and saved/reload verification. All 51 existing Scene MIDI tables were preserved and all 460 non-target saved slots were unchanged. This is isolated placement evidence, not qualification of connected audio routing, arbitrary block families or other models.

## Existing-block message execution and undo

The [0.7.0 executor](scene-midi-execution.md) uses common exact plan/scope, body preservation and independent live-table checks across all four model/capacity definitions. Explicit verified-result backup undo reuses their own model-sized restore plan; later saved/unsaved changes are refused. FM9 12.00 passed one populated PC edit/undo, preserving all 511 other saved slots during editing and restoring all 512 original slots exactly. That 0.7.0 trial did not test populated CC or batches; later 0.8.0 and Claude trials qualify those separately. FM3/Axe-Fx III physical execution remains untested. Their absence is an evidence boundary rather than an FM9-only functional gate; see [the record](scene-midi-execution-validation.json).

## Persistent recovery

[Release 0.8.0](scene-midi-recovery.md) uses the same durable original/result record, resume and explicit batch-undo contract across every model/capacity definition. Two-target PC/CC and interrupted-process paths have synthetic tests; physical scope is separately stated in [the qualification record](scene-midi-recovery-validation.json). No FM9-only functional gate is added.

The separate 0.8.0 physical trial qualifies two populated targets with PC/CC, fresh-process retained-result verification and explicit batch original-backup undo on FM9 12.00. All 510 non-target slots stayed unchanged and all 512 originals matched after undo. Later scoped Claude cancellation after verified restoration and query-only continuation passed; interruption during mutation and other-model physical operation remain unqualified.

## Local MCP model contract

[The 0.12.0 adapter](mcp.md) uses the same 26 tools and contained model definitions for FM9, FM3 and both Axe-Fx III capacities. Real relocated stdio/helper checks cover backups, decode, PC/CC planning, isolated insertion review and recovery import across all four definitions. The amp-attribution tool preserves its independently limited catalogue. Actual host-guided Claude hardware scope is recorded in [client validation](claude-desktop-validation.json); it covers FM9 12.00 and does not qualify other physical models.

## Block identity catalogue

The [contained block catalogue](../knowledge-base/catalog/block-identities.json) and [lookup/export procedure](../knowledge-base/workflows/block-identification.md) provide separate FM9, FM3 and Axe-Fx III source-qualified identities and June 2023 documented instance limits. Common mapping evidence comes from the independently pinned three-model Gen-3 implementation, not a transplanted FM9 table. Manufacturer-only Axe-Fx III mappings remain explicitly unqualified on FM9/FM3. Dynamic Distortion numeric IDs remain unmapped. This fills name lookup while leaving exact-firmware availability, DSP resources, parameters and insertion defaults unqualified; it adds no hardware compatibility claim.

## Isolated block removal and insertion undo

[Release 0.12.0](block-removal-execution.md) removes exact isolated instances through common model-aware scopes. Receipt-based insertion undo requires complete original equality and refuses later changes. Actual Claude ordinary removal and insertion undo passed on FM9 12.00 for one isolated audio family; all 512 final saved captures matched the independent baseline. Connected removal, arbitrary families and other-model hardware remain unqualified; see [validation](block-removal-validation.json).

Live amp-roster ranges, absolute scene/channel/bypass controls and bounded status sampling are implemented for FM9, FM3 and both Axe-Fx III capacities. Own-model roster parameter addresses are FM9 10, FM3 6 and Axe-Fx III 0. [Synthetic validation and protocol limits](live-tools-validation.json) are separate from historical hardware records.

## Bounded performance controls, release 0.36.0

[Tempo, tuner and looper](performance-controls.md) use one shared capture-bound contract at all four model/capacity definitions. Each model's own manual supplies whole-number 20–250 BPM bounds. Axe-Fx III MIDI specification 1.13+ supplies command forms; pinned Gen-3 code independently binds tempo queries by model and pinned FM9 code supplies tempo read/set. Other FM9/FM3 performance forms are explicitly inferred model-addressed adaptations. All have synthetic execution coverage only. Tuner and single events remain sent-unverified; tempo queries require review and unknown loaded tempo changes stop for reconciliation. No hardware compatibility, global-tempo restore or loop-audio verification is claimed.

[Preset organization](preset-organization.md) uses the same bounded whole-image compiler and guarded batch contract for FM9, FM3 and both Axe-Fx III capacities. Copy/relocation preserves donor default scenes through separate guarded before-Save/after-Reload alignment, while renaming preserves the existing default. All organization and alignment evidence is synthetic; historical same-slot FM9 restore/editing success does not qualify these new sequences.
