using System.Globalization;

using Fractal.Core;



return await Run(args);

static async Task<int> Run(string[] args)

{

    using var cancel = new CancellationTokenSource();

    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };

    try

    {

        if (args.Length == 0 || args[0] is "help" or "--help") { Console.WriteLine(Help); return 0; }

        TransportConnection.Configure(ref args);

        string command = args[0];

        string[] allowed = command switch

        {

            "ports" => [],

            "read-block-parameters" => ["model", "capacity", "firmware", "input", "output", "effect-id", "destination", "timeout", "validate-only"],
            "amp-roster" => ["model", "capacity", "firmware", "input", "output", "start", "count", "destination", "timeout"],

            "capture-control" => ["model", "capacity", "firmware", "input", "output", "destination", "timeout"],
            "capture-performance" => ["model", "capacity", "firmware", "input", "output", "destination", "timeout", "validate-only"],
            "performance-capabilities" => ["model", "capacity", "firmware"],

            "live-monitor" => ["model", "capacity", "firmware", "input", "output", "destination", "timeout", "samples", "interval-ms"],

            "blocks" => ["model", "firmware", "query", "effect-id"],

            "edit-capabilities" => ["model", "firmware", "capacity"],

            "edit-catalogue" => ["model", "firmware", "capacity", "family"],

            "decode-edit" => ["model", "firmware", "capacity", "capture"],

            "calibrate-parameter" => ["request", "destination"],
            "plan-edit" => ["request", "capture", "destination"],
            "template-import" => ["library", "request", "capture"],
            "template-list" => ["library", "query", "tag", "offset", "limit"],
            "template-inspect" => ["library", "entry", "expected-hash"],
            "plan-template" => ["entry-file", "entry-hash", "request", "capture", "destination"],
            "plan-organization" => ["request", "destination"],
            "plan-setlists" => ["request", "inventory", "destination"],
            "inspect-organization" => ["bundle", "expected-hash"],
            "organization-batch" => ["bundle", "expected-hash", "context", "discard-changes", "destination"],
            "plan-organization-undo" => ["receipt", "expected-hash", "request", "destination"],

            "plan-selective-scene-midi-undo" => ["receipt", "receipt-hash", "capture", "destination"],
            "plan-selective-recovery-undo" => ["recovery", "state-hash", "slot", "capture", "destination"],
            "recovery-migrate" => ["recovery", "state-hash", "destination"],

            "inspect-edit-plan" => ["bundle", "expected-hash"],

            "create-edit-batch" => ["request", "destination"],

            "route-midi" => ["request"],

            "plan-audio-path" => ["request", "capture", "destination"],

            "listen-notifications" => ["model", "capacity", "firmware", "input", "duration", "max-events", "destination", "validate-only"],

            "listen-midi" => ["request", "input", "duration", "max-events", "destination", "validate-only"],

            "inspect" => ["input", "output", "model", "capacity", "timeout"],

            "snapshot" => ["input", "output", "model", "capacity", "timeout", "preset", "all", "destination"],

            "inventory" => ["input", "output", "model", "firmware", "capacity", "timeout", "preset", "all", "destination", "resume"],

            "inventory-inspect" => ["inventory", "offset", "limit"],

            "inventory-export" => ["inventory", "destination"],

            "inventory-migrate" => ["inventory", "destination"],

            "inventory-compare" => ["before", "after"],

            "inventory-amp-usage" => ["inventory", "amp-type", "roster", "offset", "limit"],

            "scene-midi" => ["input", "output", "model", "capacity", "timeout", "destination"],

            "prepare-load" => ["input", "output", "model", "capacity", "timeout", "preset", "destination", "discard-changes", "midi-mapping"],

            "decode" => ["model", "firmware", "capacity", "capture", "destination"],

            "check-backup" => ["model", "capacity", "capture"],

            _ => throw new ArgumentException("Unknown command. Use help.")

        };

        var options = new Dictionary<string, List<string>>();

        for (int i = 1; i < args.Length; i++)

        {

            string key = args[i].StartsWith("--", StringComparison.Ordinal) ? args[i][2..] : "";

            if (!allowed.Contains(key)) throw new ArgumentException($"Unknown option: {args[i]}");

            string value = key == "all" ? "true" : ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value for --{key}");

            if (value.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Missing value for --{key}");

            if (!options.TryGetValue(key, out var values)) options[key] = values = [];

            if (values.Count > 0 && (key is not ("preset" or "capture") || command is "check-backup" or "decode-edit")) throw new ArgumentException($"Repeated --{key}");

            values.Add(value);

        }

        string? Optional(string key) => options.TryGetValue(key, out var values) ? values[0] : null;

        string Required(string key) => Optional(key) ?? throw new ArgumentException($"Required: --{key}");

        int Number(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : throw new ArgumentException("Expected a non-negative integer.");

        int? capacity = Optional("capacity") is string cap ? Number(cap) : null;
        if (command == "plan-setlists")
        {
            using var inventory = PresetInventory.Open(Required("inventory"));
            string requestFile = Required("request");
            if (new FileInfo(requestFile).Length > 512_000) throw new InvalidDataException("Setlist/song request exceeds size bound.");
            var request = SetlistSongs.Parse(File.ReadAllBytes(requestFile));
            Console.WriteLine(SnapshotExport.Json(SetlistSongs.Write(SetlistSongs.Plan(request, inventory), Required("destination"))));
            return 0;
        }
        if (command is "plan-organization" or "inspect-organization" or "organization-batch" or "plan-organization-undo")
        {
            byte[] ReadBounded(string path, int maximum)
            { if (new FileInfo(path).Length > maximum) throw new InvalidDataException("Organization input exceeds bound."); return File.ReadAllBytes(path); }
            if (command == "plan-organization-undo")
            {
                var current = System.Text.Json.JsonSerializer.Deserialize<PresetOrganizationUndoRequest>(ReadBounded(Required("request"), 5_000_000), PresetOrganizationPlan.JsonOptions)
                    ?? throw new InvalidDataException("Missing current undo captures.");
                Console.WriteLine(SnapshotExport.Json(PresetOrganizationPlan.PlanUndo(Required("receipt"), Required("expected-hash"), current, Required("destination")))); return 0;
            }
            var plan = command == "plan-organization" ? new PresetOrganizationPlan(PresetOrganizationPlan.Parse(ReadBounded(Required("request"), 5_000_000))) :
                PresetOrganizationPlan.Open(Required("bundle"), Required("expected-hash"));
            if (command == "plan-organization") plan.WriteBundle(Required("destination"));
            if (command == "organization-batch")
            {
                var organizationContext = System.Text.Json.JsonSerializer.Deserialize<LiveControlContext>(ReadBounded(Required("context"), 1_000_000), LiveControlScope.JsonOptions)
                    ?? throw new InvalidDataException("Missing retained current context.");
                bool discard = Optional("discard-changes") switch { "true" => true, null or "false" => false, _ => throw new ArgumentException("discard-changes must be true or false.") };
                var batch = plan.BuildBatch(organizationContext, discard); batch.WriteBundle(Required("destination"));
                Console.WriteLine(SnapshotExport.Json(new { plan_sha256 = plan.PlanSha256, organization = plan.Report(), batch = batch.Report(), ports_opened = false })); return 0;
            }
            Console.WriteLine(SnapshotExport.Json(new { plan_sha256 = plan.PlanSha256, plan = plan.Report(), ports_opened = false })); return 0;
        }

        if (command is "template-import" or "template-list" or "template-inspect" or "plan-template")
        {
            byte[] ReadBounded(string path, int max)
            { if (new FileInfo(path).Length > max) throw new InvalidDataException("Template input exceeds size bound."); return File.ReadAllBytes(path); }
            if (command == "template-list")
                Console.WriteLine(SnapshotExport.Json(TemplateLibrary.List(Required("library"), Optional("query"), Optional("tag"),
                    Number(Optional("offset") ?? "0"), Number(Optional("limit") ?? "20"))));
            else if (command == "template-inspect")
                Console.WriteLine(SnapshotExport.Json(TemplateLibrary.Inspect(Required("library"), Required("entry"), Required("expected-hash"))));
            else if (command == "template-import")
                Console.WriteLine(SnapshotExport.Json(TemplateLibrary.Import(Required("library"),
                    TemplateLibrary.Parse<TemplateImportRequest>(ReadBounded(Required("request"), 200000)), ReadBounded(Required("capture"), 60000))));
            else
            {
                var entryFile = Required("entry-file"); var entryHash = Required("entry-hash");
                var plan = TemplateLibrary.Plan(entryFile, entryHash,
                    TemplateLibrary.Parse<TemplateApplyRequest>(ReadBounded(Required("request"), 200000)), ReadBounded(Required("capture"), 60000));
                if (Optional("destination") is string dest && plan.Executable) plan.WriteBundle(dest);
                Console.WriteLine(SnapshotExport.Json(new { plan_sha256 = plan.PlanSha256, plan = plan.Report(),
                    template = new { entry_sha256 = entryHash, entry = TemplateLibrary.Summary(TemplateLibrary.ReadEntry(entryFile, entryHash)) }, ports_opened = false }));
                return plan.Executable ? 0 : 2;
            }
            return 0;
        }
        if (command == "calibrate-parameter")
        {
            string requestFile = Required("request"), calibrationDestination = Required("destination");
            if (new FileInfo(requestFile).Length > 2000000) throw new InvalidDataException("Calibration exceeds size bound.");
            var calibration = System.Text.Json.JsonSerializer.Deserialize<ParameterCalibration>(File.ReadAllBytes(requestFile), PresetEditPlan.RequestJson)
                ?? throw new InvalidDataException("Missing calibration.");
            var resolved = SavedCalibration.Resolve(calibration);
            if (Directory.Exists(calibrationDestination) || File.Exists(calibrationDestination)) throw new IOException("Choose a new calibration directory.");
            Directory.CreateDirectory(calibrationDestination);
            File.WriteAllText(Path.Combine(calibrationDestination, "calibration.json"), SnapshotExport.Json(calibration));
            File.WriteAllText(Path.Combine(calibrationDestination, "calibration-report.json"), SnapshotExport.Json(resolved));
            Console.WriteLine(SnapshotExport.Json(new { calibration = resolved, ports_opened = false, hardware_qualified = false }));
            return 0;
        }
        if (command == "plan-audio-path")

        {

            string audioRequestFile = Required("request"), audioCapture = Required("capture");

            if (new FileInfo(audioRequestFile).Length > 65536 || new FileInfo(audioCapture).Length > 60000) throw new InvalidDataException("Audio path input exceeds bound.");

            var audioRequest = AutomaticRouting.Parse(File.ReadAllBytes(audioRequestFile));

            var audioPath = AutomaticRouting.Create(audioRequest, File.ReadAllBytes(audioCapture));

            if (audioPath.Plan.Executable && Optional("destination") is string audioDestination)

            {

                audioPath.Plan.WriteBundle(audioDestination);

                File.WriteAllText(Path.Combine(audioDestination, "audio-path.json"), SnapshotExport.Json(new { request = audioRequest, path = audioPath.Path }));

            }

            Console.WriteLine(SnapshotExport.Json(new { path = audioPath.Path, added_shunts = audioPath.AddedShunts, added_edges = audioPath.AddedEdges,

                plan_sha256 = audioPath.Plan.PlanSha256, plan = audioPath.Plan.Report(), ports_opened = false }));

            return audioPath.Plan.Executable ? 0 : 2;

        }

        if (command == "listen-notifications")

        {

            var notificationDevice = DeviceDefinition.Create(Required("model"), capacity);

            string notificationFirmware = Required("firmware");

            _ = EditReadCatalogue.Capabilities(notificationDevice, notificationFirmware);

            int duration = Number(Required("duration")), limit = Number(Required("max-events"));

            if (duration is < 1 or > 3600 || limit is < 1 or > 4096) throw new ArgumentException("Choose duration 1–3600 seconds and max-events 1–4096.");

            bool validate = Optional("validate-only") switch { "true" => true, null or "false" => false, _ => throw new ArgumentException("validate-only must be true or false.") };

            if (validate) { Console.WriteLine(SnapshotExport.Json(new { ports_opened = false, midi_sends = 0, network = TransportConnection.NetworkEndpoint })); return 0; }

            string notificationDestination = Required("destination");

            if (Directory.Exists(notificationDestination) || File.Exists(notificationDestination)) throw new IOException("Choose a new private directory.");

            await using var source = await TransportConnection.OpenInputAsync(Number(Required("input")), cancel.Token);

            await DeviceNotifications.CaptureAsync(source, notificationDevice, notificationFirmware, notificationDestination, TimeSpan.FromSeconds(duration), limit, cancel.Token);

            Console.WriteLine(SnapshotExport.Json(new { complete = true, report_file = "notifications.json", midi_sends = 0, hardware_qualified = false }));

            return 0;

        }

        if (command == "listen-midi")

        {

            string listenFile = Required("request");

            if (new FileInfo(listenFile).Length > 65536) throw new InvalidDataException("Listener request exceeds bound.");

            var listenerRequest = ControllerListener.Parse(File.ReadAllBytes(listenFile));

            int duration = Number(Required("duration")), maximumEvents = Number(Required("max-events"));

            if (duration is < 1 or > 3600 || maximumEvents is < 1 or > 4096) throw new ArgumentException("Choose duration 1–3600 seconds and max-events 1–4096.");

            bool validateListener = Optional("validate-only") switch { "true" => true, null or "false" => false, _ => throw new ArgumentException("validate-only must be true or false.") };

            if (validateListener) { Console.WriteLine(SnapshotExport.Json(new { listenerRequest.Model, listenerRequest.Capacity, listenerRequest.Firmware, rules = listenerRequest.Rules.Length, duration, maximum_events = maximumEvents, ports_opened = false, sends = 0 })); return 0; }

            string listenerDestination = Required("destination");

            if (Directory.Exists(listenerDestination) || File.Exists(listenerDestination)) throw new IOException("Choose a new private listener directory.");

            await using var listener = await TransportConnection.OpenInputAsync(Number(Required("input")), cancel.Token);

            Console.WriteLine(SnapshotExport.Json(await ControllerListener.CaptureAsync((IMidiInputSource)listener, listenerRequest, listenerDestination, TimeSpan.FromSeconds(duration), maximumEvents, cancel.Token)));

            return 0;

        }

        if (command is "create-edit-batch" or "route-midi")

        {

            string batchRequest = Path.GetFullPath(Required("request"));

            if (new FileInfo(batchRequest).Length > 1_000_000) throw new InvalidDataException("Request exceeds bound.");

            if (command == "route-midi") Console.WriteLine(SnapshotExport.Json(MidiInputRouter.Plan(File.ReadAllBytes(batchRequest))));

            else

            {

                var batch = PresetEditBatch.Build(File.ReadAllBytes(batchRequest), Path.GetDirectoryName(batchRequest)!);

                string batchDestination = Required("destination"); batch.WriteBundle(batchDestination);

                Console.WriteLine(SnapshotExport.Json(new { batch_sha256 = PresetBody.Sha256(File.ReadAllBytes(Path.Combine(batchDestination, "batch.json"))), batch = batch.Report() }));

            }

            return 0;

        }

        if (command == "performance-capabilities")
        {
            var definition = DeviceDefinition.Create(Required("model"), capacity);
            _ = EditReadCatalogue.Capabilities(definition, Required("firmware"));
            Console.WriteLine(SnapshotExport.Json(PerformanceProtocol.Capabilities(definition))); return 0;
        }
        if (command is "amp-roster" or "capture-control" or "capture-performance" or "live-monitor" or "read-block-parameters")

        {

            var definition = DeviceDefinition.Create(Required("model"), capacity);

            string firmware = Required("firmware"), livedestination = Required("destination");

            _ = EditReadCatalogue.Capabilities(definition, firmware);

            int effectId = command == "read-block-parameters" ? Number(Required("effect-id")) : 58;
            _ = SysexProtocol.BlockParametersQuery(definition, effectId);
            int start = command == "amp-roster" ? Number(Required("start")) : 0, count = command == "amp-roster" ? Number(Required("count")) : 1;

            if (start > 16383 || count is < 1 or > 1024 || (long)start + count > 16384) throw new ArgumentException("Explicit roster range must end within 16384, count 1–1024.");

            int samples = command == "live-monitor" ? Number(Required("samples")) : 1, interval = command == "live-monitor" ? Number(Required("interval-ms")) : 100;

            if (samples is < 1 or > 120 || interval is < 100 or > 10000) throw new ArgumentException("Choose 1–120 samples, 100–10000 ms between samples.");

            var readTimeout = TimeSpan.FromSeconds(Optional("timeout") is string liveseconds ? Number(liveseconds) : 10);

            if (readTimeout.TotalSeconds is < 1 or > 120) throw new ArgumentException("Timeout must be 1–120 seconds.");

            if (Directory.Exists(livedestination) || File.Exists(livedestination)) throw new IOException("Choose a new private directory.");

            int liveinput = Number(Required("input")), liveoutput = Number(Required("output"));

            bool validateRead = Optional("validate-only") switch { "true" => true, null or "false" => false, _ => throw new ArgumentException("validate-only must be true or false.") };
            if (validateRead)
            {
                if (command == "capture-performance")
                {
                    Console.WriteLine(SnapshotExport.Json(new { model = definition.Model, ports_opened = false,
                        looper_query_hex = Convert.ToHexString(PerformanceProtocol.LooperQuery(definition)), tempo_queried = false, hardware_qualified = false })); return 0;
                }
                Console.WriteLine(SnapshotExport.Json(new { model = definition.Model, effect_id = effectId, ports_opened = false,
                    query_hex = Convert.ToHexString(SysexProtocol.BlockParametersQuery(definition, effectId)), hardware_qualified = false }));
                return 0;
            }
            await using var livesession = new ReadSession(await TransportConnection.OpenAsync(liveinput, liveoutput));

            var livecontext = await livesession.IdentifyAsync(definition.Model, definition.PresetCapacity, readTimeout, cancel.Token);

            if (livecontext.Firmware != firmware) throw new InvalidDataException("Firmware differs from requested context.");

            if (command == "read-block-parameters")
            {
                await BlockParameterRead.CaptureAsync(livesession, livecontext, effectId, livedestination, readTimeout, cancel.Token);
                Console.WriteLine(SnapshotExport.Json(new { complete = true, report_file = "parameters.json", hardware_qualified = false }));
            }
            else if (command == "amp-roster") Console.WriteLine(SnapshotExport.Json(await AmpRoster.CaptureAsync(livesession, livecontext, start, count, livedestination, readTimeout, cancel.Token)));
            else if (command == "capture-performance")
            {
                var captured = await PerformanceControlScope.CaptureAsync(livesession, livecontext, readTimeout, cancel.Token);
                Directory.CreateDirectory(livedestination); File.WriteAllText(Path.Combine(livedestination, "context.json"), SnapshotExport.Json(captured));
                Console.WriteLine(SnapshotExport.Json(new { captured.Preset.Model, captured.Preset.Capacity, captured.Preset.Firmware,
                    captured.Preset.Current, captured.Looper, context_file = "context.json", tempo_queried = false, hardware_qualified = false }));
            }

            else if (command == "live-monitor")

            {

                await LiveMonitor.CaptureAsync(livesession, livecontext, samples, interval, livedestination, readTimeout, cancel.Token);

                Console.WriteLine(SnapshotExport.Json(new { complete = true, model = definition.Model, samples, report_file = "monitor.json", coverage = "sampled-selection-and-block-status-only" }));

            }

            else

            {

                var captured = await LiveControlScope.CaptureAsync(livesession, livecontext, readTimeout, cancel.Token);

                Directory.CreateDirectory(livedestination); File.WriteAllText(Path.Combine(livedestination, "context.json"), SnapshotExport.Json(captured));

                Console.WriteLine(SnapshotExport.Json(new { captured.Model, captured.Capacity, captured.Firmware, captured.Current, captured.Blocks, context_file = "context.json", hardware_qualified = false }));

            }

            return 0;

        }

        if (command == "plan-selective-scene-midi-undo")
        {
            string captureFile = Required("capture"), selectiveDestination = Required("destination");
            if (new FileInfo(captureFile).Length > 60000) throw new InvalidDataException("Saved capture exceeds bound.");
            var selective = SceneMidiSelectiveUndo.Create(Required("receipt"), Required("receipt-hash"), File.ReadAllBytes(captureFile));
            selective.Plan.WriteBundle(selectiveDestination);
            File.WriteAllText(Path.Combine(selectiveDestination, "selective-undo-source.json"), SnapshotExport.Json(new {
                receipt_sha256 = selective.SourceSha256, historical_base_sha256 = selective.HistoricalBaseSha256,
                historical_result_sha256 = selective.HistoricalResultSha256, owned_rows = selective.OwnedRows,
                already_restored_rows = selective.AlreadyRestoredRows }));
            Console.WriteLine(SnapshotExport.Json(new { plan_sha256 = selective.Plan.PlanSha256, plan = selective.Plan.Report(),
                source = new { receipt_sha256 = selective.SourceSha256, historical_base_sha256 = selective.HistoricalBaseSha256,
                    historical_result_sha256 = selective.HistoricalResultSha256, owned_rows = selective.OwnedRows,
                    already_restored_rows = selective.AlreadyRestoredRows }, ports_opened = false }));
            return 0;
        }
        if (command == "plan-selective-recovery-undo")
        {
            string captureFile = Required("capture"), recoveryDestination = Required("destination");
            if (new FileInfo(captureFile).Length > 60000) throw new InvalidDataException("Saved capture exceeds bound.");
            using var store = SceneMidiRecoveryStore.OpenForSelectivePlanning(Required("recovery"), Required("state-hash"));
            var selective = SceneMidiSelectiveUndo.CreateFromRecovery(store, Number(Required("slot")), File.ReadAllBytes(captureFile));
            selective.Plan.WriteBundle(recoveryDestination);
            var source = new { recovery_state_sha256 = selective.SourceSha256,
                historical_base_sha256 = selective.HistoricalBaseSha256,
                historical_result_sha256 = selective.HistoricalResultSha256,
                owned_rows = selective.OwnedRows, already_restored_rows = selective.AlreadyRestoredRows };
            File.WriteAllText(Path.Combine(recoveryDestination, "selective-undo-source.json"), SnapshotExport.Json(source));
            Console.WriteLine(SnapshotExport.Json(new { plan_sha256 = selective.Plan.PlanSha256,
                plan = selective.Plan.Report(), source, ports_opened = false }));
            return 0;
        }

        if (command is "plan-edit" or "inspect-edit-plan")

        {

            PresetEditPlan editPlan;

            if (command == "inspect-edit-plan") editPlan = PresetEditPlan.OpenBundle(Required("bundle"), Required("expected-hash"));

            else

            {

                string requestFile = Required("request"), captureFile = Required("capture");

                if (new FileInfo(requestFile).Length > 4_000_000 || new FileInfo(captureFile).Length > 60000) throw new InvalidDataException("Edit input exceeds bound.");

                editPlan = PresetEditPlan.Create(PresetEditPlan.Parse(File.ReadAllBytes(requestFile)), File.ReadAllBytes(captureFile));

                if (Optional("destination") is string outputFolder && editPlan.Executable) editPlan.WriteBundle(outputFolder);

            }

            Console.WriteLine(SnapshotExport.Json(new { plan_sha256 = editPlan.PlanSha256, plan = editPlan.Report() }));

            return editPlan.Executable ? 0 : 2;

        }

        if (command == "recovery-migrate")
        { Console.WriteLine(SnapshotExport.Json(SceneMidiRecoveryStore.Migrate(
            Required("recovery"), Required("state-hash"), Required("destination")))); return 0; }

        if (command == "inventory-migrate")

        { Console.WriteLine(SnapshotExport.Json(PresetInventory.Migrate(Required("inventory"), Required("destination")))); return 0; }

        if (command == "inventory-compare")

        {

            using var before = PresetInventory.Open(Required("before"));

            // Release-bound journals remain separately owned; comparing a directory with itself needs only one lease.

            if (Path.GetFullPath(Required("before")).Equals(Path.GetFullPath(Required("after")), StringComparison.OrdinalIgnoreCase))

                Console.WriteLine(SnapshotExport.Json(InventoryAnalysis.Compare(before, before)));

            else

            { using var after = PresetInventory.Open(Required("after")); Console.WriteLine(SnapshotExport.Json(InventoryAnalysis.Compare(before, after))); }

            return 0;

        }

        if (command == "inventory-amp-usage")

        {

            using var inventory = PresetInventory.Open(Required("inventory")); int[]? ids = null;

            if (Optional("roster") is string rosterPath)

            {

                if (new FileInfo(rosterPath).Length > 4_000_000) throw new InvalidDataException("Roster exceeds bound.");

                ids = InventoryAnalysis.ReadRoster(File.ReadAllBytes(rosterPath), inventory.Context);

            }

            Console.WriteLine(SnapshotExport.Json(InventoryAnalysis.AmpUsage(inventory, Optional("amp-type") is string amp ? Number(amp) : null, ids,

                Optional("offset") is string offset ? Number(offset) : 0, Optional("limit") is string limit ? Number(limit) : null))); return 0;

        }

        if (command is "inventory-inspect" or "inventory-export")

        {

            using var inventory = PresetInventory.Open(Required("inventory"));

            if (command == "inventory-inspect") { Console.WriteLine(SnapshotExport.Json(inventory.Inspect(

                Optional("offset") is string offset ? Number(offset) : 0, Optional("limit") is string limit ? Number(limit) : null))); return 0; }

            var inventoryExport = inventory.Export(Required("destination"));

            Console.WriteLine(SnapshotExport.Json(inventoryExport)); return inventoryExport.DecodeErrors.Length > 0 ? 4 : 0;

        }

        if (command == "inventory")

        {

            int inputPort = Number(Required("input")), outputPort = Number(Required("output"));

            TimeSpan readTimeout = TimeSpan.FromSeconds(Optional("timeout") is string value ? Number(value) : 10);

            if (readTimeout.TotalSeconds is < 1 or > 120) throw new ArgumentException("Timeout must be 1–120 seconds per exchange.");

            PresetInventory inventory;

            if (Optional("resume") is string resume)

            {

                if (options.Keys.Any(k => k is "destination" or "model" or "firmware" or "capacity" or "preset" or "all"))

                    throw new ArgumentException("Resume uses the checkpoint's exact context and slot set; omit new-scan options.");

                inventory = PresetInventory.Open(resume);

            }

            else

            {

                var definition = DeviceDefinition.Create(Required("model"), capacity);

                bool all = options.ContainsKey("all"), selected = options.TryGetValue("preset", out var selectedSlots);

                if (all == selected) throw new ArgumentException("Choose --all or one/more --preset options.");

                int[] inventorySlots = all ? Enumerable.Range(0, definition.PresetCapacity).ToArray() : selectedSlots!.Select(Number).ToArray();

                inventory = PresetInventory.Create(Required("destination"), new(definition, Required("firmware"),

                    definition.Model == "Axe-Fx III" ? "explicit-user-capacity" : "model-definition"), inventorySlots);

            }

            using (inventory)

            {

                if (inventory.Complete) throw new InvalidOperationException("Completed inventories are immutable; start a new scan.");

                var expectedContext = inventory.Context;

                await using var reader = new ReadSession(await TransportConnection.OpenAsync(inputPort, outputPort));

                var identified = await reader.IdentifyAsync(expectedContext.Definition.Model, expectedContext.Definition.PresetCapacity, readTimeout, cancel.Token);

                await inventory.RunAsync(reader, identified, readTimeout, cancel.Token);

                Console.WriteLine(SnapshotExport.Json(new { status = "complete", inventory = Path.GetFullPath(Optional("resume") ?? Required("destination")),

                    note = "Inspect offline for semantic completeness; export explicitly to a new snapshot directory." }));

            }

            return 0;

        }

        // These commands return before any platform MIDI enumeration or transport construction.

        if (command is "edit-capabilities" or "edit-catalogue" or "decode-edit")

        {

            var device = DeviceDefinition.Create(Required("model"), capacity);

            string firmware = Required("firmware");

            if (command == "edit-capabilities") Console.WriteLine(SnapshotExport.Json(EditReadCatalogue.Capabilities(device, firmware)));

            else if (command == "edit-catalogue") Console.WriteLine(SnapshotExport.Json(EditReadCatalogue.Parameters(device, firmware, Optional("family"))));

            else

            {

                // Validate context before reading the file. One complete addressed saved capture only.

                _ = EditReadCatalogue.Capabilities(device, firmware);

                string path = Required("capture");

                if (new FileInfo(path).Length > 60000) throw new InvalidDataException("Capture exceeds single-preset size bound.");

                Console.WriteLine(SnapshotExport.Json(PresetEditProjection.Decode(device, firmware, File.ReadAllBytes(path))));

            }

            return 0;

        }

        if (command == "blocks")

        {

            Console.WriteLine(SnapshotExport.Json(BlockCatalogue.Query(Required("model"), Optional("firmware"), Optional("query"),

                Optional("effect-id") is string id ? Number(id) : null)));

            return 0;

        }

        if (command == "ports")

        {

            Console.WriteLine(SnapshotExport.Json(new { inputs = NativeMidiTransport.Ports(true), outputs = NativeMidiTransport.Ports(false) })); return 0;

        }

        if (command == "check-backup")

        {

            DeviceDefinition d = DeviceDefinition.Create(Required("model"), capacity);

            string path = Required("capture");

            if (new FileInfo(path).Length > 60000) throw new InvalidDataException("File exceeds single-preset size bound.");

            byte[] sysex = File.ReadAllBytes(path);

            PresetFileInspection file = PresetFile.Inspect(d, sysex);

            Console.WriteLine(SnapshotExport.Json(new { schema_version = 1, model = file.Model,

                file_bytes = sysex.Length, chunks = file.ChunkCount, header_payload = file.HeaderPayload.Select(b => (int)b).ToArray(),

                raw_bytes = file.RawImage.Length, body_bytes = file.Body.Length,

                sysex_sha256 = PresetBody.Sha256(sysex), raw_sha256 = PresetBody.Sha256(file.RawImage), body_sha256 = PresetBody.Sha256(file.Body),

                integrity = new[] { "strict-single-preset-framing", "frame-xor", "footer-word-xor", "raw-crc16-aa55", "bounded-huffman" },

                firmware = "not-inferred-from-file", destination_semantics = "unqualified-opaque-header", restore_verified = false }));

            return 0;

        }

        if (command == "decode")

        {

            DeviceDefinition d = DeviceDefinition.Create(Required("model"), capacity);

            string firmware = Required("firmware");

            if (!System.Text.RegularExpressions.Regex.IsMatch(firmware, @"^\d{1,3}\.\d{2}$"))

                throw new ArgumentException("Firmware must be a recorded major.minor version (e.g. 12.00); it is supplied, not inferred from a capture.");

            if (!options.TryGetValue("capture", out var paths)) throw new ArgumentException("Required: --capture (repeat for more presets)");

            var captures = paths.Select(path =>

            {

                if (new FileInfo(path).Length > 60000) throw new InvalidDataException("Capture exceeds single-preset size bound.");

                return SnapshotExport.DecodeFile(d, File.ReadAllBytes(path));

            }).ToArray();

            ExportResult export = SnapshotExport.Write(Required("destination"), new(d, firmware, "explicit-offline-context"), captures);

            Console.WriteLine(SnapshotExport.Json(new { destination = Path.GetFullPath(Required("destination")), presets = captures.Length,

                decoded_presets = export.DecodedPresets, decode_errors = export.DecodeErrors })); return export.DecodeErrors.Length > 0 ? 4 : 0;

        }

        string? expected = Optional("model");

        if (expected != null && expected is not ("FM9" or "FM3" or "Axe-Fx III")) throw new ArgumentException("Invalid model.");

        TimeSpan timeout = TimeSpan.FromSeconds(Optional("timeout") is string seconds ? Number(seconds) : 10);

        if (timeout.TotalSeconds is < 1 or > 120) throw new ArgumentException("Timeout must be 1–120 seconds per exchange.");

        // Validate scope and filesystem arguments before opening any ports.

        int[]? requested = null;

        string? destination = null;

        if (command == "snapshot")

        {

            bool all = options.ContainsKey("all");

            bool selected = options.TryGetValue("preset", out var presets);

            if (all == selected) throw new ArgumentException("Choose --all or one/more --preset options.");

            if (selected) { requested = presets!.Select(Number).ToArray(); if (requested.Distinct().Count() != requested.Length) throw new ArgumentException("Duplicate preset slots."); }

            destination = Path.GetFullPath(Required("destination"));

            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Destination already exists.");

        }

        if (command is "scene-midi" or "prepare-load")

        {

            destination = Path.GetFullPath(Required("destination"));

            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Destination already exists.");

        }

        int? loadTarget=null;bool loadDiscard=false; MidiPresetMapping? loadMapping = null;

        if(command=="prepare-load")

        {

            if(!options.TryGetValue("preset",out var loadSlots)||loadSlots.Count!=1)throw new ArgumentException("Choose exactly one --preset.");

            loadTarget=Number(loadSlots[0]);

            if(expected!=null)DeviceDefinition.Create(expected,capacity).ValidateSlot(loadTarget.Value);

            else if(loadTarget.Value>1023)throw new ArgumentException("Preset exceeds supported capacities.");

            loadDiscard=Optional("discard-changes") switch{null or "false"=>false,"true"=>true,_=>throw new ArgumentException("Use true or false for --discard-changes.")};

            if (Optional("midi-mapping") is string mappingFile)

            {

                if (new FileInfo(mappingFile).Length > 4096) throw new InvalidDataException("Mapping exceeds bound.");

                loadMapping = System.Text.Json.JsonSerializer.Deserialize<MidiPresetMapping>(File.ReadAllBytes(mappingFile), LiveControlScope.JsonOptions) ?? throw new InvalidDataException("Missing mapping.");

                loadMapping.Validate(loadTarget.Value);

            }

        }

        int input = Number(Required("input")), output = Number(Required("output"));

        await using var session = new ReadSession(await TransportConnection.OpenAsync(input, output));

        DeviceContext context = await session.IdentifyAsync(expected, capacity, timeout, cancel.Token);

        if (command == "prepare-load")

        {

            int targetSlot=loadTarget!.Value;context.Definition.ValidateSlot(targetSlot);bool discard=loadDiscard;

            var before=await session.CurrentAsync(context,timeout,cancel.Token);

            var saved=await session.SavedAsync(context,before.Slot,timeout,cancel.Token);await Task.Delay(10,cancel.Token);

            var loaded=await session.EditBufferAsync(context,timeout,cancel.Token);await Task.Delay(10,cancel.Token);

            var target=await session.SavedAsync(context,targetSlot,timeout,cancel.Token);

            if(before!=await session.CurrentAsync(context,timeout,cancel.Token))throw new InvalidDataException("Selection changed while retaining load context.");

            Directory.CreateDirectory(destination!);

            File.WriteAllBytes(Path.Combine(destination!,"current-saved.syx"),saved.Sysex);

            File.WriteAllBytes(Path.Combine(destination!,"current-loaded.syx"),loaded.Sysex);

            File.WriteAllBytes(Path.Combine(destination!,"target-saved.syx"),target.Sysex);

            _=new PresetLoadScope(context.Definition,context.Firmware,before,saved.Sysex,loaded.Sysex,target.Sysex,discard,loadMapping);

            var request=new{schema_version=1,model=context.Definition.Model,capacity=context.Definition.PresetCapacity,firmware=context.Firmware,

                current_slot=before.Slot,current_scene=before.Scene,target_slot=targetSlot,discard_changes=discard,midi_mapping=loadMapping,

                current_saved=new{file="current-saved.syx",sha256=PresetBody.Sha256(saved.Sysex)},current_loaded=new{file="current-loaded.syx",sha256=PresetBody.Sha256(loaded.Sysex)},

                target_saved=new{file="target-saved.syx",sha256=PresetBody.Sha256(target.Sysex)}};

            string file=Path.Combine(destination!,"load-request.json");File.WriteAllText(file,SnapshotExport.Json(request));

            Console.WriteLine(SnapshotExport.Json(new{mode="read-only-load-context",model=context.Definition.Model,firmware=context.Firmware,

                request_sha256=PresetBody.Sha256(File.ReadAllBytes(file)),current_slot=before.Slot,target_slot=targetSlot,destination,load_commands=0}));return 0;

        }

        if (command == "scene-midi")

        {

            CurrentState before = await session.CurrentAsync(context, timeout, cancel.Token);

            SavedCapture saved = await session.SavedAsync(context, before.Slot, timeout, cancel.Token);

            PresetSnapshot preset = PresetBody.Decode(context.Definition, saved.Image);

            SceneMidiCapture? live = preset.SceneMidiPresent ? await session.SceneMidiAsync(context, timeout, cancel.Token) : null;

            var liveScenes = live == null ? null : SceneMidiTable.Decode(context.Definition, live.Fields);

            // A live table is edit-buffer evidence; never substitute it into a saved snapshot.

            SavedCapture reread = await session.SavedAsync(context, before.Slot, timeout, cancel.Token);

            CurrentState after = await session.CurrentAsync(context, timeout, cancel.Token);

            bool stable = saved.Image.RawImage.SequenceEqual(reread.Image.RawImage);

            bool? equal = live != null && preset.SceneMidiDecodeStatus == "decoded" ?

                live.Fields.SequenceEqual(SceneMidiTable.SavedFields(context.Definition, preset.BlockRecords)) : null;

            SnapshotExport.Write(destination!, context, [saved], before, after);

            if (live != null) File.WriteAllBytes(Path.Combine(destination!, "scene-midi-live.syx"), live.Sysex);

            var comparison = new { schema_version = 1, model = context.Definition.Model, firmware = context.Firmware,

                state_before = before, state_after = after, state_preserved = before == after, saved_content_stable = stable,

                saved_content_sha256 = PresetBody.Sha256(saved.Image.RawImage), saved_decode_status = preset.SceneMidiDecodeStatus,

                live_status = live == null ? "not-queried-saved-block-absent" : "decoded", live_saved_fields_equal = equal,

                live_capture_sha256 = live == null ? null : PresetBody.Sha256(live.Sysex),

                live_fields = live?.Fields, live_extra_word = live?.ExtraWord, live_section_metadata = live?.SectionMetadata,

                live_scenes = liveScenes?.Select((messages, index) => new { number = index + 1, messages }),

                note = "Current edit-buffer table and saved table are distinct. Equality is observed data, not proof that all unsaved settings equal saved settings." };

            File.WriteAllText(Path.Combine(destination!, "scene-midi-comparison.json"), SnapshotExport.Json(comparison) + "\n");

            Console.WriteLine(SnapshotExport.Json(new { destination, model = context.Definition.Model, firmware = context.Firmware,

                state_preserved = before == after, saved_content_stable = stable, saved_decode_status = preset.SceneMidiDecodeStatus,

                live_saved_fields_equal = equal }));

            return before == after && stable ? 0 : 3;

        }

        if (command == "inspect")

        {

            CurrentState before = await session.CurrentAsync(context, timeout, cancel.Token);

            List<object> scenes = [];

            for (int scene = 1; scene <= 8; scene++) scenes.Add(new { number = scene, name = await session.SceneNameAsync(context, scene, timeout, cancel.Token) });

            CurrentState after = await session.CurrentAsync(context, timeout, cancel.Token);

            Console.WriteLine(SnapshotExport.Json(new { device = context, state_before = before, state_after = after,

                state_preserved = before == after, scenes, scene_names_coherent = before == after }));

            return before == after ? 0 : 3;

        }

        int[] slots = requested ?? Enumerable.Range(0, context.Definition.PresetCapacity).ToArray();

        ReadResult result = await session.SnapshotAsync(context, slots, timeout, cancel.Token);

        ExportResult written = SnapshotExport.Write(destination!, context, result.Captures, result.Before, result.After);

        Console.WriteLine(SnapshotExport.Json(new { destination, presets = slots.Length, state_preserved = result.StatePreserved,

            decoded_presets = written.DecodedPresets, decode_errors = written.DecodeErrors }));

        return written.DecodeErrors.Length > 0 ? 4 : result.StatePreserved ? 0 : 3;

    }

    catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled; no automatic retries sent."); return 130; }

    catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or InvalidOperationException or TimeoutException or PlatformNotSupportedException or System.Threading.Channels.ChannelClosedException)

    { Console.Error.WriteLine($"error: {error.Message}"); return 2; }

}

