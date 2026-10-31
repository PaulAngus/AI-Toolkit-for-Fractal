# Configured MIDI recall and controls

Release 0.19.0 adds typed MIDI bank/Program Change recall, assigned CC controls and host-fed note/CC input mapping. The shared runtime covers FM9, FM3 and both Axe-Fx III capacities. Assignments are explicit caller configuration; synthetic software support does not establish a device's receive settings or new hardware compatibility.

## Bank and Program Change

Pass `--midi-mapping MAPPING_JSON` to CLI `prepare-load`, or `midi_mapping` to MCP `fractal_capture_load_context`. The mapping contains `mode` (`direct` or `custom`), MIDI `channel` 1–16, `bank_controller`, `bank` and raw `program` (all data bytes 0–127). For direct addressing the caller asserts CC 0, `bank = slot / 128` rounded down and `program = slot % 128`. Custom mapping requires explicit bytes and the intended zero-based target slot. No display offset, receive channel or enabled mapping mode is inferred.

The retained load request binds current selection, full current saved/unsaved images, target saved image and mapping. Existing `fractal_prepare_load` or the `--load-preset` helper validates and reviews the exact bank/PC bytes. Execute using the usual trusted-host review or explicit helper contract. Bank select is sent once, then Program Change once. Fresh checks before each send reject intervening selection/content changes. Cancellation or transport uncertainty after bank select stops before Program Change; no blind retry follows.

Readback must confirm the intended target, saved default scene, complete loaded target and unchanged source/target saved images. A mismatched custom mapping or ignored command fails verification. Recall does not Save. Unknown unsaved differences require explicit discard intent and remain retained in private evidence. The separate existing SysEx load remains available when no mapping is supplied.

For a non-default requested scene, capture the resulting context after verified recall and prepare the scene control below. This preserves exact freshness and review boundaries between the two state changes.

## Assigned CC controls

Add `midi_assignment: { "channel": ..., "controller": ..., "value": ... }` to a schema-1 live-control request or MCP `fractal_prepare_live_control`. Keep the absolute intended operation fields: `kind: scene` and scene 1–8, `kind: channel` and exact instance/channel, or `kind: bypass` and exact instance/Boolean state. All MIDI fields are explicit, with channel 1–16 and data bytes 0–127. The configured CC replaces the direct SysEx send while retaining all existing saved/unsaved freshness and complete readback checks.

There is no inference that a given CC controls a particular effect, no implicit toggle, no Save and no general arbitrary-MIDI send tool. An already-satisfied absolute intent is a verified no-op. A wrong assignment cannot be reported as success solely because WinMM accepted the bytes. Review reports distinguish `configured-cc` from `direct-sysex`.

## Note and CC input mapping

`MidiInputRouter` is the shared deterministic, stateful host API. An integration feeds events from its existing coordinated MIDI owner; it must not open a second connection to the device. Release 0.20.0 adds a [bounded standalone input listener](controller-listener.md) using the same router. CLI `route-midi --request ROUTING_JSON` and MCP `fractal_route_midi_events` provide finite offline mapping through the same implementation.

A schema-1 routing request supplies model/capacity/firmware, 1–64 `rules` and 1–256 `events`. A rule has `input_kind` (`note` or `cc`), `input_channel` 1–16, `number` 0–127, optional `value` (required for CC, omitted for note), and one absolute `target`. A preset target supplies `kind: preset`, `slot` and `preset_mapping`; a scene/channel/bypass target supplies its usual intent fields and `cc` assignment. Conflicting duplicate input bindings are rejected.

Events contain `kind` (`note-on`, `note-off` or `cc`), `channel`, `number` and `value`. A positive-velocity Note On triggers only once while held. Note Off and velocity-zero Note On release that key without triggering. Repeated identical CC values are suppressed until that controller changes value. State is scoped to the router instance; a new finite request begins a new stream. Mapping does not claim that Fractal firmware itself interprets note messages as commands.

Mapped preset targets feed `capture_load_context` with `target_slot` and `midi_mapping`, then `prepare_load`. Mapped live targets feed `capture_live_control_context`, then `prepare_live_control` with the absolute intent and `midi_assignment` copied from `cc`. Preparation and mapping do not authorize execution. A host may use the same scope APIs with its already-authorized transport. Receiver labels, firmware updates, automatic feedback programming and downstream delivery are outside this implementation.

Manufacturer bank/PC and CC semantics are retained in the [recall workflow](../knowledge-base/workflows/recall-and-control.md) and [source map](../knowledge-base/reference-map.md). Model/firmware attribution and synthetic limits are recorded in [validation](batch-midi-validation.json).
