using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Fractal.Core;

public sealed record PresetOrganizationDonor(string Model, int Capacity, string Firmware, string Sha256, string Capture);
public sealed record PresetPlacement(int SourceSlot, int DestinationSlot);
public sealed record PresetOrganizationOperation(string Kind, int? Slot = null, int? SourceSlot = null, int? DestinationSlot = null,
    string? Name = null, bool? Overwrite = null, string? VacatedPolicy = null, int? ReplacementSlot = null,
    PresetPlacement[]? Assignments = null, PresetOrganizationDonor? Donor = null);
public sealed record PresetOrganizationRequest(int SchemaVersion, string Model, int Capacity, string Firmware,
    SavedPresetGuard[] Captures, PresetOrganizationOperation[] Operations);
public sealed record PresetOrganizationUndoRequest(int SchemaVersion, string Model, int Capacity, string Firmware, SavedPresetGuard[] Captures);

/// <summary>Immutable saved-slot organization; every donor comes from the original captured pool, never an intermediate write.</summary>
public sealed class PresetOrganizationPlan
{
    public static readonly JsonSerializerOptions JsonOptions = new(PresetEditPlan.RequestJson) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly PresetOrganizationRequest request;
    private readonly PresetEditPlan[] plans;
    private readonly object[] changes;
    public DeviceDefinition Device { get; }
    public string Firmware => request.Firmware;
    public PresetEditPlan[] Plans => plans.ToArray();
    public string PlanSha256 => PresetBody.Sha256(Encoding.UTF8.GetBytes(SnapshotExport.Json(Report())));
    public static PresetOrganizationRequest Parse(byte[] bytes)
    {
        if (bytes.Length > 5_000_000) throw new InvalidDataException("Organization request exceeds bound.");
        try { return JsonSerializer.Deserialize<PresetOrganizationRequest>(bytes, JsonOptions) ?? throw new InvalidDataException("Missing organization request."); }
        catch (JsonException e) { throw new InvalidDataException("Invalid organization JSON.", e); }
    }
    public PresetOrganizationPlan(PresetOrganizationRequest input)
    {
        request = Parse(JsonSerializer.SerializeToUtf8Bytes(input, JsonOptions));
        Device = DeviceDefinition.Create(request.Model, request.Capacity); _ = EditReadCatalogue.Capabilities(Device, Firmware);
        if (request.SchemaVersion != 1 || request.Captures == null || request.Captures.Length is < 1 or > 64 ||
            request.Captures.Any(c => c == null) || request.Captures.Select(c => c.Slot).Distinct().Count() != request.Captures.Length ||
            request.Operations == null || request.Operations.Length is < 1 or > 32 || request.Operations.Any(o => o == null))
            throw new ArgumentException("Organization needs 1–64 distinct captures and 1–32 operations.");
        request = request with { Captures = request.Captures.OrderBy(c => c.Slot).ToArray() };
        var originals = new Dictionary<int, byte[]>();
        foreach (var c in request.Captures)
        {
            Device.ValidateSlot(c.Slot); byte[] bytes = Convert.FromBase64String(c.Capture);
            if (PresetBody.Sha256(bytes) != c.Sha256 || SnapshotExport.DecodeFile(Device, bytes).Image.Slot != c.Slot)
                throw new InvalidDataException("Organization capture slot/hash differs.");
            originals.Add(c.Slot, bytes);
        }
        var used = new HashSet<int>(); var targets = new HashSet<int>(); var compiled = new List<PresetEditPlan>(); var previews = new List<object>();
        byte[] Original(int? slot)
        {
            if (slot == null) throw new ArgumentException("Explicit source/destination slot required.");
            Device.ValidateSlot(slot.Value); used.Add(slot.Value);
            return originals.GetValueOrDefault(slot.Value) ?? throw new InvalidDataException("Required source/destination capture is absent.");
        }
        void Add(int? destination, byte[] donor, string kind, int? source, string? name, bool overwrite)
        {
            byte[] baseline = Original(destination); int slot = destination!.Value;
            if (!targets.Add(slot)) throw new ArgumentException("A destination may be assigned only once; use the copy/import name field for a combined rename.");
            var operation = kind == "rename" ? new PresetEditOperation("rename-preset", Name: name) :
                new PresetEditOperation("copy-preset", Name: name, TemplateCapture: Convert.ToBase64String(donor), TemplateSha256: PresetBody.Sha256(donor), TemplateFirmware: Firmware);
            var plan = PresetEditPlan.Create(new(1, Device.Model, Device.PresetCapacity, Firmware, slot, "save", PresetBody.Sha256(baseline), [operation]), baseline);
            if (plan.Changed && kind is "copy" or "move-destination" or "import" && !overwrite)
                throw new InvalidDataException("Changed copy/move/import destination requires overwrite=true; original is retained regardless of its apparent emptiness.");
            if (plan.Changed) _ = new PresetEditExecution(plan); // qualify temporary-upload framing before publishing an executable plan
            compiled.Add(plan);
            previews.Add(new { kind, source_slot = source, destination_slot = slot, before_name = PresetFile.Inspect(Device, baseline).Name,
                after_name = PresetFile.Inspect(Device, plan.Expected).Name, changed = plan.Changed, original_sha256 = plan.BaseSha256,
                expected_sha256 = plan.ExpectedSha256, before_saved_scene = plan.BeforeScene, after_saved_scene = plan.SavedScene,
                full_original_retained = true, external_references_rewritten = false });
        }
        foreach (var op in request.Operations)
        {
            string[] fields = op.Kind switch
            {
                "rename" => ["slot", "name"], "copy" => ["source_slot", "destination_slot", "name", "overwrite"],
                "move" => ["source_slot", "destination_slot", "name", "overwrite", "vacated_policy", "replacement_slot"],
                "swap" => ["source_slot", "destination_slot"], "reorder" => ["assignments"],
                "import" => ["destination_slot", "name", "overwrite", "donor"], _ => throw new ArgumentException("Unknown organization kind.")
            };
            foreach (var f in JsonSerializer.SerializeToElement(op, JsonOptions).EnumerateObject())
                if (f.Name != "kind" && !fields.Contains(f.Name)) throw new ArgumentException("Unexpected organization field: " + f.Name);
            switch (op.Kind)
            {
                case "rename":
                    if (op.Name == null) throw new ArgumentException("Explicit preset name required.");
                    Add(op.Slot, Original(op.Slot), "rename", op.Slot, op.Name, true); break;
                case "copy": Add(op.DestinationSlot, Original(op.SourceSlot), "copy", op.SourceSlot, op.Name, op.Overwrite == true); break;
                case "move":
                    if (op.SourceSlot == op.DestinationSlot || op.ReplacementSlot == op.SourceSlot || op.VacatedPolicy != "replace-from-capture")
                        throw new ArgumentException("Move needs distinct slots and vacated_policy=replace-from-capture with an explicit other replacement slot.");
                    Add(op.DestinationSlot, Original(op.SourceSlot), "move-destination", op.SourceSlot, op.Name, op.Overwrite == true);
                    Add(op.SourceSlot, Original(op.ReplacementSlot), "move-vacated-source", op.ReplacementSlot, null, true); break;
                case "swap":
                    if (op.SourceSlot == op.DestinationSlot) throw new ArgumentException("Swap needs distinct slots.");
                    Add(op.DestinationSlot, Original(op.SourceSlot), "swap", op.SourceSlot, null, true);
                    Add(op.SourceSlot, Original(op.DestinationSlot), "swap", op.DestinationSlot, null, true); break;
                case "reorder":
                    if (op.Assignments == null || op.Assignments.Length is < 1 or > 32 || op.Assignments.Any(a => a == null) ||
                        op.Assignments.Select(a => a.SourceSlot).Distinct().Count() != op.Assignments.Length ||
                        op.Assignments.Select(a => a.DestinationSlot).Distinct().Count() != op.Assignments.Length ||
                        !op.Assignments.Select(a => a.SourceSlot).ToHashSet().SetEquals(op.Assignments.Select(a => a.DestinationSlot)))
                        throw new ArgumentException("Reordering must be a distinct closed permutation of 1–32 captured slots.");
                    foreach (var a in op.Assignments) Add(a.DestinationSlot, Original(a.SourceSlot), "reorder", a.SourceSlot, null, true);
                    break;
                case "import":
                    var donor = op.Donor ?? throw new ArgumentException("Explicit donor context/hash required.");
                    if (donor.Model != Device.Model || donor.Capacity != Device.PresetCapacity || donor.Firmware != Firmware)
                        throw new InvalidDataException("Imports require the same declared model/capacity/firmware; no conversion is performed.");
                    byte[] bytes = Convert.FromBase64String(donor.Capture);
                    if (PresetBody.Sha256(bytes) != donor.Sha256) throw new InvalidDataException("Import donor hash differs.");
                    _ = PresetFile.Inspect(Device, bytes); Add(op.DestinationSlot, bytes, "import", null, op.Name, op.Overwrite == true); break;
            }
        }
        if (compiled.Count is < 1 or > 32 || used.Count != originals.Count) throw new ArgumentException("At most 32 destinations; provide exactly the referenced captured slots.");
        plans = compiled.ToArray(); changes = previews.ToArray();
    }
    public object Report() => new { schema_version = 1, protocol_release = ProtocolRelease.Version, model = Device.Model,
        request_sha256 = PresetBody.Sha256(JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions)),
        capacity = Device.PresetCapacity, firmware = Firmware, changes, guarded_slots = request.Captures.OrderBy(c => c.Slot).Select(c => new { c.Slot, c.Sha256 }),
        changed_count = plans.Count(p => p.Changed), execution = "ordered ordinary saved-edit batch; not atomic",
        source_semantics = "All assignments use immutable original captures, including overlapping copies and permutations.",
        final_selection = "last changed destination; no-op batches preserve selection and loaded context",
        default_scene = "rename preserves original; copy/import/relocation preserves donor default scene",
        references = "Songs, footswitches, MIDI mappings, user-cab slots and system settings are not rewritten or backed up by preset organization.",
        provenance = "Imported firmware/capacity labels remain caller-declared; byte integrity does not establish hardware origin.",
        automatic_retry = false, automatic_rollback = false, hardware_qualified = false, ports_opened = false };
    public PresetEditBatch BuildBatch(LiveControlContext context, bool discardChanges = false) => new(context, plans, discardChanges, request.Captures);
    public void WriteBundle(string directory)
    {
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Choose a new organization directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "request.json"), JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions));
        File.WriteAllText(Path.Combine(directory, "plan.json"), SnapshotExport.Json(Report()));
    }
    public static PresetOrganizationPlan Open(string directory, string expectedHash)
    {
        string report = Path.Combine(directory, "plan.json"), input = Path.Combine(directory, "request.json");
        foreach (var p in new[] { report, input })
            if (new FileInfo(p).Length > 5_000_000 || (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid organization bundle file.");
        byte[] bytes = File.ReadAllBytes(report);
        if (PresetBody.Sha256(bytes) != expectedHash) throw new InvalidDataException("Organization plan hash differs.");
        var plan = new PresetOrganizationPlan(Parse(File.ReadAllBytes(input)));
        if (!bytes.SequenceEqual(Encoding.UTF8.GetBytes(SnapshotExport.Json(plan.Report())))) throw new InvalidDataException("Organization report differs from its revalidated request.");
        return plan;
    }
    public static object PlanUndo(string receiptDirectory, string progressHash, PresetOrganizationUndoRequest current, string destination)
    {
        string progressFile = Path.Combine(receiptDirectory, "progress.json");
        if (new FileInfo(progressFile).Length > 1_000_000 || (File.GetAttributes(progressFile) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid organization progress receipt.");
        byte[] receipt = File.ReadAllBytes(progressFile);
        if (PresetBody.Sha256(receipt) != progressHash) throw new InvalidDataException("Progress receipt hash differs.");
        using var doc = JsonDocument.Parse(receipt); var root = doc.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 1 || root.GetProperty("protocol_release").GetString() != ProtocolRelease.Version ||
            root.GetProperty("status").GetString() is not ("complete" or "stopped-uncertain")) throw new InvalidDataException("Use a current-release completed or stopped organization receipt.");
        var batch = PresetEditBatch.Open(Path.Combine(receiptDirectory, "batch"), root.GetProperty("batch_sha256").GetString()!);
        if (current.SchemaVersion != 1 || current.Model != batch.Device.Model || current.Capacity != batch.Device.PresetCapacity || current.Firmware != batch.Firmware ||
            batch.Plans.Any(p => !p.OrganizationOnly) || current.Captures == null || current.Captures.Any(c => c == null) ||
            current.Captures.Select(c => c.Slot).Distinct().Count() != current.Captures.Length) throw new InvalidDataException("Undo context or organization receipt differs.");
        var operations = new List<PresetOrganizationOperation>(); var evidence = new List<object>(); var eligible = new List<SavedPresetGuard>();
        var completed = root.GetProperty("completed"); var originalPlans = batch.Plans;
        if (completed.ValueKind != JsonValueKind.Array || completed.GetArrayLength() > originalPlans.Length) throw new InvalidDataException("Invalid completed-target list.");
        for (int i = completed.GetArrayLength() - 1; i >= 0; i--)
        {
            var entry = completed[i]; var p = originalPlans[i];
            if (!entry.GetProperty("complete").GetBoolean() || entry.GetProperty("slot").GetInt32() != p.Slot) throw new InvalidDataException("Completed target differs from batch order.");
            if (!p.Changed) continue;
            string item = Path.Combine(receiptDirectory, $"item-{i:D3}");
            string itemHash = PresetBody.Sha256(File.ReadAllBytes(Path.Combine(item, "attempt.json")));
            var proof = PresetEditExecution.PrepareUndo(item, itemHash);
            if (proof.Plan.PlanSha256 != p.PlanSha256 || entry.GetProperty("plan_sha256").GetString() != p.PlanSha256)
                throw new InvalidDataException("Completed item plan differs from the organization receipt.");
            string observed = PresetBody.Sha256(File.ReadAllBytes(Path.Combine(item, "verified-reloaded.syx")));
            if (entry.GetProperty("observed_result_sha256").GetString() != observed) throw new InvalidDataException("Completed observed-result hash differs.");
            var capture = current.Captures.SingleOrDefault(c => c.Slot == p.Slot) ?? throw new InvalidDataException("Current capture missing for an eligible completed target.");
            if (capture.Sha256 != observed && capture.Sha256 != p.BaseSha256) throw new InvalidDataException("A completed target has later changes; undo is refused.");
            eligible.Add(capture);
            operations.Add(new("import", DestinationSlot: p.Slot, Overwrite: true,
                Donor: new(batch.Device.Model, batch.Device.PresetCapacity, batch.Firmware, p.BaseSha256, Convert.ToBase64String(p.Baseline))));
            evidence.Add(new { slot = p.Slot, item_receipt_sha256 = itemHash, original_sha256 = p.BaseSha256, observed_result_sha256 = observed,
                current_sha256 = capture.Sha256, already_original = capture.Sha256 == p.BaseSha256 });
        }
        if (operations.Count == 0 || eligible.Count != current.Captures.Length) throw new InvalidDataException("Provide exactly the completed changed targets; uncertain and untouched targets are ineligible.");
        var inverse = new PresetOrganizationPlan(new(1, current.Model, current.Capacity, current.Firmware, eligible.ToArray(), operations.ToArray()));
        inverse.WriteBundle(destination);
        File.WriteAllText(Path.Combine(destination, "undo-provenance.json"), SnapshotExport.Json(new { progress_sha256 = progressHash, targets = evidence,
            eligibility = "Only fully verified completed changed targets; original/result current hash required; fresh execution review still required." }));
        return new { plan = inverse.Report(), plan_sha256 = inverse.PlanSha256, progress_sha256 = progressHash, eligible_count = eligible.Count, ports_opened = false };
    }
}