partial class Program

{

    private static readonly string Help = $"""

        AI Toolkit for Fractal {ProtocolRelease.Version} — read-only MIDI / saved capture tools

        ports

        blocks   --model MODEL (--query NAME_OR_ABBREVIATION | --effect-id ID) [--firmware MAJOR.MINOR]

        edit-capabilities --model MODEL --firmware MAJOR.MINOR [--capacity 512|1024]

        edit-catalogue --model MODEL --firmware MAJOR.MINOR [--capacity 512|1024] [--family NAME]

        decode-edit --model MODEL --firmware MAJOR.MINOR --capture FILE.syx [--capacity 512|1024]

        calibrate-parameter --request REQUEST_JSON --destination NEW_DIRECTORY
        plan-edit --request REQUEST.json --capture BASELINE.syx [--destination NEW_BUNDLE_DIRECTORY]
        template-import --library PRIVATE_DIRECTORY --request METADATA.json --capture DONOR.syx
        template-list --library PRIVATE_DIRECTORY [--query TEXT] [--tag TAG] [--offset N] [--limit N]
        template-inspect --library PRIVATE_DIRECTORY --entry ENTRY_ID --expected-hash ENTRY_SHA256
        plan-template --entry-file PRIVATE_ENTRY.json --entry-hash ENTRY_SHA256 --request APPLY.json --capture BASELINE.syx [--destination NEW_BUNDLE_DIRECTORY]

        plan-selective-scene-midi-undo --receipt COMPLETED_EDIT_DIRECTORY --receipt-hash ATTEMPT_SHA256 --capture CURRENT_SAVED.syx --destination NEW_BUNDLE_DIRECTORY
        plan-selective-recovery-undo --recovery PRIVATE_RECOVERY_DIRECTORY --state-hash CURRENT_STATE_SHA256 --slot ZERO_BASED --capture CURRENT_SAVED.syx --destination NEW_BUNDLE_DIRECTORY
        recovery-migrate --recovery PRIOR_RELEASE_DIRECTORY --state-hash CURRENT_STATE_SHA256 --destination NEW_RECOVERY_DIRECTORY

        inspect-edit-plan --bundle BUNDLE_DIRECTORY --expected-hash PLAN_SHA256

        inspect  --input N --output N [--model MODEL] [--capacity 512|1024] [--timeout SECONDS]

        snapshot --input N --output N (--preset SLOT [--preset SLOT...] | --all) --destination NEW_DIRECTORY

                 [--model MODEL] [--capacity 512|1024] [--timeout SECONDS]

        inventory --input N --output N --model MODEL --firmware MAJOR.MINOR [--capacity 512|1024]

                  (--preset SLOT [--preset SLOT...] | --all) --destination NEW_DIRECTORY [--timeout SECONDS]

        inventory --input N --output N --resume INVENTORY_DIRECTORY [--timeout SECONDS]

        inventory-inspect --inventory INVENTORY_DIRECTORY [--offset N] [--limit N]

        inventory-export --inventory INVENTORY_DIRECTORY --destination NEW_DIRECTORY

        inventory-compare --before INVENTORY_DIRECTORY --after INVENTORY_DIRECTORY

        inventory-migrate --inventory PRIOR_RELEASE_DIRECTORY --destination NEW_DIRECTORY

        inventory-amp-usage --inventory INVENTORY_DIRECTORY [--amp-type ID] [--roster MODEL_FIRMWARE_IDS_JSON] [--offset N] [--limit N]

        scene-midi --input N --output N --destination NEW_DIRECTORY

                 [--model MODEL] [--capacity 512|1024] [--timeout SECONDS]

        decode   --model MODEL --firmware MAJOR.MINOR --capture FILE.syx [--capture FILE.syx...] --destination NEW_DIRECTORY

                 [--capacity 512|1024]

        check-backup --model MODEL --capture FILE.syx [--capacity 512|1024]

        prepare-load --input N --output N --preset SLOT --destination NEW_DIRECTORY [--discard-changes true|false]

                     [--model MODEL] [--capacity 512|1024] [--timeout SECONDS] [--midi-mapping MAPPING_JSON]

        create-edit-batch --request BATCH_REQUEST_JSON --destination NEW_DIRECTORY

        route-midi --request ROUTING_JSON

        plan-audio-path --request PATH_REQUEST_JSON --capture FILE.syx [--destination NEW_DIRECTORY]

        listen-midi --request LISTENER_JSON --duration SECONDS --max-events COUNT [--validate-only true]

                    --input CONTROLLER_INPUT --destination NEW_DIRECTORY

        MODEL: FM9, FM3, "Axe-Fx III". Device/file commands for Axe-Fx III require an explicit capacity; blocks does not.

        read-block-parameters --model MODEL --capacity N --firmware VERSION --input N --output N --effect-id ID --destination NEW_DIRECTORY
        amp-roster --model MODEL --capacity N --firmware VERSION --input N --output N --start ORDINAL --count COUNT --destination NEW_DIRECTORY

        capture-control --model MODEL --capacity N --firmware VERSION --input N --output N --destination NEW_DIRECTORY
        capture-performance --model MODEL --capacity N --firmware VERSION --input N --output N --destination NEW_DIRECTORY
        performance-capabilities --model MODEL --capacity N --firmware VERSION
        plan-organization --request JSON --destination NEW_DIRECTORY
        plan-setlists --request JSON --inventory INVENTORY_DIRECTORY --destination NEW_DIRECTORY
          Toolkit-native offline songs/setlist proposal; reference checks only, no system read/write or editor interchange.
        inspect-organization --bundle DIRECTORY --expected-hash SHA256
        organization-batch --bundle DIRECTORY --expected-hash SHA256 --context JSON --discard-changes true|false --destination NEW_DIRECTORY
        plan-organization-undo --receipt EXECUTION_DIRECTORY --expected-hash PROGRESS_SHA256 --request CURRENT_CAPTURES_JSON --destination NEW_DIRECTORY

        live-monitor --model MODEL --capacity N --firmware VERSION --input N --output N --samples COUNT --interval-ms MS --destination NEW_DIRECTORY

        listen-notifications --model MODEL --capacity N --firmware VERSION --input N --duration SECONDS --max-events COUNT --destination NEW_DIRECTORY

        Device commands accept --network IPv4:CONTROL_PORT instead of local input/output ports (Apple RTP-MIDI).

        Live tools accept --timeout SECONDS (1–120). Roster ranges are explicit; monitoring samples selection/block status only.

        blocks is offline and names grid instances, not amp/effect types. Dated availability is not exact-firmware qualification.

        edit-capabilities, edit-catalogue, decode-edit and plan-edit are offline; executable bundles use the opted-in helper/MCP review.

        decode-edit prints a version-1 partial projection to stdout; inspect field statuses and issues even on exit 0.

        Preset slots are zero-based. Commands never navigate, edit, save or restore.

        inventory checkpoints every saved capture; resume rechecks all retained contents before reading missing slots.

        inventory-inspect/export are offline. Partial inventories cannot be exported as completed snapshots.

        scene-midi compares the current preset's saved and live Scene MIDI tables without changing scenes.

        check-backup inspects one preset file offline; its header does not establish a restore destination.

        prepare-load retains current saved/loaded and target captures plus a hash-bound request; it sends no Load.

        Live MIDI: shared Windows legacy or macOS CoreMIDI; explicit RTP-MIDI network endpoint. Offline decode: any .NET 10 platform.

        Close other port owners before opening a connection. Exit 3: state changed. Exit 4: captures saved but body decoding incomplete.

        """;

}
