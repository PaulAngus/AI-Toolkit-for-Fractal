using Fractal.Core;

static class RestoreDeadlineChecks
{
    public static IEnumerable<(string Name, Func<Task> Test)> Cases()
    {
        yield return ("upload preflight deadline prevents every write and poisons the session", async () =>
        {
            var f = new RestoreFixture(DeviceDefinition.Create("FM9"));
            await using var session = new ReadSession(f.Midi);
            session.AuthorizePresetRestore(new(f.Plan));
            await session.PreparePresetRestoreAsync(f.Context, PresetBody.Sha256(f.Base.Sysex), TimeSpan.FromSeconds(5), default);
            f.Midi.Reply = null; // The preflight query is deliberately left unanswered.
            await Throws<TimeoutException>(() => session.UploadPresetAsync(TimeSpan.FromMilliseconds(100), TimeSpan.Zero, default));
            int sent = f.Midi.Sent.Count;
            await Throws<IOException>(() => session.CurrentAsync(f.Context, TimeSpan.FromSeconds(1), default));
            Need(f.Midi.Sent.Count == sent && f.Midi.Sent.All(SysexProtocol.IsReadRequest) && f.Saved == f.Base,
                "A preflight timeout sent an upload, save or replay.");
        });
        yield return ("upload deadline stops transfer and poisons the session", async () =>
        {
            var f = new RestoreFixture(DeviceDefinition.Create("FM9"));
            await using var session = new ReadSession(f.Midi);
            session.AuthorizePresetRestore(new(f.Plan));
            await session.PreparePresetRestoreAsync(f.Context, PresetBody.Sha256(f.Base.Sysex), TimeSpan.FromSeconds(5), default);
            // Wait inside the already-sent first header until its actual deadline cancels.
            // This tests an uncertain send without assuming that preflight fits in 10 ms.
            f.Midi.AfterPresetFrame = (index, token) => index == 0 ? Task.Delay(Timeout.Infinite, token) : Task.CompletedTask;
            await Throws<TimeoutException>(() => session.UploadPresetAsync(TimeSpan.FromSeconds(5), TimeSpan.Zero, default));
            int sent = f.Midi.Sent.Count;
            await Throws<IOException>(() => session.CurrentAsync(f.Context, TimeSpan.FromSeconds(1), default));
            Need(f.Midi.Sent.Count == sent && f.Midi.Sent.Count(q => q[5] == 0x77) == 1 &&
                f.Midi.Sent.All(q => q[5] is not (0x78 or 0x79 or 1)) && f.Saved == f.Base,
                "An uncertain header was replayed or followed by a body, footer or save.");
        });
    }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
    private static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
}
