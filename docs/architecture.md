# Architecture and implementation sequence

## Provider-independent core

The toolkit owns the task contract: inspect a device or exported data, calculate proposed changes, preserve a backup, apply a requested operation, and verify the resulting saved state. AI clients interpret user intent and supply parameters. They do not improvise protocol bytes or establish compatibility by guessing.

| Layer | Responsibility | Current state |
|---|---|---|
| Knowledge and evidence | Scope, protocols, successful procedures, limits, source links | Curated here |
| Offline tools | Snapshot planning, qualified amp lookup, knowledge/source search | Implemented without MIDI or AI dependencies |
| Reusable protocol code | Framing, assembly, dump integrity, decoding | Contained versioned protocol source; origin/source hashes retained |
| Device runtime | Port ownership, requests, backups, live operations, saved readback | Windows read/export and experimental empty-preset editor pilot implemented; full-image restore implemented separately in 0.5.0; isolated insertion and existing-block Scene MIDI execution/explicit undo implemented; persistent Scene MIDI recovery and local MCP implemented |
| CLI / MCP adapters | Expose the same typed operations to people and AI clients | Read-only/offline CLI and local stdio MCP implemented; remote/cloud transport pending |
| Provider packaging | Discovery and installation metadata | Portable skill available; provider-specific packaging pending |

The Python planner has no MIDI or AI dependencies. It produces a proposed task from normalized data. The contained .NET runtime exports version-3 normalized snapshots consumed by this planner. No AI service participates in framing, decoding or planning.

## Device family support

The runtime targets **FM9, Axe-Fx III and FM3 from the outset**, with one public operation contract. Device definitions supply identity/model addressing, established preset capacity, grid/block limits, dump layout/integrity bounds, firmware-specific parameters and catalogue scope. Common code owns transport coordination, reply correlation, normalized snapshots, task planning and result reporting.

Release 0.2.1 replaced migrated FM9-only assumptions with explicit model definitions, capacity/capability data and model-specific dump sizes. The legacy skill path remains stable while its planner now accepts all three models. Offline planning should operate on validated normalized data for all three devices, while executors apply model/firmware-qualified encodings. FM9 observations do not establish another model's byte offsets or numeric amp IDs.

Missing physical evidence is reported separately from missing implementation or a genuine device limitation. The design must not make another model impossible simply because initial testing used an FM9. See [device support and acceptance criteria](device-support.md).

## Self-contained repositories and code ownership

Every consumer is self-contained. Its repository contains its supported Fractal functionality and the complete source, build inputs and tests needed to maintain it without another checkout.

Maintain model-qualified protocol code with explicit versions. Each consumer carries a complete versioned source release in its own repository, including build files, required non-personal fixtures, licence/notices, source revision and hashes. A released package may be included as a local build artifact, but an external package feed or the other source repository must not be needed to obtain the Fractal code. A bare binary without its corresponding maintainable source is insufficient for this requirement.

For example, a consumer application may contain protocol release 1.0 while the toolkit contains release 1.1. Both build from their own contents; each upgrades only through an explicit reviewed change. An upstream location is useful for developing and publishing new releases, but existing consumers remain usable if it is unavailable. This deliberately permits complete release snapshots in both repositories while keeping protocol development coordinated and avoiding independent rewrites.

Use project references only within the consumer's repository. Do not use sibling-directory paths, another application's configuration, or submodules/download scripts that require a missing repository during ordinary builds. Standard documented prerequisites such as the language toolchain, operating-system MIDI support and device drivers remain separate from the contained project code. This design does not claim that every existing third-party dependency has already been mirrored for a fully offline installation.

The credited initial implementation is `PresetNameSync.Core`, already inside PresetMaestro. Its framing, assembly and stored decoder are adapted here with complete build inputs and synthetic fixtures in contained release 0.2.1. Attribution, origin hashes, modifications and local source hashes are in [protocol-release.json](protocol-release.json). The current offline Python tools already use only code/data contained in this repository and the standard library.

