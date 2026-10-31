# Song/setlist exports and System backup formats

Offline investigation, **15 October 2026**, against contained release **0.38.0**. The [evidence record](setlists-system-format-research.json) retains exact package/binary hashes and static locators. This extends the [setlist/song assessment](setlists-songs.md); it adds no codec, runtime command or hardware qualification.

**Result:** the editor export file identifiers and a partial XML structure are now established by static analysis of each model's official editor. Numeric semantics, import compatibility and the complete System backup format remain unqualified. Editor interchange and live System execution remain deferred.

## Evidence reviewed

| Primary artifact | Version | Review scope |
| --- | --- | --- |
| [FM9-Edit for macOS](https://www.fractalaudio.com/downloads/FM9-Edit/FM9-Edit-Mac-v1p03p22.dmg) | 1.03.22 | Extracted universal executable; x86-64 export paths and XML writer |
| [FM3-Edit for macOS](https://www.fractalaudio.com/downloads/FM3-Edit/FM3-Edit-Mac-v1p07p17.dmg) | 1.07.17 | Independently extracted x86-64 export paths and matching field construction |
| [Axe-Edit III for macOS](https://www.fractalaudio.com/downloads/Axe-Edit-III/Axe-Edit-III-Mac-v1p14p34.dmg) | 1.14.34 | Independently extracted x86-64 export paths and matching field construction |

Versions were checked against each bundle's `Info.plist` and official download listing. Packages were extracted as data; editors were neither installed nor launched. ARM64 slices and Windows packages were not analyzed. These are manufacturer-produced binaries, but the format interpretation is **toolkit static analysis**, not a published manufacturer specification or observed device behavior. Proprietary packages, executable bytes and disassembly remain private; only factual findings, hashes and locators are published.

No actual `.fas-songs`, `.fas-setlist` export or complete Fractal-Bot System backup was available in this review. No round trip through an editor, device connection, MIDI transmission, restore or persistence test was performed. Editor versions do not identify a connected device's firmware; the reviewed firmware context is unknown.

## Editor export structure

The reviewed export branches construct XML elements/attributes and pass them to a file writer whose XML serialization path emits an XML declaration and uses UTF-8. This is stronger evidence than isolated strings, while remaining short of examining a generated file.

| Item | Static finding in all three reviewed editors | Remaining qualification |
| --- | --- | --- |
| Songs file extension | `.fas-songs` | Filename extension alone is not content validation |
| Songs root | `All-Songs` | Complete accepted import grammar and older/newer versions unknown |
| Setlists file extension | `.fas-setlist` | Same limitation |
| Setlists root | `Setlists` | Same limitation |
| Root metadata | `version`, `deviceId`, `deviceMajor`, `deviceMinor`; reviewed exporters assign string `1` to `version` | Device/firmware metadata interpretation and import compatibility policy need actual exports and separate verification |
| Song record | `Song`, with integer `id` and string `name` attributes | Raw ID origin/range not established by the serializer alone |
| Song sections | Six ordered `Section` children; string `name` and integer `preset`, `scene` attributes | Preset display offset, scene origin, DEFAULT/NONE sentinels and inactive-field preservation unresolved |
| Set record | `Setlist`, with integer `id` and string `name` attributes | Raw set ID origin/range unresolved |
| Set entries | 32 ordered `SongId` children, each with integer `id` | Empty-entry encoding, song numbering and import relocation/remapping unresolved |

The reviewed setlist export branch constructs references rather than embedding `Song`/`Section` definitions. **Inference:** a setlist file alone should not be treated as a self-contained song library. Preserve the corresponding songs export and test dependency/import handling before promising interchange.

The native proposal uses compact sets and one-based task identifiers; the editor serializer emits 32 positions and raw integers. These representations cannot yet be converted by simply copying JSON numbers into XML. The six child positions also need their own section-order interpretation. Keep DEFAULT symbolic in toolkit planning until the file encoding is established.

XML parsing/serialization is visible in the reviewed paths, but a complete integrity contract has not been established. Do not assert that no outer framing, extra validation, size limits or integrity fields exist in other paths/versions. A fabricated XML file that parses successfully would establish only synthetic parser behavior, not editor acceptance.

The FM3 binary contains a firmware-support message referring to 5.04, whereas the manufacturer [Mini Manual](https://www.fractalaudio.com/downloads/manuals/fas-guides/Fractal-Audio-Setlists-Songs-Mini-Manual.pdf) specifies FM3 6.00 or newer. The message is not evidence that all required functionality works on 5.04. The planner's documented 6.00 minimum remains unchanged. FM9 3.00 and Axe-Fx III 19.06 messages agree with the guide; agreement is not hardware qualification.

## System backup boundary

The Mini Manual establishes that songs and setlists persist immediately and belong to a Fractal-Bot System backup. The manufacturer [Reset System Parameters guidance](https://support.fractalaudio.com/en-US/reset-system-parameters-289296) separately identifies Global, I/O, MIDI, FC/layout, tuner and user-scale settings as system data and recommends a System backup before resetting them. This establishes a wider preservation footprint than song/setlist records; it does not serialize that footprint.

The embedded backup/restore code in the reviewed FM9 package contains a same-firmware restore warning and a warning about overwriting Global/FC-related settings. Shared warning text also names Global Blocks; this does **not** establish Global Blocks availability on FM9 or FM3. Model capabilities must come from own-model evidence. These strings are cautions from the application, not a complete backup manifest or a verified recovery transaction.

The pinned [Gen3 protocol map](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/docs/devices/axe-fx-iii/SYSEX-MAP.md) retains `SYSEX_SYSTEM_DUMP` and `SYSEX_A3_SYSTEM_DATA_START/DATA/END` symbols without establishing their numeric assignments. Its later `5A/5B/5C` candidate discussion is insufficient: the same document also traces a scene-blob transfer in that family. A family label or common Fractal checksum is not a unique System discriminator.

| Required System property | Outcome of this pass |
| --- | --- |
| Complete sample, exact device model/capacity/firmware and producing application version | Missing; editor binary version is not backup provenance |
| File versus wire framing, transfer identity, ordering, lengths, termination and completeness | Unqualified |
| Frame/footer/image integrity, packing/compression, version markers and corruption/truncation rejection | Unqualified for System data; do not borrow the preset codec |
| Song/section/set offsets, IDs, active-set state and unrelated/opaque fields | Partial upstream text-storage clues only; no complete layout |
| Destination, immediate persistence, positive completion and stable complete readback | Unqualified |
| Model/firmware-specific restoration and recovery after partial/uncertain writes | Unqualified |

The existing `check-backup` operation accepts one preset transfer only. It must continue to reject other file classes; a System backup must not be relabelled as a preset or passed to the preset upload/Save/Reload transaction. No System request or restore recipe is promoted from this static review.

## Bounded next evidence collection

Start with existing non-personal or explicitly approved private files. Record model, capacity, firmware, editor/Bot version, export selection (one/all), byte length, SHA-256 and whether the file completed successfully. Keep original bytes immutable. Use neutral artifact identifiers in public records; file names, repertoire and system settings stay private.

| Artifact comparison | Question it resolves |
| --- | --- |
| Songs export with first/last song IDs, six named sections, disabled sections, explicit first/last scenes and DEFAULT | Number origins, child order, NONE/DEFAULT, fixed versus variable records and name encoding |
| Set export with repeats, an empty tail, first/last song references; one-set and all-set selections | Reference encoding, 32-position padding, export selection and whether additional paths include dependencies |
| Unmodified export/import/re-export in an isolated, separately authorized environment | Editor acceptance, metadata handling, canonicalization and compatibility; import may immediately change system data |
| Complete System baseline and repeat from the same context | Framing, completeness, stable/volatile regions and integrity candidates |
| Complete paired System captures with exactly one independently recorded song/section/set change | Location/packing and preservation of every unrelated region; broader parameter mapping is unnecessary |
| Own-model samples for FM9, FM3 and both Axe-Fx III capacities where relevant | Evidence-based profiles rather than assumed shared offsets/capacity |

Offline inspection can advance with supplied files. Producing controlled device differences, importing files or testing full restoration requires its own explicitly authorized device scope. Before a codec is offered, establish strict bounded parsing, complete-byte retention, unknown-version refusal, numeric/sentinel semantics and independent sample comparisons. Before a writer is offered, establish exact targeting, unrelated-system preservation and verified complete recovery. Receiving a backup is not proof that it can be restored.

The decision remains **go for native offline planning; defer editor interchange and live System execution**. The next dependency is a small, provenance-complete sample set, not exhaustive parameter research.

## Verification

Focused static checks passed for each of the three binaries: both extension/root paths, metadata/version literals, six section constructors and the 32-entry set loop. Knowledge validation passed with 47 capabilities and 117 sources; source distribution privacy checks passed for 282 files. The unchanged 0.38.0 protocol release hashes verified. Runtime/planner/adapter source was unchanged, so their software pipelines were not rerun. These checks do not validate an actual editor file or System backup.
