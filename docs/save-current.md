# Save an already-loaded preset

Release 0.4.2 adds `PresetSaveScope` and a deliberately opted-in hardware checker. It saves the current edit buffer once to one authorized slot, then verifies the entire saved image against an integrity-checked expected backup. The backup is comparison evidence; this operation never transmits its file contents. Full-file upload was outside that release and is a separate [0.5.0 operation](preset-restore.md).

Use this when a known preset has already been loaded or restored into the edit buffer and needs to be stored persistently. Saving replaces the saved target with the current buffer. A channel/status query cannot establish complete live-buffer equality; only the complete saved readback establishes the result after the write. Do not use this as a substitute for inspecting unknown unsaved work.

The harness requires an exact selected slot, scene and firmware, expected block channels using model-qualified numeric instance IDs, the expected backup file and its SHA-256, and an addressed saved base capture with its SHA-256. It validates files before opening ports, identifies the device, checks selection/name, compares the entire fresh saved base, checks all eight live scene names and expected channels, retains a fresh pre-save capture, and rechecks selection. It sends Save once, retains readback, compares every raw/body byte without exclusions, then repeats the saved read. It stops on any failure and does not retry, reload, upload or automatically recover.

Build and invoke only for an explicitly authorized device operation:

```powershell
dotnet build tests/Fractal.HardwareChecks/Fractal.HardwareChecks.csproj
dotnet tests/Fractal.HardwareChecks/bin/Debug/net10.0/Fractal.HardwareChecks.dll --hardware --save-current --input INPUT --output OUTPUT --model MODEL --destination NEW_PRIVATE_DIRECTORY --preset ZERO_BASED_SLOT --scene SCENE --firmware VERSION --expected-channels EFFECT_ID:CHANNEL --backup EXPECTED_FILE.syx --expected-hash EXPECTED_SHA256 --current-backup SAVED_BASE.syx --current-hash BASE_SHA256
```

Replace placeholders with verified values. Channels and scenes are one-based; slots are zero-based. Comma-separated channel pairs may be supplied. Axe-Fx III requires `--capacity 512` or `--capacity 1024`. The ordinary CLI and default transport remain read-only. The contained implementation accepts FM9, FM3 and Axe-Fx III definitions; physical qualification is currently FM9 12.00 only.

`PresetSaveScope` rejects all operations except Save to its exact model/slot. Both `WinMmTransport` and `ReadSession` enforce it. `AuthorizePresetSave` allows only one send attempt per session, including uncertain outcomes. The editor envelope comes from the pinned [setParam.ts implementation](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/axe-fx-iii/setParam.ts); this is implementation evidence, with physical results separately recorded.

The [redacted validation record](save-current-validation.json) reports one successful populated-preset save, complete original raw/body equality, repeated persistence readback and unchanged selection. It qualifies persistence of an already-loaded buffer; no arbitrary upload stream, reload test, other-slot comparison or other-model hardware test is implied. Private before/after captures and traces remain excluded from distribution.

Full-file upload is a separate [0.5.0 restore operation](preset-restore.md), with its own [qualification record](preset-restore-validation.json). The save-only evidence above remains scoped to persistence of an already-loaded image.
