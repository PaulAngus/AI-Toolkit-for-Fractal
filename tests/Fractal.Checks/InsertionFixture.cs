using System.Buffers.Binary;
using System.Text;
using Fractal.Core;

sealed class InsertionFixture
{
    public DeviceDefinition Device { get; }
    public SavedCapture Base { get; }
    public SavedCapture Changed { get; }
    public SavedCapture Saved, Live;
    public BlockInsertionPlan Plan { get; }
    public FakeEditorMidi Midi { get; } = new();
    public DeviceContext Context => new(Device, "99.07", "synthetic");
    public int ReportedSlot;
    public bool ApplyInsertion = true;
    public bool ApplyRemoval;
    public InsertionFixture(DeviceDefinition d, int? slot = null)
    {
        Device = d;
        int targetSlot = slot ?? d.PresetCapacity - 1;
        var body = Fixtures.Preset(d, targetSlot, midi: true).Image.Body.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(body, 2);
        Base = Fixtures.FromBody(d, targetSlot, body);
        const int row = 2, column = 1, id = 114; // synthetic test identity; never a hardware default
        // Insert an independently assembled instance prefix + shape before the first Amp record.
        int start = 692, length = (23 + 10 * 4) * 2;
        var changed = new byte[body.Length + length]; body.AsSpan(0, start).CopyTo(changed); body.AsSpan(start).CopyTo(changed.AsSpan(start + length));
        BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(start), id);
        BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(start + 42), 10);
        BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(start + 44), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(0x104 + (row - 1) * 4), id);
        Changed = Fixtures.FromBody(d, Base.Image.Slot, changed);
        Plan = BlockInsertionPlan.Create(d, Base.Sysex, PresetBody.Sha256(Base.Sysex), row, column, id, 3);
        Saved = Base; Live = Base; ReportedSlot = Base.Image.Slot;
        Midi.Reply = (q, midi) =>
        {
            switch (q[5])
            {
                case 0x0d:
                    byte[] p = new byte[34]; p[0] = (byte)(ReportedSlot & 127); p[1] = (byte)(ReportedSlot >> 7);
                    Encoding.ASCII.GetBytes(Live.Image.Name).CopyTo(p, 2); midi.Emit(SysexProtocol.Frame(d.ModelByte, 0x0d, p)); break;
                case 0x0c: midi.Emit(SysexProtocol.Frame(d.ModelByte, 0x0c, 2)); break;
                case 3: midi.Emit((q[6] == 127 && q[7] == 127 ? Live : Saved).Sysex); break;
                case 1:
                    if (q[6] == 0x32 && ApplyInsertion) Live = Changed;
                    if (q[6] == 0x33 && ApplyRemoval) Live = Base;
                    if (q[6] == 0x26) Saved = Live;
                    if (q[6] == 0x27) { Live = Saved; ReportedSlot = Plan.Slot; }
                    break;
            }
        };
    }
}
