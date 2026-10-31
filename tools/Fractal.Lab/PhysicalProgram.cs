using ApplicationTestLab;
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This profile requires Windows.");
LabStage[] stages = [
    new("offline-functional-planning-and-protocol", "Functional", "bin/tests/Fractal.Checks.exe", [], ReadTestResult: false),
    new("native-fm9-read-only", "Native", "bin/hardware/Fractal.Fm9Checks.exe", ["--fm9", "fm9-read-only.json", "{result}"], ReadTestResult: true),
    new("desktop", "Desktop", UnavailableReason: "Interactive desktop automation is excluded by the offline profile; any UI checks are headless.", IncludeInAll: false)
];
return await PortableLab.RunAsync(args, new LabApplication("fractal-ai-toolkit", "AI Toolkit for Fractal", stages, ["All combines the audited offline Functional checks with a separate read-only FM9 Native stage.", "Only four exact requests: identity, firmware, current preset and current scene. No navigation, PC/CC/notes, writes, dumps, virtual ports or network MIDI sessions.", "Native exercises the maintained ReadSession through NativeMidiTransport/WinMM. Other models, full library reads and writes are not qualified.", "Current preset/scene fingerprints must agree before and after reopening. This cannot rule out brief external changes between observations."],
    new Dictionary<string, string?> {
        ["PRESET_SENDER_DATA_DIRECTORY"] = "{output}/profile",
        ["PRESET_SENDER_SCREENSHOT_DIR"] = "{output}/screenshots/main",
        ["PRESET_SENDER_CONFIG_SCREENSHOT_DIR"] = "{output}/screenshots/configuration",
        ["PRESET_SENDER_NATIVE_MIDI_TEST"] = null, ["PRESET_SENDER_VIRTUAL_DEVICE_NAME"] = null,
        ["PRESET_MAESTRO_HARDWARE"] = null, ["PRESET_MAESTRO_COMPATIBILITY"] = null,
        ["PRESET_MAESTRO_ALLOW_NAVIGATION"] = null, ["PRESET_MAESTRO_NAVIGATION"] = null,
        ["TMPDIR"] = "{output}/profile", ["TMP"] = "{output}/profile", ["TEMP"] = "{output}/profile"
    }, RunTimeout: TimeSpan.FromMinutes(30)));
