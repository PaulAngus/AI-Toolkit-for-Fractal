# Shared MIDI package integration

Native I/O uses `MightBeUseful.Midi`, `MightBeUseful.Midi.Windows` and
`MightBeUseful.Midi.CoreMidi` exactly 0.3.6.
The toolkit owns Fractal framing, model definitions, request matching, transaction
serialization, immutable plans, exact authorization and readback. RTP-MIDI uses the contained `MightBeUseful.Midi.Rtp` 0.3.6 package; the toolkit
retains authorization and endpoint ownership. See [preserved RTP policies](shared-rtp-midi.md).

CC/PC byte encoding delegates to the neutral package's `MidiMessageEncoding`.
Configured assignments and preset mappings are validated in the toolkit before
encoding; channel numbering, zero-based program values, typed command scopes and
uncertain-write handling remain unchanged. The shared encoder grants no authority.

`SharedMidiTransport` adapts the shared input/output interfaces and can be tested
with fake ports. It owns supplied ports. Hosted integrations should keep using
the host's coordinated `IMidiTransport`, without opening a second connection.
`WinMmTransport` remains a compatibility entry point, with no native P/Invoke.
It enumerates the shared WinMM catalogue and retains numeric port indices and
same-toolkit named ownership locks. Output uses the shared legacy WinMM backend;
input uses its WinRT backend after an exact, unique catalogue-name match. WinRT's
numeric display suffix (for example, `Device Input [0]`) is accepted only when
its base matches the selected WinMM name uniquely, there is exactly one exact or
indexed candidate, and its full name does not belong to another WinMM endpoint.
The suffix is not assumed to equal the global WinMM index. Duplicate, colliding,
truncated or otherwise unmatched names are refused. This is catalogue mapping,
not a retry against another port or backend after a failed open or send. Automatic
Windows MIDI Services selection is deliberately not enabled: changing endpoint
identity requires a separate configuration migration.

`NativeMidiTransport` selects the local Windows or macOS adapter for CLI, helper
and MCP connections, including input-only listeners. `CoreMidiTransport` uses the
shared standard CoreMIDI backend (DryWetMIDI 8.0.3), with bundled universal native
assets for arm64 and x64 processes. It does not use the bulk RtMidi profile or the
package's local network-MIDI implementation. All FM9, FM3 and Axe-Fx III operations
keep their common model-aware authorization; no model-specific Mac gates are added.

Mac input IDs and output indices come directly from the shared catalogue. Sparse
indices are preserved; absent, malformed or duplicate indices are refused. Names
need not be unique. Indices can change when endpoints are added/removed: enumerate
and select the intended ports again after such changes. Device identity/readback
checks remain necessary. Enumeration uses the package's transient device wrappers;
it does not start a toolkit listening session or send messages.

Mac connections acquire per-input/output file leases before opening either port.
The files live in a toolkit-specific temporary directory, are held with exclusive
sharing, and are not unlinked on release, avoiding inode replacement races. A
process registry also roots active handles until successful native cleanup; partial
open failures unwind acquired resources, while failed cleanup retains ownership.
These leases coordinate cooperating toolkit processes, not other applications.
The standard backend rejects SysEx sends above 60,000 bytes. Fractal protocol
frames and all existing authorization, timeout, no-replay and readback rules remain
unchanged. There is no automatic fallback to another backend after a failed send.

Receive chunks are copied and bounded to 64 KiB; the queue holds 256 chunks/events.
Errors and overflow fault the session. Sends are serialized and pass the existing
`MidiSendAuthority` before reaching a package port. No arbitrary send API is added
to CLI/MCP. A five-second send deadline or cancellation faults the session; no
resend occurs. Because package sends are synchronous, native work can continue
after the caller stops waiting. Output lifetime and ownership persist until that
work and cleanup complete. Cleanup waits five seconds, retains ownership on
failure, and can remain pending for a hung driver. Readback/reconciliation remains
necessary for uncertain writes. Using the same package does not coordinate ports
across different applications.

The shared short-message API supplies no driver timestamps. The existing
`DriverMilliseconds` field contains host monotonic receipt milliseconds for this
adapter; listener reports declare `timestamp_source` explicitly. RTP input keeps
its existing timing behavior. This is not precise device event timing.

## Offline builds and distribution

The sole NuGet source is the repository's `dependencies/midi` directory, containing
pinned packages, transitive dependencies, metadata, available licences and hashes.
The 0.3.6 MIDI archives are built with deterministic mapped source paths and copied
byte-for-byte; no DLL patching is needed. The manifest records the source baseline,
local source hashes and archive hashes. A repository-local NuGet cache is used. The privacy checker also
examines archive contents.
Windows builds target `net10.0-windows10.0.19041.0`; other hosts target `net10.0`.
Both preserve `bin/Debug/net10.0` and `bin/Release/net10.0` command paths.
`-p:FractalPortableBuild=true` explicitly exercises the portable target on Windows.
That portable build supports native CoreMIDI when run on macOS; Windows native
MIDI requires the Windows target. Linux remains offline/RTP-only.
The .NET 10 SDK is required. There are no feed downloads, sibling references,
bootstrap installations or hardware calls during builds. MCP packages include the
local feed as well as published runtime dependencies and complete toolkit sources.

Run `python scripts/check_shared_midi.py` for package hash checks and cold-cache
Windows/portable builds. Run the usual CLI and MCP pipelines for all-model offline
regressions and relocated distribution checks. Fake-port tests cover authorization,
input-only operation, callback copying, overflow, failure, cancellation, timeout
and pending-send lifetime. CoreMIDI-specific checks use fake package ports for
sparse indices, input-only paths, all-model scopes and open/start/cleanup failures;
file ownership is also tested in a separate .NET process. Cold-cache publication
checks verify both Mach-O architecture slices and native asset hashes without
loading a native library. None of these checks establishes hardware compatibility.
Historical native WinMM hardware results retain their original implementation
scope; the new Windows and Mac adapters have not been hardware-qualified.
Current checks ran on Windows, including portable compilation and Mac asset
inspection. Actual macOS loading, device enumeration, driver callbacks, signing/
launch behavior and USB/DIN transfer correctness remain untested.
