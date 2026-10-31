using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Fractal.Core;

static class SelectiveUndoChecks
{
    public static async Task Run(DeviceDefinition d, string? fixtureRoot = null)
    {
        var fixture = AudioRoutingChecks.Build(d);
        byte[] body = fixture.Body;
        Put(body, 0, 2);
        int shape = fixture.Offsets[78] + (23 + 29 * 4) * 2;
        Put(body, shape - 12, 190);
        Put(body, shape + 30, 257);
        Put(body, shape + 32, 1);
        Put(body, 0x104 + (d.GridRows + 3) * 4, 190);
        int[] fields = Fixtures.DefaultMidiFields();
        const int ownedRow = 8;
        fields[ownedRow] = 1; fields[64 + ownedRow] = 0; fields[128 + ownedRow] = 7; fields[192 + ownedRow] = 10;
        for (int i = 0; i < fields.Length; i++) Put(body, shape + 34 + i * 2, fields[i]);
        var baseline = Fixtures.FromBody(d, 0, body);
        var change = new PresetEditOperation("set-scene-midi", Scene: 2, MessageSlot: 1, MessageType: "CC",
            MidiChannel: 2, Controller: 74, Value: 57, Policy: "replace", ExpectedRaw: [1, 0, 7, 10]);
        var request = new PresetEditRequest(1, d.Model, d.PresetCapacity, "99.07", 0, "save", PresetBody.Sha256(baseline.Sysex), [change]);
        var historical = PresetEditPlan.Create(request, baseline.Sysex);
        Need(historical.Executable && historical.Changed, "Historical message edit must be executable.");
        string root = Path.Combine(Path.GetTempPath(), "fractal-selective-undo-" + Guid.NewGuid().ToString("N"));
        try
        {
            string receipt = Path.Combine(root, "receipt");
            await Apply(historical, baseline, receipt, d);
            string hash = PresetBody.Sha256(File.ReadAllBytes(Path.Combine(receipt, "attempt.json")));
            byte[] laterBody = SnapshotExport.DecodeFile(d, historical.Expected).Image.Body.ToArray();
            Put(laterBody, shape + 34 + (192 + 9) * 2, fields[192 + 9] + 1);
            var later = Fixtures.FromBody(d, 0, laterBody, "Later saved name");
            if (fixtureRoot != null)
            {
                string output = Path.Combine(fixtureRoot, $"SelectiveUndo-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}");
                SnapshotExport.Write(Path.Combine(output, "export"), new(d, "99.07", "synthetic"), [later]);
                foreach (string file in Directory.GetFiles(receipt, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(output, "execution", Path.GetRelativePath(receipt, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
            }
            var selective = SceneMidiSelectiveUndo.Create(receipt, hash, later.Sysex);
            Need(selective.OwnedRows.SequenceEqual([9]) && selective.AlreadyRestoredRows.Length == 0,
                "Receipt owns only the edited message row.");
            Need(selective.Plan.Executable && selective.Plan.Changed, "Selective undo must be executable.");
            byte[] expected = laterBody.ToArray();
            foreach (int bank in new[] { 0, 64, 128, 192 })
                Put(expected, shape + 34 + (bank + ownedRow) * 2, fields[bank + ownedRow]);
            Need(SnapshotExport.DecodeFile(d, selective.Plan.Expected).Image.Body.SequenceEqual(expected),
                "Undo restores owned words and preserves later unrelated words.");
            Need(SnapshotExport.DecodeFile(d, selective.Plan.Expected).Image.Name == "Later saved name",
                "Undo preserves a later preset name outside the Scene MIDI table.");
            string bundle = Path.Combine(root, "bundle"); selective.Plan.WriteBundle(bundle);
            Need(PresetEditPlan.OpenBundle(bundle, selective.Plan.PlanSha256).Expected.SequenceEqual(selective.Plan.Expected),
                "Selective plan must survive immutable bundle revalidation.");
            await Apply(selective.Plan, later, Path.Combine(root, "undo"), d);
            Need(File.ReadAllBytes(Path.Combine(root, "undo", "verified-reloaded.syx")).SequenceEqual(selective.Plan.Expected),
                "Selective undo must complete Save and Reload with the exact merged image.");
            Refuses(() => SceneMidiSelectiveUndo.Create(receipt, new string('0', 64), later.Sysex), "Wrong receipt hash");
            byte[] conflictBody = laterBody.ToArray(); Put(conflictBody, shape + 34 + (192 + ownedRow) * 2, 58);
            Refuses(() => SceneMidiSelectiveUndo.Create(receipt, hash, Fixtures.FromBody(d, 0, conflictBody).Sysex), "Later owned-row conflict");
            Refuses(() => SceneMidiSelectiveUndo.Create(receipt, hash, Fixtures.FromBody(d, 1, laterBody).Sysex), "Different preset slot");
            var repeated = SceneMidiSelectiveUndo.Create(receipt, hash, selective.Plan.Expected);
            Need(repeated.Plan.Executable && !repeated.Plan.Changed && repeated.AlreadyRestoredRows.SequenceEqual([9]),
                "Repeated undo is a no-op while preserving later settings.");
            string priorReceipt = Path.Combine(root, "prior-release");
            foreach (string file in Directory.GetFiles(receipt, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(priorReceipt, Path.GetRelativePath(receipt, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            string priorPlanPath = Path.Combine(priorReceipt, "plan", "plan.json");
            var priorPlan = JsonNode.Parse(File.ReadAllText(priorPlanPath))!;
            priorPlan["protocol_release"] = "0.25.0";
            File.WriteAllText(priorPlanPath, priorPlan.ToJsonString());
            string priorAttemptPath = Path.Combine(priorReceipt, "attempt.json");
            var priorAttempt = JsonNode.Parse(File.ReadAllText(priorAttemptPath))!;
            priorAttempt["protocol_release"] = "0.25.0";
            priorAttempt["plan_sha256"] = PresetBody.Sha256(File.ReadAllBytes(priorPlanPath));
            File.WriteAllText(priorAttemptPath, priorAttempt.ToJsonString());
            var migrated = SceneMidiSelectiveUndo.Create(priorReceipt,
                PresetBody.Sha256(File.ReadAllBytes(priorAttemptPath)), later.Sysex);
            Need(migrated.Plan.Expected.SequenceEqual(selective.Plan.Expected),
                "Prior-release receipt must revalidate exact body and report semantics.");
            var activate = new PresetEditOperation("set-scene-midi", Scene: 2, MessageSlot: 2,
                MessageType: "CC", MidiChannel: 3, Controller: 17, Value: 65);
            var activation = PresetEditPlan.Create(request with { Operations = [activate] }, baseline.Sysex);
            Need(activation.Executable, "Activation fixture must be executable.");
            string activationReceipt = Path.Combine(root, "activation-receipt");
            await Apply(activation, baseline, activationReceipt, d);
            string activationHash = PresetBody.Sha256(File.ReadAllBytes(Path.Combine(activationReceipt, "attempt.json")));
            var deactivated = SceneMidiSelectiveUndo.Create(activationReceipt, activationHash, activation.Expected);
            Need(SnapshotExport.DecodeFile(d, deactivated.Plan.Expected).Image.Body.SequenceEqual(body),
                "Selective undo restores all original Off-row inactive words exactly.");
            await Apply(deactivated.Plan, SnapshotExport.DecodeFile(d, activation.Expected),
                Path.Combine(root, "deactivation-undo"), d);
            string recoveryPath = Path.Combine(root, "recovery-store");
            var recoveryFixture = new SceneMidiEditFixture(d, 0);
            string recoveryHash;
            byte[] recoveryLaterWire;
            byte[] recoverySelectiveExpected;
            using (var store = SceneMidiRecoveryStore.Create(recoveryPath, [recoveryFixture.Plan], SceneMidiRecoveryChecks.RequestHash))
            {
                bool pendingRefused = false;
                try { store.CompletedImagesForSelectiveUndo(recoveryFixture.Plan.Slot); }
                catch (InvalidOperationException) { pendingRefused = true; }
                Need(pendingRefused, "Pending recovery item has no completed selective-undo authority.");
                var completed = await SceneMidiRecoveryChecks.Run(store, recoveryFixture.Midi);
                Need(completed.Applied == 1 && store.Items.Single().Status == "edited", "Recovery item must have a verified result.");
                byte[] recoveryLaterBody = recoveryFixture.Saved.Image.Body.ToArray(); recoveryLaterBody[3000] ^= 1;
                var recoveryLater = Fixtures.FromBody(d, recoveryFixture.Plan.Slot, recoveryLaterBody, "Later recovery name");
                recoveryLaterWire = recoveryLater.Sysex;
                var recoveryUndo = SceneMidiSelectiveUndo.CreateFromRecovery(store, recoveryFixture.Plan.Slot, recoveryLater.Sysex);
                recoverySelectiveExpected = recoveryUndo.Plan.Expected;
                byte[] recoveryExpected = recoveryFixture.Base.Image.Body.ToArray(); recoveryExpected[3000] ^= 1;
                Need(recoveryUndo.SourceKind == "scene-midi-recovery-store" && recoveryUndo.Plan.Executable &&
                    SnapshotExport.DecodeFile(d, recoveryUndo.Plan.Expected).Image.Body.SequenceEqual(recoveryExpected) &&
                    SnapshotExport.DecodeFile(d, recoveryUndo.Plan.Expected).Image.Name == "Later recovery name",
                    "Completed recovery receipt restores original raw rows and retains unrelated later settings.");
                string recoveryBundle = Path.Combine(root, "recovery-bundle"); recoveryUndo.Plan.WriteBundle(recoveryBundle);
                Need(PresetEditPlan.OpenBundle(recoveryBundle, recoveryUndo.Plan.PlanSha256).Expected.SequenceEqual(recoveryUndo.Plan.Expected),
                    "Recovery selective bundle revalidates after reopening.");
                var authoredRequest = PresetEditPlan.Parse(File.ReadAllBytes(Path.Combine(recoveryBundle, "request.json")));
                var authoredOperation = authoredRequest.Operations.Single();
                int[] wrongExpected = authoredOperation.ExpectedRowsRaw!.ToArray(); wrongExpected[0] ^= 1;
                Need(!PresetEditPlan.Create(authoredRequest with { Operations = [authoredOperation with { ExpectedRowsRaw = wrongExpected }] },
                    recoveryLater.Sysex).Executable, "Direct row restoration refuses a stale expected raw word.");
                int[] duplicateRows = authoredOperation.MessageRows!.ToArray(); duplicateRows[1] = duplicateRows[0];
                Need(!PresetEditPlan.Create(authoredRequest with { Operations = [authoredOperation with { MessageRows = duplicateRows }] },
                    recoveryLater.Sysex).Executable, "Direct row restoration refuses duplicate row ownership.");
                var otherSlotDonor = Fixtures.FromBody(d, 1, recoveryFixture.Base.Image.Body);
                Need(!PresetEditPlan.Create(authoredRequest with { Operations = [authoredOperation with {
                    TemplateCapture = Convert.ToBase64String(otherSlotDonor.Sysex),
                    TemplateSha256 = PresetBody.Sha256(otherSlotDonor.Sysex) }] }, recoveryLater.Sysex).Executable,
                    "Direct row restoration refuses a donor from another slot.");
                await Apply(recoveryUndo.Plan, recoveryLater, Path.Combine(root, "recovery-undo"), d);
                var repeatedRecovery = SceneMidiSelectiveUndo.CreateFromRecovery(store, recoveryFixture.Plan.Slot, recoveryUndo.Plan.Expected);
                Need(!repeatedRecovery.Plan.Changed && store.Items.Single().Status == "edited",
                    "Repeat is a no-op and offline planning does not mutate the recovery store.");
                var recoveryChange = recoveryFixture.Plan.Changes.First();
                var record = PresetBody.Decode(d, recoveryFixture.Saved.Image).BlockRecords
                    .Single(r => r.Columns == d.SceneMidi!.Columns && r.Rows == d.SceneMidi.Rows);
                int index = (recoveryChange.Scene - 1) * 8 + recoveryChange.MessageSlot - 1;
                byte[] conflict = recoveryLaterBody.ToArray();
                int valueOffset = record.Offset + d.SceneMidi!.FirstWord * 2 + (d.SceneMidi.ValueBank + index) * 2;
                Put(conflict, valueOffset, (recoveryChange.Value + 1) % 128);
                Refuses(() => SceneMidiSelectiveUndo.CreateFromRecovery(store, recoveryFixture.Plan.Slot,
                    Fixtures.FromBody(d, recoveryFixture.Plan.Slot, conflict).Sysex), "Changed recovery-owned row");
                recoveryHash = store.StateHash;
                if (fixtureRoot != null)
                {
                    string output = Path.Combine(fixtureRoot, $"SelectiveRecovery-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}");
                    SnapshotExport.Write(Path.Combine(output, "export"), new(d, "99.07", "synthetic"), [recoveryLater]);
                }
            }
            using (var reopened = SceneMidiRecoveryStore.OpenForSelectivePlanning(recoveryPath, recoveryHash))
                Need(reopened.Items.Single().Status == "edited", "Exact recovery head reopens read-only for planning.");
            Refuses(() => { using var _ = SceneMidiRecoveryStore.OpenForSelectivePlanning(recoveryPath, new string('0', 64)); },
                "Stale recovery state hash");
            if (fixtureRoot != null)
            {
                string output = Path.Combine(fixtureRoot, $"SelectiveRecovery-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}", "recovery");
                Directory.CreateDirectory(output);
                foreach (string file in Directory.GetFiles(recoveryPath))
                    File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
            }
            // These are synthetic current-schema records with older release labels, not archived historical files.
            foreach (string release in new[] { "0.8.0", "0.16.0", "0.25.0", "0.26.0", "0.27.0", "0.28.0", "0.29.0" })
            {
                var (priorPath, priorHash) = CopyRecoveryWithRelease(recoveryPath, root, release);
                using (var priorStore = SceneMidiRecoveryStore.OpenForSelectivePlanning(priorPath, priorHash))
                {
                    var priorSelective = SceneMidiSelectiveUndo.CreateFromRecovery(priorStore, recoveryFixture.Plan.Slot, recoveryLaterWire);
                    Need(priorSelective.Plan.Executable && priorSelective.Plan.Expected.SequenceEqual(recoverySelectiveExpected),
                        "Compatible schema-1 recovery head must reproduce the same selective plan.");
                    string unchangedHash = priorStore.StateHash;
                    bool mutationRefused = false;
                    try { priorStore.Attempt(recoveryFixture.Plan.Slot, "undo-load"); }
                    catch (InvalidOperationException) { mutationRefused = true; }
                    Need(mutationRefused && priorStore.StateHash == unchangedHash,
                        "Read-only prior-release import cannot mutate retained recovery state.");
                }
                Refuses(() => { using var _ = SceneMidiRecoveryStore.Open(priorPath, priorHash); },
                    "Prior-release recovery head in ordinary mutable import");
            }
            var currentVersion = Version.Parse(ProtocolRelease.Version);
            string futurePatch = $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build + 1}";
            string futureMinor = $"{currentVersion.Major}.{currentVersion.Minor + 1}.0";
            foreach (string release in new[] { "0.7.0", "0.8", "0.08.0", futurePatch, futureMinor, "1.0.0" })
            {
                var (unsupportedPath, unsupportedHash) = CopyRecoveryWithRelease(recoveryPath, root, release);
                Refuses(() => { using var _ = SceneMidiRecoveryStore.OpenForSelectivePlanning(unsupportedPath, unsupportedHash); },
                    $"Unsupported or noncanonical recovery release {release}");
            }
            var (alteredPath, alteredHash) = CopyRecoveryWithRelease(recoveryPath, root, "0.8.0", recoveryLaterWire);
            Refuses(() => { using var _ = SceneMidiRecoveryStore.OpenForSelectivePlanning(alteredPath, alteredHash); },
                "Older recovery release with hash-consistent but nonmatching saved result");
            string migrationSources = Path.Combine(root, "migration-sources");
            var (migrationSource, migrationHash) = CopyRecoveryWithRelease(recoveryPath, migrationSources, "0.8.0");
            if (fixtureRoot != null)
            {
                string output = Path.Combine(fixtureRoot, $"SelectiveRecovery-{d.Model.Replace(' ', '-')}-{d.PresetCapacity}", "prior-recovery");
                Directory.CreateDirectory(output);
                foreach (string file in Directory.GetFiles(migrationSource))
                    File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
            }
            string migratedPath = Path.Combine(root, "migrated-recovery");
            var migration = JsonNode.Parse(SnapshotExport.Json(SceneMidiRecoveryStore.Migrate(
                migrationSource, migrationHash, migratedPath)))!;
            string migratedHash = migration["target_state_sha256"]!.GetValue<string>();
            Need(migration["source_state_sha256"]!.GetValue<string>() == migrationHash &&
                migration["edited_count"]!.GetValue<int>() == 1 &&
                PresetBody.Sha256(File.ReadAllBytes(Path.Combine(migrationSource, "state.private.json"))) == migrationHash,
                "Migration preserves the exact prior head and records its source hash.");
            using (var migratedRecovery = SceneMidiRecoveryStore.Open(migratedPath, migratedHash))
            {
                var images = migratedRecovery.CompletedImagesForSelectiveUndo(recoveryFixture.Plan.Slot);
                Need(images.Original.SequenceEqual(recoveryFixture.Base.Sysex) &&
                    migratedRecovery.Reconcile(recoveryFixture.Plan.Slot, SnapshotExport.DecodeFile(d, images.Result), true) == "undo",
                    "Migrated completed item remains eligible only for a freshly guarded exact-result undo.");
            }
            Refuses(() => SceneMidiRecoveryStore.Migrate(migrationSource, new string('0', 64),
                Path.Combine(root, "stale-migration")), "Stale migration source hash");
            Refuses(() => SceneMidiRecoveryStore.Migrate(alteredPath, alteredHash,
                Path.Combine(root, "altered-migration")), "Altered historical result migration");
            string pendingCurrent = Path.Combine(root, "pending-current");
            using (var pending = SceneMidiRecoveryStore.Create(pendingCurrent, [recoveryFixture.Plan], SceneMidiRecoveryChecks.RequestHash)) { }
            var (pendingPath, pendingHash) = CopyRecoveryWithRelease(pendingCurrent, migrationSources, "0.16.0");
            string pendingDestination = Path.Combine(root, "pending-migration");
            var pendingMigration = JsonNode.Parse(SnapshotExport.Json(SceneMidiRecoveryStore.Migrate(
                pendingPath, pendingHash, pendingDestination)))!;
            Need(pendingMigration["pending_untouched_count"]!.GetValue<int>() == 1 &&
                pendingMigration["edited_count"]!.GetValue<int>() == 0,
                "Never-attempted pending item is explicitly counted in offline migration.");
            using (var migratedPending = SceneMidiRecoveryStore.Open(pendingDestination,
                pendingMigration["target_state_sha256"]!.GetValue<string>()))
                Need(migratedPending.Items.Single().Status == "pending" &&
                    migratedPending.Reconcile(recoveryFixture.Plan.Slot, recoveryFixture.Base, false) == "apply",
                    "Migrated untouched item still requires fresh saved-state reconciliation before application.");

            var nextFixture = new SceneMidiEditFixture(d, 1);
            string mixedCurrent = Path.Combine(root, "mixed-current");
            using (var mixed = SceneMidiRecoveryStore.Create(mixedCurrent,
                [recoveryFixture.Plan, nextFixture.Plan], SceneMidiRecoveryChecks.RequestHash))
                mixed.CompleteEdit(recoveryFixture.Plan.Slot, recoveryFixture.Saved.Sysex, recoveryFixture.Saved.Sysex);
            var (mixedPath, mixedHash) = CopyRecoveryWithRelease(mixedCurrent,
                Path.Combine(root, "mixed-sources"), "0.25.0");
            var mixedMigration = JsonNode.Parse(SnapshotExport.Json(SceneMidiRecoveryStore.Migrate(
                mixedPath, mixedHash, Path.Combine(root, "mixed-migrated"))))!;
            Need(mixedMigration["edited_count"]!.GetValue<int>() == 1 &&
                mixedMigration["pending_untouched_count"]!.GetValue<int>() == 1,
                "Mixed completed and untouched batch migrates without changing item states.");
            using (var reopened = SceneMidiRecoveryStore.Open(Path.Combine(root, "mixed-migrated"),
                mixedMigration["target_state_sha256"]!.GetValue<string>()))
                Need(reopened.Items.Select(i => i.Status).SequenceEqual(["edited", "pending"]) &&
                    reopened.Reconcile(nextFixture.Plan.Slot, nextFixture.Base, false) == "apply" &&
                    reopened.Receipt(recoveryFixture.Plan.Slot).Plan.Slot == recoveryFixture.Plan.Slot,
                    "Migration preserves completed receipt and pending baseline independently.");

            string interruptedCurrent = Path.Combine(root, "interrupted-current");
            string interruptedHash;
            using (var interrupted = SceneMidiRecoveryStore.Create(interruptedCurrent,
                [nextFixture.Plan], SceneMidiRecoveryChecks.RequestHash))
            { interrupted.Attempt(nextFixture.Plan.Slot, "load"); interruptedHash = interrupted.StateHash; }
            var (interruptedPath, olderInterruptedHash) = CopyRecoveryWithRelease(interruptedCurrent,
                Path.Combine(root, "interrupted-sources"), "0.16.0");
            string interruptedDestination = Path.Combine(root, "interrupted-migration");
            Refuses(() => SceneMidiRecoveryStore.Migrate(interruptedPath, olderInterruptedHash, interruptedDestination),
                "Interrupted write attempt remains release-bound even with no saved result.");
            Need(!Directory.Exists(interruptedDestination) && interruptedHash.Length == 64,
                "Rejected uncertain migration creates no destination.");
            using (var undoing = SceneMidiRecoveryStore.Open(mixedCurrent,
                PresetBody.Sha256(File.ReadAllBytes(Path.Combine(mixedCurrent, "state.private.json")))))
                undoing.Attempt(recoveryFixture.Plan.Slot, "undo-load");
            var (undoingPath, undoingHash) = CopyRecoveryWithRelease(mixedCurrent,
                Path.Combine(root, "undoing-sources"), "0.25.0");
            string undoingDestination = Path.Combine(root, "undoing-migration");
            Refuses(() => SceneMidiRecoveryStore.Migrate(undoingPath, undoingHash, undoingDestination),
                "Interrupted undo attempt remains release-bound even beside an untouched pending item.");
            Need(!Directory.Exists(undoingDestination), "Rejected undo migration creates no destination.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task Apply(PresetEditPlan plan, SavedCapture baseline, string receipt, DeviceDefinition d)
    {
        var execution = new PresetEditExecution(plan);
        var midi = new FakeEditorMidi { Scope = execution.Scope };
        var saved = baseline; var live = baseline; var upload = new List<byte>();
        midi.Reply = (q, m) => PresetEditChecks.Reply(q, m, d, ref saved, ref live, upload);
        await using var session = new ReadSession(midi);
        var result = await execution.RunAsync(session, new(d, "99.07", "synthetic"), receipt,
            TimeSpan.FromSeconds(1), default, TimeSpan.Zero);
        Need(result.Complete && saved.Sysex.SequenceEqual(plan.Expected), "Complete edit receipt must verify the saved image.");
    }

    private static (string Path, string Hash) CopyRecoveryWithRelease(string source, string root, string release, byte[]? alteredResult = null)
    {
        string target = Path.Combine(root, "recovery-" + release.Replace('.', '-') + (alteredResult == null ? "" : "-altered"));
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        string statePath = Path.Combine(target, "state.private.json");
        var envelope = JsonNode.Parse(File.ReadAllBytes(statePath))!;
        var payload = JsonNode.Parse(Convert.FromBase64String(envelope["payload"]!.GetValue<string>()))!;
        payload["protocol_release"] = release;
        if (alteredResult != null)
        {
            var item = payload["items"]![0]!;
            item["result"] = Convert.ToBase64String(alteredResult);
            item["reloaded"] = Convert.ToBase64String(alteredResult);
            item["result_hash"] = PresetBody.Sha256(alteredResult);
        }
        byte[] payloadWire = System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString());
        envelope["payload"] = Convert.ToBase64String(payloadWire);
        envelope["payload_sha256"] = PresetBody.Sha256(payloadWire);
        File.WriteAllText(statePath, envelope.ToJsonString());
        return (target, PresetBody.Sha256(File.ReadAllBytes(statePath)));
    }

    private static void Put(byte[] body, int offset, int value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(offset), (ushort)value);
    private static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Refuses(Action action, string message)
    { try { action(); } catch (InvalidDataException) { return; } throw new Exception(message + " was accepted."); }
}
