using System.Text;
using System.Text.Json;
using Fractal.Core;

#if TEST_LAB_OFFLINE_WINDOWS
if (args.Length != 0 && !(args.Length == 1 && (args[0] is "--checkpoint-files-only" or "--restore-deadlines-only" or "--windows-input-map-only" or "--shared-midi-only" || !args[0].StartsWith("--", StringComparison.Ordinal))))
    throw new ArgumentException("The offline profile accepts only a fixture output directory or a named offline regression group; no transport or probe arguments.");
#else
if (args.Length == 2 && args[0] == "--native-lease-probe")
{
    try { using var lease = NativeMidiPortLease.Acquire(2, null, args[1]); }
    catch (IOException) { Environment.ExitCode = 23; }
    return;
}
#endif
int passed = 0;
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
async Task Check(string name, Func<Task> test)
{ await test(); Console.WriteLine($"PASS {name}"); passed++; }
Task Sync(Action test) { test(); return Task.CompletedTask; }
void Throws<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{ try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
var definitions = new[] { DeviceDefinition.Create("FM9"), DeviceDefinition.Create("FM3"),
    DeviceDefinition.Create("Axe-Fx III", 512), DeviceDefinition.Create("Axe-Fx III", 1024) };
TimeSpan timeout = TimeSpan.FromSeconds(1);
if (args.SequenceEqual(new[] { "--windows-input-map-only" }))
{
    await Check("Windows native input names and indexed aliases require a unique exact mapping", WindowsInputSelectionChecks.Run);
    Console.WriteLine("Windows input mapping checks passed; no MIDI ports opened.");
    return;
}
if (args.SequenceEqual(new[] { "--restore-deadlines-only" }))
{
    foreach (var (name, test) in RestoreDeadlineChecks.Cases()) await Check(name, test);
    Console.WriteLine($"{passed} restore deadline checks passed; no MIDI ports opened.");
    return;
}
foreach (var (name, test) in InventoryFileChecks.Cases()) await Check(name, () => Sync(test));
if (args.SequenceEqual(new[] { "--checkpoint-files-only" }))
{
    Console.WriteLine($"{passed} checkpoint filesystem checks passed; no MIDI ports opened.");
    return;
}
await Check("Windows native input names and indexed aliases require a unique exact mapping", WindowsInputSelectionChecks.Run);
await Check("shared MIDI adapter authorization, failures and lifetime", SharedMidiTransportChecks.Run);
#if !TEST_LAB_OFFLINE_WINDOWS
await Check("CoreMIDI selection, scopes, lifecycle and port ownership", CoreMidiTransportChecks.Run);
#endif
if (args.SequenceEqual(new[] { "--shared-midi-only" })) return;
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} offline setlist/song proposals", () => SetlistSongChecks.Run(d,
        args.Length == 2 && args[0] == "--setlists-only" ? Path.GetFullPath(args[1]) :
        args.Length > 0 && !args[0].StartsWith("--") ? Path.GetFullPath(args[0]) : null));
if (args.Length > 0 && args[0] == "--setlists-only") return;
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} preset organization and eligible undo", () => PresetOrganizationChecks.Run(d,
        args.Length == 2 && args[0] == "--organization-only" ? Path.GetFullPath(args[1]) :
        args.Length > 0 && !args[0].StartsWith("--") ? Path.GetFullPath(args[0]) : null));
if (args.Length > 0 && args[0] == "--organization-only") return;
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} bounded tempo, tuner and looper controls", () => PerformanceControlChecks.Run(d,
        args.Length > 0 && !args[0].StartsWith("--") ? Path.GetFullPath(args[0]) : null));
if (args.SequenceEqual(new[] { "--performance-only" })) return;
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} private template library and channel swaps", () => TemplateLibraryChecks.Run(d,
        args.Length > 0 && !args[0].StartsWith("--") ? Path.GetFullPath(args[0]) : null));
if (args.SequenceEqual(new[] { "--templates-only" })) return;
if (args.SequenceEqual(new[] { "--inventory-only" }))
{
    foreach (var d in definitions)
        await Check($"{d.Model}/{d.PresetCapacity} inventory comparison, amp usage and migration", () => InventoryAnalysisChecks.Run(d));
    return;
}
if (args.SequenceEqual(new[] { "--selective-undo-only" }))
{
    foreach (var d in definitions)
        await Check($"{d.Model}/{d.PresetCapacity} receipt-based selective Scene MIDI undo", () => SelectiveUndoChecks.Run(d));
    return;
}
if (args.SequenceEqual(new[] { "--preset-edit-only" }))
{
    foreach (var d in definitions)
    {
        await Check($"{d.Model}/{d.PresetCapacity} general edit batches", () => BatchAndMidiChecks.Run(d, null));
        await Check($"{d.Model}/{d.PresetCapacity} complete preset editing", () => PresetEditChecks.Run(d, null));
    }
    return;
}
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} built-in saved scales and named modifier curves", () => BuiltinScaleChecks.Run(d, args.Length > 0 && !args[0].StartsWith("--") ? Path.GetFullPath(args[0]) : null));
if (args.SequenceEqual(new[] { "--scales-only" })) return;
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} calibrated setters, Scene Ignore and modifier authoring", () => CalibrationModifierChecks.Run(d, args.Length > 0 && args[0] != "--calibration-only" ? Path.GetFullPath(args[0]) : null));
if (args.SequenceEqual(new[] { "--calibration-only" })) return;
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} private parameter mappings and notifications", () => PrivateParameterChecks.Run(d));
#if !TEST_LAB_OFFLINE_WINDOWS
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} RTP network and device notifications", () => NetworkNotificationChecks.Run(d));
#endif
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} automatic paths and bounded controller listener", () => PathAndListenerChecks.Run(d, args.Length > 0 ? Path.GetFullPath(args[0]) : null));
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} general edit batches and configured MIDI routing", () => BatchAndMidiChecks.Run(d, args.Length > 0 ? Path.GetFullPath(args[0]) : null));
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} isolated scopes preserve unrelated routes and reject actual connections", () => GridIsolationChecks.Run(d));
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} live roster and scoped controls", () => LiveToolsChecks.Run(d, args.Length > 0 ? Path.GetFullPath(args[0]) : null));

foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} extended parameters, Send/Return feedback and I/O routing", () => SpecialRoutingChecks.Run(d));
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} inventory comparison, amp usage and migration", () => InventoryAnalysisChecks.Run(d));
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} combined Scene MIDI insertion, configuration and selective clear", () => CompoundMidiChecks.Run(d));
foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} receipt-based selective Scene MIDI undo", () => SelectiveUndoChecks.Run(d, args.Length > 0 ? Path.GetFullPath(args[0]) : null));

foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} compound editing, routing, complete-image execution and failure preservation", () =>
        PresetEditChecks.Run(d, args.Length > 0 ? Path.GetFullPath(args[0]) : null));

foreach (var d in definitions)
    await Check($"{d.Model}/{d.PresetCapacity} resumable inventory integrity, failure, drift and export", () =>
        InventoryChecks.Run(d, args.Length > 0 ? Path.GetFullPath(args[0]) : null));

foreach (var (name, test) in EditReadChecks.Cases(definitions, args.Length > 0 ? Path.GetFullPath(args[0]) : null))
    await Check(name, () => Sync(test));

foreach (var (name, test) in AudioRoutingChecks.Cases(definitions, args.Length > 0 ? Path.GetFullPath(args[0]) : null))
    await Check(name, () => Sync(test));

