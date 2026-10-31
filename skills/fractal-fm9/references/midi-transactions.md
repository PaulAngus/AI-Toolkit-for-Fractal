# MIDI transaction lessons

These rules generalize recorded FM9 transactions and transport observations. Source dates and limits are in [sources.md](sources.md).

## Connection and ownership

One component coordinates each device's MIDI input/output pair. When integrated into an application, use its existing coordinated transport rather than opening a competing connection. Other applications' direct writes require separate ownership or coordination; a local request gate cannot control them.

Both MIDI directions must work. Virtual ports may remain visible while a bridge's network connection is absent. Enumerating ports is not an end-to-end connection test; validate a supported device query/reply.

Different controllers may use different MIDI thru inputs. Selecting both USB and interface routes for the same controller can forward each press twice; a toggle command then reverses itself. Diagnose the actual routes and preserve incoming channel/value semantics.

## Reception and correlation

Use SysEx reception, not short-message callbacks, for exclusive messages. Reassemble fragmented frames and multiple frames in one callback. Ignore interleaved realtime bytes and bound incomplete buffers.

Allow one outstanding transaction at a time. Validate model, function, exact or bounded length, checksum, and address fields before accepting a response. Preset-name replies echo slot; scene-name replies echo only scene. For a live scene snapshot, verify the selected preset before and after and invalidate on a preset change or cancellation.

There is no general transaction ID. A sufficiently delayed same-model/same-address response may be indistinguishable from a fresh reply. Keep late-response quarantine, bounded request windows, and cancellation generation checks.

## Timing and recovery

Choose query pacing from measured target behavior. A recorded **10 ms between query starts** was an experimental transport policy, not a documented Fractal requirement or proven remedy. Start the response deadline after pacing; waiting should not consume it.

Some idempotent name/state reads use bounded retries. A roster reply without its ordinal cannot safely be retried as though a late result belonged to the next index. Correlation determines whether recovery is valid.

Mutation retries require readback and reconciliation. In the historical write helper, a short error-watch window could detect a nonzero `64` rejection; silence was not positive acknowledgement. Confirm the intended live fields and saved data rather than inferring success from the absence of rejection.

## Preserve and report state

Loading presets or scenes is a live action. A saved read and a preset selection are different operations. Returning to a starting selection does not recover discarded unsaved edits.

Retain progress, skipped/conflicting targets, cancellation state, and per-preset verification records. Bind resume records to the original snapshot and resulting saved hash. File existence alone does not prove a hardware write succeeded.
