// Editor envelopes researched in the pinned Gen-3 implementation; see docs/editing-pilot.md.
using MightBeUseful.Midi;

namespace Fractal.Core;

public enum EditorOperation { SelectCell, InsertBlock, ClearCell, SetSceneMidiField, SelectScene, SavePreset, ReloadPreset, SetBlockChannel, SetBlockBypass, ConfiguredCc, SelectMidiBank, RecallMidiProgram, QueryTempo, SetTempo, TapTempo, SetTuner, LooperButton }

/// <summary>Typed experimental commands. Transport delivery is never evidence of a successful edit.</summary>
public sealed class EditorCommand
{
    public DeviceDefinition Device { get; }
    public EditorOperation Operation { get; }
    public int Slot { get; }
    public int? Row { get; }
    public int? Column { get; }
    public int? EffectId { get; }
    private readonly byte[] frame;
    public byte[] Frame => frame.ToArray();
    public bool IsShortMessage => Operation is EditorOperation.ConfiguredCc or EditorOperation.SelectMidiBank or EditorOperation.RecallMidiProgram;
    internal static EditorCommand SendCc(DeviceDefinition d, int slot, ConfiguredCc assignment, bool bank = false)
    {
        assignment.Validate(); return new(d, bank ? EditorOperation.SelectMidiBank : EditorOperation.ConfiguredCc, slot,
            MidiMessageEncoding.GetShortMessageBytes(MidiMessageEncoding.ControlChange(assignment.Controller, assignment.Value, assignment.Channel)));
    }
    internal static EditorCommand Program(DeviceDefinition d, int slot, MidiPresetMapping mapping)
    { mapping.Validate(slot); return new(d, EditorOperation.RecallMidiProgram, slot,
        MidiMessageEncoding.GetShortMessageBytes(MidiMessageEncoding.ProgramChange(mapping.Program, mapping.Channel))); }
    private EditorCommand(DeviceDefinition device, EditorOperation operation, int slot, byte[] bytes,
        int? row = null, int? column = null, int? effectId = null)
    { device.ValidateSlot(slot); Device = device; Operation = operation; Slot = slot; frame = bytes; Row = row; Column = column; EffectId = effectId; }
    private static int Position(DeviceDefinition d, int row, int column)
    {
        if (row < 1 || row > d.GridRows || column < 1 || column > d.GridColumns)
            throw new ArgumentOutOfRangeException(nameof(row), "Cell is outside this model's grid.");
        return (column - 1) * d.GridRows + row - 1;
    }
    private static byte[] Action(DeviceDefinition d, byte sub, int effect, int parameter, uint value)
    {
        byte[] p = new byte[15]; p[0] = sub;
        p[2] = (byte)(effect & 127); p[3] = (byte)(effect >> 7);
        p[4] = (byte)(parameter & 127); p[5] = (byte)(parameter >> 7);
        for (int i = 0; i < 5; i++) p[6 + i] = (byte)((value >> (i * 7)) & 127);
        return SysexProtocol.Frame(d.ModelByte, 1, p);
    }
    public static EditorCommand SelectCell(DeviceDefinition d, int slot, int row, int column) =>
        new(d, EditorOperation.SelectCell, slot, Action(d, 0x30, 0, 0, (uint)Position(d, row, column)), row, column);
    public static EditorCommand InsertBlock(DeviceDefinition d, int slot, int row, int column, int effectId)
    {
        if (effectId is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(effectId));
        return new(d, EditorOperation.InsertBlock, slot, Action(d, 0x32, effectId, 0, (uint)Position(d, row, column)), row, column, effectId);
    }
    // Send after SelectCell at the same position. A standalone 0x30 is not deletion.
    public static EditorCommand ClearCell(DeviceDefinition d, int slot, int row, int column) =>
        new(d, EditorOperation.ClearCell, slot, Action(d, 0x33, 0, 0, (uint)Position(d, row, column)), row, column);
    public static EditorCommand SetSceneMidiField(DeviceDefinition d, int slot, int parameter, int value)
    {
        if (d.SceneMidi == null) throw new NotSupportedException("No model-specific Scene MIDI field profile.");
        int max = parameter switch { >= 0 and < 64 => 2, >= 64 and < 128 => 15, >= 128 and < 256 => 127, _ => -1 };
        if (value < 0 || value > max) throw new ArgumentOutOfRangeException(nameof(value), "Invalid Scene MIDI field/value.");
        uint bits = BitConverter.SingleToUInt32Bits(value);
        return new(d, EditorOperation.SetSceneMidiField, slot, Action(d, 9, 190, parameter, bits), effectId: 190);
    }
    public static EditorCommand SelectScene(DeviceDefinition d, int slot, int scene)
    {
        if (scene is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(scene));
        return new(d, EditorOperation.SelectScene, slot, SysexProtocol.Frame(d.ModelByte, 0x0c, (byte)(scene - 1)));
    }
    public static EditorCommand SavePreset(DeviceDefinition d, int slot) =>
        new(d, EditorOperation.SavePreset, slot, Action(d, 0x26, 0, 0, (uint)slot));
    public static EditorCommand SetBlockChannel(DeviceDefinition d, int slot, int effectId, int channel)
    {
        if (effectId is < 1 or > 1000 || channel is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(channel));
        return new(d, EditorOperation.SetBlockChannel, slot, SysexProtocol.Frame(d.ModelByte, 11, (byte)(effectId & 127), (byte)(effectId >> 7), (byte)(channel - 1)), effectId: effectId);
    }
    public static EditorCommand SetBlockBypass(DeviceDefinition d, int slot, int effectId, bool bypassed)
    {
        if (effectId is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(effectId));
        return new(d, EditorOperation.SetBlockBypass, slot, SysexProtocol.Frame(d.ModelByte, 10, (byte)(effectId & 127), (byte)(effectId >> 7), (byte)(bypassed ? 1 : 0)), effectId: effectId);
    }
    public static EditorCommand ReloadPreset(DeviceDefinition d, int slot) =>
        new(d, EditorOperation.ReloadPreset, slot, Action(d, 0x27, 0, 0, (uint)slot));
    internal static EditorCommand QueryTempo(DeviceDefinition d, int slot) =>
        new(d, EditorOperation.QueryTempo, slot, SysexProtocol.Frame(d.ModelByte, 0x14, 127, 127));
    public static EditorCommand SetTempo(DeviceDefinition d, int slot, int bpm)
    {
        PerformanceProtocol.ValidateTempo(d, bpm);
        return new(d, EditorOperation.SetTempo, slot, SysexProtocol.Frame(d.ModelByte, 0x14, (byte)(bpm & 127), (byte)(bpm >> 7)));
    }
    public static EditorCommand TapTempo(DeviceDefinition d, int slot) =>
        new(d, EditorOperation.TapTempo, slot, SysexProtocol.Frame(d.ModelByte, 0x10));
    public static EditorCommand SetTuner(DeviceDefinition d, int slot, bool enabled) =>
        new(d, EditorOperation.SetTuner, slot, SysexProtocol.Frame(d.ModelByte, 0x11, (byte)(enabled ? 1 : 0)));
    public static EditorCommand LooperButton(DeviceDefinition d, int slot, string action) =>
        new(d, EditorOperation.LooperButton, slot, SysexProtocol.Frame(d.ModelByte, 0x0f, PerformanceProtocol.Button(action)));
}

