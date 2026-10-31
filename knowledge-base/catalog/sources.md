# Curated Fractal and MIDI sources

Release 0.38.0 adds [offline songs and setlist proposals](../../docs/setlists-songs.md) against retained complete or partial inventories through shared CLI/MCP. DEFAULT stays symbolic and missing dependencies are reported. Editor interchange and live system execution are deferred pending complete codec, preservation and recovery evidence; no hardware qualification is added.

- `toolkit-template-library`: [private template and swap validation](../../docs/template-library-validation.json), 14 October 2026. Complete donor integrity, explicit row/scene scope, library paging and shared guarded execution have synthetic coverage across all definitions; no physical compatibility or factory-default claim.

Start with the [reading map](../reference-map.md). Authority, access limits and lineage are retained below. Machine-readable metadata: [sources.json](sources.json).

- `toolkit-edit-read-profiles`: [Offline editing read profiles](../../docs/editing-read-profiles.md). Contained 0.14.0 candidate saved audio/state/name reads, exact prefix association, model-specific editor mappings and directed grid edges. Wider saved-value calibration, special routing and new writes remain unqualified. Reference date: 2026-10-11. [Measured software scope](../../docs/audio-routing-validation.json).

- `fractal-midi-spec` [Axe-Fx III MIDI for third-party devices](https://www.fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf): Published framing and live/query commands for Axe-Fx III; FM9 use requires separate recorded observations.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `fm9-manual` [FM9 owner's manual](https://www.fractalaudio.com/downloads/manuals/FM9/FM9-Owners-Manual.pdf): Preset, scene, MIDI configuration and device concepts; does not specify the private editor write envelope.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `fm9-downloads` [FM9 downloads](https://www.fractalaudio.com/fm9-downloads/): Manufacturer firmware, editor and manual resources; current availability must be rechecked.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `fractal-amp-basis` [Axe-Fx II owner's manual](https://www.fractalaudio.com/downloads/manuals/axe-fx-2/Axe-Fx-II-Owners-Manual.pdf): Historical manufacturer amplifier descriptions; not an FM9 firmware availability or numeric ID table.
  Reference date: 2026-10-07
  Validation: Historical review; access and independence limits retained. Not a new assertion of current availability.
  Historical access: Downloaded PDF; extracted and inspected printed pp165-168 (PDF pp177-180).
  Lineage: Fractal Audio publication; may itself have informed the community sources.
- `fractal-wiki-amps` [Community amp-model reference](https://wiki.fractalaudio.com/wiki/index.php?title=Amp_models): Amplifier families and variant reading; qualify revisions and do not infer connected-firmware availability.
  Reference date: 2026-10-07
  Validation: Historical review; access and independence limits retained. Not a new assertion of current availability.
  Historical access: Search-index text of full mapping-table portions and targeted model sections; direct page returned HTTP 403. Indexed snapshot was reported crawled about three months before review.
  Lineage: Community synthesis with linked Fractal statements; shared lineage with Ampdex.
- `gen3-format` [Gen-3 preset implementation](https://github.com/TheAndrewStaker/mcp-midi-control/tree/main/packages/fractal-gen3): Saved preset/container/body decoding research; branch may change and is not manufacturer specification.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `gen3-body` [Gen-3 preset body layout](https://raw.githubusercontent.com/TheAndrewStaker/mcp-midi-control/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-gen3/src/presetBody.ts): Placed-block, channel/bypass and modifier layout; requires device/firmware validation.
  Reference date: 2026-10-08
  Validation: Pinned implementation retrieved for contained read-only release; not a physical toolkit test.
- `gen3-editor-write` [Gen-3 editor parameter write implementation](https://github.com/TheAndrewStaker/mcp-midi-control/blob/main/packages/fractal-midi/src/gen3/axe-fx-iii/setParam.ts): Candidate editor envelope context; FM9 persistent writes require independent recorded validation.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `tonecommand-protocol` [ToneCommand FM9 protocol](https://github.com/monzta1/ToneCommand/blob/3c97d6cff34c8b9ca842b79bed21154052411047/fm9/protocol.py): Pinned FM9 framing and firmware-query implementation.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `tonecommand-firmware` [ToneCommand FM9 firmware read](https://github.com/monzta1/ToneCommand/blob/3c97d6cff34c8b9ca842b79bed21154052411047/fm9/device.py): Pinned firmware read; offsets depend on whether F0/F7 are included.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `forgefx-firmware` [ForgeFX firmware query](https://github.com/sKuhLight/forgefx-midi/blob/553d24b7409302908d5c7e46a71b45e07dffdc05/src/shared/firmware.ts): Pinned Gen-3 firmware-query implementation; does not extend hardware scope.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `recorded-reads` [Recorded FM9 read and compatibility evidence](https://github.com/PaulAngus/Preset-Maestro/blob/078a1226559751030884aad4d95384dee37c969c/docs/hardware-tests.md): FM9 12.00 saved reads, roster and state/navigation observations; only the technical findings are curated here.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `recorded-scene-format` [Recorded stored/live scene protocol evidence](https://github.com/PaulAngus/Preset-Maestro/blob/078a1226559751030884aad4d95384dee37c969c/SCENE-NAMES.md): Stored transfer and live scene correlation observations; originating application details are excluded.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `recorded-discovery` [Recorded identity and firmware evidence](https://github.com/PaulAngus/Preset-Maestro/blob/078a1226559751030884aad4d95384dee37c969c/FRACTAL-DEVICE-INFORMATION.md): Identity/firmware transactions and source qualification.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `existing-core` [Credited reusable protocol core](https://github.com/PaulAngus/Preset-Maestro/blob/078a1226559751030884aad4d95384dee37c969c/PresetNameSync.Core/README.md): Current source of reusable read-only encoding/decoding; independent packaging is pending.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `apple-network-midi` [Apple network MIDI protocol](https://developer.apple.com/library/archive/documentation/Audio/Conceptual/MIDINetworkDriverProtocol/MIDI/MIDI.html): Archived RTP-MIDI/session and recovery-journal behavior.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `windows-endpoint-api` [Windows MIDI endpoint connection API](https://microsoft.github.io/MIDI/sdk-reference/MidiEndpointConnection/): Endpoint-specific transmission limits, batch sends and partial failures.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `windows-sysex-collision` [Multi-client SysEx collision tracking](https://github.com/microsoft/MIDI/issues/60): Tracks multi-client delivery limitations; issue status must be rechecked rather than assumed fixed or abandoned.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `voes-6x6` [VOES MX-6x6 firmware/editor](https://www.voes.be/6x6.html): Controller-specific static labels and generic commands; confirm installed firmware before applying.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `voes-commands` [VOES standard MX command reference](https://www.voes.be/commands.html): Fractal-specific controller commands and feedback; version-qualified.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `voes-downloads` [VOES firmware and editor downloads](https://www.voes.be/downloads.html): Identify matching firmware/editor documentation; toolkit has not performed controller firmware writes.
  Reference date: 2026-10-07
  Validation: curated historical reference; current content/availability not newly verified
- `recorded-scene-midi` [Recorded FM9 Scene MIDI editing observations](../../docs/evidence.md): Limited new-block PC saved/readback evidence and default-scene preservation; original writer/backups not packaged.
  Reference date: 2026-10-02
- `recorded-roster` [Recorded FM9 12.00 amp ID/name roster](../../knowledge-base/catalog/fm9-12-amp-roster.json): 336 numeric ID/name pairs; original capture hash retained; no newly executed roster read.
  Reference date: 2026-10-03
- `ampdex` [Ampdex transcription of Clayton Welch Amplifier Library Guide v1](https://github.com/rinkashimikito/ampdex/blob/1210a8c2110bfdd14d567b6262383a602657d310/src/data/amps.json): Contemporary manufacturer/model attribution, not an independently validated FM9 firmware roster.
  Reference date: 2026-10-03
  Validation: Historical review; access and independence limits retained. Not a new assertion of current availability.
  Historical access: Downloaded JSON; every supplied name reconciled to its entry; MODEL fields inspected.
  Lineage: Welch guide draws on Yek, Fractal Wiki and forum; not independent of Wiki.
- `archived-ares-models` [Archived Axe-Fx III amp-model table, Ares 12.05](https://archive.axefx.fr/AxeFX%20III/Docs/FAS_Amps%20Models%20Axe%20III%20Ares12.05.pdf): Historical B-15 Portaflex product attribution only. No confirmation of exact revision/year, FM9 firmware availability or numeric IDs.
  Reference date: 2026-10-03
  Validation: Historical review; access and independence limits retained. Not a new assertion of current availability.
  Historical access: Search-index excerpt of entry 189, Porta-Bass, checked on 2026-10-03.
  Lineage: Third-party archive of a model-reference table; authorship and independence from other community guides have not been established.
- `fullerton` [Fractal Audio ICONS Fullerton Complete product model table](https://shop.fractalaudio.com/products/icons-fullerton-complete): Primary name-to-Fender mapping; plugin names can differ from processor names. Not firmware roster evidence.
  Reference date: 2026-10-03
  Validation: Historical review; access and independence limits retained. Not a new assertion of current availability.
  Historical access: Opened current official product page, AMPS sections Volumes 1-4.
  Lineage: Fractal Audio publication.
- `wiki-draft` [Fractal Audio Wiki: Greg Ferguson model draft](https://wiki.fractalaudio.com/wiki/index.php?title=User%3AGreg_Ferguson): Community model descriptions; firmware/version scope may differ.
  Reference date: 2026-10-03
  Validation: Historical review; access and independence limits retained. Not a new assertion of current availability.
  Historical access: Cited by the supplied research; not independently fetched in this follow-up.
  Lineage: Supplied follow-up research and its cited sources; not counted as independent confirmations.
- `vibroverb-admin` [FractalAudio: Vibroverb Custom modification](https://forum.fractalaudio.com/threads/vibroverb.214777/post-2697459): Normal-channel triode removed, increasing gain and changing vibrato-channel bias. Exact Custom reissue identification also uses the supplied research and existing guide.
  Reference date: 2026-10-03
  Validation: Historical review; access and independence limits retained. Not a new assertion of current availability.
  Historical access: Direct forum post checked; FractalAudio, 2025-08-15, post 24.
  Lineage: Supplied follow-up research and its cited sources; not counted as independent confirmations.
- `jubilee-admin` [Fractal firmware 24.03: Silver Jubilee reference](https://forum.fractalaudio.com/threads/axe-fx-iii-firmware-24-03-release.200627/page-16): Reported change from Lerxst Omega to Silver Jubilee; 2555 detail comes from supplied model-reference research.
  Reference date: 2026-10-03
  Validation: Historical review; access and independence limits retained. Not a new assertion of current availability.
  Historical access: Cited by supplied research; direct page could not be independently confirmed in this follow-up.
  Lineage: Supplied follow-up research and its cited sources; not counted as independent confirmations.
- `bright-release-repost` [Historical Fractal release notes reproduced on Guitariste](https://www.guitariste.com/forums/pedales-effets-homestudio%2Cfractal-audio-axe-fx-ii-user-s-club%2C406485%2C12900.html): Historical non-Top-Boost Bright-channel description, not exact production year or current DSP identity.
  Reference date: 2026-10-03
  Validation: Historical review; access and independence limits retained. Not a new assertion of current availability.
  Historical access: Cited by supplied research; not independently authenticated as an official publication.
  Lineage: Supplied follow-up research and its cited sources; not counted as independent confirmations.
- `marsha-admin` [FractalAudio: original Marsha versus later BE-100 references](https://forum.fractalaudio.com/threads/which-are-the-new-be-hbe-models.114510/latest): Original Marsha and later Mark Day BE-100 are separate references. Does not independently establish current 2010 numeric IDs.
  Reference date: 2026-10-03
  Validation: Historical review; access and independence limits retained. Not a new assertion of current availability.
  Historical access: Direct forum statement checked; FractalAudio, 2016-05-07, post 5.
  Lineage: Supplied follow-up research and its cited sources; not counted as independent confirmations.
- `marsha-announcement` [Fractal firmware 10.0 announcement published by Premier Guitar](https://www.premierguitar.com/fractal-audio-systems-releases-firmware-10-0): Historical Marsha HB/HBE development with Dave Friedman, not a current firmware roster.
  Reference date: 2026-10-03
  Validation: Historical review; access and independence limits retained. Not a new assertion of current availability.
  Historical access: Opened article, 2010-06-18; labelled press release.
  Lineage: Supplied follow-up research and its cited sources; not counted as independent confirmations.
- `v10-release-repost` [Axe-Fx II firmware 10 release notes reproduced on Rig-Talk](https://www.rig-talk.com/forum/threads/axe-fx-ii-firmware-10-release-notes.134534/): Historical ODS HRM serial 0213, Fox ODS Mid-switch pair, and 1963 Vibrolux descriptions.
  Reference date: 2026-10-03
  Validation: Historical review; access and independence limits retained. Not a new assertion of current availability.
  Historical access: Opened reproduced release text; original authorship not independently authenticated here.
  Lineage: Supplied follow-up research and its cited sources; not counted as independent confirmations.
- `fm9-12-release` [Fractal FM9 firmware 12.00 release notes, 2026-07-07](https://www.fractalaudio.com/downloads/firmware-presets/fm9/12p0/fm9_dsp_rel_12p00.zip): New JVM410 Crunch channel models, PVH Block Clean, Deluxe Tweed Normal and Bright rename. Numeric IDs separately confirmed from attached FM9.
  Reference date: 2026-10-03
  Validation: Historical review; access and independence limits retained. Not a new assertion of current availability.
  Historical access: Official ZIP downloaded; FM9 Release Notes.pdf, first page extracted and read.
  Lineage: Official Fractal firmware publication.
- `fractal-blocks-guide` [Fractal Audio Blocks Guide](https://www.fractalaudio.com/downloads/manuals/fas-guides/Fractal-Audio-Blocks-Guide.pdf): Scene MIDI block operation, PDF page 89 (zero-based page 88); product/firmware capabilities still require matching.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `fractal-footswitch-guide` [Fractal Audio Footswitch Functions Guide](https://www.fractalaudio.com/downloads/manuals/fas-guides/Fractal-Audio-Footswitch-Functions-Guide.pdf): Fractal onboard/FC functions, preset/scene/channel actions and labels. Does not describe third-party VOES firmware.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `fm3-manual` [FM3 owner manual](https://www.fractalaudio.com/downloads/manuals/FM3/FM3-Owners-Manual.pdf): FM3 capacity, grid and block limits; reference does not establish toolkit hardware compatibility.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `axe3-manual` [Axe-Fx III owner manual](https://www.fractalaudio.com/downloads/manuals/axe-fx-3/Axe-Fx-III-Owners-Manual.pdf): Axe-Fx III capacity, MIDI and block limits; keep device-specific limits distinct.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `fm9-editor-guide` [FM9-Edit user guide](https://www.fractalaudio.com/downloads/manuals/FM9-Edit/FM9-Edit-User-Guide.pdf): Independent editor comparison of scene/channel/model values and editing behavior; not a wire protocol specification.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `fm9-editor-downloads` [FM9-Edit downloads](https://www.fractalaudio.com/fm9-edit/): Resolve the current compatible editor and user guide when an older PDF is unavailable.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `gen3-dump` [Gen-3 saved transfer framing](https://raw.githubusercontent.com/TheAndrewStaker/mcp-midi-control/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-gen3/src/presetDump.ts): parsePresetDump, extractPresetName, frame counts, checksum and footer XOR. Read direction and restore direction have different evidence.
  Reference date: 2026-10-08
  Validation: Pinned implementation retrieved for contained read-only release; not a physical toolkit test.
- `gen3-tables` [Gen-3 effect and parameter tables](https://github.com/TheAndrewStaker/mcp-midi-control/blob/main/packages/fractal-gen3/src/gen3BodyTables.ts): Effect IDs, parameter layout and historical roster candidates; connected FM9 12.00 roster supersedes older candidate amp tables.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `gen3-container` [Gen-3 preset container integrity](https://github.com/TheAndrewStaker/mcp-midi-control/blob/main/packages/fractal-midi/src/shared/presetContainer.ts): Container CRC, compression and image parsing. Validate exact model and firmware before adoption.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `gen3-capture-guide` [FM9 physical capture procedure](https://raw.githubusercontent.com/TheAndrewStaker/mcp-midi-control/main/packages/fractal-midi/docs/capture-guides/testing-fm9.md): Candidate external hardware capture procedure; not a recorded toolkit test result.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `forgefx-grid` [ForgeFX preset grid codec research](https://github.com/sKuhLight/ForgeFX/blob/main/docs/preset-grid-codec.md): Candidate grid layout research; inspect model/firmware and test against captured saved data.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `codec-origin` [Original fractal-syx-codec provenance](https://github.com/drewmerc302/fractal-syx-codec): Credited upstream format research. Link returned 404 during this curation; use retained local summaries and accessible Gen-3 implementations.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `windows-midi-reporting` [Windows MIDI endpoint reporting](https://microsoft.github.io/MIDI/sdk-reference/Reporting/MidiReporting/): Endpoint enumeration/reporting and host diagnostics; endpoint availability depends on installed runtime and backend.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `windows-midi-batch` [Windows MIDI batch-send implementation](https://raw.githubusercontent.com/microsoft/MIDI/main/src/api/Client/WinRT/t/core/MidiEndpointConnection_SendMultipleMessages.cpp): Batch send sizes, partial failures and synchronization. Branch code must be matched to the installed runtime.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `voes-mx6` [VOES MX-6 product and firmware](https://voes.be/mx6.html): MX-6 hardware and standard firmware; distinguish it from MX-6x6 firmware/editor behavior.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `wiki-section-index` [Amp-model Wiki section index](https://wiki.fractalaudio.com/wiki/api.php?action=parse&page=Amp_models&prop=sections&format=json): Section anchors retrieved 2026-10-04. Supplementary reading links do not prove amp attribution or firmware availability.
  Reference date: 2026-10-07
  Validation: Curated from useful historical references; current contents not newly verified unless explicitly recorded below.
- `reviewed-amp-identities` [Reviewed FM9 12.00 real amplifier identities](../../knowledge-base/catalog/fm9-12-amp-identities.json): All 336 observed ID/name pairs; reviewed amplifier identities and 162 family/original groups. Exact years/circuits and variants retain qualification. No compatibility claim for other firmware or models.
  Reference date: 2026-10-07
- `winmm-header` [Windows MIDIHDR buffer contract](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/ns-mmeapi-midihdr): Native struct fields, preparation and driver buffer ownership.
  Reference date: 2026-10-08
  Validation: Official API contract read during implementation; toolkit FM9 12.00 physical read/recovery checks passed, cable removal/driver hangs untested.
- `winmm-input-buffer` [Windows midiInAddBuffer](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-midiinaddbuffer): Prepared input buffers and returned buffer reuse.
  Reference date: 2026-10-08
  Validation: Official API contract read during implementation; toolkit FM9 12.00 physical read/recovery checks passed, cable removal/driver hangs untested.
- `winmm-input-reset` [Windows midiInReset](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-midiinreset): Stop input and return pending buffers before releasing storage.
  Reference date: 2026-10-08
  Validation: Official API contract read during implementation; toolkit FM9 12.00 physical read/recovery checks passed, cable removal/driver hangs untested.
- `winmm-output-long` [Windows midiOutLongMsg](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-midioutlongmsg): Asynchronous SysEx output and prepared buffer lifetime.
  Reference date: 2026-10-08
  Validation: Official API contract read during implementation; toolkit FM9 12.00 physical read/recovery checks passed, cable removal/driver hangs untested.
- `toolkit-read-runtime` [Contained read-only runtime 0.3.0](../../docs/runtime.md): Common FM9/FM3/Axe-Fx III reads/export; FM9 12.00 physical evidence, other models synthetic-only.
  Reference date: 2026-10-08
  Validation: 38 .NET checks, 51 Python tests and five CLI pipeline groups, including reusable block placement; prior full FM9 scan and focused Scene MIDI physical comparison. No new hardware operations in the placement update.
- `toolkit-fm9-12-hardware` [Toolkit FM9 12.00 read-only physical validation](../../docs/hardware-validation.md): All 512 saved slots; repeat and addressed-name checks; observed state preservation; receive-loss cancellation/timeout and reopen. Read-only, one model/firmware; no editor semantic comparison or writes.
  Reference date: 2026-10-08
  Validation: 46 physical assertions passed after retained failed pilots; private wire captures retained, redacted report and hashes public.
- `gen3-fm9-midi-banks` [FM9 editor Scene MIDI parameter catalogue](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/fm9/params.ts): Own-model MIDIBLOCK bank starts 0/64/128/192; no firmware version or physical compatibility inferred.
  Reference date: 2026-10-08
  Validation: Pinned source retrieved; bank symbols/numbers compared independently for each model. Synthetic decode tests; hardware coverage recorded separately.
- `gen3-fm3-midi-banks` [FM3 editor Scene MIDI parameter catalogue](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/fm3/params.ts): Own-model MIDIBLOCK bank starts 0/64/128/192; no firmware version or physical compatibility inferred.
  Reference date: 2026-10-08
  Validation: Pinned source retrieved; bank symbols/numbers compared independently for each model. Synthetic decode tests; hardware coverage recorded separately.
- `gen3-axe-fx-iii-midi-banks` [Axe-Fx III editor Scene MIDI parameter catalogue](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/axe-fx-iii/params.ts): Own-model MIDIBLOCK bank starts 0/64/128/192; no firmware version or physical compatibility inferred.
  Reference date: 2026-10-08
  Validation: Pinned source retrieved; bank symbols/numbers compared independently for each model. Synthetic decode tests; hardware coverage recorded separately.
- `gen3-param-ordinals-negative` [Cross-model parameter ordinal reuse negative finding](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/docs/research/primitives/_negative/gen3-paramid-reuse-across-model-bytes.md): Matching wire codecs do not establish matching effect parameter ordinals across models; use own-model catalogues.
  Reference date: 2026-10-08
  Validation: Pinned author research read; not independently reproduced in toolkit.
- `toolkit-scene-midi-reading` [Toolkit Scene MIDI saved/live reading validation](../../docs/scene-midi-reading.md): FM9 12.00 current saved/live PC/Off comparison and offline redecoding of 512 retained captures; FM3/Axe-Fx III and active CC hardware cases untested.
  Reference date: 2026-10-08
  Validation: 10 focused physical assertions; 512 bodies decoded; 254 unchanged populated presets in an idempotent plan. Private bytes retained, public redacted evidence.

- `toolkit-block-planning`: [General offline block placement contract](../../skills/fractal-fm9/references/block-insertion.md). Contained implementation; caller-resolved instance IDs and chosen cells across all models. Grid-only qualification; hardware availability/defaults/DSP and insertion remain unqualified.

- `gen3-editor-actions-pinned` [Pinned Gen-3 editor insertion, clear, typed fields, store and reload encodings](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/axe-fx-iii/setParam.ts): Select/insert/clear companion, float32 typed value and LE14 save/reload envelopes. External model observations retain their limits; no full-dump restore protocol.
  Reference date: 2026-10-10
  Validation: Pinned source retrieved; local FM9 12.00 pilot independently qualified the stated operations. FM3/Axe-Fx III toolkit hardware tests missing.

- `toolkit-editor-pilot` [Toolkit empty-preset Scene MIDI physical editing and recovery qualification](../../docs/editor-pilot-validation.json): FM9 12.00 single empty preset: owned Scene MIDI PC/CC, save/reload, default preservation and exact original recovery. No populated-preset or full-dump restoration claim.
  Reference date: 2026-10-10
  Validation: 62 complete physical assertions, 106 sends, six integrity-checked exports; three failed attempts and two explicit recoveries retained privately with public hashes.

- `toolkit-backup-inspection` [Actual backup integrity and live/saved comparison](../../docs/backup-file-validation.json): One private FM9 file passed complete offline integrity; read-only FM9 12.00 observations distinguish loaded and saved data, with two changed channel words in a controlled baseline. Opaque file headers are preserved without assigning a destination. Read-only observations do not establish that persistence occurred.
- `toolkit-save-current` [Save an already-loaded preset](../../docs/save-current.md) and [physical evidence](../../docs/save-current-validation.json): FM9 12.00 single Save to an authorized populated target, 18 checks, 22 reads, complete repeated raw/body equality and selection preservation. The expected backup was never transmitted. Common model scopes have synthetic coverage; that save-only run did not qualify full-file upload; current restore evidence is separate. Other-model hardware remains unqualified.

## Full-preset upload research

- `forgefx-upload-driver`: [Pinned Gen-3 temporary-buffer upload driver](https://github.com/sKuhLight/ForgeFX/blob/c22862a5b2f2078f3cb92a2735e51f94c39a0062/server/src/drivers/gen3.ts). Gen3Driver.loadPresetBytes: retarget temporary memory then paced/queued transfer across Gen-3 definitions. Open implementation; not manufacturer specification. SHA-256 `576571d91af18d651c06598f5856a101035e79dbbe62d0539b3faffd10572042`.
- `forgefx-upload-helper`: [Pinned edit-buffer retargeting helper](https://github.com/sKuhLight/forgefx-midi/blob/553d24b7409302908d5c7e46a71b45e07dffdc05/src/devices/gen3/presetDump.ts). retargetPresetDumpToEditBuffer: 7F7F header and new XOR; payload unchanged. Explicit external FM3 audition claim coexists with older writeback caveat; no external trace retained. SHA-256 `ed6bff970ad08849cca056dfed37c7a7987b3a5e3f4d273caa154ff6517a8f8b`.
- `toolkit-preset-restore`: [Contained sequence](../../docs/preset-restore.md), [qualification record](../../docs/preset-restore-validation.json). Common model implementation; measured hardware scope is recorded separately.

FM9 12.00 passed two complete restoration trials: changed image and return to original. Full loaded/saved/reloaded contents matched exactly, and all 511 other saved slots were unchanged in both. FM3 and Axe-Fx III toolkit physical tests remain missing. See [the redacted qualification record](../../docs/preset-restore-validation.json).

- `toolkit-block-insertion`: [Isolated insertion execution](../../docs/block-insertion-execution.md) and [redacted validation](../../docs/block-insertion-validation.json). Common caller-driven operation, exact preservation before Save, repeated saved read and Reload. FM9 12.00 passed 52 insertions, 51 existing MIDI tables preserved and 460 non-target saved slots unchanged; pre-edit timeouts and fresh reconciliation retained. Other block families/models, routing and parameters remain unqualified. Exact body/instance and command source locators are in the [reading map](../reference-map.md).

- `toolkit-scene-midi-execution`: [Existing-block message execution and verified-result undo](../../docs/scene-midi-execution.md), [separate qualification record](../../docs/scene-midi-execution-validation.json). Complete planner/raw before-values, once-only ordered fields, full body/header/padding and independent table checks, saved/reload receipt and explicit later-edit refusal. Hardware PC/CC/model scope is recorded separately from synthetic coverage; no automatic rollback, selective merge, block removal or resume claim.

- `toolkit-scene-midi-recovery`: [Persistent recovery and batch undo](../../docs/scene-midi-recovery.md), [qualified scope](../../docs/scene-midi-recovery-validation.json). Common original/result records, exact-head import, explicit fresh-state resume and guarded batch undo. Synthetic interruption and two-target PC/CC coverage across all models; physical scope recorded separately. Reuses pinned editor/upload sources rather than new protocol assumptions.

- `toolkit-recovery-migration`: [Offline migration validation](../../docs/scene-midi-recovery-migration-validation.json). Synthetic all-model compatible-record checks, four locally retained release 0.11.0 pending records and one actual privately retained release 0.8.0 FM9 12.00 terminal record migrated to 0.30.0 with identical item evidence and unchanged sources. No MIDI ports opened; other historical records and fresh device state remain unqualified.

- `toolkit-preset-load`: [Explicit preset load](../../docs/preset-load.md) and [physical scope](../../docs/scene-midi-recovery-validation.json). Retains current saved/loaded and target captures; binds exact selection, refuses late changes and permits only one caller-requested Load per scope. Two separate FM9 12.00 loads passed with both saved images unchanged; other models have synthetic coverage.

## Local MCP sources

Use [the local interface](../../docs/mcp.md) and [its measured qualification](../../docs/mcp-validation.json). The interface document retains exact primary-specification sections and code symbols.

- `mcp-2025-stdio`: [MCP negotiated stdio transport](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports). stdio: UTF-8, newline framing, JSON-RPC-only stdout and stderr diagnostics Primary specification consulted for the deliberately supported negotiated 2025 revision family; newer stateless drafts are not implemented.
- `mcp-2025-lifecycle`: [MCP initialization and negotiated lifecycle](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle). Initialization: initialize request/result, protocolVersion negotiation, notifications/initialized Primary specification consulted for the deliberately supported negotiated 2025 revision family; newer stateless drafts are not implemented.
- `mcp-2025-tools`: [MCP tool discovery, results and annotations](https://modelcontextprotocol.io/specification/2025-11-25/server/tools). Tool definition/inputSchema, structuredContent with compatible TextContent, error handling and annotation hints Primary specification consulted for the deliberately supported negotiated 2025 revision family; newer stateless drafts are not implemented.
- `mcp-2025-resources`: [MCP static resource discovery/read](https://modelcontextprotocol.io/specification/2025-11-25/server/resources). Capability declaration, resources/list and resources/read, contents/URI/MIME fields Primary specification consulted for the deliberately supported negotiated 2025 revision family; newer stateless drafts are not implemented.
- `mcp-2025-cancellation`: [MCP request cancellation](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/cancellation). notifications/cancelled: requestId matching, stop processing and discard results Primary specification consulted for the deliberately supported negotiated 2025 revision family; newer stateless drafts are not implemented.
- `mcp-python-sdk`: [Official MCP Python client SDK](https://github.com/modelcontextprotocol/python-sdk). Optional independent client interoperability; SDK is not a toolkit server/build dependency. Installed official Python SDK 1.30.0 passed initialization, all 21 schemas, structured status/knowledge, resource discovery/read and default-live refusal with an offline configuration.
- `toolkit-local-mcp`: [Contained local MCP interface and qualification](../../docs/mcp-validation.json). 21 typed tools/static references, retained knowledge, canonical offline planning, bounded real stdio-to-contained-dotnet validation, exact local operator review approval, once-only execution and durable evidence discovery. Four model/capacity definitions checked with relocated package and real helper binaries; execution/uncertainty boundaries tested with synthetic runtimes. No MIDI ports opened or new adapter hardware qualification.

- `toolkit-block-catalogue` [Contained model-qualified block catalogue](block-identities.json): factual IDs/names with independently scoped model mapping evidence and June 2023 manufacturer instance limits. Retrieved/hashes checked 2026-10-11; synthetic validation is recorded in [the qualification record](../../docs/block-catalogue-validation.json). No new hardware qualification.

- `gen3-fm9-audio-parameters`: [FM9 audio editor parameter catalogue](https://raw.githubusercontent.com/TheAndrewStaker/mcp-midi-control/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/fm9/params.ts). Own-model editor parameter IDs and documentary display hints; not saved-file offsets or qualified value scales. Exact firmware not established. Retrieved 2026-10-11; SHA-256 `e6c32114fa75fd995669e15b9205a05757acbe1c3e6254e6192bc64cc38ee243`.

- `gen3-fm3-audio-parameters`: [FM3 audio editor parameter catalogue](https://raw.githubusercontent.com/TheAndrewStaker/mcp-midi-control/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/fm3/params.ts). Own-model editor parameter IDs and documentary display hints; not saved-file offsets or qualified value scales. Exact firmware not established. Retrieved 2026-10-11; SHA-256 `e2a2ddd58ffd9299c33cb13558531a757627edb167bc3cf89410fc9c8b18e8b9`.

- `gen3-axe-fx-iii-audio-parameters`: [Axe-Fx III audio editor parameter catalogue](https://raw.githubusercontent.com/TheAndrewStaker/mcp-midi-control/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/axe-fx-iii/params.ts). Own-model editor parameter IDs and documentary display hints; not saved-file offsets or qualified value scales. Exact firmware not established. Retrieved 2026-10-11; SHA-256 `a104ca6f463f2217ee4eae7e915e6d33a4dfe74e9bd5a38e353008e36dd66817`.

- `gen3-grid-layout`: [Gen-3 incoming grid row-mask interpretation](https://raw.githubusercontent.com/TheAndrewStaker/mcp-midi-control/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/axe-fx-iii/gridLayout.ts). Corroborates destination-cell incoming mask from the previous column. Live grid source is not a saved-word write encoding or toolkit hardware qualification. Retrieved 2026-10-11; SHA-256 `847ba8f83f14e88685ba7bd6634d988e81117fd224bd12315974cfad8235e205`.

- `toolkit-live-tools`: [contained live tools and synthetic checks](../../docs/live-tools-validation.json). All model definitions; no new hardware evidence.
- `toolkit-batch-midi`: [general edit batches and configured MIDI routing](../../docs/batch-midi-validation.json). All model definitions; synthetic transaction, recall, assigned CC and host-fed input mapping checks. No new hardware evidence.
- `toolkit-path-listener`: [automatic paths and bounded controller listening](../../docs/path-listener-validation.json). All-model synthetic scope; no new hardware tests.
- `winmm-short-input`: Microsoft [MIM_DATA](https://learn.microsoft.com/en-us/windows/win32/multimedia/mim-data) and [MIM_ERROR](https://learn.microsoft.com/en-us/windows/win32/multimedia/mim-error), consulted 12 October 2026. Expanded short-message packing, timestamps and input failure; no driver/device qualification.
- `gen3-live-roster-parser`: [pinned request/reply parser](https://raw.githubusercontent.com/PaulAngus/Preset-Maestro/078a1226559751030884aad4d95384dee37c969c/PresetMaestro.Tests/HardwareProtocol.cs), revision `078a1226559751030884aad4d95384dee37c969c`, SHA-256 `64b78990a9c4453a74ad5541767d32a260d782d9ca425a98054a9340788ccb73`. Retrieved 2026-10-12. Strict descriptor/string format, not termination or new compatibility evidence.

- `toolkit-automatic-placement`: [automatic placement and isolation checks](../../docs/automatic-placement.md), synthetic all-model planning/execution-scope checks dated 2026-10-12. No hardware qualification added.

- `toolkit-private-parameters`: [private mappings, reads and notification validation](../../docs/live-parameters-validation.json), synthetic all-model checks and narrowly scoped FM9 12.00 Amp plus seven additional family table reads through 2026-10-13. Pinned `gen3-editor-actions-pinned` additionally supplies private table/echo locators documented in [the contract](../../docs/live-parameters.md). Notification emission, parameter writes and other-model hardware remain unqualified.

- `toolkit-calibrated-authoring`: [capture-backed setters and modifier validation](../../docs/calibrated-authoring-validation.json), dated 2026-10-12. Synthetic all-model preservation and execution; no new hardware calibration. Pinned `gen3-body` supplies the qualified chain modifier layout.

Built-in saved scales and modifier fields: Big Saurus, Axe-Fx III SysEx & Parameter Maps revision `4463ae9b7e388d2fe98e24fdd7068b1c35865277`, source IDs `axe32-word-laws`, `axe32-map`, `axe32-modifiers`. Selected factual data, including two positive direct-integer saved fields, is adapted under CC BY 4.0; [attribution, hashes and qualification](../../docs/builtin-scales.md). Its modifier table identifies saved Scale/Offset/Attack/Release and Damping Type word positions, while manufacturer documentation describes the controls without serializing them; display conversions and selector labels remain unqualified. Toolkit software checks are recorded separately in `toolkit-builtin-scales`.


## Performance controls evidence

The source register records a 14 October 2026 own-model tempo review and byte hashes for the FM9, FM3 and Axe-Fx III owner manuals and Axe-Fx III third-party MIDI specification. `gen3-performance-commands` retains pinned command forms and model-bound tempo queries; `tonecommand-protocol` independently supplies FM9 tempo read/set. `gen3-tempo-query-limit` preserves the upstream query-as-set report with unspecified early firmware and no retained original hardware capture. The [performance validation](../../docs/performance-controls-validation.json) distinguishes synthetic runtime coverage from inferred model adaptations and unknown hardware behavior.

## Preset organization evidence

`fractal-preset-management` supplies manufacturer operation guidance; `gen3-body` has a 14 October 2026 pinned-byte review of the uncompressed 32-byte name field. `toolkit-preset-organization` records [all-model synthetic checks](../../docs/preset-organization-validation.json) for full-image assignments, immutable overlaps, source/destination guards, guarded default-scene alignment and eligible completed-target restoration. No physical relocation or external reference/asset qualification is inferred.

- `fractal-setlists-songs-manual`: [manufacturer mini manual](https://www.fractalaudio.com/downloads/manuals/fas-guides/Fractal-Audio-Setlists-Songs-Mini-Manual.pdf), version 1.04, 2023-10-30; fetched/hash retained 2026-10-14. Domain structure and system persistence only.
- `toolkit-setlists-songs`: [software validation](../../docs/setlists-songs-validation.json); offline all-model evidence, no system codec or hardware qualification. `axe32-map` and `gen3-tempo-query-limit` retain additional bounded storage/negative review in sources.json.

## Editor export and System format evidence

`fm9-editor-export-binary`, `fm3-editor-export-binary` and `axe3-editor-export-binary` identify official macOS packages independently extracted on 15 October 2026. [Exact package/executable hashes and x86-64 locators](../../docs/setlists-system-format-research.json) support the [partial XML assessment](../../docs/setlists-system-format-research.md). Manufacturer binary provenance does not make this static interpretation a manufacturer-published format specification. Proprietary bytes/disassembly remain private; no actual export, complete System sample, editor round trip or device operation was tested.

`fractal-system-reset-guidance` supplies official system-data scope and backup guidance, reviewed through indexed web text; direct byte retrieval returned HTTP 403, so no original hash is claimed. `toolkit-setlists-system-format-review` records the bounded findings and remaining sample/integrity/preservation/recovery gates. Existing preset integrity/restore qualifications do not extend to System data.
