namespace Fractal.Core;

/// <summary>The same typed send boundary is used by local and network transports.</summary>
internal sealed class MidiSendAuthority(EditorScope? scope)
{
    private int next;
    private PresetUploadPlan? upload;
    private int performanceSends, performanceQueries;
    public void Read(byte[] frame)
    {
        if (!SysexProtocol.IsReadRequest(frame)) throw new InvalidOperationException("Transport rejected a non-read MIDI request.");
    }
    public void Editor(EditorCommand command)
    {
        if (scope == null) throw new InvalidOperationException("Transport is read-only; no editor scope was authorized.");
        scope.Validate(command);
        if (scope is PerformanceControlScope performance)
        {
            lock (this)
            {
                if (command.Operation == EditorOperation.QueryTempo)
                {
                    if (performanceQueries >= performance.QueryLimit) throw new InvalidOperationException("Transport tempo-query budget exhausted; no replay.");
                    performanceQueries++;
                }
                else
                {
                    if (performanceSends != 0) throw new InvalidOperationException("Transport performance command already attempted; no replay.");
                    performanceSends++;
                }
            }
        }
    }
    public void Upload(PresetUploadPlan plan, int index)
    {
        lock (this)
        {
            if (scope is PresetRestoreScope restore) restore.ValidateUpload(plan, index);
            else if (scope is SceneMidiRecoveryAuthority recovery) recovery.ValidateUpload(plan, index);
            else if (scope is EditBatchScope batch) batch.ValidateUpload(plan, index);
            else throw new InvalidOperationException("Transport has no preset restore scope.");
            if (scope is SceneMidiRecoveryAuthority or EditBatchScope && !ReferenceEquals(upload, plan))
            {
                if (index != 0) throw new InvalidOperationException("A new upload must start with its header.");
                upload = plan; next = 0;
            }
            if (index != next) throw new InvalidOperationException("Upload frames must be sent once in order; no replay.");
            next++;
        }
    }
}
