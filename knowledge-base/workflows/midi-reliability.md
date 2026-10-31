# Coordinate MIDI transactions and diagnose transport failures

General task: distinguish routing, response correlation, device/driver timing, network transport and host scheduling before choosing a remedy.

## Routing and ownership

Trace a controller press from each input to the output. Two routes carrying the same toggle CC can cancel each other on the device. Preserve the configured channel/value semantics and remove unintended duplication; do not assume the application should create a second toggle state.

An unavailable endpoint discovered while enumerating ports does not prove failure of another owned connection. Validate the actual device pair separately. Virtual ports can remain visible while their network path is disconnected; test bidirectional device replies.

Use one shared transaction coordinator for all queries to a device, including background polling. A lock within one process cannot control other applications writing directly to the endpoint. Coordination across applications requires their traffic to pass through a common owner or an explicit ownership arrangement.

## Match responses and recover conservatively

Reassemble SysEx fragments and multiple frames, tolerate interleaved realtime bytes and bound incomplete buffers. Validate model, function, length, checksum and any address fields before accepting a response.

Retry only idempotent reads that remain correlatable. Preset-name replies echo an address; a roster reply omitting its ordinal can mislabel a late response if retried. Complete an initial sweep, recover missing addresses within bounded attempts and distinguish fresh results from cached values. Honor cancellation and quarantine late responses.

Pacing is a measured transport policy, not a universal device requirement. Adjust query intervals with controlled comparisons and record initial misses, recovery, duration and competing traffic. A reported 10 ms experiment is not evidence that every FM9 needs or benefits from that interval. Start response deadlines after scheduling waits.

An open editor does not prove query collisions. Compare repeat runs under controlled routes and activity, inspect received/matching/rejected frames and queue drops, and retain both successful and failed observations. A clean coexistence run cannot prove every concurrency scenario is safe.

## Keep measurement independent of the operation

Time queue wait, pacing, quarantine, send/reply, decoding, disk/checkpoint work and posted UI progress separately. Nested phases cannot be added as if they were independent durations. Frames received without a matching reply differ from dropped receive data; zero observed frames does not prove the device sent nothing.

Keep read loops and MIDI callbacks independent of rendering and log I/O. Use bounded asynchronous metadata logging with rotation and drop counts. Avoid payloads, credentials and personal names unless an explicitly scoped capture requires them. Isolate simulated tests from live diagnostic files so a test failure cannot masquerade as device evidence.

## Network MIDI

Preserve logical SysEx messages across fragments, groups and streams. Bound assembly and queues, discard stale commands and incomplete messages across reconnects, establish session readiness, respect endpoint send limits and stop on partial delivery failure. TCP's ordered delivery within a session does not make replaying old MIDI commands safe.

Complete-message batching reduces fragmentation exposure but does not guarantee exclusive delivery against another writer. Bulk ownership works only if all competing writers participate. Transaction correlation remains distinct from byte transport.

For a network connection, validate full SysEx reads, loss and reconnect behavior on the actual route; do not assume note-message success proves bulk preset transfer reliability. A remote host must coordinate its physical MIDI ports. Platform support and tools are time-sensitive; consult the [transport sources](../catalog/sources.md).

The contained [RTP-MIDI implementation](../../docs/network-notifications.md) now supplies explicit network endpoint selection for existing reads and scoped edits. It stops on observed sequence discontinuity, expired fragments or session failure without replay. One remote host must coordinate the physical device ports; loopback checks do not qualify external peers or hardware.
