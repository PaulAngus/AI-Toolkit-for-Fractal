using System.Diagnostics;
using Fractal.Core;

// Real private files and Windows file handles only. No MIDI transport is constructed.
static class InventoryFileChecks
{
    public static IEnumerable<(string Name, Action Test)> Cases()
    {
        yield return ("inventory atomic create and replace retain complete bytes", () => InDirectory(root =>
        {
            string path = Path.Combine(root, "inventory.json");
            PresetInventory.AtomicWrite(path, "original"u8.ToArray(), false);
            PresetInventory.AtomicWrite(path, "replacement"u8.ToArray(), true);
            Need(File.ReadAllText(path) == "replacement", "Replacement bytes differ.");
            NoTemporaryFiles(root);
        }));
        yield return ("inventory capture collision never overwrites existing evidence", () => InDirectory(root =>
        {
            string path = Path.Combine(root, "capture.syx");
            File.WriteAllText(path, "original");
            try { PresetInventory.AtomicWrite(path, "replacement"u8.ToArray(), false); throw new Exception("Capture was overwritten."); }
            catch (IOException) { }
            Need(File.ReadAllText(path) == "original", "Collision changed the original.");
            NoTemporaryFiles(root);
        }));
        if (!OperatingSystem.IsWindows()) yield break;
        yield return ("inventory checkpoint survives a real temporary Windows reader lock", () => InDirectory(root =>
        {
            string path = Path.Combine(root, "inventory.json"); File.WriteAllText(path, "original");
            using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Exception? failure = null;
            using var entered = new ManualResetEventSlim();
            var writer = new Thread(() =>
            {
                entered.Set();
                try { PresetInventory.AtomicWrite(path, "replacement"u8.ToArray(), true); }
                catch (Exception error) { failure = error; }
            });
            writer.Start();
            try
            {
                Need(entered.Wait(TimeSpan.FromSeconds(5)), "Writer did not start.");
                Need(SpinWait.SpinUntil(() => Directory.GetFiles(root, "*.tmp").Length > 0 || !writer.IsAlive, TimeSpan.FromSeconds(5)), "Writer did not stage its file.");
                Thread.Sleep(120);
                bool waitedForReader = writer.IsAlive;
                reader.Dispose();
                Need(writer.Join(TimeSpan.FromSeconds(5)), "Rename did not finish after releasing reader.");
                if (failure != null) throw new Exception("A temporary checkpoint reader aborted the write.", failure);
                Need(waitedForReader && File.ReadAllText(path) == "replacement", "Writer did not wait for the lock or replace complete bytes.");
                NoTemporaryFiles(root);
            }
            finally { reader.Dispose(); writer.Join(); }
        }));
        yield return ("inventory persistent Windows lock fails within a bound and preserves checkpoint", () => InDirectory(root =>
        {
            string path = Path.Combine(root, "inventory.json"); File.WriteAllText(path, "original");
            using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var elapsed = Stopwatch.StartNew();
            try { PresetInventory.AtomicWrite(path, "replacement"u8.ToArray(), true); throw new Exception("Persistent lock was ignored."); }
            catch (UnauthorizedAccessException error)
            {
                Need(error.Message.Contains("6 attempts", StringComparison.Ordinal) && error.InnerException?.HResult == unchecked((int)0x80070005), "Missing final attempt/native-error diagnostics.");
            }
            Need(elapsed.Elapsed < TimeSpan.FromSeconds(5), "Retry bound exceeded.");
            Need(File.ReadAllText(path) == "original", "Persistent failure changed the checkpoint.");
            NoTemporaryFiles(root);
        }));
        yield return ("inventory read-only Windows checkpoint is refused immediately without clearing attributes", () => InDirectory(root =>
        {
            string path = Path.Combine(root, "inventory.json"); File.WriteAllText(path, "original");
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                try { PresetInventory.AtomicWrite(path, "replacement"u8.ToArray(), true); throw new Exception("Read-only file was overwritten."); }
                catch (UnauthorizedAccessException error) { Need(error.Message.Contains("1 attempt", StringComparison.Ordinal), "Permanent read-only denial was retried."); }
                Need(File.ReadAllText(path) == "original" && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly), "Read-only checkpoint or attributes changed.");
                NoTemporaryFiles(root);
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        }));
        yield return ("inventory destination directory is refused without changing its contents", () => InDirectory(root =>
        {
            string path = Path.Combine(root, "inventory.json"); Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "retained"), "original");
            try { PresetInventory.AtomicWrite(path, "replacement"u8.ToArray(), true); throw new Exception("Directory was overwritten."); }
            catch (UnauthorizedAccessException error) { Need(error.Message.Contains("1 attempt", StringComparison.Ordinal), "Directory collision was retried."); }
            Need(File.ReadAllText(Path.Combine(path, "retained")) == "original", "Directory contents changed.");
            NoTemporaryFiles(root);
        }));
    }
    private static void InDirectory(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "fractal-checkpoint-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root); }
        finally { Directory.Delete(root, true); }
    }
    private static void NoTemporaryFiles(string root) => Need(Directory.GetFiles(root, "*.tmp").Length == 0, "Staged temporary file leaked.");
    private static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
}
