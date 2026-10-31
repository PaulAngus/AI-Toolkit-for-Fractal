# Read-only hardware validation

## FM9, firmware 12.00 — 8 October 2026

Toolkit release **0.2.1** passed the connected FM9 read-only run over the Windows WinMM backend. The full run took **199.2 seconds**, including repeat and failure-recovery checks. The [machine-readable result](hardware-fm9-12.00.json) records evidence hashes and exact checks. Personal names, raw captures and traces are retained in a local ignored private folder and are not distributed.

| Check | Observed result |
|---|---|
| Identity and firmware | FM9 / 12.00 through validated replies |
| Live scenes and current state | Eight scene names read; stable selection at the observed checks |
| Saved capacity | All 512 slots, zero failed reads or body-decode errors |
| Saved content | 254 populated / 258 empty presets; 296 Amp blocks; 254 Scene MIDI blocks |
| Integrity | Frame XOR, footer word XOR, raw CRC and bounded Huffman decoding passed |
| Repeat and boundary reads | Nine samples, including bank boundaries and last slot; wire/raw/body bytes identical |
| Preset-name correlation | Exact-address name replies matched the sampled saved names |
| State preservation | Checked after every 32 reads, before/after repeats and after recovery; selection unchanged |
| Timeout and cancellation | Valid identity requests sent; actual replies deliberately suppressed; bounded failure and no automatic replay |
| Late reply / port recovery | Held late replies rejected; poisoned sessions sent no further request; close/reopen succeeded |
| Transmission trace | 586 requests, all within the read-only allowlist; 5,473 received frames including unrelated traffic |
| Export verification | All 512 capture-file hashes and snapshot-file hash checked independently |

All nine earlier pilot captures also matched the full scan's capture/raw/body hashes. The active preset's eight saved scene names matched its live scene-name replies. All decoded numeric Amp IDs were present in the retained FM9 12.00 roster; this is a plausibility check, not an independent editor comparison of every channel/model.

## Findings retained as general protocol knowledge

Captured FM9 12.00 chunks repeat **`00 08`** in their leading fields; these fields are not established sequence numbers. Treat them as opaque while requiring the model's complete transfer size, footer checksum, raw CRC and bounded decompression. A uniqueness interpretation failed qualification. Synthetic fixtures exercise repeated fields and replacement/corruption without borrowing personal preset bytes.

All 512 qualified FM9 12.00 bodies contain a **350×1-marked, 736-byte terminal tail**. The model definition recognizes only that exact shape and preserves the entire tail opaquely; its semantic contents are unknown. Interpreting it as an ordinary effect record failed decoding of nine integrity-valid images in a partial qualification run. Unknown shapes still fail decoding while retaining valid captures. This layout must not be copied to another model without evidence.

The CLI now returns a structured error for `InvalidDataException`. Both failed pilot stages remain in the private evidence; their outcomes are included in the machine-readable public record. Body header words are labelled as opaque values; a semantic saved-default-scene field has not been qualified. Exact raw/body preservation remains the basis for preserving that setting during future edits.

The planner was exercised against the complete real snapshot. It correctly reports **254 conflicts** because all populated presets already contain Scene MIDI blocks whose saved message encoding is still unread. No changes were applied. Block presence does not establish message type/channel/value or downstream behavior.

## Validation limits

Only the connected **FM9 / 12.00** has physical toolkit evidence. FM3 and both Axe-Fx III capacities retain their common implementation and synthetic checks; they have not been physically tested here. Scene MIDI message decoding, persistent writes and restore were not qualified by this read-only run; consult their separate operation records. Navigation, physical cable removal, driver hangs, audio continuity and independent editor/front-panel parameter comparisons were not tested. State observations cannot rule out a transient change and return between checks.

## Reproduce deliberately

Use the .NET 10 SDK, the appropriate model/capacity and actual port indices. Close other port owners. This harness always requires an explicit hardware flag and is excluded from ordinary CI:

```powershell
dotnet run --project tests/Fractal.HardwareChecks/Fractal.HardwareChecks.csproj -- --hardware --input INPUT --output OUTPUT --model MODEL --destination private/hardware-new-run
python scripts/summarize_hardware_validation.py --evidence private/hardware-new-run --output docs/hardware-new-result.json
```

FM3 uses `--model FM3`; Axe-Fx III uses `--model "Axe-Fx III" --capacity 512` or `--capacity 1024`. This opens real MIDI ports and scans saved capacity. It sends no navigation, editor, save, restore or PC/CC commands. A failure stops the run, records it and exports completed captures as a partial result where possible. Do not label a partial result a hardware pass.

The later [Scene MIDI reading milestone](scene-midi-reading.md) qualifies saved table offsets and current live reads in release 0.3.0. The 0.2.1 results and limitations above remain the scope of that earlier run.

## Later write qualification

The read-only run above is unchanged historical evidence. Release 0.4.0 adds [scoped empty-preset editing and exact recovery](editing-pilot.md), with [redacted results](editor-pilot-validation.json). Only FM9 12.00 was physically tested; that editing pilot did not qualify full-preset restoration. See the separate [0.5.0 operation](preset-restore.md) and [record](preset-restore-validation.json) for current scope.
