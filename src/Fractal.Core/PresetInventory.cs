using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fractal.Core;

/// <summary>A private, resumable saved-read journal. No device mutation or identity inference.</summary>
public sealed class PresetInventory : IDisposable
{
    private readonly string root;
    private readonly FileStream lease;
    private InventoryJournal journal;
    private readonly DeviceDefinition device;
    private bool disposed;
    private int running;
    private static readonly int[] RenameRetryDelays = [25, 50, 100, 200, 400];
    private PresetInventory(string root, FileStream lease, InventoryJournal journal)
    { this.root = root; this.lease = lease; this.journal = journal; device = DeviceDefinition.Create(journal.Model, journal.Capacity); }

    public static PresetInventory Create(string directory, DeviceContext context, IEnumerable<int> slots)
    {
        ValidateContext(context);
        int[] selected = slots.Order().ToArray();
        if (selected.Length == 0 || selected.Distinct().Count() != selected.Length) throw new ArgumentException("Select distinct inventory slots.");
        foreach (int slot in selected) context.Definition.ValidateSlot(slot);
        string root = Path.GetFullPath(directory);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Inventory destination already exists; use resume or choose a new directory.");
        Directory.CreateDirectory(root);
        FileStream lease = Lock(root);
        try
        {
            if (File.Exists(Path.Combine(root, "inventory.json"))) throw new IOException("Inventory checkpoint already exists.");
            var journal = new InventoryJournal(1, ProtocolRelease.Version, context.Definition.Model,
                context.Definition.PresetCapacity, context.Firmware, context.CapacityEvidence, selected, [], []);
            var store = new PresetInventory(root, lease, journal);
            store.Save(); return store;
        }
        catch { lease.Dispose(); throw; }
    }

    public static PresetInventory Open(string directory) => Open(directory, false);
    private static PresetInventory Open(string directory, bool migration)
    {
        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Inventory directory does not exist.");
        RejectLink(root);
        FileStream lease = Lock(root);
        try
        {
            string path = Path.Combine(root, "inventory.json");
            RejectLink(path);
            if (new FileInfo(path).Length > 4_000_000) throw new InvalidDataException("Inventory checkpoint exceeds size bound.");
            InventoryJournal journal;
            try { journal = JsonSerializer.Deserialize<InventoryJournal>(File.ReadAllText(path), SnapshotExport.JsonOptions)
                ?? throw new InvalidDataException("Missing inventory checkpoint."); }
            catch (JsonException e) { throw new InvalidDataException("Malformed inventory checkpoint.", e); }
            ValidateJournal(journal, migration);
            var store = new PresetInventory(root, lease, journal);
            foreach (var entry in journal.Captures) _ = store.Read(entry);
            return store;
        }
        catch { lease.Dispose(); throw; }
    }

