# Single-preset restoration

Release 0.5.0 adds a common, explicitly authorized full-preset upload, verification, save and reload path for FM9, FM3 and Axe-Fx III. The ordinary CLI remains read-only. Physical qualification is recorded separately in [the validation record](preset-restore-validation.json); synthetic model coverage is not proof of hardware compatibility.

## Transfer evidence

The pinned [ForgeFX driver](https://github.com/sKuhLight/ForgeFX/blob/c22862a5b2f2078f3cb92a2735e51f94c39a0062/server/src/drivers/gen3.ts), `Gen3Driver.loadPresetBytes`, uses the [forgefx-midi helper](https://github.com/sKuhLight/forgefx-midi/blob/553d24b7409302908d5c7e46a71b45e07dffdc05/src/devices/gen3/presetDump.ts), `retargetPresetDumpToEditBuffer`. The helper explicitly claims FM3 live validation for auditioning. Its older file-level writeback caveat remains; neither source supplies a manufacturer upload specification or retained physical trace for this toolkit.

The researched transfer changes the first two header payload bytes to `7F 7F` for temporary memory and recomputes that header's frame XOR. The remaining header fields, every chunk and the footer stay byte-identical. Replaying a stored-slot header is forbidden: it could address persistent storage. A backup's opaque original address never authorizes a destination.

`PresetUploadPlan.Create` requires the expected source hash, strict single-preset framing, exact model-sized chunk count, frame/footer integrity, raw CRC and bounded decompression. Upload additionally requires the researched word-count header and chunk shape. Unknown header variants remain available for inspection, without upload permission. No format conversion, payload reserialization or integrity normalization occurs.

## Caller inputs and sequence

The caller supplies the model, actual capacity, recorded firmware, zero-based destination slot, expected scene, source backup and hash, retained addressed saved baseline and hash, and coordinated MIDI ports. No preset, placement, channel or port defaults are supplied. Axe-Fx III capacity must be explicit. The current selected target and scene must already match.

1. Retain integrity-checked backups. A full unit backup is a separate precaution; this operation consumes one preset file.
2. Identify the actual model and firmware. Capture a fresh saved baseline and a complete current edit-buffer dump. Require saved bytes to match the authorized baseline hash and complete live raw/body bytes to equal that baseline. Unknown unsaved work is rejected.
3. Bind one immutable plan and `PresetRestoreScope` to one session. Upload once to temporary memory, holding the request gate for the whole transfer. Upload acknowledgements and unsolicited parameter notifications are monitored without filling an unused query queue; rejection still poisons the session.
4. Read the complete loaded image and require exact raw/body equality with the source. Check target/scene before and after. A successful send or acknowledgement does not enable Save by itself.
5. Recheck the selection and send one exact-target Save. Read the complete saved image, compare all raw/body bytes, and repeat the saved read.
6. After saved verification, recheck selection and Reload once. If Reload selects another scene, send one reviewed absolute scene command only after the saved result remains byte-identical and the loaded body differs solely in its selected-scene word, with opaque raw bytes preserved. Verify the complete loaded and saved contents again. Preserve the source's saved default scene through exact body comparison.

Any timeout, cancellation, rejection or ambiguous transport failure ends that session. There is no automatic replay, retry, cleanup or recovery upload. Reconnect for read-only reconciliation first. A scope cannot transfer verification to another session. One coordinated caller must own the entire restore sequence; unrelated writers or forwarding loops must be stopped.

Selection checks are observations rather than a device lock. They cannot detect a brief change and return, or guarantee preservation if another owner edits the buffer between verification and saving. Full inventories also consist of sequential reads rather than an atomic unit-wide snapshot. Keep the device under exclusive control throughout qualification.

`ReadSession.EditBufferAsync` is a distinct live capture, never a saved-slot export. It requires the same strict payload integrity as file inspection; it does not claim to support every arbitrary unsaved buffer. `PreparePresetRestoreAsync`, `UploadPresetAsync`, `VerifyUploadedPresetAsync` and `VerifyRestoredSavedAsync` implement the sequence. `IPresetRestoreTransport` exposes plan-bound frames rather than an arbitrary SysEx send operation.

## Opt-in hardware qualification

Build `tests/Fractal.HardwareChecks/Fractal.HardwareChecks.csproj`. The following is a template: replace every angle-bracket value with a caller-supplied value; it is not a runnable default.

```text
dotnet tests/Fractal.HardwareChecks/bin/Debug/net10.0/Fractal.HardwareChecks.dll
  --hardware --restore-preset --input <INPUT> --output <OUTPUT>
  --model <MODEL> --destination <NEW_PRIVATE_DIRECTORY>
  --preset <ZERO_BASED_SLOT> --scene <SCENE> --firmware <VERSION>
  --backup <SOURCE_FILE> --expected-hash <SOURCE_SHA256>
  --current-backup <ADDRESSED_SAVED_FILE> --current-hash <BASE_SHA256>
```

For Axe-Fx III add `--capacity <512_OR_1024>`. The checker rejects corrupt files, mismatched hashes and wrong baseline addresses before opening ports. It captures the entire saved inventory before and after, compares every other slot byte-for-byte, and retains each phase privately. Qualifying a changed-image trial also requires returning the authorized target to its original image and verifying that return.

The 100 ms inter-frame spacing, 200 ms rejection observation and 30 second transfer deadline are toolkit policy, not manufacturer requirements. The external project's serial CDC pacing is not treated as a general MIDI rule.

This operation covers a same-model single preset. Bank, system, firmware, user cab, cross-model conversion, batches and populated Scene MIDI editing are outside its scope. Cable removal, power loss and interrupted-transfer recovery require separate hardware trials. FM3 external evidence is distinct from toolkit testing; Axe-Fx III currently has synthetic coverage only. Public records retain model/firmware, counts and neutral evidence hashes; personal captures, names, targets and port choices stay private.

FM9 12.00 passed two complete restoration trials: changed image and return to original. Full loaded/saved/reloaded contents matched exactly, and all 511 other saved slots were unchanged in both. FM3 and Axe-Fx III toolkit physical tests remain missing. See [the redacted qualification record](preset-restore-validation.json).
