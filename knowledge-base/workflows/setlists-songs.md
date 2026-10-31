# Plan songs and setlists offline

For editor exports and System backups, use the [focused format evidence and sample requirements](../../docs/setlists-system-format-research.md). Static analysis identifies `.fas-songs` / `.fas-setlist` and partial XML construction in each model's editor. Raw numbering, NONE/DEFAULT, empty positions, actual file acceptance and System framing remain unqualified; do not translate proposal numbers directly into editor XML.

Use the [feasibility map and native proposal contract](../../docs/setlists-songs.md). The shared [planner](../../src/Fractal.Core/SetlistSongs.cs) accepts caller-supplied definitions and a retained complete or partial [preset inventory](preset-inventory.md). It is independent of AI providers and has no transport dependency.

1. Confirm exact model, capacity and recorded firmware context. Keep preset slots zero-based and song/section/set identifiers one-based. Use a matching current-release inventory or explicitly migrate a supported prior-release journal.
2. Describe numbered song sections and ordered set entries. Choose explicit scene or symbolic DEFAULT for enabled sections; omit a preset target for disabled sections. Preserve deliberate repeated songs.
3. Run CLI `plan-setlists` or MCP `fractal_plan_setlists`. Inspect ordered previews, inventory coverage, hashes and unresolved dependencies. Missing captured presets are missing evidence, not claims that a hardware slot is empty.
4. Retain the proposal privately. Resolve dependencies with additional caller-supplied definitions or approved captures, then replan. Replanning is deterministic and does not update hardware.

Manufacturer structure/minimums and immediate system persistence are documented in the linked assessment. Naming repertoire, JSON schema and missing-reference handling are toolkit policies. Source hashes identify reviewed bytes; all new software validation is synthetic across FM9, FM3 and both Axe-Fx III capacities.

The proposal is not an editor file or a device patch. Existing system records and footswitch assignments are unknown. Preset backups cannot provide system recovery, DEFAULT may change after a later preset edit, and preset relocation does not move these references automatically. Live or interchange extensions require their own complete codec, preservation and recovery evidence.