    public DeviceContext Context => new(device, journal.Firmware, journal.CapacityEvidence);
    public static object Migrate(string source, string destination)
    {
        using var old = Open(source, true);
        using var migrated = Create(destination, old.Context, old.journal.Slots);
        foreach (var entry in old.journal.Captures)
            AtomicWrite(migrated.CapturePath(entry), old.Read(entry).Sysex, false);
        migrated.journal = old.journal with { ProtocolRelease = ProtocolRelease.Version };
        migrated.Save();
        var provenance = new { source_protocol_release = old.journal.ProtocolRelease, target_protocol_release = ProtocolRelease.Version,
            source_checkpoint_sha256 = PresetBody.Sha256(File.ReadAllBytes(Path.Combine(old.root, "inventory.json"))),
            original_unchanged = true, capture_count = old.journal.Captures.Length, ports_opened = false, refreshed = false };
        AtomicWrite(Path.Combine(migrated.root, "migration.json"), System.Text.Encoding.UTF8.GetBytes(SnapshotExport.Json(provenance)), false);
        return provenance;
    }
    public bool Complete => journal.Captures.Length == journal.Slots.Length && journal.Attempts.LastOrDefault()?.Outcome == "complete";
    public int[] RequestedSlots => journal.Slots.ToArray();
    public SavedCapture[] ReadCaptures()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Volatile.Read(ref running) != 0) throw new InvalidOperationException("Wait for the running scan before analysis.");
        return journal.Captures.OrderBy(c => c.Slot).Select(Read).ToArray();
    }

    /// <summary>Resume rechecks every retained raw image before reading missing slots. Failure stops the session.</summary>
    public async Task RunAsync(ReadSession session, DeviceContext identified, TimeSpan timeout, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateContext(identified);
        if (identified.Definition != device || identified.Firmware != journal.Firmware || identified.CapacityEvidence != journal.CapacityEvidence)
            throw new InvalidDataException("Inventory model, capacity, firmware or capacity evidence differs from the connected context.");
        if (Complete) throw new InvalidOperationException("Completed inventories are immutable; start a new scan for fresh evidence.");
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("Inventory scan already running.");
        try
        {
            if (journal.Attempts.Length >= 1024) throw new InvalidOperationException("Inventory attempt limit reached; retain this journal and start a new scan.");
            // A started attempt from another process is historical interrupted evidence, never success.
            var attempt = new InventoryAttempt(DateTimeOffset.UtcNow, null, "started", "state-before", null, null, null, 0, null);
            journal = journal with { Attempts = [.. journal.Attempts, attempt] }; Save();
            try
            {
                attempt = attempt with { Before = await session.CurrentAsync(identified, timeout, token) }; Checkpoint(attempt);
                foreach (var entry in journal.Captures.OrderBy(e => e.Slot))
                {
                    attempt = attempt with { Phase = "revalidate", Slot = entry.Slot }; Checkpoint(attempt);
                    var old = Read(entry);
                    var fresh = await session.SavedAsync(identified, entry.Slot, timeout, token);
                    var checkedFresh = SnapshotExport.DecodeFile(device, fresh.Sysex);
                    if (checkedFresh.Image.Slot != entry.Slot || !checkedFresh.Image.RawImage.SequenceEqual(old.Image.RawImage))
                        throw new InvalidDataException("Retained saved contents changed; keep this partial inventory and start a new scan.");
                    attempt = attempt with { Revalidated = attempt.Revalidated + 1 }; Checkpoint(attempt);
                    await Task.Delay(10, token); // Toolkit pacing policy, not a manufacturer requirement.
                }
                foreach (int slot in journal.Slots.Except(journal.Captures.Select(e => e.Slot)))
                {
                    attempt = attempt with { Phase = "capture", Slot = slot }; Checkpoint(attempt);
                    var captured = await session.SavedAsync(identified, slot, timeout, token);
                    Retain(slot, captured);
                    await Task.Delay(10, token);
                }
                attempt = attempt with { Phase = "state-after", Slot = null }; Checkpoint(attempt);
                attempt = attempt with { After = await session.CurrentAsync(identified, timeout, token) };
                if (attempt.Before != attempt.After) throw new InvalidDataException("Selection drift observed; inventory remains partial.");
                attempt = attempt with { Outcome = "complete", EndedUtc = DateTimeOffset.UtcNow }; Checkpoint(attempt);
            }
            catch (Exception error)
            {
                attempt = attempt with { Outcome = error is OperationCanceledException ? "cancelled" : "failed",
                    EndedUtc = DateTimeOffset.UtcNow, Error = error.Message };
                Checkpoint(attempt); throw;
            }
        }
        finally { Volatile.Write(ref running, 0); }
    }

    public object Inspect(int offset = 0, int? limit = null)
    {
        if (offset < 0 || limit < 0 || offset > device.PresetCapacity || limit > device.PresetCapacity) throw new ArgumentException("Invalid inventory page bounds.");
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Volatile.Read(ref running) != 0) throw new InvalidOperationException("Wait for the running scan before inspection.");
        var captures = journal.Captures.ToDictionary(e => e.Slot);
        var entries = journal.Slots.Select(slot =>
        {
            PresetSnapshot? preset = null; string? decodeError = null;
            captures.TryGetValue(slot, out var entry);
            if (entry != null)
            {
                var capture = Read(entry);
                try { preset = PresetBody.Decode(device, capture.Image); }
                catch (InvalidDataException e) { decodeError = e.Message; }
            }
            var lastCaptureAttempt = journal.Attempts.LastOrDefault(a => a.Slot == slot && a.Phase == "capture");
            string status = entry != null ? "captured" : lastCaptureAttempt == null ? "unattempted" :
                lastCaptureAttempt.Outcome == "started" ? "interrupted" : lastCaptureAttempt.Outcome;
            return new { slot, status, capture = entry, preset, decode_error = decodeError };
        }).ToArray();
        return new { schema_version = 1, protocol_release = journal.ProtocolRelease, model = journal.Model,
            firmware = journal.Firmware, capacity = journal.Capacity, capacity_evidence = journal.CapacityEvidence,
            status = Complete ? "complete" : "partial", scope = journal.Slots.Length == device.PresetCapacity ? "full-capacity" : "selected-slots",
            capture_complete = Complete, decode_complete = Complete && entries.All(e => e.decode_error == null),
            captured_slots = captures.Count, requested_slots = journal.Slots.Length,
            attempts = journal.Attempts, entries = entries.Skip(offset).Take(limit ?? entries.Length).ToArray(),
            offset, entry_count = entries.Length, next_offset = offset + (limit ?? entries.Length) < entries.Length ? (int?)(offset + (limit ?? entries.Length)) : null,
            ports_opened = false, restore_verified = false,
            physical_identity = "not-established", consistency = "independent-saved-reads; not an atomic snapshot",
            freshness = "capture timestamps retained; resume revalidates all retained raw images before missing reads" };
    }

    public ExportResult Export(string destination)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Volatile.Read(ref running) != 0) throw new InvalidOperationException("Wait for the running scan before export.");
        if (!Complete) throw new InvalidOperationException("Partial inventories cannot be exported as completed snapshots.");
        // This is an offline projection of retained captures, not a newly connected read.
        return SnapshotExport.Write(destination, Context, journal.Captures.Select(Read).ToArray());
    }

    private void Retain(int slot, SavedCapture supplied)
    {
        var capture = SnapshotExport.DecodeFile(device, supplied.Sysex);
        if (capture.Image.Slot != slot) throw new InvalidDataException("Inventory capture address mismatch.");
        string wire = PresetBody.Sha256(capture.Sysex);
        var entry = new InventoryCapture(slot, wire, PresetBody.Sha256(capture.Image.RawImage),
            PresetBody.Sha256(capture.Image.Body), DateTimeOffset.UtcNow);
        string path = CapturePath(entry);
        if (File.Exists(path))
        {
            // An orphan from a crash before checkpointing is reusable only after this fresh read and exact comparison.
            if (!ReadBounded(path).SequenceEqual(capture.Sysex)) throw new InvalidDataException("Existing capture differs from fresh data.");
        }
        else AtomicWrite(path, capture.Sysex, false);
        journal = journal with { Captures = [.. journal.Captures, entry] }; Save();
    }
    private SavedCapture Read(InventoryCapture entry)
    {
        byte[] bytes = ReadBounded(CapturePath(entry));
        if (PresetBody.Sha256(bytes) != entry.SysexSha256) throw new InvalidDataException("Inventory capture hash mismatch.");
        var capture = SnapshotExport.DecodeFile(device, bytes);
        if (capture.Image.Slot != entry.Slot || PresetBody.Sha256(capture.Image.RawImage) != entry.RawSha256 ||
            PresetBody.Sha256(capture.Image.Body) != entry.BodySha256) throw new InvalidDataException("Inventory capture metadata mismatch.");
        return capture;
    }
    private string CapturePath(InventoryCapture entry) => Path.Combine(root, $"preset-{entry.Slot:D4}-{entry.SysexSha256}.syx");
    private static byte[] ReadBounded(string path)
    {
        RejectLink(path);
        if (new FileInfo(path).Length > 60000) throw new InvalidDataException("Capture exceeds single-preset size bound.");
        return File.ReadAllBytes(path);
    }
    private void Checkpoint(InventoryAttempt attempt)
    { journal.Attempts[^1] = attempt; Save(); }
    private void Save() => AtomicWrite(Path.Combine(root, "inventory.json"), System.Text.Encoding.UTF8.GetBytes(SnapshotExport.Json(journal) + "\n"), true);
    internal static void AtomicWrite(string path, byte[] bytes, bool replace)
    {
        if (File.Exists(path)) RejectLink(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(true); }
            for (int attempt = 0; ; attempt++)
            {
                // Recheck links on every attempt; never fall back to truncating the old file.
                RejectLink(temporary);
                if (File.Exists(path) || Directory.Exists(path)) RejectLink(path);
                try { File.Move(temporary, path, replace); break; }
                catch (Exception error) when (OperatingSystem.IsWindows() && IsRenameContention(error))
                {
                    // Windows also reports access denied when a reader briefly prevents replacement.
                    // Retry only the rename of these already flushed bytes, never the device operation.
                    if (attempt >= RenameRetryDelays.Length || !CanRetryRename(temporary, path, replace))
                    {
                        string message = $"Atomic inventory rename failed after {attempt + 1} attempt{(attempt == 0 ? "" : "s")} (HRESULT 0x{error.HResult:X8}); checkpoint replacement did not complete.";
                        if (error is UnauthorizedAccessException) throw new UnauthorizedAccessException(message, error);
                        throw new IOException(message, error);
                    }
                    Thread.Sleep(RenameRetryDelays[attempt]);
                }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static bool IsRenameContention(Exception error) => error is IOException or UnauthorizedAccessException &&
        error.HResult is unchecked((int)0x80070005) or unchecked((int)0x80070020) or unchecked((int)0x80070021);
    private static bool CanRetryRename(string temporary, string path, bool replace)
    {
        static bool WritableRegularFile(string file)
        {
            try
            {
                var attributes = File.GetAttributes(file);
                return (attributes & (FileAttributes.ReadOnly | FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        }
        if (!WritableRegularFile(temporary) || Directory.Exists(path)) return false;
        return !File.Exists(path) || replace && WritableRegularFile(path);
    }
    private static FileStream Lock(string root)
    {
        RejectLink(root);
        string path = Path.Combine(root, ".inventory.lock");
        if (File.Exists(path)) RejectLink(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static void RejectLink(string path)
    { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Inventory files and directory must not be symbolic links."); }
    private static void ValidateContext(DeviceContext context)
    {
        if (context.Definition != DeviceDefinition.Create(context.Definition.Model, context.Definition.PresetCapacity) ||
            !Regex.IsMatch(context.Firmware, @"^\d{1,3}\.\d{2}$") || string.IsNullOrWhiteSpace(context.CapacityEvidence))
            throw new ArgumentException("Inventory requires an established device definition, capacity evidence and recorded firmware.");
    }
    private static void ValidateJournal(InventoryJournal j, bool migration = false)
    {
        bool compatible = j.ProtocolRelease == ProtocolRelease.Version || migration && j.ProtocolRelease is "0.15.0" or "0.16.0" or "0.17.0" or "0.24.0" or "0.25.0" or "0.26.0" or "0.27.0" or "0.28.0" or "0.35.0" or "0.36.0" or "0.37.0" or "0.38.0";
        if (j.SchemaVersion != 1 || !compatible || j.Model == null || j.Firmware == null ||
            j.CapacityEvidence == null || j.Slots == null || j.Captures == null || j.Attempts == null)
            throw new InvalidDataException("Unsupported or incomplete inventory checkpoint/release.");
        var d = DeviceDefinition.Create(j.Model, j.Capacity);
        ValidateContext(new(d, j.Firmware, j.CapacityEvidence));
        if (j.Slots.Length == 0 || j.Slots.Length > d.PresetCapacity || !j.Slots.SequenceEqual(j.Slots.Distinct().Order()) ||
            j.Captures.Any(e => e == null) || j.Captures.Select(e => e.Slot).Distinct().Count() != j.Captures.Length || j.Attempts.Length > 1024)
            throw new InvalidDataException("Invalid inventory slots/captures/attempts.");
        foreach (int slot in j.Slots) d.ValidateSlot(slot);
        foreach (var e in j.Captures)
            if (!j.Slots.Contains(e.Slot) || new[] { e.SysexSha256, e.RawSha256, e.BodySha256 }.Any(h => h == null || !Regex.IsMatch(h, "^[0-9a-f]{64}$")) || e.CapturedUtc == default)
                throw new InvalidDataException("Invalid inventory capture metadata.");
        foreach (var a in j.Attempts)
        {
            if (a == null || a.StartedUtc == default || a.Outcome is not ("started" or "complete" or "failed" or "cancelled") ||
                a.Phase is not ("state-before" or "revalidate" or "capture" or "state-after") || a.Revalidated < 0 || a.Revalidated > j.Captures.Length ||
                (a.Slot != null && !j.Slots.Contains(a.Slot.Value)) || (a.Outcome != "started" && a.EndedUtc == null))
                throw new InvalidDataException("Invalid inventory attempt.");
            foreach (var state in new[] { a.Before, a.After }.OfType<CurrentState>())
                if (state.Slot < 0 || state.Slot >= d.PresetCapacity || state.Scene is < 1 or > 8 || state.Name == null)
                    throw new InvalidDataException("Invalid inventory selection evidence.");
            if (a.Outcome == "complete" && (a.Before == null || a.Before != a.After || a.Phase != "state-after" || a.Slot != null || j.Captures.Length != j.Slots.Length))
                throw new InvalidDataException("Incomplete inventory labelled complete.");
        }
    }
    public void Dispose() { disposed = true; lease.Dispose(); }
}

internal sealed record InventoryJournal(int SchemaVersion, string ProtocolRelease, string Model, int Capacity,
    string Firmware, string CapacityEvidence, int[] Slots, InventoryCapture[] Captures, InventoryAttempt[] Attempts);
internal sealed record InventoryCapture(int Slot, string SysexSha256, string RawSha256, string BodySha256, DateTimeOffset CapturedUtc);
internal sealed record InventoryAttempt(DateTimeOffset StartedUtc, DateTimeOffset? EndedUtc, string Outcome, string Phase,
    int? Slot, CurrentState? Before, CurrentState? After, int Revalidated, string? Error);
