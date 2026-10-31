using System.Text.Json;
using Fractal.Core;

static class PrivateParameterChecks
{
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected invalid private capture."); }
    static async Task RejectAsync(Func<Task> action) { try { await action(); } catch (Exception e) when (e is IOException or InvalidDataException or TimeoutException) { return; } throw new Exception("Expected failed capture."); }
    static byte[][] Burst(DeviceDefinition d, int effect = 58, int total = 292)
    {
        // Independent positional words and actual little-first per-page field counts.
        byte[] Page(int start, int count) => SysexProtocol.Frame(d.ModelByte, 0x75,
            [(byte)(count & 127), (byte)(count >> 7), .. Enumerable.Range(start, count).SelectMany(v => new[] { (byte)(v & 127), (byte)(v >> 7), (byte)0 })]);
        return [SysexProtocol.Frame(d.ModelByte, 0x74, (byte)(effect & 127), (byte)(effect >> 7), (byte)(total & 127), (byte)(total >> 7)),
            .. Enumerable.Range(0, (total + 255) / 256).Select(i => Page(i * 256, Math.Min(256, total - i * 256))),
            SysexProtocol.Frame(d.ModelByte, 0x76)];
    }
    public static async Task Run(DeviceDefinition d)
    {
        var frames = Burst(d); var decoder = new BlockParameterDecoder(d, 58);
        Need(frames.Take(3).All(f => decoder.Accept(f) == null) && decoder.Incomplete, "Partial table never becomes complete");
        var result = decoder.Accept(frames[3])!;
        int typeId = d.Model == "FM9" ? 10 : d.Model == "FM3" ? 6 : 0;
        Need(result.Parameters.Length == 292 && result.Parameters[291].RawValue == 291 && result.Parameters[typeId].Mappings.Any(m => m.Symbol == "DISTORT_TYPE"), "Own-model Amp ID mapping across section boundary");
        Need(result.SectionMetadata.SequenceEqual(new[] { 256, 36 }) && !result.HardwareQualified && !decoder.Incomplete, "Page counts and evidence limits");
        var threePages = new BlockParameterDecoder(d, 58); BlockParameterCapture? longer = null;
        foreach (var frame in Burst(d, total: 588)) longer = threePages.Accept(frame);
        Need(longer!.SectionMetadata.SequenceEqual(new[] { 256, 256, 76 }) && longer.Parameters[587].RawValue == 587,
            "Three count-prefixed pages preserve positional order and exact terminal total");
        using var page = JsonDocument.Parse(SnapshotExport.Json(DeviceNotifications.Decode(d, frames[1]).Value!));
        Need(page.RootElement.GetProperty("field_count").GetInt32() == 256, "Notification page count uses actual two-byte field count");
        var wrongCount = frames[1].ToArray(); wrongCount[6] = 127;
        wrongCount[^2] = SysexProtocol.ComputeChecksum(wrongCount.AsSpan(0, wrongCount.Length - 2));
        Reject(() => { var state = new BlockParameterDecoder(d); state.Accept(frames[0]); state.Accept(wrongCount); });
        Need(!DeviceNotifications.Decode(d, wrongCount).Valid, "Notification decoder refuses inconsistent page count");
        foreach (var family in EditReadCatalogue.Parameters(d, "99.07").EditorParameters.Entries.GroupBy(p => p.Family))
        {
            string blockFamily = family.Key switch { "Gate" => "Gate/Expander", "Megatap Delay" => "Megatap", "Tremolo" => "Tremolo/Panner", _ => family.Key };
            var identity = Enumerable.Range(1, 1000).Select(id => BlockCatalogue.Resolve(d.Model, id)).FirstOrDefault(b => b.Family == blockFamily);
            if (identity == null) continue; // An unresolved block ID is not invented from a parameter family.
            var state = new BlockParameterDecoder(d); BlockParameterCapture? mapped = null;
            foreach (var frame in Burst(d, identity.EffectId)) mapped = state.Accept(frame);
            Need(mapped!.Parameters.SelectMany(p => p.Mappings).SequenceEqual(family.Where(p => p.ParameterId < 292).OrderBy(p => p.ParameterId)), "Broader families use their own model catalogue, preserving aliases");
        }
        var flagged = new BlockParameterDecoder(d); flagged.Accept(SysexProtocol.Frame(d.ModelByte, 0x74, 58, 0, 36, 2, 17));
        flagged.Accept(frames[1]); flagged.Accept(frames[2]); Need(flagged.Accept(frames[3])!.HeaderMetadata == 17, "Optional documented header metadata retained");
        Reject(() => decoder.Accept(frames[0]));
        foreach (int bad in new[] { 0, 1, 2, 3 })
        {
            var corrupt = frames.Select(f => f.ToArray()).ToArray(); corrupt[bad][^2] ^= 1;
            Reject(() => { var state = new BlockParameterDecoder(d); foreach (var f in corrupt) state.Accept(f); });
        }
        foreach (var order in new[] { new[] { 0, 2, 1, 3 }, new[] { 0, 1, 1, 3 }, new[] { 0, 1, 3 }, new[] { 0, 0 }, new[] { 0, 1, 2, 2 } })
            Reject(() => { var state = new BlockParameterDecoder(d); foreach (int i in order) state.Accept(frames[i]); });
        var interleaved = new BlockParameterDecoder(d, 58); interleaved.Accept(frames[0]); Reject(() => interleaved.Accept(Burst(d, 66)[0]));
        var unrelated = new BlockParameterDecoder(d, 66); Need(frames.All(f => unrelated.Accept(f) == null) && !unrelated.Incomplete, "Unmatched address ignored before a transfer");
        var unknown = new BlockParameterDecoder(d); BlockParameterCapture? unknownResult = null;
        foreach (var f in Burst(d, 1)) unknownResult = unknown.Accept(f);
        Need(unknownResult!.Parameters.All(p => p.Mappings.Length == 0), "Unknown family retains words without borrowed mappings");
        var overflow = frames[1].ToArray(); overflow[10] = 4; overflow[^2] = SysexProtocol.ComputeChecksum(overflow.AsSpan(0, overflow.Length - 2));
        Reject(() => { var state = new BlockParameterDecoder(d); state.Accept(frames[0]); state.Accept(overflow); });
        Need(SysexProtocol.IsReadRequest(SysexProtocol.BlockParametersQuery(d, 66)) && !SysexProtocol.IsReadRequest(SysexProtocol.Frame(d.ModelByte, 31, 66, 0, 1)), "Typed query admits no write field");

        // IEEE754 0.5 = 0x3f000000 -> five little-endian septets; display 'A' -> MSB-packed 32,64.
        byte[] Echo(byte action, byte final = 3, params byte[] tail) => SysexProtocol.Frame(d.ModelByte, 1,
            [action, 0, 58, 0, (byte)typeId, 0, 0, 0, 0, 120, final, 1, 0, (byte)(tail.Length > 0 ? 1 : 0), 0, .. tail]);
        using var echo = JsonDocument.Parse(SnapshotExport.Json(DeviceNotifications.Decode(d, Echo(9)).Value!));
        Need(echo.RootElement.GetProperty("raw_float_value").GetSingle() == 0.5f && echo.RootElement.GetProperty("type_ordinal").ValueKind == JsonValueKind.Null, "Float is raw, not an inferred type ordinal");
        foreach (byte action in new byte[] { 9, 0x52, 0x1f })
        {
            using var display = JsonDocument.Parse(SnapshotExport.Json(DeviceNotifications.Decode(d, Echo(action, 3, 32, 64)).Value!));
            Need(display.RootElement.GetProperty("display").GetString() == "A", "MSB-packed display, partial group");
        }
        Need(!DeviceNotifications.Decode(d, Echo(9, 16)).Valid && !DeviceNotifications.Decode(d, Echo(9, 3, 32, 65)).Valid, "Float overflow and display padding refused");
        Need(DeviceNotifications.Decode(d, SysexProtocol.Frame(d.ModelByte, 1, 4, 1, 58, 0)).Kind == "unknown-fractal", "Unproven 04/01 does not invent a parameter");
        Need(DeviceNotifications.Decode(d, SysexProtocol.Frame(d.ModelByte, 0x64, 1, 0)).Kind == "private-result", "Raw result not persistence");
        var observer = new DeviceNotificationObserver(d);
        Need(!observer.Observe(frames[1]).Valid, "Orphan private section visible");
        observer.Observe(frames[0]); Need(!observer.Observe(frames[0]).Valid && observer.Incomplete, "Interrupted transfer recorded before fresh start");
        observer.Observe(frames[1]); observer.Observe(frames[2]);
        Need(observer.Observe(frames[3]).Kind == "private-block-parameters" && !observer.Incomplete, "Observer emits complete mapped burst");

        string root = Path.Combine(Path.GetTempPath(), "fractal-private-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var context = new DeviceContext(d, "99.07", "synthetic"); int polls = 0, statuses = 0;
            bool truncate = false, drift = false, absent = false;
            var midi = new FakeMidi { Reply = (q, m) =>
            {
                if (q[5] == 31) { polls++; foreach (var f in frames.Take(truncate ? 2 : 4)) m.Fragment(f); }
                else if (q[5] == 19) { statuses++; m.Emit(SysexProtocol.Frame(d.ModelByte, 19, absent ? [] : new byte[] { 58, 0, (byte)(64 | (drift && statuses > 1 ? 1 : 0)) })); }
                else if (q[5] == 12) m.Emit(SysexProtocol.Frame(d.ModelByte, 12, 0));
                else if (q[5] == 13) m.Emit(SysexProtocol.Frame(d.ModelByte, 13, new byte[34]));
            } };
            await using (var session = new ReadSession(midi))
                await BlockParameterRead.CaptureAsync(session, context, 58, Path.Combine(root, "complete"), TimeSpan.FromSeconds(1), default);
            Need(polls == 1 && File.ReadAllBytes(Path.Combine(root, "complete/received.syx")).SequenceEqual(frames.SelectMany(f => f)), "Read retains exact reply, one query");
            truncate = true;
            await using (var session = new ReadSession(midi))
            {
                await RejectAsync(() => BlockParameterRead.CaptureAsync(session, context, 58, Path.Combine(root, "partial"), TimeSpan.FromMilliseconds(80), default));
                await RejectAsync(() => session.BlockParametersAsync(context, 58, TimeSpan.FromSeconds(1), default));
            }
            Need(polls == 2 && new FileInfo(Path.Combine(root, "partial/received.syx")).Length > 0 && !JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "partial/parameters.json"))).RootElement.GetProperty("complete").GetBoolean(), "Partial read retained, failed session cannot replay");
            truncate = false; drift = true; statuses = 0;
            await using (var session = new ReadSession(midi))
                await RejectAsync(() => BlockParameterRead.CaptureAsync(session, context, 58, Path.Combine(root, "drift"), TimeSpan.FromSeconds(1), default));
            absent = true;
            await using (var session = new ReadSession(midi))
                await RejectAsync(() => BlockParameterRead.CaptureAsync(session, context, 58, Path.Combine(root, "absent"), TimeSpan.FromSeconds(1), default));
            Need(polls == 3, "Absent instance refused before parameter query");
            var passive = new FakeMidi();
            var capture = DeviceNotifications.CaptureAsync(passive, d, "99.07", Path.Combine(root, "passive"), TimeSpan.FromSeconds(1), 4, default);
            foreach (var f in frames) passive.Fragment(f);
            await capture; Need(passive.Sent.Count == 0, "Passive private capture sends nothing");
            var incomplete = DeviceNotifications.CaptureAsync(passive, d, "99.07", Path.Combine(root, "incomplete"), TimeSpan.FromMilliseconds(80), 4, default);
            passive.Emit(frames[0]); await RejectAsync(() => incomplete);
        }
        finally { Directory.Delete(root, true); }
    }
}
