using System.Buffers.Binary;
using Fractal.Core;

static class GridIsolationChecks
{
    public static Task Run(DeviceDefinition d)
    {
        var fixture = new InsertionFixture(d);
        int Cell(int row, int col) => 0x104 + ((col - 1) * d.GridRows + row - 1) * 4;
        void Put(byte[] b, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(offset), (ushort)value);
        var before = fixture.Base.Image.Body.ToArray(); var after = fixture.Changed.Image.Body.ToArray();
        Put(after, Cell(2, 1), 0); Put(after, Cell(2, 3), 114);
        foreach (byte[] body in new[] { before, after })
        {
            Put(body, Cell(1, 2) + 2, 1); // Another destination's input does not connect the target.
            Put(body, Cell(1, 4) + 2, 1); // Outgoing from row 1, not target row 2.
        }
        var baseline = Fixtures.FromBody(d, 0, before); var inserted = Fixtures.FromBody(d, 0, after);
        _ = BlockInsertionPlan.Create(d, baseline.Sysex, PresetBody.Sha256(baseline.Sysex), 2, 3, 114, 3);
        _ = BlockRemovalPlan.Create(d, inserted.Sysex, PresetBody.Sha256(inserted.Sysex), 2, 3, 114, 3);
        foreach (var (offset, value) in new[] { (Cell(2, 3) + 2, 1), (Cell(1, 4) + 2, 2), (Cell(1, 4) + 2, 32768) })
        {
            byte[] b = before.ToArray(), a = after.ToArray(); Put(b, offset, value); Put(a, offset, value);
            var saved = Fixtures.FromBody(d, 0, b); var result = Fixtures.FromBody(d, 0, a);
            Reject(() => BlockInsertionPlan.Create(d, saved.Sysex, PresetBody.Sha256(saved.Sysex), 2, 3, 114, 3));
            Reject(() => BlockRemovalPlan.Create(d, result.Sysex, PresetBody.Sha256(result.Sysex), 2, 3, 114, 3));
        }
        return Task.CompletedTask;
    }
    static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Connected or unknown target routing accepted."); }
}
