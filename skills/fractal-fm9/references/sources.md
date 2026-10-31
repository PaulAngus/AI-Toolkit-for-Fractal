# Sources and provenance

Imported knowledge date: **7 October 2026**. The summaries qualify model, firmware, observation date, and validation form. Links to a branch may evolve; pinned source links identify the imported committed documents. The local working-tree hashes are recorded in the toolkit's `docs/provenance.json`.

## Manufacturer sources

- [FM9 owner's manual](https://www.fractalaudio.com/downloads/manuals/FM9/FM9-Owners-Manual.pdf): user-level presets, scenes, MIDI setup, and configurable controls. Reviewed during toolkit initialization; this is not documentation of the private Scene MIDI editor envelope.
- [Axe-Fx III MIDI for third-party devices, revision 1.4](https://www.fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf): published live-control/query format for **Axe-Fx III**. FM9 applicability requires the separate recorded hardware evidence.
- [FM9 downloads](https://www.fractalaudio.com/fm9-downloads/): obtain manuals, firmware, editor, and release information from the manufacturer. A firmware download's availability does not extend the toolkit's verified coverage.

## Technical evidence from the originating project

Initial checkout HEAD: `078a1226559751030884aad4d95384dee37c969c`. Some imported material was uncommitted, so HEAD alone does not identify the whole local snapshot.

- [Shared core contract](https://github.com/PaulAngus/Preset-Maestro/blob/078a1226559751030884aad4d95384dee37c969c/PresetNameSync.Core/README.md): framing, assembly, stored names, port ownership, serialized requests, read-only boundary.
- [Scene-name and saved-dump notes](https://github.com/PaulAngus/Preset-Maestro/blob/078a1226559751030884aad4d95384dee37c969c/SCENE-NAMES.md): wire format, stored/live distinction, and initial hardware scope.
- [Connection and firmware notes](https://github.com/PaulAngus/Preset-Maestro/blob/078a1226559751030884aad4d95384dee37c969c/FRACTAL-DEVICE-INFORMATION.md): rejected query, captured identity/name transaction, source-verified firmware query, and limitations.
- [Hardware test record](https://github.com/PaulAngus/Preset-Maestro/blob/078a1226559751030884aad4d95384dee37c969c/docs/hardware-tests.md): 5 October 2026 hardware scope, failures, later successful rerun, and compatibility boundaries.
- [Protocol research](https://github.com/PaulAngus/Preset-Maestro/blob/078a1226559751030884aad4d95384dee37c969c/docs/fractal-protocol-research.md): technical observations and evidence categories. Its early statements must be read alongside subsequent connection and hardware records; application designs are outside toolkit scope.
- [Fixture provenance](https://github.com/PaulAngus/Preset-Maestro/blob/078a1226559751030884aad4d95384dee37c969c/PresetMaestro.Tests/Fixtures/Fractal/README.md): original saved-preset replay fixture.
- `docs/midi-query-reliability.md` in the imported working tree: 7 October query pacing, diagnostic observations, and lack of physical validation of improvement. This file was untracked at import, so there is no pinned public source link for that snapshot.

## Open implementations

These are implementation sources, not manufacturer specifications or newly performed hardware tests:

- [mcp-midi-control Gen-3 sources](https://github.com/TheAndrewStaker/mcp-midi-control/tree/main/packages/fractal-gen3): saved-preset/container/body decoding and editor research. Assess reuse before creating another codec. This repository has not vendored this implementation.
- [ToneCommand FM9 protocol](https://github.com/monzta1/ToneCommand/blob/3c97d6cff34c8b9ca842b79bed21154052411047/fm9/protocol.py) and [firmware read](https://github.com/monzta1/ToneCommand/blob/3c97d6cff34c8b9ca842b79bed21154052411047/fm9/device.py): pinned firmware-query implementations. API boundaries that omit `F0`/`F7` shift reported offsets.
- [ForgeFX firmware protocol](https://github.com/sKuhLight/forgefx-midi/blob/553d24b7409302908d5c7e46a71b45e07dffdc05/src/shared/firmware.ts): additional Gen-3 firmware-query implementation; does not expand local FM9 validation.

Scene MIDI field mappings summarize recorded FM9 12.00 device operations on 2 October 2026. The procedure retains validation limits and the default-scene preservation requirement. Private history, backups and the temporary writer are not distributed.

Scene MIDI banks were independently checked in pinned own-model catalogues: [FM9](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/fm9/params.ts), [FM3](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/fm3/params.ts), [Axe-Fx III](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/axe-fx-iii/params.ts). Search MIDIBLOCK / CMD_*_BEGIN. This is source evidence; saved/live PC/Off qualification was performed only on FM9 12.00. Other model profiles and active CC cases remain physically untested.
