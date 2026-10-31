using System.Buffers.Binary;
using System.Text;
using Fractal.Core;

// Synthetic populated sound preset with an existing, relocated Scene MIDI block and opaque inactive fields.
sealed class SceneMidiEditFixture
{
    public DeviceDefinition Device { get; }
    public SavedCapture Base { get; }
    public SavedCapture Saved,Live;
    public SceneMidiEditPlan Plan { get; }
    public FakeEditorMidi Midi { get; }=new();
    public DeviceContext Context=>new(Device,"99.07","synthetic");
    public int ReportedSlot,ReportedScene=3,ExtraWord=123;
    public bool ApplyField=true,ApplyUpload=true;
    public SceneMidiEditFixture(DeviceDefinition d,int? slot=null)
    {
        Device=d;int target=slot??d.PresetCapacity-1;
        var initial=Fixtures.Preset(d,target,midi:true);var body=initial.Image.Body.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(body,2);
        var record=PresetBody.Decode(d,initial.Image).BlockRecords.Single(r=>r.Columns==257&&r.Rows==1);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(record.Offset-12),190);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0x104),0);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0x104+(d.GridRows+1)*4),190);
        Base=Fixtures.FromBody(d,target,body);Saved=Base;Live=Base;ReportedSlot=target;
        Plan=SceneMidiEditPlan.Create(d,"99.07",Base.Sysex,PresetBody.Sha256(Base.Sysex),
            [new(2,3,"PC",9,42),new(8,2,"CC",5,126,119)]);
        Midi.Reply=Reply;
    }
    private void Reply(byte[] q,FakeEditorMidi midi)
    {
        switch(q[5])
        {
            case 0:midi.Emit(SysexProtocol.Frame(Device.ModelByte,0x64,0,0));break;
            case 8:midi.Emit(SysexProtocol.Frame(Device.ModelByte,8,99,7));break;
            case 0x0d:
                byte[] p=new byte[34];p[0]=(byte)(ReportedSlot&127);p[1]=(byte)(ReportedSlot>>7);
                Encoding.ASCII.GetBytes(Live.Image.Name).CopyTo(p,2);midi.Emit(SysexProtocol.Frame(Device.ModelByte,0x0d,p));break;
            case 0x0c:midi.Emit(SysexProtocol.Frame(Device.ModelByte,0x0c,(byte)(ReportedScene-1)));break;
            case 3:midi.Emit((q[6]==127&&q[7]==127?Live:Saved).Sysex);break;
            case 0x1f:
                var fields=SceneMidiTable.SavedFields(Device,PresetBody.Decode(Device,Live.Image).BlockRecords);
                var frames=Fixtures.LiveTable(Device,fields);
                frames[2]=SysexProtocol.Frame(Device.ModelByte,0x75,1,0,(byte)(ExtraWord&127),(byte)(ExtraWord>>7),0);
                foreach(var frame in frames)midi.Emit(frame);break;
            case 0x79:if(ApplyUpload)Live=Base;break;
            case 1:
                if(q[6]==9&&ApplyField)
                {
                    int parameter=q[10]|q[11]<<7;
                    uint bits=0;for(int j=0;j<5;j++)bits|=(uint)q[12+j]<<(7*j);
                    int value=(int)BitConverter.UInt32BitsToSingle(bits);
                    var record=PresetBody.Decode(Device,Live.Image).BlockRecords.Single(r=>r.Columns==257&&r.Rows==1);
                    var body=Live.Image.Body.ToArray();BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(record.Offset+34+parameter*2),(ushort)value);
                    Live=Fixtures.FromBody(Device,Base.Image.Slot,body);
                }
                if(q[6]==0x26)Saved=Live;
                if(q[6]==0x27){Live=Saved;ReportedSlot=Base.Image.Slot;ReportedScene=3;}
                break;
        }
    }
}
