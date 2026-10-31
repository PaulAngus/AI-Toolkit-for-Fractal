# Standalone controller input listener

Release 0.20.0 connects native Windows MIDI input to the shared stateful note/CC router. It runs independently as a bounded CLI process or through MCP. The input-only connection opens no output port, exposes no send capability to the collector, sends no identity queries and performs no preset or control writes. FM9, FM3 and Axe-Fx III routing definitions remain supported independently of hardware qualification.

## Explicit input and bounds

Choose the controller input port explicitly; it may be a controller or virtual input rather than the Fractal device's return port. Coordinate ownership with other applications. The existing toolkit native port lock rejects a second toolkit owner of the same input. A hosted integration should supply its existing `IMidiInputSource` to `ControllerListener.CaptureAsync` instead of opening another connection.

The listener request is schema version 1 with `model`, `capacity`, `firmware` and 1–64 `rules` using the existing [configured MIDI routing contract](configured-midi-routing.md). These are caller-supplied target-routing context, not observed device identity. Each input note/channel or exact CC/channel/value maps to one absolute preset/scene/channel/bypass intent with explicit output assignments. The listener never sends those assignments itself.

CLI: `listen-midi --request LISTENER_JSON --input CONTROLLER_INPUT --duration SECONDS --max-events COUNT --destination NEW_DIRECTORY`. Duration must be 1–3600 seconds and event limit 1–4096; both are explicit. The process can run in the background under the caller's normal process manager. It owns its connection until completion/cancellation and does not install an unbounded service or auto-restart itself. Ctrl+C cancels. `--validate-only true` validates the request and bounds offline without input/output options or transport construction.

MCP: `fractal_listen_controller(input, duration_seconds, max_events, rules)` requires configured live-read access. Calls are bounded to 30 seconds, can finish earlier at the event limit, and retain an artifact on success or failure. Write access is not required. Cancelling the tool stops its owned child; inspect the retained artifact before starting another observation period. Model-controlled calls still cannot approve themselves for device mutations.

## Input processing and evidence

WinMM `MIM_DATA` supplies an expanded short message and driver milliseconds since input start. The native callback only queues input; decoding, mapping and file I/O run outside it. Valid note-on, note-off and CC events are decoded with task channels 1–16. Other short-message families do not produce routed intents. Invalid relevant data bytes, `MIM_ERROR`, receive-queue overflow and source faults stop with incomplete evidence. Realtime messages and SysEx do not imply preset changes or controller feedback coverage.

The same router handles held-note duplicate suppression, Note Off, velocity-zero release and repeated CC values. State persists for this subscription; a new listener starts a new stream. Driver timestamps are retained as unsigned native values, may wrap, and do not establish synchronized wall-clock timing. The listener does not infer keys held before subscription or messages missed before capture began.

Private `request.json` retains the rules. `listener.json` is flushed and replaced atomically after each observed note/CC event, with index, driver timestamp, decoded input and mapped target or null. It records `complete`, stop reason, failure, bounds and `sends: 0`. A normal duration/event limit means completion of that bounded observation only. Cancellation, malformed input, overflow or faults preserve completed observations with `complete: false`; there is no replay of lost or queued inputs. A forced process kill may leave the latest checkpoint marked incomplete. Queued traffic beyond the selected event limit is outside coverage.

For a mapped intent, capture fresh target state and use existing `prepare_load` or `prepare_live_control` followed by exact trusted-host execution. Captured controller events are evidence of input, not authority to execute or proof of downstream delivery.

## Source and qualification

Native packing, expanded running status and timestamp interpretation follow Microsoft's [MIM_DATA documentation](https://learn.microsoft.com/en-us/windows/win32/multimedia/mim-data); invalid-input handling follows [MIM_ERROR](https://learn.microsoft.com/en-us/windows/win32/multimedia/mim-error), consulted 12 October 2026. These API sources do not validate any particular driver or Fractal hardware. [Synthetic validation](path-listener-validation.json) exercises decoding, stateful routing, limits, cancellation, partial failures and overflow without opening ports. Real controller/native-driver behavior remains a separate hardware test.
