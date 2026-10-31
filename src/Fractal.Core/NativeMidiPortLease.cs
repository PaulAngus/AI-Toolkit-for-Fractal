namespace Fractal.Core;

/// <summary>Process-wide and cross-process ownership. Files are never unlinked while others may hold them.</summary>
internal sealed class NativeMidiPortLease : IDisposable
{
    private static readonly object Gate = new();
    // Root active handles even after failed native cleanup; only successful cleanup releases them.
    private static readonly Dictionary<string, FileStream> Owned = new(StringComparer.Ordinal);
    private readonly List<string> paths = [];
    internal static NativeMidiPortLease Acquire(int input, int? output, string? directory = null)
    {
        if (input < 0 || output is < 0) throw new ArgumentOutOfRangeException(nameof(input));
        directory = Path.GetFullPath(directory ?? Path.Combine(Path.GetTempPath(), "fractal-toolkit-coremidi-locks"));
        Directory.CreateDirectory(directory);
        var lease = new NativeMidiPortLease();
        try
        {
            lease.Take(Path.Combine(directory, $"input-{input}.lock"));
            if (output is int selected) lease.Take(Path.Combine(directory, $"output-{selected}.lock"));
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    private void Take(string path)
    {
        lock (Gate)
        {
            if (Owned.ContainsKey(path)) throw new IOException("Another toolkit session owns this CoreMIDI port.");
            var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Owned.Add(path, file); paths.Add(path);
        }
    }
    public void Dispose()
    {
        lock (Gate)
        {
            foreach (string path in paths) { Owned[path].Dispose(); Owned.Remove(path); }
            paths.Clear();
        }
    }
}
