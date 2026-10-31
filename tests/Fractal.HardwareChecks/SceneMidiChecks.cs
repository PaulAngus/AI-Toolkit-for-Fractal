using System.Buffers.Binary;
using Fractal.Core;

internal static class SceneMidiChecks
{
    public static async Task<int> Run(int input, int output, DeviceDefinition requested, string folder)
    {
        using var trace = new Trace(Path.Combine(folder, "trace.jsonl"));
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var checks = new List<object>();
        DeviceContext? context = null;
        CurrentState? before = null, after = null;
        SavedCapture? saved = null;
        SceneMidiCapture? live = null;
        PresetSnapshot? preset = null;
        string? failure = null;
        TimeSpan timeout = TimeSpan.FromSeconds(5);
        void Check(string name, bool passed)
        { checks.Add(new { name, passed }); if (!passed) throw new InvalidDataException($"Scene MIDI hardware check failed: {name}"); }
        try
        {
            await using (var session = new ReadSession(new TracedMidi(await TransportConnection.OpenAsync(input, output), trace)))
            {
                context = await session.IdentifyAsync(requested.Model, requested.PresetCapacity, timeout, cancellation.Token);
                Check("identity-and-firmware", context.Definition == requested);
                before = await session.CurrentAsync(context, timeout, cancellation.Token);
                saved = await session.SavedAsync(context, before.Slot, timeout, cancellation.Token);
                preset = PresetBody.Decode(context.Definition, saved.Image);
                Check("current-saved-table-decoded", preset.SceneMidiPresent && preset.SceneMidiDecodeStatus == "decoded");
                live = await session.SceneMidiAsync(context, timeout, cancellation.Token);
                Check("live-table-complete", live.Fields.Length == 256 && live.SectionMetadata.Length == 2);
                Check("eight-scenes-eight-slots", preset.Scenes.Length == 8 && preset.Scenes.All(s => s.Messages.Length == 8));
                // Compare direct body reads against the independent live transfer. This
                // avoids confirming an offset solely with a fixture built from that offset.
                var layout = context.Definition.SceneMidi!;
                var record = preset.BlockRecords.Single(r => r.Columns == layout.Columns && r.Rows == layout.Rows);
                int offset = record.Offset + layout.FirstWord * 2;
                int[] direct = Enumerable.Range(0, 256).Select(i => (int)BinaryPrimitives.ReadUInt16LittleEndian(saved.Image.Body.AsSpan(offset + i * 2))).ToArray();
                Check("all-256-direct-saved-words-match-live", direct.SequenceEqual(live.Fields));
                var repeat = await session.SceneMidiAsync(context, timeout, cancellation.Token);
                Check("repeat-live-transfer-identical", repeat.Sysex.SequenceEqual(live.Sysex));
                var reread = await session.SavedAsync(context, before.Slot, timeout, cancellation.Token);
                Check("saved-capture-byte-preservation", saved.Sysex.SequenceEqual(reread.Sysex) && saved.Image.RawImage.SequenceEqual(reread.Image.RawImage));
                after = await session.CurrentAsync(context, timeout, cancellation.Token);
                Check("selection-preserved", before == after);
            }
            await using (var session = new ReadSession(new TracedMidi(await TransportConnection.OpenAsync(input, output), trace)))
            {
                var reopened = await session.IdentifyAsync(requested.Model, requested.PresetCapacity, timeout, cancellation.Token);
                after = await session.CurrentAsync(reopened, timeout, cancellation.Token);
                Check("port-reopen-state-preserved", context == reopened && before == after);
            }
            Check("all-transmissions-read-allowlisted", trace.AllReads);
        }
        catch (Exception error) { failure = $"{error.GetType().Name}: {error.Message}"; }
        if (context != null && saved != null)
            SnapshotExport.Write(Path.Combine(folder, "current-snapshot"), context, [saved], before, after);
        if (live != null) File.WriteAllBytes(Path.Combine(folder, "live-table.syx"), live.Sysex);
        var report = new { protocol_release = ProtocolRelease.Version, started_utc = trace.Started,
            completed_utc = DateTimeOffset.UtcNow, mode = "scene-midi-read-only", device = context,
            complete = failure == null, failure, state_before = before, state_after = after,
            results = checks, trace_sends = trace.Sends, trace_receives = trace.Receives,
            all_transmissions_read_allowlisted = trace.AllReads,
            saved_decode_status = preset?.SceneMidiDecodeStatus,
            saved_body_sha256 = saved == null ? null : PresetBody.Sha256(saved.Image.Body),
            saved_sysex_sha256 = saved == null ? null : PresetBody.Sha256(saved.Sysex),
            live_sysex_sha256 = live == null ? null : PresetBody.Sha256(live.Sysex),
            live_section_metadata = live?.SectionMetadata, live_extra_word = live?.ExtraWord,
            message_counts = preset?.Scenes.SelectMany(s => s.Messages).GroupBy(m => m.Type).ToDictionary(g => g.Key, g => g.Count()),
            limitations = new[] { "Only connected model/firmware physically tested", "No navigation or writes", "Active CC and nondefault channel/value hardware cases not exercised", "No front-panel or editor UI comparison" } };
        File.WriteAllText(Path.Combine(folder, "report.private.json"), SnapshotExport.Json(report));
        Console.WriteLine(SnapshotExport.Json(new { complete = failure == null, failure, model = requested.Model,
            firmware = context?.Firmware, checks = checks.Count, transmissions = trace.Sends,
            all_transmissions_read_allowlisted = trace.AllReads }));
        return failure == null ? 0 : 1;
    }
}
