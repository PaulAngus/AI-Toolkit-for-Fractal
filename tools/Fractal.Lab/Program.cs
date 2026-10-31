using ApplicationTestLab;

string suffix = OperatingSystem.IsWindows() ? ".exe" : "";
var stages = new List<LabStage>();
stages.Add(new("functional", "Functional", "bin/tests/Fractal.Checks" + suffix, [], ReadTestResult: false));
stages.Add(new("native-midi", "Native", UnavailableReason: "Native transport and physical Fractal device checks require the separate explicitly scoped hardware workflow."));
stages.Add(new("desktop", "Desktop", UnavailableReason: "This library/CLI has no desktop UI suite.", IncludeInAll: false));
try
{
    return await PortableLab.RunAsync(args, new LabApplication("fractal-ai-toolkit", "AI Toolkit for Fractal", stages.ToArray(), ["Functional uses the existing offline .NET tests. Physical hardware is not exercised.", "Native transport and physical Fractal device checks require the separate explicitly scoped hardware workflow.", "Python planner, MCP and packaging checks remain development/CI checks and are not included in this self-contained .NET bundle."], new Dictionary<string, string?> { ["TMPDIR"] = "{output}/profile", ["PRESET_MAESTRO_HARDWARE"] = null, ["PRESET_MAESTRO_COMPATIBILITY"] = null, ["PRESET_MAESTRO_NAVIGATION"] = null, ["PRESET_MAESTRO_SCREENSHOT_DIR"] = "{output}/screenshots", ["MIDI_FORWARDER_UI_SNAPSHOTS"] = "{output}/screenshots" }, RunTimeout: TimeSpan.FromMinutes(30)));
}
catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
