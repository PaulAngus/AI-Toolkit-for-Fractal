using TestLab.Fm9;
if (args.Length == 2 && args[0] == "--catalogs")
{
    var catalogs = new
    {
        winMmInputs = Fractal.Core.NativeMidiTransport.Ports(true),
        winMmOutputs = Fractal.Core.NativeMidiTransport.Ports(false),
        sharedInputs = MightBeUseful.Midi.Windows.WindowsLegacyMidiBackend.GetInputDevices().Select(p => new { p.Id, p.Name }).ToArray(),
        sharedOutputs = MightBeUseful.Midi.Windows.WindowsLegacyMidiBackend.GetOutputDevices().Select(p => new { p.Index, p.Name }).ToArray(),
        midiOpened = false
    };
    File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(catalogs, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
return await Fm9ReadOnly.Run(args, selection => new Fm9ToolkitTransport(selection), "Toolkit production ReadSession / NativeMidiTransport / WinMM");
