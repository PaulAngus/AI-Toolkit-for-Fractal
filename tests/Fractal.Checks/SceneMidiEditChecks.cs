using Fractal.Core;

static class SceneMidiEditChecks
{
    public static async Task<SceneMidiEditReceipt> Apply(SceneMidiEditFixture f,ReadSession s,TimeSpan timeout)
    {
        s.AuthorizeSceneMidiEdit(new(f.Plan));await s.PrepareSceneMidiEditAsync(f.Context,timeout,default);
        for(int i=0;i<f.Plan.CommandCount;i++)
        {
            await s.SendEditorAsync(f.Plan.Command(i),timeout,default);await s.VerifySceneMidiFieldAsync(f.Context,timeout,default);
        }
        await s.VerifySceneMidiBufferAsync(f.Context,timeout,default);
        await s.SendEditorAsync(EditorCommand.SavePreset(f.Device,f.Plan.Slot),timeout,default);
        await s.VerifySceneMidiSavedAsync(f.Context,timeout,default);await s.VerifySceneMidiSavedAsync(f.Context,timeout,default);
        await s.SendEditorAsync(EditorCommand.ReloadPreset(f.Device,f.Plan.Slot),timeout,default);
        await s.VerifySceneMidiBufferAsync(f.Context,timeout,default);await s.VerifySceneMidiSavedAsync(f.Context,timeout,default);
        return s.SceneMidiReceipt();
    }
}
