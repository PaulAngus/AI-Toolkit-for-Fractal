# Setlists and songs: feasibility and offline planning

Release **0.38.0** delivers a toolkit-native offline proposal planner. **Go** for bounded planning against retained preset inventories; **no-go** for editor interchange or live system-data execution with the evidence currently reviewed. [Validation](setlists-songs-validation.json) separates software checks from hardware qualification.

The [15 October format investigation](setlists-system-format-research.md) identifies `.fas-songs` and `.fas-setlist` and traces partial XML export structure independently in the three official editors. Actual export samples, numeric/sentinel semantics and complete System backup framing/recovery remain missing. This is static evidence, with no added runtime support or hardware qualification.

## Manufacturer scope

The [Setlists & Songs Mini Manual](https://www.fractalaudio.com/downloads/manuals/fas-guides/Fractal-Audio-Setlists-Songs-Mini-Manual.pdf), version 1.04, 30 October 2023, documents these minimums:

| Model | Minimum firmware | Controller requirement |
| --- | --- | --- |
| FM9 | 3.00 | Onboard switches or FC |
| FM3 | 6.00 | Onboard switches or FC |
| Axe-Fx III, either capacity | 19.06 | At least one FC |

Its structure is 128 numbered songs, six sections per song, four setlists and 32 ordered positions per setlist. Names allow ten characters. Sections select a preset and scene 1–8 or DEFAULT; NONE disables a section. Repeated songs are allowed in a set. Empty set positions belong at the end. Edits persist immediately without preset Save and are included in Fractal-Bot System backups. Compatible editors provide import/export, but the guide does not specify their file formats or MIDI encoding. See PDF pages 1–3; these are manufacturer statements, not new toolkit hardware results.

## Requirement-to-source/code map

| Requirement | Evidence and implementation | Decision / remaining gap |
| --- | --- | --- |
| Model/firmware applicability | Mini Manual p1; [DeviceDefinition](../src/Fractal.Core/DeviceDefinition.cs) and [SetlistSongs](../src/Fractal.Core/SetlistSongs.cs) | Own-model minimum checked; capacity explicit; Axe-Fx III FC dependency reported from caller declaration |
| Names, record identifiers and limits | Mini Manual pp1–2; `SongDefinition`, `SongSection`, `SetlistDefinition` | Offline validation implemented; printable ASCII without trailing spaces is a narrower toolkit policy |
| Song/section and set ordering | Mini Manual pp1–3; `SetlistSongs.Plan` | Distinct identifiers, explicit section numbers and compact ordered set sequences; repeated songs retained |
| Preset/scene dependencies | Mini Manual p1; [PresetInventory](../src/Fractal.Core/PresetInventory.cs), [SnapshotExport](../src/Fractal.Core/SnapshotExport.cs), saved default-scene interpretation in [PresetEditPlan](../src/Fractal.Core/PresetEditPlan.cs) | Integrity-checked captured slots resolve previews; missing captures/song definitions remain unresolved |
| DEFAULT and disabled sections | Mini Manual p1; `SongSection.DefaultScene`, `SongSectionPreview.SceneSelection` | DEFAULT stays symbolic; resolved scene comes from retained saved bytes only; disabled references have no target |
| Complete existing system representation | Mini Manual p1 establishes system scope; open storage notes below | No contained full system decoder or preservation proof; proposals do not merge or replace existing device records |
| System read/export framing | Published [third-party MIDI specification](https://www.fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf), revision 1.4; pinned Gen3 map below | Specification supplies no song/setlist/system transfer contract; symbols and partial storage clues are insufficient |
| Editor file interchange | Mini Manual p1 names the facility; [official-editor static analysis](setlists-system-format-research.md) identifies extensions, partial XML fields and metadata | Actual files, numeric sentinels, complete integrity rules and version/model compatibility remain unqualified; native JSON is not editor-importable |
| Write addressing / sentinels | Pinned GLOBAL storage notes below | Names are text regions, not ordinary float parameters; section/set payloads and addressing remain unresolved |
| Immediate persistence and base comparison | Mini Manual p1; no system transaction scope exists in toolkit | Requires a separate system-data scope; preset Save/Reload and saved-preset guards cannot provide it |
| Full backup, restoration and uncertain writes | Mini Manual p1 identifies Fractal-Bot System backup; [backup workflow](../knowledge-base/workflows/backups-verification.md) | No verified target-specific system restore stream/readback; do not infer recovery from receiving backup bytes |
| Common CLI/MCP contracts and isolation | [CLI](../src/Fractal.Cli/Program.cs), [MCP](../src/fractal_mcp/tools.py), [checks](../tests/Fractal.Checks/SetlistSongChecks.cs) | Shared offline service delivered across all definitions; no transport or execution review |

## Bounded open-source investigation

The pinned [mcp-midi-control Gen3 map](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/docs/devices/axe-fx-iii/SYSEX-MAP.md), SHA-256 `61adf33821a75bdeefcb111923b7b1ad283dc6120110b9e82ed1bbaa5f7890f1`, retains editor-binary symbols for system transfer. The symbol listing explicitly does not establish function-byte assignments. Later static dispatcher analysis in the same document does not supply a complete song/set codec, validated read/export implementation or recoverable write transaction. A search of 75 source files under its Gen3/shared Fractal implementation directories found no song/setlist implementation. The reviewed [ToneCommand protocol](https://github.com/monzta1/ToneCommand/blob/3c97d6cff34c8b9ca842b79bed21154052411047/fm9/protocol.py) likewise supplied no such codec. This is a bounded negative result, not a claim that no implementation exists elsewhere.

The already-pinned [Axe-Fx III map](https://github.com/BigSaurus/axefx-sysex-maps/blob/4463ae9b7e388d2fe98e24fdd7068b1c35865277/maps/axefx_sysex_map.json) contains `/blocks/GLOBAL/note` and `/blocks/GLOBAL/storage_regions_s54`. It reports Axe-Fx III 32.02 observations of song and set name storage. The companion [display-label index](https://github.com/BigSaurus/axefx-sysex-maps/blob/4463ae9b7e388d2fe98e24fdd7068b1c35865277/maps/display_label_index.json), `/blocks/GLOBAL/gaps/unmapped/Songs/Setlists text regions (DO-NOT-SET)`, expressly excludes these regions from parameter writing. Neither supplies a complete interoperable song/section/set transfer and recovery contract. The review read 27 text/JSON files in that revision, with targeted inspection of these locators. Upstream observation claims are not toolkit hardware qualification and do not establish FM9/FM3 layout compatibility. Retained byte hashes are in [the source register](../knowledge-base/catalog/sources.json).

## Native proposal contract

`plan-setlists` consumes schema-1 JSON plus an integrity-checked current-release inventory. Complete selected-slot and partial inventories are both useful. Context must match exact model, capacity and firmware; this is retained evidence, without a freshness or physical-identity claim. Explicitly migrate compatible prior-release inventories first; 0.35.0–0.37.0 schema-1 inventories have been admitted to the existing migration path. Migration does not refresh captures.

- Song identifiers are 1–128, section identifiers 1–6, set identifiers 1–4 and set positions 1–32. Preset slots remain **zero-based**, independent of display offsets.
- `songs` and `setlists` are required arrays. At least one proposed record is needed. Song/section/set names use 0–10 printable ASCII characters without trailing spaces. Identifiers must be distinct within their scope.
- An enabled section specifies `preset_slot` and exactly one `scene` (integer 1–8) or `default_scene: true`. A disabled section omits all three. An omitted section is disabled in the proposal. Section numbers may have gaps.
- Set `songs` is a compact array of song identifiers, including deliberate repeats. An empty array defines an empty proposed set; internal null/empty positions are not accepted. Undefined song identifiers are unresolved dependencies, with set/position retained.
- Optional `active_setlist` must be 1–4. If its definition is absent, the dependency stays unresolved. `fc_controller_present` is optional caller evidence: an Axe-Fx III needs `true` to resolve that dependency.
- Every referenced retained capture is independently decoded and integrity-checked. The preview includes preset/scene names and source hashes. DEFAULT keeps its selection policy while showing the saved default scene at capture time. Missing captures never become assumed defaults. Effective on-device recall, including system configuration, remains unverified by this saved-data preview.

The output is one private `plan.json`, format `toolkit-setlists-songs-proposal`, with canonical request hash, output byte hash, referenced preset wire/raw/body hashes, explicit inventory coverage, ordered previews and location-specific unresolved dependencies. The artifact can contain personal song/preset names and belongs outside distributable source.

Proposals describe only supplied records. They neither inspect nor preserve existing system data, and omissions do not authorize device deletions. Resolved dependencies do not make a proposal executable. Preset organization does not update these references automatically; replan against the resulting inventory. There is no automatic MIDI playback, footswitch assignment, system edit, restore or editor-format conversion.

Synthetic caller example:

```json
{
  "schema_version": 1,
  "model": "FM3",
  "capacity": 512,
  "firmware": "99.07",
  "songs": [{"number": 1, "name": "Song One", "sections": [
    {"number": 1, "name": "Intro", "preset_slot": 0, "default_scene": true},
    {"number": 2, "name": "Solo", "preset_slot": 1, "scene": 8},
    {"number": 6, "name": "Unused"}
  ]}],
  "setlists": [{"number": 1, "name": "Set One", "songs": [1, 1]}],
  "active_setlist": 1
}
```

```powershell
dotnet run --project src/Fractal.Cli -- plan-setlists --request PRIVATE_REQUEST_JSON --inventory PRIVATE_INVENTORY_DIRECTORY --destination NEW_PRIVATE_DIRECTORY
```

MCP `fractal_plan_setlists` accepts `inventory_artifact`, `songs`, `setlists`, optional `active_setlist` and optional `fc_controller_present`. It derives context from the validated inventory, checks configured context when present, and calls the same core via CLI. It creates a private proposal artifact; no live-access permission or execution review is involved.

## Next evidence gate

Keep live execution and editor interchange deferred. The [format investigation and sample matrix](setlists-system-format-research.md) now narrow the next step to non-personal or explicitly approved `.fas-songs`, `.fas-setlist` exports and complete System backup samples with exact model/firmware/editor provenance. Establish framing, integrity, version markers, numbering/sentinels, all affected system fields and unrelated-data preservation before authoring a codec. Only a separate explicitly authorized hardware scope can establish system read/write, immediate persistence, complete restoration and bounded recovery. Do not expand this into an all-parameter mapping campaign.
