using System.Text.Json;
using Fractal.Core;

static class PerformanceControlChecks
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    public static async Task Run(DeviceDefinition d, string? destination)
    {
        string root = Path.Combine(Path.GetTempPath(), "fractal-performance-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            string prefix = $"F0000174{d.ModelByte:X2}";
            Need(Convert.ToHexString(EditorCommand.SetTempo(d, 0, 250).Frame)[..12] == prefix + "14", "Own model tempo frame");
            Need(EditorCommand.SetTempo(d, 0, 250).Frame.AsSpan(6, 2).SequenceEqual(new byte[] { 122, 1 }), "Tempo LS-first septets");
            Need(PerformanceProtocol.DecodeTempo(d, SysexProtocol.Frame(d.ModelByte, 20, 20, 0)) == 20, "Lower manual bound");
            Need(PerformanceProtocol.DecodeTempo(d, SysexProtocol.Frame(d.ModelByte, 20, 122, 1)) == 250, "Upper manual bound");
            Need(!SysexProtocol.IsReadRequest(SysexProtocol.Frame(d.ModelByte, 20, 127, 127)), "Legacy-risk tempo query outside general read allowlist");
            Need(SysexProtocol.IsReadRequest(PerformanceProtocol.LooperQuery(d)), "Only looper query allowlisted");
            foreach (int button in Enumerable.Range(0, 6)) Need(!SysexProtocol.IsReadRequest(SysexProtocol.Frame(d.ModelByte, 15, (byte)button)), "Looper buttons not reads");
            var buttons = new[] { "record", "play", "undo", "once", "reverse", "half-speed" };
            for (int i = 0; i < buttons.Length; i++) Need(EditorCommand.LooperButton(d, 0, buttons[i]).Frame[6] == i, "Independent six-button command values");
            Need(EditorCommand.TapTempo(d, 0).Frame.Length == 8 && EditorCommand.TapTempo(d, 0).Frame[5] == 16, "Single tap has no payload");
            Need(EditorCommand.SetTuner(d, 0, false).Frame[6] == 0 && EditorCommand.SetTuner(d, 0, true).Frame[6] == 1, "Explicit tuner Boolean values");
            Reject<ArgumentOutOfRangeException>(() => EditorCommand.SetTempo(d, 0, 19));
            Reject<ArgumentOutOfRangeException>(() => EditorCommand.SetTempo(d, 0, 251));
            Reject<InvalidDataException>(() => PerformanceProtocol.DecodeTempo(d, SysexProtocol.Frame(d.ModelByte, 20, 127, 127)));
            Reject<InvalidDataException>(() => PerformanceProtocol.DecodeLooper(d, SysexProtocol.Frame(d.ModelByte, 15, 64)));
            byte[] bad = SysexProtocol.Frame(d.ModelByte, 15, 1); bad[^2] ^= 1;
            Reject<InvalidDataException>(() => PerformanceProtocol.DecodeLooper(d, bad));
            Reject<InvalidDataException>(() => PerformanceProtocol.DecodeLooper(d, SysexProtocol.Frame(d.ModelByte, 15, 1, 0)));
            var context = new DeviceContext(d, "99.07", "synthetic");
            var fixture = AudioRoutingChecks.Build(d);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(fixture.Body.AsSpan(0, 2), 2);
            foreach (string kind in new[] { "tempo-read", "tempo-set", "tempo-tap", "tuner-set", "looper-button", "looper-state" })
            foreach (string absoluteAction in kind == "looper-state" ? new[] { "play", "reverse", "half-speed" } : new[] { "reverse" })
            foreach (string outcome in new[] { "success", "ignored", "stale", "unrelated", "saved-change", "nack", "timeout", "cancel", "legacy-query" })
            {
                if (outcome is "timeout" or "legacy-query" && kind is not ("tempo-read" or "tempo-set")) continue;
                if (outcome == "ignored" && kind != "tempo-set" && kind != "looper-state") continue;
                var saved = Fixtures.FromBody(d, 0, fixture.Body);
                byte[] unsaved = fixture.Body.ToArray(); unsaved[^1] ^= 1;
                var loaded = Fixtures.FromBody(d, 0, unsaved);
                int bpm = 120, flags = 0, commands = 0, queries = 0; bool running = false;
                using var cancellation = new CancellationTokenSource();
                var midi = new FakeEditorMidi();
                midi.Reply = (q, m) =>
                {
                    if (q[5] == 15 && q[6] == 127)
                    {
                        m.Emit(SysexProtocol.Frame((byte)(d.ModelByte == 16 ? 17 : 16), 15, 63));
                        m.Emit(SysexProtocol.Frame(d.ModelByte, 15, (byte)flags));
                    }
                    else if (q[5] == 20 && q[6] == 127 && q[7] == 127)
                    {
                        queries++;
                        if (outcome == "timeout") return;
                        if (outcome == "legacy-query") { bpm = 250; byte[] b = loaded.Image.Body.ToArray(); b[^2] ^= 1; loaded = Fixtures.FromBody(d, 0, b); }
                        m.Emit(new byte[] { 240, 0, 1, 116, d.ModelByte, 16, 247 }); // checksumless push cannot satisfy this exchange
                        m.Emit(SysexProtocol.Frame((byte)(d.ModelByte == 16 ? 17 : 16), 20, 99, 0));
                        m.Emit(SysexProtocol.Frame(d.ModelByte, 20, (byte)(bpm & 127), (byte)(bpm >> 7)));
                    }
                    else if (q[5] is 15 or 16 or 17 or 20)
                    {
                        commands++;
                        if (outcome == "nack") { m.Emit(SysexProtocol.Frame(d.ModelByte, 100, q[5], 1)); return; }
                        if (outcome == "cancel") { cancellation.Cancel(); return; }
                        if (outcome == "ignored") return;
                        if (q[5] == 20) bpm = q[6] | q[7] << 7;
                        if (q[5] == 15) flags ^= q[6] switch { 1 => 2, 4 => 16, 5 => 32, _ => 1 };
                        if (outcome == "unrelated") { byte[] b = loaded.Image.Body.ToArray(); b[^2] ^= 1; loaded = Fixtures.FromBody(d, 0, b); }
                        if (outcome == "saved-change") saved = loaded;
                    }
                    else if (q[5] == 19) m.Emit(SysexProtocol.Frame(d.ModelByte, 19, 58, 0, 64, 38, 1, 64)); // IDs 58 and 166
                    else PresetEditChecks.Reply(q, m, d, ref saved, ref loaded, []);
                    if (running && kind == "tempo-read" && outcome is "unrelated" or "saved-change" or "nack" or "cancel")
                    {
                        if (outcome == "cancel") cancellation.Cancel();
                        else if (outcome == "nack") m.Error(new IOException("Synthetic query failure"));
                        else if (outcome == "saved-change") saved = loaded;
                        else { byte[] b = loaded.Image.Body.ToArray(); b[^2] ^= 1; loaded = Fixtures.FromBody(d, 0, b); }
                    }
                };
                PerformanceControlRequest request;
                await using (var reader = new ReadSession(midi))
                {
                    request = new(1, await PerformanceControlScope.CaptureAsync(reader, context, Timeout, default), kind,
                        kind == "tempo-set" ? 140 : null, kind is "tuner-set" or "looper-state" ? true : null,
                        kind == "looper-state" ? absoluteAction : kind == "looper-button" ? "record" : null);
                }
                var scope = new PerformanceControlScope(request); midi.Scope = scope; running = true;
                var boundaryScope = new PerformanceControlScope(request) { Ready = true };
                var authority = new MidiSendAuthority(boundaryScope);
                var boundaryCommand = kind switch
                {
                    "tempo-read" => EditorCommand.QueryTempo(d, 0), "tempo-set" => EditorCommand.SetTempo(d, 0, 140),
                    "tempo-tap" => EditorCommand.TapTempo(d, 0), "tuner-set" => EditorCommand.SetTuner(d, 0, true),
                    _ => EditorCommand.LooperButton(d, 0, request.Action!)
                };
                authority.Editor(boundaryCommand);
                Reject<InvalidOperationException>(() => authority.Editor(boundaryCommand));
                if (kind == "tempo-set")
                {
                    authority.Editor(EditorCommand.QueryTempo(d, 0)); authority.Editor(EditorCommand.QueryTempo(d, 0));
                    Reject<InvalidOperationException>(() => authority.Editor(EditorCommand.QueryTempo(d, 0)));
                }
                Reject<InvalidOperationException>(() => scope.Validate(EditorCommand.TapTempo(d, 0)));
                if (outcome == "stale") { byte[] b = loaded.Image.Body.ToArray(); b[^3] ^= 1; loaded = Fixtures.FromBody(d, 0, b); }
                string folder = Path.Combine(root, kind + "-" + absoluteAction + "-" + outcome);
                await using (var session = new ReadSession(midi))
                {
                    if (outcome == "success")
                    {
                        var result = JsonSerializer.SerializeToElement(await scope.ExecuteAsync(session, context, folder, Timeout, default), PerformanceControlScope.JsonOptions);
                        Need(result.GetProperty("state_verified").GetBoolean() == (kind is "tempo-read" or "tempo-set" or "looper-state"), "Honest verification limits for events and tuner");
                        Need(commands == (kind == "tempo-read" ? 0 : 1) && queries == (kind == "tempo-set" ? 2 : kind == "tempo-read" ? 1 : 0), "Exact event and query budget; no unrelated queries");
                        Need(saved.Image.Body.SequenceEqual(fixture.Body) && loaded.Image.Body.SequenceEqual(unsaved), "Saved and unsaved preservation");
                        await RejectAsync<InvalidOperationException>(() => session.SendEditorAsync(EditorCommand.TapTempo(d, 0), Timeout, default));
                        if (destination != null)
                        {
                            string output = Path.Combine(destination, $"Performance-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}"); Directory.CreateDirectory(output);
                            File.WriteAllText(Path.Combine(output, kind + ".json"), SnapshotExport.Json(request));
                            File.WriteAllText(Path.Combine(output, "context.json"), SnapshotExport.Json(request.Context));
                        }
                    }
                    else
                    {
                        await RejectAsync<Exception>(() => scope.ExecuteAsync(session, context, folder, outcome == "timeout" ? TimeSpan.FromMilliseconds(50) : Timeout, cancellation.Token));
                        Need(commands <= 1 && queries <= 2 && (outcome != "stale" || commands == 0 && queries == 0), "Stale input rejected and uncertain commands never replayed");
                        Need(File.ReadAllText(Path.Combine(folder, "attempt.json")).Contains("stopped-uncertain"), "Failure evidence retained");
                        if (outcome == "legacy-query") Need(commands == 0 && queries == 1 && File.Exists(Path.Combine(folder, "after-query.json")), "Legacy query side effect stops before set");
                    }
                }
                if (outcome == "success" && kind is "tempo-set" or "looper-state")
                {
                    await using var reader = new ReadSession(midi);
                    var repeat = new PerformanceControlScope(request with { Context = await PerformanceControlScope.CaptureAsync(reader, context, Timeout, default) }); midi.Scope = repeat;
                    await repeat.ExecuteAsync(reader, context, folder + "-repeat", Timeout, default);
                    Need(repeat.Unchanged && commands == 1, "Repeated absolute intent produces no button or set");
                }
                Reject<ArgumentException>(() => new PerformanceControlScope(request with { Kind = "tuner-set", Bpm = 120, Enabled = true, Action = null }));
                Reject<ArgumentException>(() => new PerformanceControlScope(request with { Kind = "looper-state", Bpm = null, Enabled = true, Action = "record" }));
                Reject<ArgumentException>(() => new PerformanceControlScope(request with { Context = request.Context with { Looper = null } }));
            }
        }
        finally { Directory.Delete(root, true); }
    }
    static void Need(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static async Task RejectAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
