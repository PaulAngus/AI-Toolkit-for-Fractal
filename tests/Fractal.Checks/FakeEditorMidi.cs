using Fractal.Core;

sealed class FakeEditorMidi : IPresetRestoreTransport
{
    public event Action<byte[]>? Received;
    public event Action<Exception>? Faulted;
    public List<byte[]> Sent { get; } = [];
    public Action<byte[], FakeEditorMidi>? Reply { get; set; }
    public Func<int, CancellationToken, Task>? AfterPresetFrame { get; set; }
    public EditorScope? Scope { get; set; }
    public Task SendReadAsync(byte[] request, CancellationToken token) => Send(request, token);
    public Task SendEditorAsync(EditorCommand command, CancellationToken token)
    {Scope?.Validate(command);return Send(command.Frame, token);}
    public async Task SendPresetFrameAsync(PresetUploadPlan plan, int index, CancellationToken token)
    {
        if(Scope is SceneMidiRecoveryAuthority recovery)recovery.ValidateUpload(plan,index);
        else if(Scope is EditBatchScope batch)batch.ValidateUpload(plan,index);
        else if(Scope is PresetRestoreScope restore)restore.ValidateUpload(plan,index);
        else if(Scope!=null)throw new InvalidOperationException("No upload authority.");
        await Send(plan.Frame(index), token);
        if (AfterPresetFrame is { } after) await after(index, token);
    }
    private Task Send(byte[] request, CancellationToken token)
    { token.ThrowIfCancellationRequested(); Sent.Add(request); Reply?.Invoke(request, this); return Task.CompletedTask; }
    public void Emit(byte[] frame) => Received?.Invoke(frame);
    public void Error(Exception error) => Faulted?.Invoke(error);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
