using System.Buffers.Binary;
namespace Fractal.Core;

/// <summary>Integrity-checked, immutable single-preset upload. Only temporary-buffer addressing is emitted.</summary>
public sealed class PresetUploadPlan
{
    public DeviceDefinition Device { get; }
    public int Slot { get; }
    public int ExpectedScene { get; }
    public int BeforeScene { get; }
    public string SourceSha256 { get; }
    public string UploadSha256 { get; }
    public string RawSha256 { get; }
    public string BodySha256 { get; }
    public int FrameCount => frames.Length;
    private readonly byte[][] frames;
    private readonly PresetFileInspection expected;
    private readonly bool allowEquivalentCompression;
    private PresetUploadPlan(DeviceDefinition device, int slot, int scene, byte[] source, PresetFileInspection image, bool allowEquivalentCompression, int beforeScene)
    {
        Device = device; Slot = slot; ExpectedScene = scene; expected = image; this.allowEquivalentCompression = allowEquivalentCompression;
        BeforeScene = beforeScene;
        SourceSha256 = PresetBody.Sha256(source);
        byte[] upload = source.ToArray();
        upload[6] = 127; upload[7] = 127; // external FM3 audition evidence; local qualification is separate
        upload[11] = SysexProtocol.ComputeChecksum(upload.AsSpan(0, 11));
        frames = new SysexAssembler().Feed(upload).ToArray();
        UploadSha256 = PresetBody.Sha256(upload);
        RawSha256 = PresetBody.Sha256(image.RawImage); BodySha256 = PresetBody.Sha256(image.Body);
    }
    public static PresetUploadPlan Create(DeviceDefinition device, int slot, int expectedScene, byte[] source, string sourceHash) =>
        CreateCore(device, slot, expectedScene, source, sourceHash, false);
    internal static PresetUploadPlan CreateForEdit(DeviceDefinition device, int slot, int expectedScene, byte[] source, string sourceHash, int? beforeScene = null) =>
        CreateCore(device, slot, expectedScene, source, sourceHash, true, beforeScene);
    private static PresetUploadPlan CreateCore(DeviceDefinition device, int slot, int expectedScene, byte[] source, string sourceHash, bool allowEquivalentCompression, int? beforeScene = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (device != DeviceDefinition.Create(device.Model, device.PresetCapacity))
            throw new ArgumentException("Upload requires a contained supported model definition.");
        device.ValidateSlot(slot);
        if (expectedScene is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(expectedScene));
        if (beforeScene is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(beforeScene));
        byte[] retained = source.ToArray();
        if (!StringComparer.OrdinalIgnoreCase.Equals(PresetBody.Sha256(retained), sourceHash))
            throw new InvalidDataException("Expected backup hash does not match.");
        PresetFileInspection image = PresetFile.Inspect(device, retained);
        // Require the researched model-sized word count; other opaque header variants are inspection-only.
        int words = image.RawImage.Length / 2;
        byte[] count = [(byte)(words & 127), (byte)((words >> 7) & 127), (byte)((words >> 14) & 127)];
        if (!image.HeaderPayload.AsSpan(2).SequenceEqual(count))
            throw new InvalidDataException("Unqualified upload header word-count fields.");
        for (int offset = 13; offset < retained.Length - 11; offset += 3082)
            if (retained[offset + 6] != 0 || retained[offset + 7] != 8)
                throw new InvalidDataException("Unqualified upload chunk word-count fields.");
        return new(device, slot, expectedScene, retained, image, allowEquivalentCompression, beforeScene ?? expectedScene);
    }
    public byte[] Frame(int index) => frames[index].ToArray();
    internal void Verify(PresetFileInspection image)
    {
        if (image.Model != Device.Model || !Equivalent(image.RawImage, image.Body))
            throw new InvalidDataException("Complete preset raw image/body does not match the expected backup; stop without saving or replaying.");
    }
    internal void Verify(StoredPresetImage image)
    {
        if (image.Slot != Slot || !Equivalent(image.RawImage, image.Body))
            throw new InvalidDataException("Complete saved preset does not match the expected backup; reconcile without replay.");
    }
    private bool Equivalent(byte[] raw, byte[] body) => raw.SequenceEqual(expected.RawImage) && body.SequenceEqual(expected.Body) ||
        allowEquivalentCompression && EquivalentCompression(expected.RawImage, expected.Body, raw, body);
    internal void VerifyUploadSceneVariant(PresetFileInspection observed, int scene)
    {
        byte[] selected = expected.Body.ToArray(); BinaryPrimitives.WriteUInt16LittleEndian(selected, (ushort)(scene - 1));
        if (!Equivalent(observed.RawImage, observed.Body) &&
            (!allowEquivalentCompression || !EquivalentCompression(expected.RawImage, selected, observed.RawImage, observed.Body)))
            throw new InvalidDataException("Uploaded content changed beyond its selected-scene word; no alignment is permitted.");
    }