foreach (var d in definitions)
{
    await Check($"{d.Model}/{d.PresetCapacity} sourced block identities and model instance limits", () => Sync(() =>
    {
        var reverb = BlockCatalogue.Resolve(d.Model, 66);
        Assert(reverb.Name == "Reverb 1" && reverb.Instance == 1 && reverb.Availability.DocumentedAvailable == true, "Reverb identity");
        var second = BlockCatalogue.Resolve(d.Model, 67);
        Assert(second.Name == "Reverb 2" && second.Availability.DocumentedAvailable == (d.Model != "FM3"), "Per-model Reverb limits");
        Assert(BlockCatalogue.Resolve(d.Model, 58).Availability.DocumentedInstances == d.MaxAmps, "Amp instance limits");
        Assert(BlockCatalogue.Resolve(d.Model, 70).Name == "Delay 1" && BlockCatalogue.Resolve(d.Model, 118).Name == "Drive 1", "Other effect names");
        Assert(BlockCatalogue.Resolve(d.Model, 190).Name == "Scene MIDI", "Single block label");
        Assert(BlockCatalogue.Resolve(d.Model, 65535).Name == null && BlockCatalogue.Resolve(d.Model, 65535).Availability.DocumentedAvailable == null, "Unknown is not guessed");
        Assert(BlockCatalogue.Resolve(d.Model, 142).IdentificationStatus == (d.Model == "Axe-Fx III" ? "source-mapped" : "unqualified-for-model"), "Manufacturer scope not copied across models");
        Assert(BlockCatalogue.Resolve(d.Model, 40).Availability.DocumentedAvailable == (d.Model == "Axe-Fx III"), "FM9 Input USB is not Input 4");
        Assert(BlockCatalogue.Resolve(d.Model, 41).Availability.DocumentedAvailable == (d.Model != "FM3"), "Input USB limits");
        Assert(BlockCatalogue.Resolve(d.Model, 41).Name == (d.Model == "Axe-Fx III" ? "Input USB" : null), "Input USB numeric identity retains manufacturer scope");
        using var query = JsonDocument.Parse(SnapshotExport.Json(BlockCatalogue.Query(d.Model, "99.07", "REV", null)));
        Assert(query.RootElement.GetProperty("matches").GetArrayLength() == 4 && !query.RootElement.GetProperty("ports_opened").GetBoolean(), "Alias search is offline");
        using var unmapped = JsonDocument.Parse(SnapshotExport.Json(BlockCatalogue.Query(d.Model, null, "Dynamic Distortion", null)));
        Assert(unmapped.RootElement.GetProperty("matches").GetArrayLength() == 0 && unmapped.RootElement.GetProperty("unmapped_families").GetArrayLength() == 1, "Unmapped family reported without inventing IDs");
        Throws<ArgumentException>(() => BlockCatalogue.Query(d.Model, null, null, null));
        Throws<ArgumentException>(() => BlockCatalogue.Query(d.Model, null, " ", null));
        Throws<ArgumentException>(() => BlockCatalogue.Query(d.Model, "99.7", "Reverb", null));
        Throws<ArgumentOutOfRangeException>(() => BlockCatalogue.Resolve(d.Model, -1));
    }));
    await Check($"{d.Model}/{d.PresetCapacity} named grid preserves bytes, positions and routing", () => Sync(() =>
    {
        var fixture = Fixtures.Preset(d, 0);
        byte[] body = fixture.Image.Body.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0x104), 66);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0x106), 5);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0x108), 65535);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0x10c), 1024);
        byte[] original = body.ToArray();
        var decoded = PresetBody.Decode(d, fixture.Image with { Body = body });
        Assert(decoded.TopLeftBlock == "Reverb 1", "General top-left label");
        Assert(decoded.Grid[0] == new GridCell(1, 1, 66, 5) { Name = "Reverb 1" }, "Numeric grid preserved");
        Assert(decoded.Grid[1].Name == null && decoded.Grid[1].EffectId == 65535 && decoded.Grid[1].Kind == "unknown", "Unknown retained");
        Assert(decoded.Grid[2].Name == "Shunt" && decoded.Grid[2].Kind == "shunt" && decoded.Grid[3].Kind == "empty", "Grid elements distinguished");
        Assert(body.SequenceEqual(original), "Naming never changes preset bytes");
    }));
    await Check($"{d.Model}/{d.PresetCapacity} addressing and read policy", () => Sync(() =>
    {
        foreach (int slot in new[] { 0, 127, 128, d.PresetCapacity - 1 })
        {
            byte[] q = SysexProtocol.SavedQuery(d, slot);
            Assert((q[6] << 7 | q[7]) == slot && q[4] == d.ModelByte && SysexProtocol.IsReadRequest(q), "Saved addressing");
            Assert(SysexProtocol.PresetAddress(SysexProtocol.PresetNameQuery(d, slot)) == slot, "Name addressing");
        }
        Throws<ArgumentOutOfRangeException>(() => SysexProtocol.SavedQuery(d, d.PresetCapacity));
        Throws<ArgumentOutOfRangeException>(() => SysexProtocol.SavedQuery(d, -1));
        Assert(!SysexProtocol.IsReadRequest(SysexProtocol.Frame(d.ModelByte, 0x0c, 1)), "Scene navigation rejected");
        Assert(!SysexProtocol.IsReadRequest(SysexProtocol.Frame(d.ModelByte, 1, 0x26)), "Editor save rejected");
        Assert(SysexProtocol.IsReadRequest(SysexProtocol.SceneMidiQuery(d)), "Scene MIDI read allowlisted");
        Assert(SysexProtocol.IsReadRequest(SysexProtocol.BlockParametersQuery(d, 58)), "Explicit block reads allowlisted");
        Assert(!SysexProtocol.IsReadRequest(SysexProtocol.Frame(d.ModelByte, 0x1f, 0, 0)), "Invalid block addresses rejected");
        Assert(SysexProtocol.IsReadRequest(SysexProtocol.EditBufferQuery(d)), "Explicit temporary-buffer read allowed without assigning a saved slot");
        Assert(!SysexProtocol.IsReadRequest(SysexProtocol.Frame(d.ModelByte, 3, 127, 0, 0)), "Other out-of-capacity addresses rejected");
    }));
    SavedCapture fixture = Fixtures.Preset(d, d.PresetCapacity - 1);
    await Check($"{d.Model}/{d.PresetCapacity} dump integrity, body, Amp channels/scenes", () => Sync(() =>
    {
        SavedCapture decoded = SnapshotExport.DecodeFile(d, fixture.Sysex);
        Assert(decoded.Image.RawImage.SequenceEqual(fixture.Image.RawImage) && decoded.Image.Body.SequenceEqual(fixture.Image.Body), "Byte preservation");
        var p = PresetBody.Decode(d, decoded.Image);
        Assert(p.Slot == d.PresetCapacity - 1 && p.Scenes[7].Name == "Synthetic scene 8", "Saved names");
        Assert(p.Amps.Single().Channels.Select(c => c.ModelId).SequenceEqual(new[] { 20, 21, 22, 23 }), "Model-specific Amp layout");
        Assert(p.Amps[0].Scenes[7] == new AmpScene(8, "D", true), "Scene channel/bypass");
        Assert(p.BodyHeaderWords.SequenceEqual(new[] { 17, 6 }) && p.TopLeftBlock == null && p.Populated, "Opaque header/grid preserved");
        Assert(p.Grid.Length == d.GridRows * d.GridColumns, "Model grid size");
    }));
    await Check($"{d.Model}/{d.PresetCapacity} missing/duplicate/extra chunks and corruption rejected", () => Sync(() =>
    {
        byte[][] frames = Fixtures.Frames(fixture.Sysex);
        byte[] Flatten(IEnumerable<byte[]> selected) => selected.SelectMany(f => f).ToArray();
        Throws<InvalidDataException>(() => SnapshotExport.DecodeFile(d, Flatten(frames.Where((_, i) => i != 2))));
        Throws<InvalidDataException>(() => SnapshotExport.DecodeFile(d, Flatten(frames.Take(2).Concat(new[] { frames[1] }).Concat(frames.Skip(2)))));
        Throws<InvalidDataException>(() => SnapshotExport.DecodeFile(d, Flatten(frames.Take(frames.Length - 1).Concat(new[] { frames[1], frames[^1] }))));
        // Same chunk count but a repeated substantive chunk: integrity must reject it.
        var repeated = frames.ToArray(); repeated[2] = frames[1];
        Throws<InvalidDataException>(() => SnapshotExport.DecodeFile(d, Flatten(repeated)));
        byte[] corrupt = fixture.Sysex.ToArray(); corrupt[100] ^= 1;
        Throws<InvalidDataException>(() => SnapshotExport.DecodeFile(d, corrupt));
        // Valid frame checksums/footer after raw CRC corruption: CRC must independently reject it.
        var changed = frames.Select(f => f.ToArray()).ToArray();
        changed[1][14] ^= 1; changed[1][^2] = SysexProtocol.ComputeChecksum(changed[1].AsSpan(0, changed[1].Length - 2));
        changed[^1][6] ^= 1; changed[^1][^2] = SysexProtocol.ComputeChecksum(changed[^1].AsSpan(0, changed[^1].Length - 2));
        Throws<InvalidDataException>(() => SnapshotExport.DecodeFile(d, Flatten(changed)));
        var other = DeviceDefinition.Create(d.Model == "FM9" ? "FM3" : "FM9");
        Throws<InvalidDataException>(() => SnapshotExport.DecodeFile(other, fixture.Sysex));
        Throws<InvalidDataException>(() => SnapshotExport.DecodeFile(d, fixture.Sysex[..^1]));
    }));
    await Check($"{d.Model}/{d.PresetCapacity} opaque backup inspection and strict saved-read addressing", () => Sync(() =>
    {
        byte[][] frames = Fixtures.Frames(fixture.Sysex);
        byte[] header = [127, 0, 0, frames[0][9], frames[0][10]];
        frames[0] = SysexProtocol.Frame(d.ModelByte, 0x77, header);
        byte[] backup = frames.SelectMany(f => f).ToArray();
        PresetFileInspection file = PresetFile.Inspect(d, backup);
        Assert(file.HeaderPayload.SequenceEqual(header) && file.ChunkCount == d.DumpChunks, "Opaque header retained");
        Assert(file.RawImage.SequenceEqual(fixture.Image.RawImage) && file.Body.SequenceEqual(fixture.Image.Body), "Same integrity-checked payload, no assigned slot");
        Throws<InvalidDataException>(() => SnapshotExport.DecodeFile(d, backup));
        Throws<InvalidDataException>(() => new StoredPresetDecoder(d).AcceptImage(frames[0]));
        var addressed = PresetFile.Inspect(d, fixture.Sysex);
        Assert(addressed.Body.SequenceEqual(file.Body), "Backup and saved-read paths share the payload decoder");
        Throws<InvalidDataException>(() => PresetFile.Inspect(d, backup.Concat(backup).ToArray()));
        Throws<InvalidDataException>(() => PresetFile.Inspect(d, new byte[] { 0xf8 }.Concat(backup).ToArray()));
        Throws<InvalidDataException>(() => PresetFile.Inspect(d, backup[..^1]));
        byte[][] reordered = frames.ToArray(); (reordered[0], reordered[^1]) = (reordered[^1], reordered[0]);
        Throws<InvalidDataException>(() => PresetFile.Inspect(d, reordered.SelectMany(f => f).ToArray()));
        byte[] corrupt = backup.ToArray(); corrupt[100] ^= 1;
        Throws<InvalidDataException>(() => PresetFile.Inspect(d, corrupt));
    }));
    await Check($"{d.Model}/{d.PresetCapacity} complete saved Scene MIDI slots and raw preservation", () => Sync(() =>
    {
        var midi = Fixtures.Preset(d, 0, midi: true);
        var p = PresetBody.Decode(d, SnapshotExport.DecodeFile(d, midi.Sysex).Image);
        Assert(p.SceneMidiPresent && p.TopLeftBlock == "Scene MIDI" && p.SceneMidiDecodeStatus == "decoded", "Saved table decoded");
        Assert(p.Scenes.All(s => s.Messages.Length == 8 && s.Messages[0].Type == "PC" && s.Messages[0].Channel == 1 && s.Messages[0].Value == 1), "Eight scenes and slots");
        Assert(p.Scenes[0].Messages[1] == new MidiMessage(2, "Off", null, null, Raw: new(0, 3000, 12345, 54321)), "Inactive fields preserved");
        Assert(p.BlockRecords.Single(r => r.Columns == 257).ParameterWords[251] == 201, "Opaque suffix preserved");
    }));
    await Check($"{d.Model}/{d.PresetCapacity} mixed PC/CC edges, unsupported and ambiguous saved tables", () => Sync(() =>
    {
        int[] fields = Fixtures.DefaultMidiFields();
        fields[0] = 1; fields[64] = 15; fields[128] = 127; fields[192] = 0;
        fields[63] = 2; fields[127] = 0; fields[191] = 65535; fields[255] = 127;
        var image = Fixtures.Preset(d, 0, midi: true, midiFields: fields).Image;
        var p = PresetBody.Decode(d, image);
        Assert(p.Scenes[0].Messages[0] == new MidiMessage(1, "CC", 16, 0, 127, new(1, 15, 127, 0)), "CC controller/channel bounds");
        Assert(p.Scenes[7].Messages[7] == new MidiMessage(8, "PC", 1, 127, Raw: new(2, 0, 65535, 127)), "PC ignores inactive controller");
        var record = p.BlockRecords.Single(r => r.Columns == 257);
        foreach ((int index, int value) in new[] { (0, 3), (64, 16), (128, 128), (192, 128) })
        {
            int[] invalid = fields.ToArray(); invalid[index] = value;
            var bad = PresetBody.Decode(d, Fixtures.Preset(d, 0, midi: true, midiFields: invalid).Image);
            Assert(bad.SceneMidiDecodeStatus == "unsupported-saved-table-layout" && bad.Scenes.All(s => s.Messages.Length == 0) && bad.Limitations.Length == 1, "Unknown fields cannot look empty");
        }
        byte[] changed = image.Body.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(record.Offset + 30), 258);
        Assert(PresetBody.Decode(d, image with { Body = changed }).SceneMidiDecodeStatus == "unsupported-saved-table-layout", "Changed record shape");
        changed = image.Body.ToArray(); changed.AsSpan(record.Offset, 560).CopyTo(changed.AsSpan(record.Offset + 560));
        Assert(PresetBody.Decode(d, image with { Body = changed }).SceneMidiDecodeStatus == "unsupported-saved-table-layout", "Ambiguous record cannot be guessed");
    }));
    await Check($"{d.Model}/{d.PresetCapacity} live table correlation, sections and failure bounds", async () =>
    {
        int[] fields = Fixtures.DefaultMidiFields(); var frames = Fixtures.LiveTable(d, fields);
        var decoder = new SceneMidiTableDecoder(d);
        Assert(decoder.Accept(SysexProtocol.Frame(d.ModelByte, 0x74, 58, 0, 1, 2)) == null, "Wrong effect ignored before header");
        SceneMidiCapture? table = null; foreach (var frame in frames) table = decoder.Accept(frame) ?? table;
        Assert(table != null && table.Fields.SequenceEqual(fields) && table.ExtraWord == 123 && table.SectionMetadata.SequenceEqual(new[] { 2, 0 }), "Full fields and opaque metadata");
        Throws<InvalidDataException>(() => decoder.Accept(frames[^1]));
        foreach (var sequence in new[] { new[] { frames[0], frames[^1] }, new[] { frames[0], frames[2] },
            new[] { frames[0], frames[1], frames[1] }, new[] { frames[0], frames[0] },
            new[] { frames[0], SysexProtocol.Frame(d.ModelByte, 0x74, 58, 0, 1, 2) } })
        {
            var bad = new SceneMidiTableDecoder(d);
            Throws<InvalidDataException>(() => { foreach (var f in sequence) bad.Accept(f); });
        }
        byte[] corrupt = frames[1].ToArray(); corrupt[8] ^= 1;
        var broken = new SceneMidiTableDecoder(d); broken.Accept(frames[0]); Throws<InvalidDataException>(() => broken.Accept(corrupt));
        corrupt = frames[1].ToArray(); corrupt[10] = 4;
        corrupt[^2] = SysexProtocol.ComputeChecksum(corrupt.AsSpan(0, corrupt.Length - 2));
        broken = new SceneMidiTableDecoder(d); broken.Accept(frames[0]); Throws<InvalidDataException>(() => broken.Accept(corrupt));
        var fake = new FakeMidi { Reply = (_, m) => { m.Emit(SysexProtocol.Frame(d.ModelByte == 0x12 ? (byte)0x11 : (byte)0x12, 0x76)); foreach (var f in frames) m.Fragment(f); } };
        await using var session = new ReadSession(fake);
        var actual = await session.SceneMidiAsync(new(d, "99.07", "synthetic"), timeout, default);
        Assert(actual.Fields.SequenceEqual(fields) && fake.Sent.Count == 1 && fake.Sent.All(SysexProtocol.IsReadRequest), "Fragmented read-only session");
    });
    await Check($"{d.Model}/{d.PresetCapacity} fragmented full session with unrelated/wrong model replies", async () =>
    {
        var fake = new FakeMidi();
        fake.Reply = (q, midi) =>
        {
            midi.Emit(SysexProtocol.Frame(d.ModelByte == 0x11 ? (byte)0x12 : (byte)0x11, q[5], 1, 2));
            switch (q[5])
            {
                case 0: midi.Fragment(SysexProtocol.Frame(d.ModelByte, 0x64, 0, 0)); break;
                case 8: midi.Fragment(SysexProtocol.Frame(d.ModelByte, 8, 99, 7)); break;
                case 0x0d:
                    byte[] p = new byte[34]; p[0] = 5; Encoding.ASCII.GetBytes("Synthetic current").CopyTo(p, 2);
                    midi.Fragment(SysexProtocol.Frame(d.ModelByte, 0x0d, p)); break;
                case 0x0c: midi.Fragment(SysexProtocol.Frame(d.ModelByte, 0x0c, 3)); break;
                case 0x0e:
                    byte[] name = new byte[33]; name[0] = q[6]; Encoding.ASCII.GetBytes("Live scene").CopyTo(name, 1);
                    midi.Fragment(SysexProtocol.Frame(d.ModelByte, 0x0e, name)); break;
                case 3:
                    var wrong = Fixtures.Preset(d, 1); midi.Fragment(Fixtures.Frames(wrong.Sysex)[0]);
                    midi.Fragment(fixture.Sysex); break;
            }
        };
        await using var session = new ReadSession(fake);
        var context = await session.IdentifyAsync(d.Model, d.PresetCapacity, timeout, default);
        Assert(context.Firmware == "99.07", "Unknown firmware is queried, not gated");
        Assert(await session.SceneNameAsync(context, 8, timeout, default) == "Live scene", "Scene name");
        ReadResult result = await session.SnapshotAsync(context, [d.PresetCapacity - 1], timeout, default);
        Assert(result.StatePreserved && result.Before.Scene == 4 && result.Before.Slot == 5, "Observed current state");
        Assert(result.Captures[0].Sysex.SequenceEqual(fixture.Sysex), "Capture retains exact frames");
        Assert(fake.Sent.All(SysexProtocol.IsReadRequest), "Only allowlisted reads sent");
    });
}
await Check("Scene MIDI partial transfer timeout poisons session without replay", async () =>
{
    var d = definitions[0]; var context = new DeviceContext(d, "12.00", "synthetic");
    var frames = Fixtures.LiveTable(d, Fixtures.DefaultMidiFields());
    var fake = new FakeMidi { Reply = (_, m) => { m.Emit(frames[0]); m.Emit(frames[1]); } };
    await using var s = new ReadSession(fake);
    await ThrowsAsync<TimeoutException>(() => s.SceneMidiAsync(context, TimeSpan.FromMilliseconds(30), default));
    fake.Emit(frames[2]); fake.Emit(frames[3]);
    await ThrowsAsync<IOException>(() => s.SceneMidiAsync(context, timeout, default));
    Assert(fake.Sent.Count == 1, "Partial reply cannot trigger replay or complete a new request");
} );
await Check("timeout poisons session; no blind retries/late reply reuse", async () =>
{
    var fake = new FakeMidi(); await using var s = new ReadSession(fake);
    await ThrowsAsync<TimeoutException>(() => s.IdentifyAsync(null, null, TimeSpan.FromMilliseconds(30), default));
    fake.Emit(SysexProtocol.Frame(0x12, 0x64, 0, 0));
    await ThrowsAsync<IOException>(() => s.IdentifyAsync(null, null, timeout, default));
    Assert(fake.Sent.Count == 1, "No replay after timeout");
});
await Check("qualified FM9 terminal tail is preserved, not parsed as an effect", () => Sync(() =>
{
    var d = definitions[0]; var capture = Fixtures.Preset(d, 0, midi: true, withSystemTail: true);
    var decoded = SnapshotExport.DecodeFile(d, capture.Sysex);
    var p = PresetBody.Decode(d, decoded.Image);
    Assert(p.OpaqueTailOffset.HasValue && p.OpaqueTailHex?.Length == 736 * 2, "Complete tail preserved");
    Assert(!p.BlockRecords.Any(r => r.Columns == 350), "Tail not misreported as effect parameters");
    Assert(p.Amps.Length == 1 && p.SceneMidiPresent, "Effect records still decoded");
    var malformed = decoded.Image.Body.ToArray(); Array.Resize(ref malformed, malformed.Length - 2);
    Throws<InvalidDataException>(() => PresetBody.Decode(d, decoded.Image with { Body = malformed }));
}));
await Check("FM9 terminal layout is not silently borrowed for FM3", () => Sync(() =>
{
    var d = definitions[1];
    var syntheticDefinition = d with { OpaqueTailColumns = 350, OpaqueTailBytes = 736 };
    var capture = Fixtures.Preset(syntheticDefinition, 0, withSystemTail: true);
    var image = SnapshotExport.DecodeFile(d, capture.Sysex).Image;
    Throws<InvalidDataException>(() => PresetBody.Decode(d, image));
}));
await Check("cancellation and transport failure poison session", async () =>
{
    var fake = new FakeMidi(); await using var s = new ReadSession(fake);
    using var cancel = new CancellationTokenSource(30);
    await ThrowsAsync<OperationCanceledException>(() => s.IdentifyAsync(null, null, timeout, cancel.Token));
    await ThrowsAsync<IOException>(() => s.IdentifyAsync(null, null, timeout, default));
    var other = new FakeMidi(); await using var session = new ReadSession(other);
    other.Error(new IOException("Synthetic disconnect"));
    await ThrowsAsync<IOException>(() => session.IdentifyAsync(null, null, timeout, default));
    Assert(other.Sent.Count == 0, "No send on disconnected session");
});
await Check("serialized callers and receive-before-send subscription", async () =>
{
    var fake = new FakeMidi(); await using var s = new ReadSession(fake);
    bool first = true;
    fake.Reply = (q, midi) =>
    {
        if (q[5] == 0 && first) { first = false; return; }
        if (q[5] == 0) midi.Emit(SysexProtocol.Frame(0x12, 0x64, 0, 0));
        if (q[5] == 8) midi.Emit(SysexProtocol.Frame(0x12, 8, 12, 0));
    };
    Task<DeviceContext> a = s.IdentifyAsync("FM9", null, timeout, default);
    Task<DeviceContext> b = s.IdentifyAsync("FM9", null, timeout, default);
    Assert(fake.Sent.Count == 1 && !a.IsCompleted && !b.IsCompleted, "Second caller cannot send while first waits");
    fake.Emit(SysexProtocol.Frame(0x12, 0x64, 0, 0));
    await Task.WhenAll(a, b);
    Assert(fake.Sent.Count == 4, "Each request gets its synchronous response");
});
await Check("unknown capacity, model mismatch and bounded input", async () =>
{
    Throws<ArgumentException>(() => DeviceDefinition.Create("Axe-Fx III"));
    Throws<ArgumentException>(() => DeviceDefinition.Create("FM3", 1024));
    var fake = new FakeMidi { Reply = (_, m) => m.Emit(SysexProtocol.Frame(0x11, 0x64, 0, 0)) };
    await using var s = new ReadSession(fake);
    await ThrowsAsync<InvalidDataException>(() => s.IdentifyAsync("FM9", null, timeout, default));
    Assert(fake.Sent.Count == 1, "Wrong target never queried further");
    Throws<InvalidDataException>(() => new SysexAssembler().Feed(new byte[] { 0xf0 }.Concat(new byte[4096]).ToArray()).ToArray());
});
await Check("overflow rejects even a queued matching reply and prevents replay", async () =>
{
    var fake = new FakeMidi { Reply = (_, midi) =>
    {
        midi.Emit(SysexProtocol.Frame(0x12, 0x64, 0, 0));
        for (int i = 0; i < 100; i++) midi.Emit(SysexProtocol.Frame(0x12, 8, 12, 0));
    }};
    await using var s = new ReadSession(fake);
    await ThrowsAsync<IOException>(() => s.IdentifyAsync("FM9", null, timeout, default));
    Assert(fake.Sent.Count == 1, "Overflow cannot report successful identity");
});
await Check("named preset reply correlation and changed state detection", async () =>
{
    var d = definitions[0]; var context = new DeviceContext(d, "12.00", "synthetic");
    var fake = new FakeMidi(); int stateQueries = 0;
    fake.Reply = (q, midi) =>
    {
        if (q[5] == 0x0d)
        {
            byte[] payload = new byte[34]; payload[0] = 1; midi.Emit(SysexProtocol.Frame(d.ModelByte, 0x0d, payload));
            payload[0] = q[6] == 127 ? (byte)5 : q[6]; payload[1] = q[7] == 127 ? (byte)0 : q[7];
            Encoding.ASCII.GetBytes("Correlated name").CopyTo(payload, 2); midi.Emit(SysexProtocol.Frame(d.ModelByte, 0x0d, payload));
        }
        if (q[5] == 0x0c) midi.Emit(SysexProtocol.Frame(d.ModelByte, 0x0c, (byte)(stateQueries++ == 0 ? 2 : 4)));
        if (q[5] == 3) midi.Fragment(Fixtures.Preset(d, q[6] << 7 | q[7]).Sysex);
    };
    await using var s = new ReadSession(fake);
    Assert(await s.PresetNameAsync(context, 152, timeout, default) == "Correlated name", "Wrong address ignored");
    var result = await s.SnapshotAsync(context, [0], timeout, default);
    Assert(!result.StatePreserved && result.Before.Scene == 3 && result.After.Scene == 5, "State drift retained");
});
await Check("snapshot export hashes, no overwrite and complete records", () => Sync(() =>
{
    string parent = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "fractal-toolkit-checks", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(parent);
    foreach (var d in definitions)
    {
        string folder = Path.Combine(parent, $"{d.Model.Replace(' ', '-')}-{d.PresetCapacity}");
        SavedCapture capture = Fixtures.Preset(d, d.PresetCapacity - 1);
        int[] ccFields = Fixtures.DefaultMidiFields(); ccFields[0] = 1; ccFields[128] = 34; ccFields[192] = 127;
        int[] unsupportedFields = Fixtures.DefaultMidiFields(); unsupportedFields[0] = 3;
        SnapshotExport.Write(folder, new(d, "99.07", "synthetic"), [capture, Fixtures.Preset(d, 0, midi: true),
            Fixtures.Preset(d, 1, midi: true, midiFields: ccFields), Fixtures.Preset(d, 2, midi: true, midiFields: unsupportedFields)]);
        byte[] snapshot = File.ReadAllBytes(Path.Combine(folder, "snapshot.json"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
        Assert(manifest.RootElement.GetProperty("snapshot_sha256").GetString() == PresetBody.Sha256(snapshot), "Snapshot manifest hash");
        Assert(File.ReadAllBytes(Path.Combine(folder, $"preset-{d.PresetCapacity - 1:D4}.syx")).SequenceEqual(capture.Sysex), "Exact capture file");
        Throws<IOException>(() => SnapshotExport.Write(folder, new(d, "99.07", "synthetic"), [capture]));
        var editFixture=new SceneMidiEditFixture(d);
        SnapshotExport.Write(Path.Combine(parent,$"SceneMidi-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}"),editFixture.Context,[editFixture.Base]);
        using var recoveryFixture=SceneMidiRecoveryStore.Create(Path.Combine(parent,$"Recovery-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}"),[editFixture.Plan],SceneMidiRecoveryChecks.RequestHash);
    }
    Console.WriteLine($"Synthetic artifacts: {parent}");
    var unknown = Fixtures.Preset(definitions[0], 2, unknownAmpLayout: true);
    string unknownFolder = Path.Combine(parent, "unknown-body");
    var exported = SnapshotExport.Write(unknownFolder, new(definitions[0], "99.07", "synthetic"), [unknown]);
    Assert(exported.DecodeErrors.Single().Slot == 2 && exported.DecodedPresets == 0, "Unknown body retained as failure");
    Assert(File.ReadAllBytes(Path.Combine(unknownFolder, "preset-0002.syx")).SequenceEqual(unknown.Sysex), "Unknown body raw backup preserved");
    byte[] garbage = new byte[] { 1 }.Concat(unknown.Sysex).ToArray();
    Throws<InvalidDataException>(() => SnapshotExport.DecodeFile(definitions[0], garbage));
}));
foreach (var d in definitions)
{
    await Check($"{d.Model}/{d.PresetCapacity} editor wire vectors, scope and model grid", () => Sync(() =>
    {
        var scope = new EmptyPresetPilotScope(d, 257, 3, 10);
        var insert = EditorCommand.InsertBlock(d, 257, 3, 10, 190);
        byte[] expected = [0x32, 0, 62, 1, 0, 0, (byte)(9 * d.GridRows + 2), 0, 0, 0, 0, 0, 0, 0, 0];
        Assert(insert.Frame.SequenceEqual(SysexProtocol.Frame(d.ModelByte, 1, expected)), "Captured action envelope and row stride");
        scope.Validate(insert);
        Throws<InvalidOperationException>(() => scope.Validate(EditorCommand.SavePreset(d, 258)));
        Throws<InvalidOperationException>(() => scope.Validate(EditorCommand.InsertBlock(d, 257, 3, 10, 58)));
        Throws<InvalidOperationException>(() => scope.Validate(EditorCommand.ClearCell(d, 257, 2, 10)));
        Throws<ArgumentOutOfRangeException>(() => EditorCommand.SelectCell(d, 257, d.GridRows + 1, 1));
        Throws<ArgumentOutOfRangeException>(() => EditorCommand.SelectCell(d, 257, 1, d.GridColumns + 1));
        Throws<ArgumentOutOfRangeException>(() => EditorCommand.SetSceneMidiField(d, 257, 0, 3));
        Throws<ArgumentOutOfRangeException>(() => EditorCommand.SetSceneMidiField(d, 257, 64, 16));
        Throws<ArgumentOutOfRangeException>(() => EditorCommand.SetSceneMidiField(d, 257, 256, 0));
        var parameter = EditorCommand.SetSceneMidiField(d, 257, 192, 1).Frame;
        Assert(parameter.AsSpan(12, 5).SequenceEqual(new byte[] { 0, 0, 0, 124, 3 }), "Known IEEE float 1.0 septets");
        Assert(EditorCommand.SavePreset(d, 257).Frame.AsSpan(12, 2).SequenceEqual(new byte[] { 1, 2 }), "Save slot 257 uses little-endian septets");
        Assert(!SysexProtocol.IsReadRequest(parameter) && !SysexProtocol.IsReadRequest(insert.Frame), "Default read policy stays closed");
        byte[] copy = insert.Frame; copy[6] = 0;
        Assert(insert.Frame[6] == 0x32, "External frame mutation cannot change typed command");
        var status = BlockStatusTable.Decode(d, SysexProtocol.Frame(d.ModelByte, 0x13, 62, 1, 0x40));
        Assert(status.Single() == new BlockStatus(190, false, 1, 4), "Status vector");
        Throws<InvalidDataException>(() => BlockStatusTable.Decode(d, SysexProtocol.Frame(d.ModelByte, 0x13, 62, 1, 0, 62, 1, 0)));
    }));
}
foreach (var d in definitions)
{
    await Check($"{d.Model}/{d.PresetCapacity} save-only scope and single attempt", async () =>
    {
        var scope = new PresetSaveScope(d, 257);
        scope.Validate(EditorCommand.SavePreset(d, 257));
        Throws<InvalidOperationException>(() => scope.Validate(EditorCommand.SavePreset(d, 258)));
        Throws<InvalidOperationException>(() => scope.Validate(EditorCommand.SavePreset(definitions.First(x => x.Model != d.Model), 257)));
        Throws<InvalidOperationException>(() => scope.Validate(EditorCommand.ReloadPreset(d, 257)));
        Throws<InvalidOperationException>(() => scope.Validate(EditorCommand.SelectScene(d, 257, 1)));
        Throws<InvalidOperationException>(() => scope.Validate(EditorCommand.InsertBlock(d, 257, 1, 1, 190)));
        var fake = new FakeEditorMidi(); await using var s = new ReadSession(fake);
        s.AuthorizePresetSave(scope);
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d, 258), timeout, default));
        Assert(fake.Sent.Count == 0, "Wrong target sent nothing and did not consume authorized attempt");
        await s.SendEditorAsync(EditorCommand.SavePreset(d, 257), timeout, default);
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d, 257), timeout, default));
        Assert(fake.Sent.Count == 1, "Only one save may be sent, even when no rejection was received");
    });
}
await Check("save-only rejection poisons session and prevents replay", async () =>
{
    var d = definitions[0]; var fake = new FakeEditorMidi { Reply = (_, f) => f.Emit(SysexProtocol.Frame(d.ModelByte, 0x64, 1, 5)) };
    await using var s = new ReadSession(fake); s.AuthorizePresetSave(new(d, 257));
    await ThrowsAsync<IOException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d, 257), timeout, default));
    await ThrowsAsync<IOException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d, 257), timeout, default));
    Assert(fake.Sent.Count == 1, "Rejected save cannot be retried");
});
await Check("save-only cancellation after transmission poisons session", async () =>
{
    var d = definitions[0]; using var cancel = new CancellationTokenSource();
    var fake = new FakeEditorMidi { Reply = (_, _) => cancel.Cancel() };
    await using var s = new ReadSession(fake); s.AuthorizePresetSave(new(d, 257));
    await ThrowsAsync<OperationCanceledException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d, 257), timeout, cancel.Token));
    await ThrowsAsync<IOException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d, 257), timeout, default));
    Assert(fake.Sent.Count == 1, "Cancellation cannot release another save attempt");
});
await Check("editor needs session authorization and never treats silence as readback", async () =>
{
    var d = definitions[0]; var fake = new FakeEditorMidi(); await using var s = new ReadSession(fake);
    var cmd = EditorCommand.SavePreset(d, 257);
    await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(cmd, timeout, default));
    Assert(fake.Sent.Count == 0, "Read-only session sent nothing");
    s.AuthorizeEmptyPresetPilot(new(d, 257, 3, 10));
    await s.SendEditorAsync(cmd, timeout, default);
    Assert(fake.Sent.Count == 1, "Silence returns sent-unverified only, no retry");
    fake.Emit(SysexProtocol.Frame(d.ModelByte, 0x64, 1, 5)); // delayed NACK outside active exchange
    await ThrowsAsync<IOException>(() => s.SendEditorAsync(cmd, timeout, default));
    Assert(fake.Sent.Count == 1, "Late rejection poisons session before another send");
});
await Check("editor rejection during send poisons read and write without replay", async () =>
{
    var d = definitions[0]; var fake = new FakeEditorMidi { Reply = (q, f) => f.Emit(SysexProtocol.Frame(d.ModelByte, 0x64, q[5], 5)) };
    await using var s = new ReadSession(fake); s.AuthorizeEmptyPresetPilot(new(d, 257, 3, 10));
    await ThrowsAsync<IOException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d, 257), timeout, default));
    await ThrowsAsync<IOException>(() => s.CurrentAsync(new(d, "synthetic", "synthetic"), timeout, default));
    Assert(fake.Sent.Count == 1, "No read/write on ambiguous session");
});
await Check("editor cancellation and timeout cannot silently replay", async () =>
{
    var d = definitions[0]; var fake = new FakeEditorMidi(); await using var s = new ReadSession(fake);
    s.AuthorizeEmptyPresetPilot(new(d, 257, 3, 10));
    await ThrowsAsync<TimeoutException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d, 257), TimeSpan.FromMilliseconds(10), default));
    await ThrowsAsync<IOException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d, 257), timeout, default));
    Assert(fake.Sent.Count == 1, "Timed-out send not replayed");
});
foreach (var d in definitions)
{
    await Check($"{d.Model}/{d.PresetCapacity} independent inserted-interval preservation", () => Sync(() =>
    {
        var original = Fixtures.Preset(d, 257, amp: false).Image;
        byte[] initial = original.Body.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(initial, 0);
        original = original with { Body = initial };
        // Build an actual identity prefix + record interval independently of the write builders.
        int start = 692; byte[] body = new byte[initial.Length + 560];
        initial.AsSpan(0, start).CopyTo(body); initial.AsSpan(start).CopyTo(body.AsSpan(start + 560));
        void Word(int o, int v) => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(o, 2), (ushort)v);
        Word(start, 190); Word(start + 42, 257); Word(start + 44, 1);
        int offset = 0x104 + (9 * d.GridRows + 2) * 4; Word(offset, 190); Word(0, 6);
        var changed = original with { Body = body };
        EditorPreservation.VerifySceneMidiInsertion(d, original, changed, 3, 10, 7);
        byte[] unrelated = body.ToArray(); unrelated[3000] = 1;
        Throws<InvalidDataException>(() => EditorPreservation.VerifySceneMidiInsertion(d, original, changed with { Body = unrelated }, 3, 10, 7));
        byte[] routing = body.ToArray(); routing[offset + 2] = 1;
        Throws<InvalidDataException>(() => EditorPreservation.VerifySceneMidiInsertion(d, original, changed with { Body = routing }, 3, 10, 7));
        Throws<InvalidDataException>(() => EditorPreservation.VerifySceneMidiInsertion(d, original, changed, 3, 10, 1));
        Throws<InvalidDataException>(() => EditorPreservation.VerifySceneMidiInsertion(d, original, changed with { Name = "Other" }, 3, 10, 7));
    }));
}
foreach (var d in definitions)
{
    await Check($"{d.Model}/{d.PresetCapacity} immutable upload, integrity and closed temporary destination", () => Sync(() =>
    {
        var fixture = new RestoreFixture(d); var plan = fixture.Plan;
        Assert(plan.FrameCount == d.DumpChunks + 2 && plan.Frame(0).AsSpan(6, 2).SequenceEqual(new byte[] { 127, 127 }), "Temporary-buffer sentinel only");
        Assert(plan.Frame(0).AsSpan(8, 3).SequenceEqual(fixture.Expected.Sysex.AsSpan(8, 3)), "Other header fields preserved");
        byte[] uploaded = Enumerable.Range(0, plan.FrameCount).SelectMany(i => plan.Frame(i)).ToArray();
        Assert(uploaded.AsSpan(13).SequenceEqual(fixture.Expected.Sysex.AsSpan(13)), "All chunks and footer preserved byte-exact");
        Assert(PresetFile.Inspect(d, uploaded).Body.SequenceEqual(fixture.Expected.Image.Body), "Integrity still valid after addressing change");
        byte[] copy = plan.Frame(0); copy[6] = 0;
        Assert(plan.Frame(0)[6] == 127, "Public frame copies cannot mutate authorized upload");
        Throws<InvalidDataException>(() => PresetUploadPlan.Create(d, fixture.Slot, 3, uploaded, new string('0', 64)));
        byte[] corrupt = uploaded.ToArray(); corrupt[100] ^= 1;
        Throws<InvalidDataException>(() => PresetUploadPlan.Create(d, fixture.Slot, 3, corrupt, PresetBody.Sha256(corrupt)));
        Throws<ArgumentOutOfRangeException>(() => PresetUploadPlan.Create(d, d.PresetCapacity, 3, uploaded, PresetBody.Sha256(uploaded)));
        Throws<ArgumentOutOfRangeException>(() => PresetUploadPlan.Create(d, 0, 9, uploaded, PresetBody.Sha256(uploaded)));
        byte[] unknown = uploaded.ToArray(); unknown[8] = 1; unknown[11] = SysexProtocol.ComputeChecksum(unknown.AsSpan(0, 11));
        Throws<InvalidDataException>(() => PresetUploadPlan.Create(d, 0, 3, unknown, PresetBody.Sha256(unknown)));
        byte[] bank = uploaded.Concat(uploaded).ToArray();
        Throws<InvalidDataException>(() => PresetUploadPlan.Create(d, 0, 3, bank, PresetBody.Sha256(bank)));
        var other = definitions.First(x => x.Model != d.Model);
        Throws<InvalidDataException>(() => PresetUploadPlan.Create(other, 0, 3, uploaded, PresetBody.Sha256(uploaded)));
        var scope = new PresetRestoreScope(plan);
        Throws<InvalidOperationException>(() => scope.Validate(EditorCommand.SavePreset(d, fixture.Slot)));
        Throws<InvalidOperationException>(() => scope.Validate(EditorCommand.ReloadPreset(d, fixture.Slot)));
        Throws<InvalidOperationException>(() => scope.Validate(EditorCommand.SelectScene(d, fixture.Slot, 3)));
        Throws<InvalidOperationException>(() => scope.ValidateUpload(new RestoreFixture(d).Plan, 0));
    }));
    await Check($"{d.Model}/{d.PresetCapacity} full restore preflight, upload, verification, save and reload", async () =>
    {
        var f = new RestoreFixture(d); await using var s = new ReadSession(f.Midi);
        s.AuthorizePresetRestore(new(f.Plan));
        await ThrowsAsync<InvalidOperationException>(() => s.UploadPresetAsync(timeout, TimeSpan.Zero, default));
        await s.PreparePresetRestoreAsync(f.Context, PresetBody.Sha256(f.Base.Sysex), timeout, default);
        await s.UploadPresetAsync(timeout, TimeSpan.Zero, default);
        Assert(f.Saved == f.Base, "Upload left flash unchanged");
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d, f.Slot), timeout, default));
        await s.VerifyUploadedPresetAsync(f.Context, timeout, default);
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d, f.Slot - 1), timeout, default));
        await s.SendEditorAsync(EditorCommand.SavePreset(d, f.Slot), timeout, default);
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.ReloadPreset(d, f.Slot), timeout, default));
        await s.VerifyRestoredSavedAsync(f.Context, timeout, default);
        await s.SendEditorAsync(EditorCommand.ReloadPreset(d, f.Slot), timeout, default);
        await s.VerifyUploadedPresetAsync(f.Context, timeout, default);
        await s.VerifyRestoredSavedAsync(f.Context, timeout, default);
        await ThrowsAsync<InvalidOperationException>(() => s.UploadPresetAsync(timeout, TimeSpan.Zero, default));
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d, f.Slot), timeout, default));
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.ReloadPreset(d, f.Slot), timeout, default));
        Assert(f.Midi.Sent.Count(q => q[5] is 0x77 or 0x78 or 0x79) == f.Plan.FrameCount, "One ordered complete upload only");
        Assert(f.Saved.Image.RawImage.SequenceEqual(f.Expected.Image.RawImage), "Complete final expected saved image");
    });
    await Check($"{d.Model}/{d.PresetCapacity} changed base and unsaved work reject before upload", async () =>
    {
        var f = new RestoreFixture(d); await using var s = new ReadSession(f.Midi); s.AuthorizePresetRestore(new(f.Plan));
        await ThrowsAsync<InvalidDataException>(() => s.PreparePresetRestoreAsync(f.Context, new string('0', 64), timeout, default));
        f.Live = f.Expected;
        await ThrowsAsync<InvalidDataException>(() => s.PreparePresetRestoreAsync(f.Context, PresetBody.Sha256(f.Base.Sysex), timeout, default));
        await ThrowsAsync<InvalidOperationException>(() => s.UploadPresetAsync(timeout, TimeSpan.Zero, default));
        Assert(f.Midi.Sent.All(SysexProtocol.IsReadRequest), "Failed base/live preflight never wrote");
    });
}
await Check("unmatched uploaded contents never enable saving", async () =>
{
    var f = new RestoreFixture(definitions[0]) { ApplyUpload = false };
    await using var s = new ReadSession(f.Midi); s.AuthorizePresetRestore(new(f.Plan));
    await s.PreparePresetRestoreAsync(f.Context, PresetBody.Sha256(f.Base.Sysex), timeout, default);
    await s.UploadPresetAsync(timeout, TimeSpan.Zero, default);
    await ThrowsAsync<InvalidDataException>(() => s.VerifyUploadedPresetAsync(f.Context, timeout, default));
    await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.SavePreset(f.Device, f.Slot), timeout, default));
    Assert(f.Saved == f.Base, "No persistence after unmatched upload");
});
await Check("selection drift rejects restore before upload", async () =>
{
    var f = new RestoreFixture(definitions[0]); await using var s = new ReadSession(f.Midi); s.AuthorizePresetRestore(new(f.Plan));
    await s.PreparePresetRestoreAsync(f.Context, PresetBody.Sha256(f.Base.Sysex), timeout, default);
    f.ReportedSlot--;
    await ThrowsAsync<InvalidDataException>(() => s.UploadPresetAsync(timeout, TimeSpan.Zero, default));
    Assert(f.Midi.Sent.All(SysexProtocol.IsReadRequest), "Drift caused no writes");
});
foreach (bool reject in new[] { true, false })
    await Check(reject ? "upload NACK stops partial transfer and poisons session" : "upload cancellation stops partial transfer without replay", async () =>
    {
        var f = new RestoreFixture(definitions[0]); using var cancel = new CancellationTokenSource();
        await using var s = new ReadSession(f.Midi); s.AuthorizePresetRestore(new(f.Plan));
        await s.PreparePresetRestoreAsync(f.Context, PresetBody.Sha256(f.Base.Sysex), timeout, default);
        var originalReply = f.Midi.Reply;
        f.Midi.Reply = (q, midi) =>
        {
            originalReply!(q, midi);
            if (q[5] == 0x78)
            {
                if (reject) midi.Emit(SysexProtocol.Frame(f.Device.ModelByte, 0x64, 0x78, 5));
                else cancel.Cancel();
            }
        };
        if (reject) await ThrowsAsync<IOException>(() => s.UploadPresetAsync(timeout, TimeSpan.Zero, default));
        else await ThrowsAsync<OperationCanceledException>(() => s.UploadPresetAsync(timeout, TimeSpan.Zero, cancel.Token));
        int writes = f.Midi.Sent.Count(q => q[5] is 0x77 or 0x78 or 0x79);
        await ThrowsAsync<IOException>(() => s.CurrentAsync(f.Context, timeout, default));
        Assert(writes == 2 && f.Saved == f.Base, "Stopped after header/first chunk with no save or retry");
    });
