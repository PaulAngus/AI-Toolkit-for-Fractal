using System.Text;
using Fractal.Core;

// Entirely synthetic device state. No personal capture, preset settings or transport configuration.
sealed class RestoreFixture
{
    public DeviceDefinition Device { get; }
    public int Slot { get; }
    public SavedCapture Base { get; }
    public SavedCapture Expected { get; }
    public SavedCapture Saved { get; set; }
    public SavedCapture Live { get; set; }
    public PresetUploadPlan Plan { get; }
    public FakeEditorMidi Midi { get; } = new();
    public DeviceContext Context => new(Device, "99.07", "synthetic");
    public bool ApplyUpload { get; set; } = true;
    public int ReportedSlot { get; set; }
    public RestoreFixture(DeviceDefinition device)
    {
        Device = device; Slot = device.PresetCapacity - 1; ReportedSlot = Slot;
        Base = Fixtures.Preset(device, Slot, amp: false);
        Expected = Fixtures.Preset(device, Slot);
        Saved = Base; Live = Base;
        Plan = PresetUploadPlan.Create(device, Slot, 3, Expected.Sysex, PresetBody.Sha256(Expected.Sysex));
        Midi.Reply = Reply;
    }
    private void Reply(byte[] q, FakeEditorMidi midi)
    {
        switch (q[5])
        {
            case 0x0d:
                byte[] payload = new byte[34]; payload[0] = (byte)(ReportedSlot & 127); payload[1] = (byte)(ReportedSlot >> 7);
                Encoding.ASCII.GetBytes(Live.Image.Name).CopyTo(payload, 2);
                midi.Emit(SysexProtocol.Frame(Device.ModelByte, 0x0d, payload)); break;
            case 0x0c: midi.Emit(SysexProtocol.Frame(Device.ModelByte, 0x0c, 2)); break;
            case 3:
                midi.Emit((q[6] == 127 && q[7] == 127 ? Live : Saved).Sysex); break;
            case 0x79: if (ApplyUpload) Live = Expected; break;
            case 1:
                if (q[6] == 0x26) Saved = Live;
                if (q[6] == 0x27) Live = Saved;
                break;
        }
    }
}