    // General editing only: the device can re-encode the same decoded body, including
    // a different stream length. Keep opaque header and fixed-position trailing bytes.
    internal static bool EquivalentCompression(byte[] expectedRaw, byte[] expectedBody, byte[] observedRaw, byte[] observedBody)
    {
        if (!expectedBody.SequenceEqual(observedBody) || expectedRaw.Length != observedRaw.Length || expectedRaw.Length < 0x4c)
            return false;
        int expectedCompressed = expectedRaw[0x4a] | expectedRaw[0x4b] << 8;
        int observedCompressed = observedRaw[0x4a] | observedRaw[0x4b] << 8;
        if (expectedCompressed == 0 || observedCompressed == 0 ||
            expectedCompressed > expectedRaw.Length - 0x4c || observedCompressed > observedRaw.Length - 0x4c ||
            !expectedRaw.AsSpan(0, 4).SequenceEqual(observedRaw.AsSpan(0, 4)) ||
            !expectedRaw.AsSpan(6, 0x4a - 6).SequenceEqual(observedRaw.AsSpan(6, 0x4a - 6)) ||
            !expectedRaw.AsSpan(0x4c + Math.Max(expectedCompressed, observedCompressed))
                .SequenceEqual(observedRaw.AsSpan(0x4c + Math.Max(expectedCompressed, observedCompressed))))
            return false;
        return true;
    }
}

