using Fractal.Core;

static class WindowsInputSelectionChecks
{
    public static Task Run()
    {
        MidiPort[] one = [new(7, "Device Input")];
        Need(WinMmTransport.ResolveInputId(one, 7, [("exact", "Device Input")]) == "exact");
        Need(WinMmTransport.ResolveInputId(one, 7, [("indexed", "Device Input [0]")]) == "indexed");
        Need(WinMmTransport.ResolveInputId(one, 7, [("indexed", "Device Input [12]")]) == "indexed");
        Refuse([new(7, "Device Input"), new(8, "Device Input")], [("id", "Device Input [0]")]);
        Refuse(one, [("a", "Device Input"), ("b", "Device Input [0]")]);
        Refuse(one, [("a", "Device Input [0]"), ("b", "Device Input [1]")]);
        Refuse([new(7, "Device Input"), new(8, "Device Input [0]")], [("id", "Device Input [0]")]);
        foreach (string name in new[] { "Device Input Extra", "Device Input []", "Device Input [-1]", "Device Input [x]", "Device Input [ 0]", "Device Input [0] extra" })
            Refuse(one, [("id", name)]);
        try { WinMmTransport.ResolveInputId(one, 0, [("id", "Device Input")]); throw new Exception("Absent index accepted."); }
        catch (ArgumentOutOfRangeException) { }
        return Task.CompletedTask;
    }
    private static void Refuse(MidiPort[] catalog, (string Id, string Name)[] shared)
    {
        try { WinMmTransport.ResolveInputId(catalog, 7, shared); throw new Exception("An ambiguous or unrelated input was selected."); }
        catch (IOException) { }
    }
    private static void Need(bool condition) { if (!condition) throw new Exception("Native input alias mapping differs."); }
}