Historical live-write observations do not establish current runtime compatibility. Current typed editor, save, restore and isolated insertion operations have their own maintained implementations and qualified evidence; the ordinary CLI remains read-only. Local MCP exposure uses the separate provider-independent adapter.

## Scene MIDI task

One Scene MIDI block occupies a cell in the **preset** grid. Its table provides message settings for each scene. Inserting eight grid blocks would misrepresent the task.

The [general block operation](../skills/fractal-fm9/references/block-insertion.md) plans insertion of a caller-resolved instance into a chosen empty cell. It reuses existing instances, preserves opaque routing flags and has explicit grid-only qualification. Scene MIDI calls that same operation when its block is absent, using explicit placement. The planner handles PC/CC messages in a chosen message slot across selected scenes. It leaves conflicting existing messages and occupied grid cells unresolved and visible. Execution verifies the current base content hash, integrity-checked backup, hardware and firmware, all plan parameters, and any conflicts before a write. A plan must not grant its own execution authorization.

Readback should verify the intended messages after saving and separately compare preserved content: names, routing, other blocks and messages, and default scene. Loading a preset can discard unsaved edits; restoring the selected preset/scene cannot restore those edits.

## Delivery sequence

1. Preserve the knowledge and original scene-editing findings; test offline planning. This is the initial repository version.
2. **Implemented offline in release 0.2.1:** integrate a complete versioned protocol source release in this repository and add a standalone read-only runtime for FM9, Axe-Fx III and FM3 through explicit device definitions. Verify builds with the other repository unavailable. Exercise identity, firmware, current state, saved reads, fragmentation, timeouts, and unknown firmware across every model path.
3. **Contract/export implemented; FM9 12.00 reads physically validated:** generalize the snapshot/planning contract for all three models, using established capacity and operation capabilities rather than an FM9 firmware whitelist. Produce normalized snapshots and immutable backup manifests from real reads. Compare plans against independent editor displays; report physical verification separately for each model.
4. **Scene MIDI reading/planning implemented in 0.3.0:** complete slots/raw fields, PC/CC planning and FM9 12.00 saved/live qualification. FM3/Axe-Fx III physical profiles remain provisional.
5. **Empty-preset editing pilot implemented in 0.4.0:** common typed commands and scoped execution, independent preservation checks, FM9 12.00 PC/CC persistence/default preservation and exact operation-based recovery; see [editing boundaries](editing-pilot.md).
6. **Full-preset restore implemented in 0.5.0:** immutable same-model upload, complete verification before Save and after Reload, with separately recorded FM9 12.00 qualification.
7. **Isolated insertion implemented in 0.6.0:** exact caller-resolved instance/cell, complete preservation and saved/reloaded verification across explicit targets. FM9 12.00 passed 52 insertions and a full before/after inventory comparison; other-model hardware remains untested.
8. **Existing-block Scene MIDI execution implemented in 0.7.0:** complete planner/base checks, exact ordered PC/CC fields, complete preservation, Save/Reload verification and explicit verified-result backup undo. Physical model/operation scope is qualified separately.
9. **Persistent recovery implemented in 0.8.0:** hash-bound original/result receipt import, flushed pre-attempt checkpoints, explicit fresh-state resume and batch undo. Conflicts and interruptions have separate synthetic/physical qualification; see [the contract](scene-midi-recovery.md).
10. **Local MCP implemented in 0.9.0:** typed inspection, retained references, canonical plans and exact operator-reviewed execution/recovery; complete relocatable packaging and offline client checks. See [the interface](mcp.md). Remote/cloud transport and provider-specific packaging remain separate work.

For cloud clients, the runtime needs a connection from the client to the computer attached to the Fractal device. A local MCP connection and a cloud tunnel have different deployment requirements; select and test the actual client mechanism during adapter implementation.

Release 0.2.1 hardware qualification is recorded in [hardware-validation.md](hardware-validation.md). Actual FM9 chunk fields repeat and are opaque, not sequence numbers. Terminal metadata has an explicit model layout and is preserved without invented semantics. FM3/Axe-Fx III read implementations remain available with their own synthetic evidence; missing physical hardware does not add a common-code FM9-only gate.
