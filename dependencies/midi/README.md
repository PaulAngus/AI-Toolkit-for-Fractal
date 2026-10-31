# Contained MIDI dependencies

This is the toolkit's only NuGet feed. Normal restore/build/publish requires the
.NET 10 SDK, but no sibling checkout or remote feed. All package hashes are in
manifest.json. The first-party 0.3.12 archives are copied byte-for-byte from the
MIDIHelperLibrary source build, with deterministic source paths mapped at build
time. They need no CodeView patching. prepare_shared_midi.py is retained only for
the historical 0.3.4 preparation workflow, not this release.

The source baseline and hashes identify this local, uncommitted library snapshot;
they do not claim that the baseline commit contains these changes. RTP source,
Apache-2.0 attribution for toolkit-derived portions and the original licence are
inside the RTP archive. Other first-party package metadata declares no licence;
no broader licence grant is inferred. Original third-party notices are retained.

- MightBeUseful.Midi, Windows, CoreMidi and Rtp: exactly 0.3.12. Older archives
  remain in the feed as historical inputs; the exact pin excludes them.
- Melanchall.DryWetMidi 8.0.3: MIT; original signed archive and universal Mac assets.
- NAudio.Midi, WinMM and Core 3.0.1: MIT, original signed archives.
- System.Numerics.Tensors 9.0.0: Microsoft, MIT.
- Microsoft.Windows.SDK.NET.Ref 10.0.19041.57: included Microsoft licence.

Optional MIDI Services bootstrap content remains inert. No installer, native
enumeration or hardware operation runs during builds. This feed is copied into
MCP distributions and covered by the toolkit protocol-release manifest.

Exact package selection: MidiVersion.props keeps the plain release value for
packaging filenames and exposes SharedMidiPackageVersion as [0.3.12]. All direct
MIDI references, including the Mac RTP dependency, use that exact range.
NuGet source mapping routes MightBeUseful.Midi and MightBeUseful.Midi.* exclusively
to this bundled feed. Other configured sources retain their existing third-party
roles. The repository-local .build/nuget-contained cache isolates builds from
unrelated global cache entries. Source-development overrides remain explicit.

On supported Windows 11 systems, the native Toolkit connection now uses the shared
Windows MIDI Services provider, including its complete-message batching and input
recovery. Older systems retain the existing WinRT/WinMM path. Run `fractal ports`
and check existing native connection selections after updating: the current provider
lists its endpoint names rather than the older WinMM aliases. Read/write scope and
exclusive Toolkit ownership remain enforced.

## Historical 0.3.6 verification

Release verification: restores used exact closed ranges and exclusive bundled
MIDI source mapping. Local empty-cache probes restore 0.3.6, refuse a missing
bundle despite an alternate feed, and refuse a synthetic higher version. Active
restore graphs, tested Release outputs and isolated self-contained publication
payloads match the selected archives. Toolkit CLI/MCP relocation checks and
Forwarder managed suites passed. No native MIDI ports were opened. See
[offline validation and policy limits](../../docs/shared-rtp-midi.md).