/// <summary>Closed family of explicitly authorized editor scopes; default sessions remain read-only.</summary>
public abstract class EditorScope
{
    private protected EditorScope() { }
    public abstract DeviceDefinition Device { get; }
    public abstract void Validate(EditorCommand command);
}

/// <summary>Only save the current edit buffer to one explicitly authorized slot. No upload, reload or edits.</summary>
public sealed class PresetSaveScope : EditorScope
{
    public override DeviceDefinition Device { get; }
    public int Slot { get; }
    public PresetSaveScope(DeviceDefinition device, int slot)
    { device.ValidateSlot(slot); Device = device; Slot = slot; }
    public override void Validate(EditorCommand command)
    {
        if (command.Device != Device || command.Slot != Slot || command.Operation != EditorOperation.SavePreset)
            throw new InvalidOperationException("Command is outside the authorized single-preset save scope.");
    }
}

public sealed class EmptyPresetPilotScope : EditorScope
{
    public override DeviceDefinition Device { get; }
    public int Slot { get; }
    public int Row { get; }
    public int Column { get; }
    public EmptyPresetPilotScope(DeviceDefinition device, int slot, int row, int column)
    {
        _ = EditorCommand.SelectCell(device, slot, row, column);
        Device = device; Slot = slot; Row = row; Column = column;
    }
    public override void Validate(EditorCommand command)
    {
        if (command.Device != Device || command.Slot != Slot ||
            command.Row.HasValue && (command.Row != Row || command.Column != Column) ||
            command.EffectId.HasValue && command.EffectId != 190)
            throw new InvalidOperationException("Command is outside the authorized single-preset pilot scope.");
    }
}

public interface IEditorTransport : IMidiTransport
{
    Task SendEditorAsync(EditorCommand command, CancellationToken token);
}

public sealed record BlockStatus(int EffectId, bool Bypassed, int Channel, int ChannelCount);
public static class BlockStatusTable
{
    public static BlockStatus[] Decode(DeviceDefinition device, byte[] frame)
    {
        if (!SysexProtocol.ValidFrame(frame, device.ModelByte, 0x13) || (frame.Length - 8) % 3 != 0)
            throw new InvalidDataException("Invalid block status response.");
        List<BlockStatus> entries = [];
        for (int i = 6; i < frame.Length - 2; i += 3)
        {
            int id = frame[i] | frame[i + 1] << 7, flags = frame[i + 2];
            if (id is < 1 or > 1000 || entries.Any(e => e.EffectId == id)) throw new InvalidDataException("Invalid/duplicate status instance.");
            entries.Add(new(id, (flags & 1) != 0, ((flags >> 1) & 7) + 1, (flags >> 4) & 7));
        }
        return entries.ToArray();
    }
}