/// <summary>One upload to temporary memory, then one exact-slot save and reload after complete verification.</summary>
public sealed class PresetRestoreScope : EditorScope
{
    public PresetUploadPlan Plan { get; }
    public override DeviceDefinition Device => Plan.Device;
    public bool AllowSave { get; }
    internal bool BufferVerified { get; private set; }
    internal bool SavedVerified { get; private set; }
    internal bool Completed { get; set; }
    private byte[]? verifiedSaved, alignmentSaved, alignmentLoaded;
    private CurrentState? alignmentState;
    internal bool SceneAlignmentReady { get; private set; }
    internal bool SceneAlignmentAttempted { get; private set; }
    private bool sceneAlignmentSending;
    internal bool UploadSceneAlignment { get; private set; }
    private bool uploadSceneAlignmentAttempted;
    private int sessionClaimed;
    internal void ClaimSession()
    {
        if (Interlocked.Exchange(ref sessionClaimed, 1) != 0)
            throw new InvalidOperationException("Restore authorization cannot be reused across sessions.");
    }
    public PresetRestoreScope(PresetUploadPlan plan, bool allowSave = true)
    { Plan = plan ?? throw new ArgumentNullException(nameof(plan)); AllowSave = allowSave; }
    public override void Validate(EditorCommand command)
    {
        bool permitted = command.Operation switch
        {
            EditorOperation.SavePreset => AllowSave && BufferVerified,
            EditorOperation.ReloadPreset => AllowSave && SavedVerified,
            EditorOperation.SelectScene => (SceneAlignmentReady || sceneAlignmentSending) &&
                command.Frame.SequenceEqual(EditorCommand.SelectScene(Device, Plan.Slot, Plan.ExpectedScene).Frame),
            _ => false
        };
        if (command.Device != Device || command.Slot != Plan.Slot || !permitted)
            throw new InvalidOperationException("Command is outside the verified single-preset restore scope.");
    }
    public void ValidateUpload(PresetUploadPlan plan, int index)
    {
        if (!ReferenceEquals(plan, Plan) || index < 0 || index >= Plan.FrameCount)
            throw new InvalidOperationException("Upload frame is outside the authorized backup plan.");
    }
    internal void VerifyBuffer(PresetFileInspection image) { Plan.Verify(image); BufferVerified = true; }
    internal void Reloading() { BufferVerified=false; Completed=false; }
    internal void VerifySaved(SavedCapture capture)
    {
        Plan.Verify(capture.Image);
        if (verifiedSaved != null && !verifiedSaved.SequenceEqual(capture.Sysex))
            throw new InvalidDataException("Saved result changed between verified reads.");
        verifiedSaved ??= capture.Sysex.ToArray(); SavedVerified = true;
    }
    internal void PrepareSceneAlignment(CurrentState state, SavedCapture saved, EditBufferCapture live)
    {
        if (!SavedVerified || verifiedSaved == null || !verifiedSaved.SequenceEqual(saved.Sysex) ||
            state.Slot != Plan.Slot || state.Scene is < 1 or > 8 || state.Scene == Plan.ExpectedScene)
            throw new InvalidDataException("Reloaded scene alignment lacks an unchanged verified saved result.");
        Plan.Verify(saved.Image);
        byte[] selectedBody = saved.Image.Body.ToArray();
        if (BinaryPrimitives.ReadUInt16LittleEndian(selectedBody) != Plan.ExpectedScene - 1)
            throw new InvalidDataException("Saved default scene differs from the reviewed restore scene.");
        BinaryPrimitives.WriteUInt16LittleEndian(selectedBody, (ushort)(state.Scene - 1));
        if (!PresetUploadPlan.EquivalentCompression(saved.Image.RawImage, selectedBody,
                live.Image.RawImage, live.Image.Body))
            throw new InvalidDataException("Reload changed loaded content beyond the selected scene; no scene alignment was sent.");
        alignmentState = state; alignmentSaved = saved.Sysex.ToArray(); alignmentLoaded = live.Sysex.ToArray();
        UploadSceneAlignment = false;
        SceneAlignmentReady = true;
    }
    internal void PrepareUploadSceneAlignment(CurrentState state, SavedCapture saved, EditBufferCapture live, string baseHash)
    {
        if (SavedVerified || BufferVerified || uploadSceneAlignmentAttempted || Plan.BeforeScene == Plan.ExpectedScene ||
            state.Slot != Plan.Slot || state.Scene != Plan.BeforeScene || saved.Image.Slot != Plan.Slot || PresetBody.Sha256(saved.Sysex) != baseHash)
            throw new InvalidDataException("Upload scene alignment lacks the exact original saved base and reviewed before scene.");
        Plan.VerifyUploadSceneVariant(live.Image, state.Scene);
        alignmentState = state; alignmentSaved = saved.Sysex.ToArray(); alignmentLoaded = live.Sysex.ToArray();
        UploadSceneAlignment = true; SceneAlignmentReady = true;
    }
    internal void VerifySceneAlignmentBefore(CurrentState state, SavedCapture saved, EditBufferCapture live)
    {
        if (!SceneAlignmentReady || (UploadSceneAlignment ? uploadSceneAlignmentAttempted : SceneAlignmentAttempted) || state != alignmentState ||
            alignmentSaved == null || !alignmentSaved.SequenceEqual(saved.Sysex) ||
            alignmentLoaded == null || !alignmentLoaded.SequenceEqual(live.Sysex))
            throw new InvalidDataException("Reloaded scene or complete saved/loaded content changed before alignment.");
    }
    internal void ConsumeSceneAlignment()
    {
        if (!SceneAlignmentReady || (UploadSceneAlignment ? uploadSceneAlignmentAttempted : SceneAlignmentAttempted)) throw new InvalidOperationException("Scene alignment already attempted.");
        if (UploadSceneAlignment) uploadSceneAlignmentAttempted = true; else SceneAlignmentAttempted = true;
        SceneAlignmentReady = false; sceneAlignmentSending = true;
    }
    internal void FinishSceneAlignmentSend() => sceneAlignmentSending = false;
}

public interface IPresetRestoreTransport : IEditorTransport
{
    Task SendPresetFrameAsync(PresetUploadPlan plan, int index, CancellationToken token);
}
public sealed record EditBufferCapture(PresetFileInspection Image, byte[] Sysex);
