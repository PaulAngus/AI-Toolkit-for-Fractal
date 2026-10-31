namespace Fractal.Core;

/// <summary>Model-specific read layouts. Capacity is explicit, never inferred from firmware.</summary>
public sealed record DeviceDefinition(string Model, byte ModelByte, int PresetCapacity,
    int GridRows, int GridColumns, int AmpColumns, int AmpTypeWord, int MaxAmps, int DumpChunks,
    int? OpaqueTailColumns = null, int? OpaqueTailBytes = null, SceneMidiLayout? SceneMidi = null)
{
    public static DeviceDefinition Create(string model, int? capacity = null) => model switch
    {
        "FM9" => Fixed(new("FM9", 0x12, 512, 6, 14, 147, 4, 2, 8, 350, 736,
            new(257, 1, 17, 0, 64, 128, 192)), capacity),
        "FM3" => Fixed(new("FM3", 0x11, 512, 4, 12, 144, 0, 1, 8, SceneMidi:
            new(257, 1, 17, 0, 64, 128, 192)), capacity),
        "Axe-Fx III" when capacity is 512 or 1024 => new(model, 0x10, capacity.Value, 6, 14, 142, -1, 2, 16, SceneMidi:
            new(257, 1, 17, 0, 64, 128, 192)),
        "Axe-Fx III" => throw new ArgumentException("Axe-Fx III capacity must be established explicitly: 512 or 1024."),
        _ => throw new ArgumentException("Model must be FM9, FM3 or Axe-Fx III.")
    };
    private static DeviceDefinition Fixed(DeviceDefinition device, int? capacity) =>
        capacity is null || capacity == device.PresetCapacity ? device :
        throw new ArgumentException($"{device.Model} read definition requires capacity {device.PresetCapacity}.");
    public static string ModelName(byte model) => model switch
    {
        0x10 => "Axe-Fx III", 0x11 => "FM3", 0x12 => "FM9",
        _ => throw new InvalidDataException("Unsupported Fractal model byte.")
    };
    public void ValidateSlot(int slot)
    {
        if (slot < 0 || slot >= PresetCapacity) throw new ArgumentOutOfRangeException(nameof(slot));
    }
}
