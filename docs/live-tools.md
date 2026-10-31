# Live amp roster, controls and sampled state

Release 0.18.0 implements these operations in the shared runtime for FM9, FM3 and Axe-Fx III, including both established Axe-Fx III capacities. CLI and MCP use the same codecs, scope checks and coordinated transport. Software availability is separate from physical qualification: this release adds synthetic checks, not hardware evidence. Existing operator access configuration and exact trusted-host approvals continue to apply.

## Amp roster

`amp-roster --model MODEL --capacity CAPACITY --firmware VERSION --input INPUT --output OUTPUT --start ORDINAL --count COUNT --destination NEW_DIRECTORY` reads an explicit range. Count is 1–1024 and the range must fit the 14-bit ordinal field. Identity and firmware must match before roster queries. Each reply is retained with its hash; `roster.json` is replaced atomically after every entry. Timeout, malformed matching replies, cancellation or selection drift end the capture with partial evidence and no retry. A new deliberate capture uses a new directory/session.

Function `0x01`, sub-action `0x1F`, Amp instance 58 uses each model's own `DISTORT_TYPE` parameter: FM9 10, FM3 6, Axe-Fx III 0. The response validates model, function, checksum, descriptor, instance, parameter, declared string length, MSB-first septet packing, zero padding and NUL-terminated printable ASCII. This is a read-only query distinct from setting the current amp type. Unknown descriptors/encodings fail explicitly.

Replies omit the queried ordinal. Serialized requests and stopping on uncertainty limit ambiguity; late duplicate replies cannot be uniquely correlated. A successful bounded range sets `range_complete=true` and always `roster_complete=false`. Silence and empty/malformed names are not termination markers. The historical 336-entry FM9 12.00 roster remains qualified to that observation. A later single read at the next ordinal stopped with an unknown/empty-name encoding result and did not establish the roster endpoint. No range or firmware-wide count is assumed for other firmware/models.

The completed report can be supplied to `inventory-amp-usage --roster`; absence then concerns only the supplied IDs and still requires complete, decodable saved inventory coverage. Incomplete live ranges are refused as availability catalogues. Names from live firmware do not automatically acquire the contained historical real-amplifier attributions. A new FM9 12.00 read of the explicit 336-ID historical range validated every reply and matched every retained ID/name pair. Offline analysis of a separate complete 512-slot saved inventory returned qualified absence within that range. Neither result proves the firmware has no further amp IDs.

## Absolute live controls

`capture-control` takes the same model, capacity, firmware, port and new-directory options. It captures selection, block status and integrity-checked saved/temporary images, bracketed by current-state reads. The private `context.json` retains unsaved work rather than requiring it to equal the saved image. FM9 12.00 readback used temporary header bytes `7F 00`; the live-control scope also accepts the previously modeled `7F 7F` form without confusing either with an addressed saved capture.

The request is schema version 1 with this context and exactly one operation:

| Kind | Required operation fields | Wire operation |
| --- | --- | --- |
| `scene` | `scene` 1–8 | Function `0x0C`, zero-based scene |
| `channel` | `effect_id`, `channel` A–D within reported channel count | Function `0x0B`, low-first instance ID, zero-based channel |
| `bypass` | `effect_id`, Boolean `bypassed` | Function `0x0A`, low-first instance ID, 0 engaged / 1 bypassed |

The explicit helper is `--hardware --live-control --request REQUEST_JSON --expected-hash SHA256`. `--validate-only true` validates offline without constructing a transport. Execution additionally requires input/output ports and a new evidence directory. The helper's historical `--hardware` opt-in denotes live access, not a hardware-qualification claim.

The scope freezes the entire request. It checks model, firmware, preset identity, live block presence, exact saved and unsaved captures and status immediately before the one command. An already-satisfied intent is a verified no-op. These controls never upload, Save, reload, toggle, automatically undo or replay a command. No configured CC assignment is guessed.

Readback checks the selected scene, unchanged saved image, preserved unrelated loaded body/header, and the exact effective block-state change for channel/bypass. A mapped current-scene assignment word may remain unchanged (effective state only) or become the requested value; arbitrary changes are refused. Scene selection permits only the selected-scene body word to change. Scene Revert or external changes that alter other unsaved content cause a stopped-uncertain result with retained evidence. Scene selection may emit configured MIDI; receiver delivery is not verified here.

`attempt.json`, the frozen request and the observed complete context distinguish verified completion from a sent-but-unverified command. After uncertainty, capture fresh state and reconcile; never reuse the consumed review or blindly repeat the write. There is no automatic rollback that could overwrite later edits.

## Bounded observations

`live-monitor` takes the same connection options plus `--samples 1..120 --interval-ms 100..10000`. It queries current selection and effective block status, brackets each sample for lasting selection drift, and atomically retains `monitor.json`. Indices identify differences between adjacent completed samples. Intervals are waits between completed samples, not a timing guarantee. Cancellation, timeout or malformed state retains incomplete evidence.

This is finite sampling, not an unsolicited-event subscription. Parameters outside block status, transient changes between samples, a change and return during a bracket, and downstream MIDI delivery remain outside coverage. It never claims that unchanged samples prove no internal change occurred.

## Local MCP

- `fractal_read_amp_roster(start, count)` retains the requested range and raw replies.
- `fractal_capture_live_control_context()` captures the context for a later review.
- `fractal_prepare_live_control(context_artifact, kind, ...)` freezes exact intent and performs real offline helper validation.
- The existing trusted-host approval and `fractal_execute_review` execute once and report verified completion or uncertainty.
- `fractal_monitor_live_state(samples, interval_ms)` retains finite observations; use `fractal_read_artifact` for bounded pages of its report.

Reads honor configured live access; preparation is offline. Supported operations stay available across model definitions. The [validation record](live-tools-validation.json) separates protocol sources, synthetic coverage and FM9 12.00 physical trials. The initial trial read four roster ordinals, completed sampled monitoring, and changed then restored absolute scene, channel and bypass state while saved images remained unchanged. A later read validated an explicit 336-ID roster range against the retained catalogue and used a complete saved inventory for qualified absence within that range. Neither trial establishes a firmware end marker, receiver delivery or other-model compatibility.

[Broader live parameter mapping](live-parameters.md) adds one-instance block reads and private table/echo observation through common CLI/MCP contracts. Raw values retain their own-model symbolic mappings; no calibrated wider saved setter or hardware qualification is implied.
