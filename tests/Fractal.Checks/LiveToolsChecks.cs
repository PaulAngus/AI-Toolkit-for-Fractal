using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Fractal.Core;

static class LiveToolsChecks
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    public static async Task Run(DeviceDefinition d, string? destination)
    {
        string root = Path.Combine(Path.GetTempPath(), "fractal-live-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            int address = d.Model == "FM9" ? 10 : d.Model == "FM3" ? 6 : 0;
            byte[] query = AmpRoster.Query(d, 129);
            Need(query.Length == 23 && query[6] == 31 && query[8] == 58 && query[10] == address && query[12] == 1 && query[13] == 1 && SysexProtocol.IsReadRequest(query), "Independent per-model roster query");
            foreach (string name in new[] { "A", "SixSix", "Seven77", "Synthetic amp " + new string('x', 45) })
                Need(AmpRoster.Decode(d, 129, RosterReply(d, address, name)).Name == name, "Packed display string including partial groups");
            byte[] bad = RosterReply(d, address, "Synthetic"); bad[^2] ^= 1;
            Reject<InvalidDataException>(() => AmpRoster.Decode(d, 0, bad));
            Reject<InvalidDataException>(() => AmpRoster.Decode(d, 0, RosterReply(d, address + 1, "Synthetic")));
            Reject<InvalidDataException>(() => AmpRoster.Decode(d, 0, RosterReply(d, address, "")));
            var changed = query.ToArray(); changed[14] = 1; changed[^2] = SysexProtocol.ComputeChecksum(changed.AsSpan(0, changed.Length - 2));
            Need(!SysexProtocol.IsReadRequest(changed), "Read allowlist cannot admit a write payload");
            var context = new DeviceContext(d, "99.07", "synthetic");
            var fixture = AudioRoutingChecks.Build(d); Put(fixture.Body, 0, 2);
            var saved = Fixtures.FromBody(d, 0, fixture.Body); var loaded = saved;
            var rosterMidi = new FakeEditorMidi(); int reads = 0;
            rosterMidi.Reply = (q, m) =>
            {
                if (q[5] == 1) { reads++; if (reads <= 2) m.Emit(RosterReply(d, address, "Synthetic " + reads)); }
                else PresetEditChecks.Reply(q, m, d, ref saved, ref loaded, []);
            };
            await using (var session = new ReadSession(rosterMidi))
            {
                await RejectAsync<ArgumentException>(() => AmpRoster.CaptureAsync(session, context, int.MaxValue, 2, Path.Combine(root, "invalid"), Timeout, default));
                Need(rosterMidi.Sent.Count == 0 && !Directory.Exists(Path.Combine(root, "invalid")), "Overflowing range refused before reads or output");
                await RejectAsync<TimeoutException>(() => AmpRoster.CaptureAsync(session, context, 0, 3, Path.Combine(root, "partial"), TimeSpan.FromMilliseconds(60), default));
                using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "partial/roster.json")));
                Need(!report.RootElement.GetProperty("range_complete").GetBoolean() && !report.RootElement.GetProperty("roster_complete").GetBoolean() && report.RootElement.GetProperty("entries").GetArrayLength() == 2 && reads == 3, "Partial roster retained, no retry/end inferred");
                await RejectAsync<IOException>(() => session.AmpRosterEntryAsync(context, 3, Timeout, default));
                Reject<InvalidDataException>(() => InventoryAnalysis.ReadRoster(File.ReadAllBytes(Path.Combine(root, "partial/roster.json")), context));
            }
            reads = 0;
            await using (var session = new ReadSession(rosterMidi))
            {
                await AmpRoster.CaptureAsync(session, context, 0, 2, Path.Combine(root, "complete"), Timeout, default);
                Need(InventoryAnalysis.ReadRoster(File.ReadAllBytes(Path.Combine(root, "complete/roster.json")), context).SequenceEqual(new[] { 0, 1 }), "Completed explicit range usable for qualified set difference");
            }
            int statusReads = 0;
            var monitorMidi = new FakeEditorMidi { Reply = (q, m) =>
            {
                if (q[5] == 19) { statusReads++; m.Emit(SysexProtocol.Frame(d.ModelByte, 19, 58, 0, (byte)(64 | (statusReads > 1 ? 1 : 0)))); }
                else PresetEditChecks.Reply(q, m, d, ref saved, ref loaded, []);
            } };
            await using (var monitorSession = new ReadSession(monitorMidi))
            {
                await LiveMonitor.CaptureAsync(monitorSession, context, 2, 100, Path.Combine(root, "monitor"), Timeout, default);
                using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "monitor/monitor.json")));
                Need(result.RootElement.GetProperty("complete").GetBoolean() && result.RootElement.GetProperty("changed_sample_indices")[0].GetInt32() == 1, "Bounded monitor reports sampled effective-state change");
                Need(monitorMidi.Sent.All(SysexProtocol.IsReadRequest), "Monitor only sends allowlisted queries");
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                await RejectAsync<OperationCanceledException>(() => LiveMonitor.CaptureAsync(monitorSession, context, 2, 100, Path.Combine(root, "monitor-cancel"), Timeout, cancelled.Token));
                Need(File.ReadAllText(Path.Combine(root, "monitor-cancel/monitor.json")).Contains("\"complete\": false"), "Monitor cancellation retains incomplete evidence");
            }
            foreach (string kind in new[] { "scene", "channel", "bypass" })
            foreach (bool configured in new[] { false, true })
            foreach (string outcome in new[] { "success", "ignored", "unrelated", "stale", "saved-change", "nack" })
            {
                saved = Fixtures.FromBody(d, 0, fixture.Body);
                byte[] unsaved = fixture.Body.ToArray(); unsaved[^1] ^= 1; loaded = Fixtures.FromBody(d, 0, unsaved);
                int scene = 3, channel = 1, sends = 0; bool bypassed = false;
                var midi = new FakeEditorMidi();
                midi.Reply = (q, m) =>
                {
                    bool shortMessage = q.Length == 3;
                    bool control = shortMessage || q[5] is 10 or 11 || q[5] == 12 && q[6] != 127;
                    if (control)
                    {
                        sends++;
                        if (shortMessage) Need(q.SequenceEqual(new byte[] { 0xbf, 17, 99 }), "Explicit configured CC bytes and channel conversion");
                        if (outcome == "nack") { m.Error(new IOException("Synthetic rejection")); return; }
                        if (outcome == "ignored") return;
                        byte[] b = loaded.Image.Body.ToArray();
                        if (kind == "scene") { scene = shortMessage ? 5 : q[6] + 1; Put(b, 0, scene - 1); }
                        if (kind == "channel") { channel = shortMessage ? 3 : q[8] + 1; Put(b, fixture.Offsets[58] + 2, channel - 1); }
                        if (kind == "bypass") { bypassed = shortMessage || q[8] == 1; Put(b, fixture.Offsets[58] + 18, bypassed ? 1 : 0); }
                        if (outcome == "unrelated") b[^2] ^= 1;
                        loaded = Fixtures.FromBody(d, 0, b);
                        if (outcome == "saved-change") saved = loaded;
                    }
                    else if (q[5] == 12) m.Emit(SysexProtocol.Frame(d.ModelByte, 12, (byte)(scene - 1)));
                    else if (q[5] == 19) m.Emit(SysexProtocol.Frame(d.ModelByte, 19, 58, 0, (byte)(64 | (channel - 1) << 1 | (bypassed ? 1 : 0))));
                    else PresetEditChecks.Reply(q, m, d, ref saved, ref loaded, []);
                };
                LiveControlRequest request;
                await using (var reader = new ReadSession(midi))
                {
                    var capture = await LiveControlScope.CaptureAsync(reader, context, Timeout, default);
                    request = new(1, capture, kind, kind == "scene" ? null : 58, kind == "channel" ? "C" : null, kind == "bypass" ? true : null, kind == "scene" ? 5 : null, configured ? new(16, 17, 99) : null);
                }
                var scope = new LiveControlScope(request); midi.Scope = scope;
                var hardwareHeader = Convert.FromBase64String(request.Context.LoadedCapture);
                hardwareHeader[7] = 0;
                hardwareHeader[11] = SysexProtocol.ComputeChecksum(hardwareHeader.AsSpan(0, 11));
                _ = new LiveControlScope(request with { Context = request.Context with { LoadedCapture = Convert.ToBase64String(hardwareHeader) } });
                Reject<InvalidOperationException>(() => scope.Validate(EditorCommand.SelectScene(d, 0, 5)));
                if (outcome == "stale") { byte[] b = loaded.Image.Body.ToArray(); b[^3] ^= 1; loaded = Fixtures.FromBody(d, 0, b); }
                await using (var execution = new ReadSession(midi))
                {
                    string folder = Path.Combine(root, kind + configured + "-" + outcome);
                    if (outcome == "success")
                    {
                        await scope.ExecuteAsync(execution, context, folder, Timeout, default);
                        Need(sends == 1 && saved.Image.Body.SequenceEqual(fixture.Body) && loaded.Image.Body[^1] == unsaved[^1], "One live command preserves saved and unrelated unsaved content");
                    }
                    else
                    {
                        if (outcome == "nack") await RejectAsync<IOException>(() => scope.ExecuteAsync(execution, context, folder, Timeout, default));
                        else await RejectAsync<InvalidDataException>(() => scope.ExecuteAsync(execution, context, folder, Timeout, default));
                        Need(sends == (outcome == "stale" ? 0 : 1), "No blind retries or stale writes");
                        Need(File.ReadAllText(Path.Combine(folder, "attempt.json")).Contains("stopped-uncertain"), "Failure evidence retained");
                    }
                }
                if (outcome == "success")
                {
                    await using var reader = new ReadSession(midi);
                    var repeat = request with { Context = await LiveControlScope.CaptureAsync(reader, context, Timeout, default) };
                    var noOp = new LiveControlScope(repeat); midi.Scope = noOp;
                    await noOp.ExecuteAsync(reader, context, Path.Combine(root, kind + configured + "-repeat"), Timeout, default);
                    Need(noOp.Unchanged && sends == 1, "Repeat absolute intent is a no-op");
                    Reject<ArgumentException>(() => new LiveControlScope(request with { Kind = "channel", Scene = null, Bypassed = null, Channel = "E", EffectId = 58 }));
                    Reject<InvalidDataException>(() => new LiveControlScope(request with { Kind = "bypass", Scene = null, Channel = null, Bypassed = true, EffectId = 999 }));
                    if (destination != null && kind == "channel")
                    {
                        string output = Path.Combine(destination, $"{(configured ? "Configured" : "Live")}-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}"); Directory.CreateDirectory(output);
                        File.WriteAllText(Path.Combine(output, "request.json"), SnapshotExport.Json(request));
                        File.WriteAllText(Path.Combine(output, "context.json"), SnapshotExport.Json(request.Context));
                    }
                }
            }
        }
        finally { Directory.Delete(root, true); }
    }
    static byte[] RosterReply(DeviceDefinition d, int address, string name)
    {
        // Independent bit-string encoder, not the production unpacker or parameter catalogue.
        byte[] raw = Encoding.ASCII.GetBytes(name + "\0"); var packed = new List<byte>();
        foreach (var group in raw.Chunk(7))
        {
            string bits = string.Concat(group.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
            bits = bits.PadRight(((bits.Length + 6) / 7) * 7, '0');
            for (int i = 0; i < bits.Length; i += 7) packed.Add(Convert.ToByte(bits.Substring(i, 7), 2));
        }
        byte[] payload = new byte[15 + packed.Count]; payload[0] = 31; payload[2] = 58; payload[4] = (byte)address; payload[13] = (byte)raw.Length;
        packed.CopyTo(payload, 15); return SysexProtocol.Frame(d.ModelByte, 1, payload);
    }
    static void Put(byte[] b, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(offset), (ushort)value);
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static async Task RejectAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
