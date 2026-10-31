# Network transport and device notifications

Release 0.21.0 adds a contained Apple RTP-MIDI initiator and passive device-message capture. Existing supported reads, scoped live controls, preset loads, complete-image edits, batches, reconciliation and undo remain available on FM9, FM3 and both Axe-Fx III capacity definitions. Physical qualification is separate.

## Network connection

Use `--network IPv4:CONTROL_PORT` in place of local `--input`/`--output` on device CLI commands and the authorized execution helper. The MIDI port is the following UDP port. Supply an explicit unicast IPv4 address; discovery, DNS and automatic reconnection are outside this implementation. A compatible, explicitly configured remote MIDI session must route to the intended device and coordinate its physical MIDI ports. The toolkit does not configure that remote routing or open local hardware ports in network mode.

For MCP, the operator's `device` configuration uses `model`, `capacity`, `firmware` and `network`, replacing `input` and `output`. For example, this **synthetic loopback configuration** names no hardware:

```json
{"model":"FM9","capacity":512,"firmware":"99.07","network":"127.0.0.1:5004"}
```

The configured endpoint is included in the immutable execution review and exact operator approval. Changing it invalidates that review. Tool calls cannot choose arbitrary hosts or send arbitrary SysEx. The same `MidiSendAuthority`, model definitions, scope preconditions and independent readback checks apply to native and network transports. Existing access settings retain their meaning. Notification capture needs live-read access; authorized edits remain enabled when writes are enabled.

One cooperating toolkit process owns each canonical peer endpoint. Different network endpoints that reach the same physical device still require coordination at the remote host. Apple network MIDI is unauthenticated: configure a trusted network/VPN and a single coordinated device writer. Endpoint/SSRC matching is correlation, not authentication.

The implementation uses the [Apple network MIDI session protocol](https://developer.apple.com/library/archive/documentation/Audio/Conceptual/MIDINetworkDriverProtocol/MIDI/MIDI.html) and [RFC 6295 command encoding](https://www.rfc-editor.org/rfc/rfc6295.html). Receiver feedback uses the RTP sequence in the leading half of its four-byte field, consistent with the [Wireshark Apple MIDI dissector](https://gitlab.com/wireshark/wireshark/-/blob/v4.4.0/epan/dissectors/packet-applemidi.c) (open implementation evidence). It supports two-port invitations, clock synchronization, receiver feedback, explicit session exit, running-status input, bounded SysEx segmentation/reassembly and typed CC/PC output. It is an initiator with Apple's fixed RTP header profile, not a general RTP engine. OS-provided network MIDI ports can still use the native backend.

Invitations have a 12-second total connection bound; sends have a five-second bound. Incoming packets must use the selected endpoint and session source. Sequence gaps, duplicates or reordering stop the session. Journals may accompany packets but are never used to reconstruct or replay device operations. Initial loss before the first received sequence and silent remote downstream loss cannot be established by sequence checks; normal task readback remains essential. Feedback acknowledges transport reception only.

Commands are sent in order without retries. Incomplete SysEx expires after ten seconds; clock-heartbeat loss stops the session after fifteen seconds. SysEx frames are bounded to 4096 bytes; outgoing segments carry at most 1000 data bytes. Future input timestamps are honored up to a two-second bound. No buffered messages survive a failed connection. Retain uncertain-write evidence and use the existing query-only reconciliation workflow before preparing another operation.

## Passive notifications

CLI: `listen-notifications --model MODEL --capacity N --firmware VERSION --input N --duration SECONDS --max-events COUNT --destination NEW_DIRECTORY`. Substitute `--network IPv4:CONTROL_PORT` for the local input. `--validate-only true` checks arguments without opening a connection. CLI bounds are 1–3600 seconds and 1–4096 frames. MCP exposes `fractal_listen_device_notifications` with 1–30 seconds and the same event bound, using the operator-configured device connection.

The capture sends no MIDI queries or edits. Network session/clock packets are necessary when using RTP-MIDI. A private `notifications.json` is atomically checkpointed after each observation; `received.syx` retains received fragments, including an incomplete final frame. This file contains MIDI bytes delivered by the transport; incomplete RTP segments remain inside the network decoder and cause a transport failure, rather than appearing as a complete device message. Raw capture is bounded to four MB and the input queue to 256 chunks. Cancellation, source failure, overflow and incomplete trailing SysEx leave an incomplete report. Completion means the bounded collection finished, not that every device change was observed.

The decoder recognizes tempo beats, tuner pitch/state, block bypass/channel/status, scene, preset/scene names, looper state and tempo values. Published tempo-beat and tuner-pitch pushes have exact checksumless formats; ordinary replies retain strict checksum and length checks. Unknown Fractal functions, malformed complete frames and other model addresses remain visible. Scene-name messages never acquire an invented preset identity. Reply-shaped observations do not prove that the device sent them unsolicited, and a preset-name reply does not prove current selection.

Refresh hints identify affected live data; they never replace capture freshness checks or verify saved persistence. `ReadSession` excludes well-formed published pushes from its solicited-reply queue. A hosted integration may attach capture and query consumers to the same coordinated transport. The standalone input listener should not compete with another process for the same device input.

The [manufacturer specification, revision 1.4](https://www.fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf) documents Axe-Fx III tempo/tuner pushes on MIDI OUT, enabled by Send Realtime Sysex, rather than USB. That publication does not establish emission behavior on FM9/FM3 or a comprehensive change feed. All model decoders are enabled with those evidence qualifications. Unknown private parameter messages remain raw until their semantics are established.

## Validation and remaining limits

[Validation record](network-notifications-validation.json) distinguishes independent wire vectors, synthetic loopback devices, runtime/adapter checks and pending hardware work. No physical MIDI ports, external peers or devices were used. Interoperability with actual Apple/third-party peers, remote routing, DIN/USB behavior, congested networks and model/firmware notification coverage require separate qualification. Server/discovery support, journal recovery and complete private device-change semantics remain outside this release.

Release 0.22.0 extends the same passive observer with [private parameter echoes and strict block-table assembly](live-parameters.md), retaining invalid/interrupted sequences and incomplete boundaries. Public formats and network transport remain supported.


The runtime now delegates RTP coding and session I/O to contained MightBeUseful.Midi.Rtp 0.3.6. Authorization and endpoint locks remain in the toolkit; all policies above remain in force. See [shared RTP policies](shared-rtp-midi.md).