await Check("complete upload holds the request gate against concurrent reads", async () =>
{
    var f = new RestoreFixture(definitions[0]); await using var s = new ReadSession(f.Midi); s.AuthorizePresetRestore(new(f.Plan));
    await s.PreparePresetRestoreAsync(f.Context, PresetBody.Sha256(f.Base.Sysex), timeout, default);
    Task upload = s.UploadPresetAsync(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(10), default);
    Task<CurrentState> queued = s.CurrentAsync(f.Context, timeout, default);
    Assert(f.Midi.Sent[^1][5] == 0x77 && !queued.IsCompleted, "Concurrent read cannot inject after upload header");
    await Task.WhenAll(upload, queued);
    int start = f.Midi.Sent.FindIndex(q => q[5] == 0x77);
    Assert(f.Midi.Sent.Skip(start).Take(f.Plan.FrameCount).All(q => q[5] is 0x77 or 0x78 or 0x79), "Whole transfer stays contiguous");
});
foreach (var (name, test) in RestoreDeadlineChecks.Cases()) await Check(name, test);
await Check("upload notification flood cannot overflow an unused reply queue", async () =>
{
    var f = new RestoreFixture(definitions[0]); await using var s = new ReadSession(f.Midi); s.AuthorizePresetRestore(new(f.Plan));
    await s.PreparePresetRestoreAsync(f.Context, PresetBody.Sha256(f.Base.Sysex), timeout, default);
    var reply = f.Midi.Reply;
    f.Midi.Reply = (q, midi) => {
        reply!(q, midi);
        if (q[5] is 0x77 or 0x78 or 0x79)
        {
            midi.Emit(SysexProtocol.Frame(f.Device.ModelByte, 0x64, q[5], 0));
            for (int i = 0; i < 256; i++) midi.Emit(SysexProtocol.Frame(f.Device.ModelByte, 1, 0));
        }
    };
    await s.UploadPresetAsync(timeout, TimeSpan.Zero, default);
    await s.VerifyUploadedPresetAsync(f.Context, timeout, default);
    Assert(f.Saved == f.Base, "Notifications do not authorize persistence");
});
await Check("restore scope cannot transfer prior verification to another session", async () =>
{
    var f = new RestoreFixture(definitions[0]); var scope = new PresetRestoreScope(f.Plan);
    await using var first = new ReadSession(f.Midi); first.AuthorizePresetRestore(scope);
    await using var other = new ReadSession(new FakeEditorMidi());
    Throws<InvalidOperationException>(() => other.AuthorizePresetRestore(scope));
});
await Check("selection drift after buffer verification prevents save", async () =>
{
    var f = new RestoreFixture(definitions[0]); await using var s = new ReadSession(f.Midi); s.AuthorizePresetRestore(new(f.Plan));
    await s.PreparePresetRestoreAsync(f.Context, PresetBody.Sha256(f.Base.Sysex), timeout, default);
    await s.UploadPresetAsync(timeout, TimeSpan.Zero, default);
    await s.VerifyUploadedPresetAsync(f.Context, timeout, default); f.ReportedSlot--;
    await ThrowsAsync<InvalidDataException>(() => s.SendEditorAsync(EditorCommand.SavePreset(f.Device, f.Slot), timeout, default));
    Assert(f.Saved == f.Base && f.Midi.Sent.All(q => q[5] != 1), "No save after selection drift");
});
foreach (var d in definitions)
{
    await Check($"{d.Model}/{d.PresetCapacity} populated generic insertion preserves MIDI, routing, default and other blocks", async () =>
    {
        var f = new InsertionFixture(d); var p = f.Plan;
        EditorPreservation.VerifyBlockInsertion(d, f.Base.Image, f.Changed.Image, p.Row, p.Column, p.EffectId);
        var bad = f.Changed.Image.Body.ToArray(); bad[0] ^= 1;
        Throws<InvalidDataException>(() => EditorPreservation.VerifyBlockInsertion(d, f.Base.Image, f.Changed.Image with { Body = bad }, p.Row, p.Column, p.EffectId));
        bad = f.Changed.Image.Body.ToArray(); bad[3000] ^= 1;
        Throws<InvalidDataException>(() => EditorPreservation.VerifyBlockInsertion(d, f.Base.Image, f.Changed.Image with { Body = bad }, p.Row, p.Column, p.EffectId));
        await using var s = new ReadSession(f.Midi); var scope = new BlockInsertionScope(p); s.AuthorizeBlockInsertion(scope);
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d,p.Slot),timeout,default));
        await s.PrepareBlockInsertionAsync(f.Context,timeout,default);
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.InsertBlock(d,p.Slot,p.Row,p.Column,p.EffectId),timeout,default));
        await s.SendEditorAsync(EditorCommand.SelectCell(d,p.Slot,p.Row,p.Column),timeout,default);
        await s.SendEditorAsync(EditorCommand.InsertBlock(d,p.Slot,p.Row,p.Column,p.EffectId),timeout,default);
        await s.VerifyInsertedBufferAsync(f.Context,timeout,default);
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.ClearCell(d,p.Slot,p.Row,p.Column),timeout,default));
        await s.SendEditorAsync(EditorCommand.SavePreset(d,p.Slot),timeout,default);
        var first = await s.VerifyInsertedSavedAsync(f.Context,timeout,default);
        await s.SendEditorAsync(EditorCommand.ReloadPreset(d,p.Slot),timeout,default);
        var repeat = await s.VerifyInsertedSavedAsync(f.Context,timeout,default);
        Assert(first.Sysex.SequenceEqual(repeat.Sysex),"Full saved result persists after reload");
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d,p.Slot),timeout,default));
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.InsertBlock(d,p.Slot,p.Row,p.Column,p.EffectId),timeout,default));
        Throws<InvalidDataException>(() => BlockInsertionPlan.Create(d,f.Base.Sysex,new string('0',64),p.Row,p.Column,p.EffectId,3));
        Throws<InvalidDataException>(() => BlockInsertionPlan.Create(d,f.Base.Sysex,p.BaseSha256,1,1,p.EffectId,3));
        Throws<InvalidDataException>(() => BlockInsertionPlan.Create(d,f.Base.Sysex,p.BaseSha256,p.Row,p.Column,58,3));
        Throws<InvalidDataException>(() => BlockInsertionPlan.Create(d,f.Base.Sysex,p.BaseSha256,p.Row,p.Column,p.EffectId,4));
    });
}
await Check("populated insertion rejects unsaved work and mismatched live result without Save", async () =>
{
    var f = new InsertionFixture(definitions[0]); var p=f.Plan; await using var s=new ReadSession(f.Midi);s.AuthorizeBlockInsertion(new(p));
    f.Live=f.Changed;
    await ThrowsAsync<InvalidDataException>(()=>s.PrepareBlockInsertionAsync(f.Context,timeout,default));
    Assert(f.Midi.Sent.All(SysexProtocol.IsReadRequest),"Unknown unsaved work was not discarded");
    f.Live=f.Base;await s.PrepareBlockInsertionAsync(f.Context,timeout,default);
    await s.SendEditorAsync(EditorCommand.SelectCell(f.Device,p.Slot,p.Row,p.Column),timeout,default);
    f.ApplyInsertion=false;await s.SendEditorAsync(EditorCommand.InsertBlock(f.Device,p.Slot,p.Row,p.Column,p.EffectId),timeout,default);
    await ThrowsAsync<InvalidDataException>(()=>s.VerifyInsertedBufferAsync(f.Context,timeout,default));
    await ThrowsAsync<InvalidOperationException>(()=>s.SendEditorAsync(EditorCommand.SavePreset(f.Device,p.Slot),timeout,default));
    Assert(f.Saved==f.Base,"Rejected verification cannot persist");
});
await Check("populated insertion rejects selection drift, raw metadata changes and reused authorization", async () =>
{
    var f=new InsertionFixture(definitions[0]);var p=f.Plan;var scope=new BlockInsertionScope(p);
    await using var s=new ReadSession(f.Midi);s.AuthorizeBlockInsertion(scope);
    await using var other=new ReadSession(new FakeEditorMidi());Throws<InvalidOperationException>(()=>other.AuthorizeBlockInsertion(scope));
    await s.PrepareBlockInsertionAsync(f.Context,timeout,default);
    await s.SendEditorAsync(EditorCommand.SelectCell(f.Device,p.Slot,p.Row,p.Column),timeout,default);
    await s.SendEditorAsync(EditorCommand.InsertBlock(f.Device,p.Slot,p.Row,p.Column,p.EffectId),timeout,default);
    await s.VerifyInsertedBufferAsync(f.Context,timeout,default);f.ReportedSlot--;
    await ThrowsAsync<InvalidDataException>(()=>s.SendEditorAsync(EditorCommand.SavePreset(f.Device,p.Slot),timeout,default));
    Assert(f.Saved==f.Base,"Drift did not save");
    var raw=f.Changed.Image.RawImage.ToArray();raw[6]^=1;
    Throws<InvalidDataException>(()=>EditorPreservation.VerifyBlockInsertion(f.Device,f.Base.Image,f.Changed.Image with {RawImage=raw},p.Row,p.Column,p.EffectId));
});
foreach(bool reject in new[]{true,false})
await Check(reject?"populated insertion rejection stops without Save or replay":"populated insertion cancellation stops without Save or replay", async () =>
{
    var f=new InsertionFixture(definitions[0]);var p=f.Plan;await using var s=new ReadSession(f.Midi);s.AuthorizeBlockInsertion(new(p));
    await s.PrepareBlockInsertionAsync(f.Context,timeout,default);
    await s.SendEditorAsync(EditorCommand.SelectCell(f.Device,p.Slot,p.Row,p.Column),timeout,default);
    using var cancel=new CancellationTokenSource();var reply=f.Midi.Reply;
    f.Midi.Reply=(q,midi)=>{reply!(q,midi);if(q[5]==1&&q[6]==0x32){if(reject)midi.Emit(SysexProtocol.Frame(f.Device.ModelByte,0x64,1,5));else cancel.Cancel();}};
    var insert=EditorCommand.InsertBlock(f.Device,p.Slot,p.Row,p.Column,p.EffectId);
    if(reject)await ThrowsAsync<IOException>(()=>s.SendEditorAsync(insert,timeout,default));
    else await ThrowsAsync<OperationCanceledException>(()=>s.SendEditorAsync(insert,timeout,cancel.Token));
    await ThrowsAsync<IOException>(()=>s.CurrentAsync(f.Context,timeout,default));
    Assert(f.Saved==f.Base&&f.Midi.Sent.Count(q=>q[5]==1&&q[6]==0x32)==1,"One insertion attempt; no save or retry");
});
await Check("persistent batch owner permits only completed scope transitions and no target reuse", async () =>
{
    var d=definitions[0];var f=new InsertionFixture(d);var next=new InsertionFixture(d,d.PresetCapacity-2);
    var firstScope=new BlockInsertionScope(f.Plan);var nextScope=new BlockInsertionScope(next.Plan);
    var batch=new BlockInsertionBatchScope(new[]{firstScope,nextScope});
    Throws<ArgumentException>(()=>new BlockInsertionBatchScope(new[]{firstScope,firstScope}));
    Throws<InvalidOperationException>(()=>batch.Validate(EditorCommand.SavePreset(d,f.Plan.Slot)));
    batch.Activate(firstScope);Throws<InvalidOperationException>(()=>batch.Activate(nextScope));
    await using var s=new ReadSession(f.Midi);s.AuthorizeBlockInsertion(firstScope);var p=f.Plan;
    await s.PrepareBlockInsertionAsync(f.Context,timeout,default);
    await s.SendEditorAsync(EditorCommand.SelectCell(d,p.Slot,p.Row,p.Column),timeout,default);
    await s.SendEditorAsync(EditorCommand.InsertBlock(d,p.Slot,p.Row,p.Column,p.EffectId),timeout,default);
    await s.VerifyInsertedBufferAsync(f.Context,timeout,default);
    batch.Validate(EditorCommand.SavePreset(d,p.Slot));
    await s.SendEditorAsync(EditorCommand.SavePreset(d,p.Slot),timeout,default);
    await s.VerifyInsertedSavedAsync(f.Context,timeout,default);
    Throws<InvalidOperationException>(()=>batch.Activate(nextScope));
    await s.SendEditorAsync(EditorCommand.ReloadPreset(d,p.Slot),timeout,default);
    await s.VerifyInsertedBufferAsync(f.Context,timeout,default);
    await s.VerifyInsertedSavedAsync(f.Context,timeout,default);
    batch.Activate(nextScope);
    Throws<InvalidOperationException>(()=>batch.Activate(firstScope));
    Throws<InvalidOperationException>(()=>batch.Validate(EditorCommand.SavePreset(d,p.Slot)));
});
foreach(var d in definitions)
await Check($"{d.Model}/{d.PresetCapacity} populated Scene MIDI PC/CC exact preservation and qualified backup undo",async()=>
{
    var f=new SceneMidiEditFixture(d);SceneMidiEditReceipt receipt;
    await using(var s=new ReadSession(f.Midi))
    {
        receipt=await SceneMidiEditChecks.Apply(f,s,timeout);
        f.Plan.VerifyProgress(f.Saved.Image,f.Plan.CommandCount);
        var table=SceneMidiTable.SavedFields(d,PresetBody.Decode(d,f.Saved.Image).BlockRecords);
        Assert(table[128+10]==12345,"Unused PC controller word preserved");
        var messages=SceneMidiTable.Decode(d,table);
        Assert(messages[1][2].Type=="PC"&&messages[1][2].Channel==9&&messages[1][2].Value==42,"Intended PC");
        Assert(messages[7][1].Type=="CC"&&messages[7][1].Controller==119&&messages[7][1].Channel==5,"Intended CC");
        var idempotent=SceneMidiEditPlan.Create(d,"99.07",f.Saved.Sysex,receipt.ResultSha256,[new(2,3,"PC",9,42),new(8,2,"CC",5,126,119)]);
        Assert(idempotent.Unchanged&&idempotent.CommandCount==0,"Repeat plan sends no fields");
        await ThrowsAsync<InvalidOperationException>(()=>s.SendEditorAsync(EditorCommand.SavePreset(d,f.Plan.Slot),timeout,default));
    }
    await using(var undo=new ReadSession(f.Midi))
    {
        undo.AuthorizePresetRestore(new(receipt.UndoUpload));
        await undo.PrepareSceneMidiUndoAsync(receipt,f.Context,timeout,default);
        await undo.UploadPresetAsync(timeout,TimeSpan.Zero,default);await undo.VerifyUploadedPresetAsync(f.Context,timeout,default);
        receipt.VerifyUndoBase(f.Saved);
        await undo.SendEditorAsync(EditorCommand.SavePreset(d,f.Plan.Slot),timeout,default);
        await undo.VerifyRestoredSavedAsync(f.Context,timeout,default);
        await undo.SendEditorAsync(EditorCommand.ReloadPreset(d,f.Plan.Slot),timeout,default);
        await undo.VerifyUploadedPresetAsync(f.Context,timeout,default);await undo.VerifyRestoredSavedAsync(f.Context,timeout,default);
        Assert(f.Saved.Sysex.SequenceEqual(f.Base.Sysex)&&f.Live.Sysex.SequenceEqual(f.Base.Sysex),"Exact original raw/body restored");
    }
});
await Check("Scene MIDI plans reject occupied rows, unknown prefixes, wrong hashes and preserve default/opaque body",()=>
{
    var f=new SceneMidiEditFixture(definitions[0]);var p=f.Plan;
    Throws<InvalidDataException>(()=>SceneMidiEditPlan.Create(f.Device,"99.07",f.Base.Sysex,p.BaseSha256,[new(1,1,"PC",2,99)]));
    Throws<InvalidDataException>(()=>SceneMidiEditPlan.Create(f.Device,"99.07",f.Base.Sysex,new string('0',64),[new(1,2,"PC",2,99)]));
    Throws<ArgumentException>(()=>SceneMidiEditPlan.Create(f.Device,"99.07",f.Base.Sysex,p.BaseSha256,[new(1,2,"CC",2,99)]));
    Throws<ArgumentException>(()=>SceneMidiEditPlan.Create(f.Device,"99.07",f.Base.Sysex,p.BaseSha256,[new(1,2,"PC",2,99),new(1,2,"PC",2,99)]));
    var body=f.Base.Image.Body.ToArray();body[0]^=1;
    Throws<InvalidDataException>(()=>p.VerifyProgress(f.Base.Image with{Body=body},0));body=f.Base.Image.Body.ToArray();body[3000]^=1;
    Throws<InvalidDataException>(()=>p.VerifyProgress(f.Base.Image with{Body=body},0));
    var raw=f.Base.Image.RawImage.ToArray();raw[6]^=1;
    Throws<InvalidDataException>(()=>p.VerifyProgress(f.Base.Image with{RawImage=raw},0));
    var unknown=Fixtures.Preset(f.Device,p.Slot,midi:true);
    Throws<InvalidDataException>(()=>SceneMidiEditPlan.Create(f.Device,"99.07",unknown.Sysex,PresetBody.Sha256(unknown.Sysex),[new(1,2,"PC",2,99)]));
    return Task.CompletedTask;
});
await Check("Scene MIDI scope enforces exact ordered fields, verified steps, no replay and no insertion/removal/scene change",async()=>
{
    var f=new SceneMidiEditFixture(definitions[0]);var scope=new SceneMidiEditScope(f.Plan);
    await using var s=new ReadSession(f.Midi);s.AuthorizeSceneMidiEdit(scope);
    await using var other=new ReadSession(new FakeEditorMidi());Throws<InvalidOperationException>(()=>other.AuthorizeSceneMidiEdit(scope));
    await s.PrepareSceneMidiEditAsync(f.Context,timeout,default);
    foreach(var c in new[]{EditorCommand.SelectScene(f.Device,f.Plan.Slot,1),EditorCommand.ClearCell(f.Device,f.Plan.Slot,1,1),
        EditorCommand.InsertBlock(f.Device,f.Plan.Slot,1,1,190),EditorCommand.SetSceneMidiField(f.Device,f.Plan.Slot,0,1),f.Plan.Command(1)})
        await ThrowsAsync<InvalidOperationException>(()=>s.SendEditorAsync(c,timeout,default));
    await s.SendEditorAsync(f.Plan.Command(0),timeout,default);
    await ThrowsAsync<InvalidOperationException>(()=>s.SendEditorAsync(f.Plan.Command(0),timeout,default));
    await ThrowsAsync<InvalidOperationException>(()=>s.SendEditorAsync(f.Plan.Command(1),timeout,default));
    await ThrowsAsync<InvalidOperationException>(()=>s.SendEditorAsync(EditorCommand.SavePreset(f.Device,f.Plan.Slot),timeout,default));
    Throws<InvalidOperationException>(()=>s.SceneMidiReceipt());await s.VerifySceneMidiFieldAsync(f.Context,timeout,default);
    await s.SendEditorAsync(f.Plan.Command(1),timeout,default);await s.VerifySceneMidiFieldAsync(f.Context,timeout,default);
});
await Check("Scene MIDI unsaved work and live table mismatch prevent navigation/writes",async()=>
{
    var f=new SceneMidiEditFixture(definitions[0]);var body=f.Base.Image.Body.ToArray();body[3000]^=1;f.Live=Fixtures.FromBody(f.Device,f.Plan.Slot,body);
    await using var s=new ReadSession(f.Midi);s.AuthorizeSceneMidiEdit(new(f.Plan));
    await ThrowsAsync<InvalidDataException>(()=>s.PrepareSceneMidiEditAsync(f.Context,timeout,default));
    Assert(f.Midi.Sent.All(SysexProtocol.IsReadRequest),"No discarded unsaved work");
});
await Check("Scene MIDI ignored field, opaque live change and selection drift cannot enable Save",async()=>
{
    var f=new SceneMidiEditFixture(definitions[0]);await using var s=new ReadSession(f.Midi);s.AuthorizeSceneMidiEdit(new(f.Plan));
    await s.PrepareSceneMidiEditAsync(f.Context,timeout,default);f.ApplyField=false;
    await s.SendEditorAsync(f.Plan.Command(0),timeout,default);
    await ThrowsAsync<InvalidDataException>(()=>s.VerifySceneMidiFieldAsync(f.Context,timeout,default));
    await ThrowsAsync<InvalidOperationException>(()=>s.SendEditorAsync(EditorCommand.SavePreset(f.Device,f.Plan.Slot),timeout,default));
    Assert(f.Saved==f.Base,"Unverified field not saved");
    var g=new SceneMidiEditFixture(definitions[0]);await using var t=new ReadSession(g.Midi);t.AuthorizeSceneMidiEdit(new(g.Plan));
    await t.PrepareSceneMidiEditAsync(g.Context,timeout,default);g.ExtraWord++;
    await ThrowsAsync<InvalidDataException>(()=>t.SendEditorAsync(g.Plan.Command(0),timeout,default));
    g.ExtraWord--;g.ReportedSlot--;
    await ThrowsAsync<InvalidDataException>(()=>t.SendEditorAsync(g.Plan.Command(0),timeout,default));
    Assert(g.Midi.Sent.All(q=>q[5]!=1||q[6]!=9),"No field written after drift");
});
foreach(bool reject in new[]{true,false})
await Check(reject?"Scene MIDI NACK stops without Save/replay":"Scene MIDI cancellation stops without Save/replay",async()=>
{
    var f=new SceneMidiEditFixture(definitions[0]);await using var s=new ReadSession(f.Midi);s.AuthorizeSceneMidiEdit(new(f.Plan));
    await s.PrepareSceneMidiEditAsync(f.Context,timeout,default);using var cancel=new CancellationTokenSource();var reply=f.Midi.Reply;
    f.Midi.Reply=(q,m)=>{reply!(q,m);if(q[5]==1&&q[6]==9){if(reject)m.Emit(SysexProtocol.Frame(f.Device.ModelByte,0x64,1,5));else cancel.Cancel();}};
    if(reject)await ThrowsAsync<IOException>(()=>s.SendEditorAsync(f.Plan.Command(0),timeout,default));
    else await ThrowsAsync<OperationCanceledException>(()=>s.SendEditorAsync(f.Plan.Command(0),timeout,cancel.Token));
    await ThrowsAsync<IOException>(()=>s.CurrentAsync(f.Context,timeout,default));Throws<InvalidOperationException>(()=>s.SceneMidiReceipt());
    Assert(f.Saved==f.Base&&f.Midi.Sent.Count(q=>q[5]==1&&q[6]==9)==1,"No persistence or retry");
});
await Check("Scene MIDI backup undo rejects later saved edits and unknown unsaved edits before upload",async()=>
{
    var f=new SceneMidiEditFixture(definitions[0]);SceneMidiEditReceipt receipt;
    await using(var s=new ReadSession(f.Midi))receipt=await SceneMidiEditChecks.Apply(f,s,timeout);
    var result=f.Saved;var body=result.Image.Body.ToArray();body[3000]^=1;f.Saved=f.Live=Fixtures.FromBody(f.Device,f.Plan.Slot,body);
    int sent=f.Midi.Sent.Count;
    await using(var undo=new ReadSession(f.Midi))
    {
        undo.AuthorizePresetRestore(new(receipt.UndoUpload));await ThrowsAsync<InvalidDataException>(()=>undo.PrepareSceneMidiUndoAsync(receipt,f.Context,timeout,default));
        await ThrowsAsync<InvalidOperationException>(()=>undo.UploadPresetAsync(timeout,TimeSpan.Zero,default));
    }
    Assert(f.Midi.Sent.Skip(sent).All(SysexProtocol.IsReadRequest),"Later saved data not overwritten");
    f.Saved=result;sent=f.Midi.Sent.Count;
    await using(var undo=new ReadSession(f.Midi))
    {
        undo.AuthorizePresetRestore(new(receipt.UndoUpload));await ThrowsAsync<InvalidDataException>(()=>undo.PrepareSceneMidiUndoAsync(receipt,f.Context,timeout,default));
        await ThrowsAsync<InvalidOperationException>(()=>undo.UploadPresetAsync(timeout,TimeSpan.Zero,default));
    }
    Assert(f.Midi.Sent.Skip(sent).All(SysexProtocol.IsReadRequest),"Unsaved later work not discarded");
});
await Check("Scene MIDI batch scope yields only after saved/reloaded completion",async()=>
{
    var d=definitions[0];var f=new SceneMidiEditFixture(d);var g=new SceneMidiEditFixture(d,d.PresetCapacity-2);
    var a=new SceneMidiEditScope(f.Plan);var b=new SceneMidiEditScope(g.Plan);var batch=new SceneMidiEditBatchScope([a,b]);
    Throws<ArgumentException>(()=>new SceneMidiEditBatchScope([a,a]));batch.Activate(a);Throws<InvalidOperationException>(()=>batch.Activate(b));
    await using var s=new ReadSession(f.Midi);s.AuthorizeSceneMidiEdit(a);await s.PrepareSceneMidiEditAsync(f.Context,timeout,default);
    for(int i=0;i<f.Plan.CommandCount;i++){await s.SendEditorAsync(f.Plan.Command(i),timeout,default);await s.VerifySceneMidiFieldAsync(f.Context,timeout,default);}
    await s.VerifySceneMidiBufferAsync(f.Context,timeout,default);await s.SendEditorAsync(EditorCommand.SavePreset(d,f.Plan.Slot),timeout,default);
    await s.VerifySceneMidiSavedAsync(f.Context,timeout,default);Throws<InvalidOperationException>(()=>batch.Activate(b));
    await s.SendEditorAsync(EditorCommand.ReloadPreset(d,f.Plan.Slot),timeout,default);await s.VerifySceneMidiBufferAsync(f.Context,timeout,default);await s.VerifySceneMidiSavedAsync(f.Context,timeout,default);
    batch.Activate(b);Throws<InvalidOperationException>(()=>batch.Activate(a));
});
await Check("Scene MIDI undo rechecks later saved/loaded changes immediately before Save",async()=>
{
    var f=new SceneMidiEditFixture(definitions[0]);SceneMidiEditReceipt receipt;
    await using(var s=new ReadSession(f.Midi))receipt=await SceneMidiEditChecks.Apply(f,s,timeout);
    var result=f.Saved;await using var undo=new ReadSession(f.Midi);undo.AuthorizePresetRestore(new(receipt.UndoUpload));
    await undo.PrepareSceneMidiUndoAsync(receipt,f.Context,timeout,default);
    await undo.UploadPresetAsync(timeout,TimeSpan.Zero,default);await undo.VerifyUploadedPresetAsync(f.Context,timeout,default);
    var body=result.Image.Body.ToArray();body[3000]^=1;f.Saved=Fixtures.FromBody(f.Device,f.Plan.Slot,body);
    int sent=f.Midi.Sent.Count;await ThrowsAsync<InvalidDataException>(()=>undo.SendEditorAsync(EditorCommand.SavePreset(f.Device,f.Plan.Slot),timeout,default));
    Assert(f.Midi.Sent.Skip(sent).All(SysexProtocol.IsReadRequest),"Late saved edit not overwritten");
    f.Saved=result;body=f.Base.Image.Body.ToArray();body[3000]^=1;f.Live=Fixtures.FromBody(f.Device,f.Plan.Slot,body);
    sent=f.Midi.Sent.Count;await ThrowsAsync<InvalidDataException>(()=>undo.SendEditorAsync(EditorCommand.SavePreset(f.Device,f.Plan.Slot),timeout,default));
    Assert(f.Midi.Sent.Skip(sent).All(SysexProtocol.IsReadRequest),"Late loaded edit not saved over original");
});
await SceneMidiRecoveryChecks.RunAll(definitions,Check);
foreach (var d in definitions)
{
    await Check($"{d.Model}/{d.PresetCapacity} exact isolated removal and insertion undo preserve entire original", async () =>
    {
        var f = new InsertionFixture(d) { ApplyRemoval = true }; f.Saved = f.Live = f.Changed;
        var p = BlockRemovalPlan.Create(d, f.Changed.Sysex, PresetBody.Sha256(f.Changed.Sysex), f.Plan.Row, f.Plan.Column, f.Plan.EffectId, 3, f.Base.Sysex);
        var scope = new BlockRemovalScope(p); await using var s = new ReadSession(f.Midi); s.AuthorizeBlockRemoval(scope);
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.ClearCell(d,p.Slot,p.Row,p.Column),timeout,default));
        await s.PrepareBlockRemovalAsync(f.Context,timeout,default);
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.ClearCell(d,p.Slot,p.Row,p.Column),timeout,default));
        await s.SendEditorAsync(EditorCommand.SelectCell(d,p.Slot,p.Row,p.Column),timeout,default);
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.ClearCell(d,p.Slot,1,p.Column),timeout,default));
        await s.SendEditorAsync(EditorCommand.ClearCell(d,p.Slot,p.Row,p.Column),timeout,default);
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.ClearCell(d,p.Slot,p.Row,p.Column),timeout,default));
        await s.VerifyRemovedBufferAsync(f.Context,timeout,default);
        await s.SendEditorAsync(EditorCommand.SavePreset(d,p.Slot),timeout,default);
        var saved = await s.VerifyRemovedSavedAsync(f.Context,timeout,default);
        await s.SendEditorAsync(EditorCommand.ReloadPreset(d,p.Slot),timeout,default);
        await s.VerifyRemovedBufferAsync(f.Context,timeout,default);
        var reload = await s.VerifyRemovedSavedAsync(f.Context,timeout,default);
        Assert(saved.Sysex.SequenceEqual(f.Base.Sysex) && reload.Sysex.SequenceEqual(f.Base.Sysex), "Removal restores exact original, not merely an empty cell");
        await ThrowsAsync<InvalidOperationException>(() => s.SendEditorAsync(EditorCommand.SavePreset(d,p.Slot),timeout,default));
        await using var another = new ReadSession(new FakeEditorMidi()); Throws<InvalidOperationException>(() => another.AuthorizeBlockRemoval(scope));
        var bad = f.Base.Image.Body.ToArray(); bad[3000] ^= 1;
        Throws<InvalidDataException>(() => EditorPreservation.VerifyBlockRemoval(d, f.Changed.Image, f.Base.Image with {Body=bad},p.Row,p.Column,p.EffectId));
        Throws<InvalidDataException>(() => BlockRemovalPlan.Create(d,f.Changed.Sysex,new string('0',64),p.Row,p.Column,p.EffectId,3));
        Throws<InvalidDataException>(() => BlockRemovalPlan.Create(d,f.Base.Sysex,PresetBody.Sha256(f.Base.Sysex),p.Row,p.Column,p.EffectId,3));
        Throws<InvalidDataException>(() => BlockRemovalPlan.Create(d,f.Changed.Sysex,p.BaseSha256,p.Row,p.Column,p.EffectId,4));
        bad = f.Changed.Image.Body.ToArray(); bad[0x104+(p.Row-1)*4+2]=1;
        var connected=Fixtures.FromBody(d,p.Slot,bad);
        Throws<InvalidDataException>(() => BlockRemovalPlan.Create(d,connected.Sysex,PresetBody.Sha256(connected.Sysex),p.Row,p.Column,p.EffectId,3));
        if (args.Length > 0)
        {
            string folder = Path.Combine(Path.GetFullPath(args[0]), $"Removal-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}");
            SnapshotExport.Write(folder, f.Context, [f.Changed]);
            SnapshotExport.Write(folder+"-Original", f.Context, [f.Base]);
        }
    });
}
await Check("removal refuses unsaved work and unexpected result before Save", async () =>
{
    var f=new InsertionFixture(definitions[0]); f.Saved=f.Changed; f.Live=f.Base;
    var p=BlockRemovalPlan.Create(f.Device,f.Changed.Sysex,PresetBody.Sha256(f.Changed.Sysex),f.Plan.Row,f.Plan.Column,f.Plan.EffectId,3);
    await using var s=new ReadSession(f.Midi); s.AuthorizeBlockRemoval(new(p));
    await ThrowsAsync<InvalidDataException>(()=>s.PrepareBlockRemovalAsync(f.Context,timeout,default));
    Assert(f.Midi.Sent.All(SysexProtocol.IsReadRequest),"Unsaved work was preserved without navigation");
    f.Live=f.Changed; await s.PrepareBlockRemovalAsync(f.Context,timeout,default);
    await s.SendEditorAsync(EditorCommand.SelectCell(f.Device,p.Slot,p.Row,p.Column),timeout,default);
    await s.SendEditorAsync(EditorCommand.ClearCell(f.Device,p.Slot,p.Row,p.Column),timeout,default);
    await ThrowsAsync<InvalidDataException>(()=>s.VerifyRemovedBufferAsync(f.Context,timeout,default));
    await ThrowsAsync<InvalidOperationException>(()=>s.SendEditorAsync(EditorCommand.SavePreset(f.Device,p.Slot),timeout,default));
    Assert(f.Saved==f.Changed,"Mismatched removal result not saved");
});
foreach (bool reject in new[] {true,false})
await Check(reject ? "removal rejection stops without Save or replay" : "removal cancellation poisons session without replay", async () =>
{
    var f=new InsertionFixture(definitions[0]) {ApplyRemoval=true}; f.Live=f.Saved=f.Changed;
    var p=BlockRemovalPlan.Create(f.Device,f.Changed.Sysex,PresetBody.Sha256(f.Changed.Sysex),f.Plan.Row,f.Plan.Column,f.Plan.EffectId,3);
    await using var s=new ReadSession(f.Midi); s.AuthorizeBlockRemoval(new(p));
    await s.PrepareBlockRemovalAsync(f.Context,timeout,default);
    await s.SendEditorAsync(EditorCommand.SelectCell(f.Device,p.Slot,p.Row,p.Column),timeout,default);
    using var cancel=new CancellationTokenSource(); var reply=f.Midi.Reply;
    f.Midi.Reply=(q,midi)=>{reply!(q,midi);if(q[5]==1&&q[6]==0x33){if(reject)midi.Emit(SysexProtocol.Frame(f.Device.ModelByte,0x64,1,5));else cancel.Cancel();}};
    var clear=EditorCommand.ClearCell(f.Device,p.Slot,p.Row,p.Column);
    if(reject)await ThrowsAsync<IOException>(()=>s.SendEditorAsync(clear,timeout,default));
    else await ThrowsAsync<OperationCanceledException>(()=>s.SendEditorAsync(clear,timeout,cancel.Token));
    await ThrowsAsync<IOException>(()=>s.CurrentAsync(f.Context,timeout,default));
    Assert(f.Saved==f.Changed&&f.Midi.Sent.Count(q=>q[5]==1&&q[6]==0x33)==1,"One removal transmission; no Save or replay");
});
await Check("removal rechecks later saved and loaded changes immediately before mutation and Save", async () =>
{
    var f=new InsertionFixture(definitions[0]) {ApplyRemoval=true}; f.Live=f.Saved=f.Changed;
    var p=BlockRemovalPlan.Create(f.Device,f.Changed.Sysex,PresetBody.Sha256(f.Changed.Sysex),f.Plan.Row,f.Plan.Column,f.Plan.EffectId,3,f.Base.Sysex);
    await using var s=new ReadSession(f.Midi);s.AuthorizeBlockRemoval(new(p));
    await s.PrepareBlockRemovalAsync(f.Context,timeout,default);
    await s.SendEditorAsync(EditorCommand.SelectCell(f.Device,p.Slot,p.Row,p.Column),timeout,default);
    f.Saved=f.Base;int sent=f.Midi.Sent.Count;
    await ThrowsAsync<InvalidDataException>(()=>s.SendEditorAsync(EditorCommand.ClearCell(f.Device,p.Slot,p.Row,p.Column),timeout,default));
    Assert(f.Midi.Sent.Skip(sent).All(SysexProtocol.IsReadRequest),"Later saved change not followed by Clear");
    f.Saved=f.Changed;
    await s.SendEditorAsync(EditorCommand.ClearCell(f.Device,p.Slot,p.Row,p.Column),timeout,default);
    await s.VerifyRemovedBufferAsync(f.Context,timeout,default);
    byte[] body=f.Base.Image.Body.ToArray();body[3000]^=1;f.Live=Fixtures.FromBody(f.Device,p.Slot,body);sent=f.Midi.Sent.Count;
    await ThrowsAsync<InvalidDataException>(()=>s.SendEditorAsync(EditorCommand.SavePreset(f.Device,p.Slot),timeout,default));
    Assert(f.Midi.Sent.Skip(sent).All(SysexProtocol.IsReadRequest),"Late unsaved edit not overwritten by Save");
});
Console.WriteLine($"{passed} checks passed. Synthetic transport/data only; no hardware ports opened.");
