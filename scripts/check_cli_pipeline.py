#!/usr/bin/env python3
"""Exercise contained .NET -> .syx -> CLI decode -> Python plan for every model, offline."""
import json
import base64
import hashlib
from pathlib import Path
import subprocess
import sys
import tempfile
import argparse
import uuid

ROOT = Path(__file__).resolve().parents[1]


def run(*args, expected=0, error_contains=None):
    result = subprocess.run(args, cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
    if result.returncode != expected:
        raise RuntimeError(f"{args[0]} exited {result.returncode}:\n{result.stdout}\n{result.stderr}")
    if error_contains is not None and error_contains not in result.stderr:
        raise AssertionError(f"Expected compiled no-MIDI boundary: {result.stderr}")
    return result.stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--offline-windows', action='store_true', help='Compile the audited offline check allowlist and prohibit native/network MIDI connections.')
    parser.add_argument('--work-directory', type=Path, help='Fresh build/output directory for the offline Windows variant; must not exist.')
    options = parser.parse_args()
    if options.offline_windows:
        build = (options.work_directory or ROOT / '.build' / ('cli-offline-' + uuid.uuid4().hex)).resolve()
        build.mkdir(parents=True, exist_ok=False)
        targets = [('cli', 'src/Fractal.Cli/Fractal.Cli.csproj'),
                   ('hardware', 'tests/Fractal.HardwareChecks/Fractal.HardwareChecks.csproj'),
                   ('checks', 'tests/Fractal.Checks/Fractal.Checks.csproj')]
        for name, project in targets:
            log = run('dotnet', 'publish', project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
                      '--artifacts-path', str(build / 'build'), '-p:UseArtifactsOutput=true',
                      '-p:TestLabProfile=OfflineWindows', '-p:FractalNoMidi=true', '-o', str(build / name), '--nologo')
            (build / (name + '-publish.log')).write_text(log, encoding='utf-8')
        cli = build / 'cli/fractal.dll'
        hardware = build / 'hardware/Fractal.HardwareChecks.dll'
        checks = build / 'checks/Fractal.Checks.exe'
        print('Fresh audited offline pipeline build:', build, flush=True)
        boundary = 'MIDI connections are disabled in this offline pipeline build.'
        for selection in [('--input', '0', '--output', '0'), ('--network', '127.0.0.1:5004')]:
            run('dotnet', str(cli), 'inspect', '--model', 'FM9', *selection, expected=2, error_contains=boundary)
        print('PASS compiled boundary blocks native and RTP MIDI before opening connections', flush=True)
    else:
        if options.work_directory: parser.error('--work-directory requires --offline-windows')
        run("dotnet", "build", "src/Fractal.Cli/Fractal.Cli.csproj", "--nologo")
        run("dotnet", "build", "tests/Fractal.HardwareChecks/Fractal.HardwareChecks.csproj", "--nologo")
        hardware = ROOT / "tests/Fractal.HardwareChecks/bin/Debug/net10.0/Fractal.HardwareChecks.dll"
        cli = ROOT / "src/Fractal.Cli/bin/Debug/net10.0/fractal.dll"
    # Missing explicit opt-in must stop before enumerating/opening MIDI ports.
    run("dotnet", str(hardware), expected=2)
    with tempfile.TemporaryDirectory(prefix="fractal-offline-checks-") as work:
        work = Path(work)
        if options.offline_windows:
            output = run(str(checks), str(work / 'fixtures'))
            print(output, end='', flush=True)
        else:
            run("dotnet", "run", "--project", "tests/Fractal.Checks/Fractal.Checks.csproj", "--", str(work / "fixtures"))
        for model, capacity in [("FM9", 512), ("FM3", 512), ("Axe-Fx III", 512), ("Axe-Fx III", 1024)]:
            name = f"{model.replace(' ', '-')}-{capacity}"
            setlists = work / 'fixtures' / f'Setlists-{name}'
            song_plan = json.loads(run('dotnet', str(cli), 'plan-setlists', '--request', str(setlists / 'request.json'),
                '--inventory', str(setlists / 'inventory'), '--destination', str(work / f'setlists-{name}')))
            assert song_plan['plan']['dependencies_resolved'] and not song_plan['plan']['device_executable'] and not song_plan['plan']['editor_importable']
            assert song_plan['plan']['songs'][0]['sections'][0]['resolved_scene'] == 6 and not song_plan['ports_opened']
            assert song_plan['plan_sha256'] == hashlib.sha256((work / f'setlists-{name}/plan.json').read_bytes()).hexdigest()
            missing_songs = json.loads((setlists / 'request.json').read_text(encoding='utf-8'))
            missing_songs['songs'][0]['sections'][0]['preset_slot'] = 1
            missing_file = work / f'setlists-missing-{name}.json'; missing_file.write_text(json.dumps(missing_songs), encoding='utf-8')
            missing_plan = json.loads(run('dotnet', str(cli), 'plan-setlists', '--request', str(missing_file), '--inventory', str(setlists / 'inventory'),
                '--destination', str(work / f'setlists-missing-{name}')))
            assert not missing_plan['plan']['dependencies_resolved'] and missing_plan['plan']['unresolved_dependencies'][0]['code'] == 'preset-not-in-inventory'
            # A prior-release checkpoint must migrate explicitly; the source remains intact.
            prior_inventory = json.loads((setlists / 'inventory/inventory.json').read_text(encoding='utf-8'))
            prior_inventory['protocol_release'] = '0.37.0'
            (setlists / 'inventory/inventory.json').write_text(json.dumps(prior_inventory), encoding='utf-8')
            prior_hash = hashlib.sha256((setlists / 'inventory/inventory.json').read_bytes()).hexdigest()
            migrated = json.loads(run('dotnet', str(cli), 'inventory-migrate', '--inventory', str(setlists / 'inventory'), '--destination', str(work / f'setlists-migrated-{name}')))
            assert migrated['source_protocol_release'] == '0.37.0' and migrated['original_unchanged'] and not migrated['refreshed']
            assert prior_hash == hashlib.sha256((setlists / 'inventory/inventory.json').read_bytes()).hexdigest()
            print(f'PASS {name}: offline setlists/songs, symbolic default, missing references and prior inventory migration')
            organization = work / 'fixtures' / f'Organization-{name}'
            planned = json.loads(run('dotnet', str(cli), 'plan-organization', '--request', str(organization / 'request.json'), '--destination', str(work / f'organization-{name}')))
            inspected = json.loads(run('dotnet', str(cli), 'inspect-organization', '--bundle', str(work / f'organization-{name}'), '--expected-hash', planned['plan_sha256']))
            assert inspected['plan_sha256'] == planned['plan_sha256'] and planned['plan']['changed_count'] == 3 and not planned['ports_opened']
            organization_batch = work / f'organization-batch-{name}'
            compiled = json.loads(run('dotnet', str(cli), 'organization-batch', '--bundle', str(work / f'organization-{name}'), '--expected-hash', planned['plan_sha256'],
                '--context', str(organization / 'context.json'), '--destination', str(organization_batch)))
            assert len(compiled['batch']['ordered_targets']) == 3 and len(compiled['batch']['saved_guards']) == 3
            for mode in ('apply', 'reconcile'):
                reviewed = json.loads(run('dotnet', str(hardware), '--hardware', '--edit-batch', '--bundle', str(organization_batch),
                    '--expected-hash', hashlib.sha256((organization_batch / 'batch.json').read_bytes()).hexdigest(), '--mode', mode, '--validate-only', 'true'))
                assert reviewed['model'] == model and not reviewed['ports_opened']
            current = json.loads((organization / 'request.json').read_text(encoding='utf-8'))
            current.pop('operations')
            current['captures'] = [{'slot': slot, 'sha256': hashlib.sha256((organization / 'execution' / f'item-{index:03}' / 'verified-reloaded.syx').read_bytes()).hexdigest(),
                'capture': base64.b64encode((organization / 'execution' / f'item-{index:03}' / 'verified-reloaded.syx').read_bytes()).decode('ascii')}
                for index, slot in enumerate([1, 2, 0])]
            current_file = work / f'organization-current-{name}.json'; current_file.write_text(json.dumps(current), encoding='utf-8')
            undone = json.loads(run('dotnet', str(cli), 'plan-organization-undo', '--receipt', str(organization / 'execution'),
                '--expected-hash', hashlib.sha256((organization / 'execution/progress.json').read_bytes()).hexdigest(), '--request', str(current_file), '--destination', str(work / f'organization-undo-{name}')))
            assert undone['eligible_count'] == 3 and not undone['ports_opened']
            run('dotnet', str(cli), 'inspect-organization', '--bundle', str(work / f'organization-{name}'), '--expected-hash', '0' * 64, expected=2)
            print(f'PASS {name}: immutable preset permutation, exact batch review, source guards and receipt-based undo without MIDI')
            performance = work / 'fixtures' / f'Performance-{name}'
            caps = json.loads(run('dotnet', str(cli), 'performance-capabilities', '--model', model, '--capacity', str(capacity), '--firmware', '99.07'))
            assert caps['minimum_bpm'] == 20 and caps['maximum_bpm'] == 250 and caps['tempo_query_requires_review'] and not caps['tuner_state_query_supported']
            for request_file in performance.glob('*-*.json'):
                reviewed = json.loads(run('dotnet', str(hardware), '--hardware', '--performance-control', '--request', str(request_file),
                    '--expected-hash', hashlib.sha256(request_file.read_bytes()).hexdigest(), '--validate-only', 'true'))
                assert reviewed['model'] == model and not reviewed['ports_opened'] and not reviewed['hardware_qualified']
            run('dotnet', str(hardware), '--hardware', '--performance-control', '--request', str(performance / 'tempo-set.json'), '--expected-hash', '0' * 64, '--validate-only', 'true', expected=2)
            capture = json.loads(run('dotnet', str(cli), 'capture-performance', '--model', model, '--capacity', str(capacity), '--firmware', '99.07',
                '--network', '127.0.0.1:5004', '--destination', str(work / 'performance-validation'), '--validate-only', 'true'))
            assert not capture['ports_opened'] and not capture['tempo_queried']
            print(f'PASS {name}: bounded performance capabilities, captures and exact review validation without MIDI')
            template_fixture = work / 'fixtures' / f'Templates-{name}'
            library = work / f'library-{name}'
            imported = json.loads(run('dotnet', str(cli), 'template-import', '--library', str(library), '--request', str(template_fixture / 'import.json'), '--capture', str(template_fixture / 'baseline.syx')))
            entry_id, entry_hash = imported['entry_id'], imported['entry_sha256']
            listed = json.loads(run('dotnet', str(cli), 'template-list', '--library', str(library), '--query', 'synthetic', '--tag', 'delay'))
            assert listed['total'] == 1 and listed['entries'][0]['entry_sha256'] == entry_hash
            inspected = json.loads(run('dotnet', str(cli), 'template-inspect', '--library', str(library), '--entry', entry_id, '--expected-hash', entry_hash))
            assert inspected['entry']['channel'] == 'C' and not inspected['ports_opened']
            copied = json.loads(run('dotnet', str(cli), 'plan-template', '--entry-file', str(library / f'{entry_id}.json'), '--entry-hash', entry_hash,
                '--request', str(template_fixture / 'apply.json'), '--capture', str(template_fixture / 'baseline.syx'), '--destination', str(work / f'template-plan-{name}')))
            assert copied['plan']['executable'] and (work / f'template-plan-{name}' / 'expected.syx').read_bytes() == (template_fixture / 'copy-expected.syx').read_bytes()
            swapped = json.loads(run('dotnet', str(cli), 'plan-edit', '--request', str(template_fixture / 'swap.json'), '--capture', str(template_fixture / 'baseline.syx'), '--destination', str(work / f'swap-plan-{name}')))
            assert swapped['plan']['executable'] and (work / f'swap-plan-{name}' / 'expected.syx').read_bytes() == (template_fixture / 'swap-expected.syx').read_bytes()
            for label, planned in [('template', copied), ('swap', swapped)]:
                checked = json.loads(run('dotnet', str(hardware), '--hardware', '--apply-preset-edit', '--bundle', str(work / f'{label}-plan-{name}'), '--expected-hash', planned['plan_sha256'], '--validate-only', 'true'))
                assert not checked['ports_opened']
            run('dotnet', str(cli), 'template-inspect', '--library', str(library), '--entry', entry_id, '--expected-hash', '0' * 64, expected=2)
            print(f'PASS {name}: private library import/search/inspection/application and channel swap reviews')
            if model == 'Axe-Fx III':
                scales = work / 'fixtures' / f'Scales-{capacity}'
                scale_catalogue = json.loads(run('dotnet', str(cli), 'edit-catalogue', '--model', model, '--capacity', str(capacity), '--firmware', '32.02'))
                assert len(scale_catalogue['builtin_scales']['fields']) == 59
                scaled = json.loads(run('dotnet', str(cli), 'plan-edit', '--request', str(scales / 'request.json'), '--capture', str(scales / 'baseline.syx'), '--destination', str(work / f'scaled-{capacity}')))
                assert scaled['plan']['executable'] and (work / f'scaled-{capacity}' / 'expected.syx').read_bytes() == (scales / 'expected.syx').read_bytes()
                scale_review = json.loads(run('dotnet', str(hardware), '--hardware', '--apply-preset-edit', '--bundle', str(work / f'scaled-{capacity}'), '--expected-hash', scaled['plan_sha256'], '--validate-only', 'true'))
                assert not scale_review['ports_opened']
                print(f'PASS {name}: built-in signed/logarithmic scales and named modifier curve review')
            folder = work / "decoded" / name
            fixture = work / "fixtures" / name
            calibration_fixture = work / 'fixtures' / f'Calibration-{name}'
            calibrated = json.loads(run('dotnet', str(cli), 'calibrate-parameter', '--request', str(calibration_fixture / 'calibration.json'), '--destination', str(work / f'calibration-{name}')))
            assert not calibrated['ports_opened'] and calibrated['calibration']['relative_offset'] == 100
            authored = json.loads(run('dotnet', str(cli), 'plan-edit', '--request', str(calibration_fixture / 'request.json'), '--capture', str(calibration_fixture / 'baseline.syx'), '--destination', str(work / f'calibrated-plan-{name}')))
            assert authored['plan']['executable'] and (work / f'calibrated-plan-{name}' / 'expected.syx').read_bytes() == (calibration_fixture / 'expected.syx').read_bytes()
            checked = json.loads(run('dotnet', str(hardware), '--hardware', '--apply-preset-edit', '--bundle', str(work / f'calibrated-plan-{name}'), '--expected-hash', authored['plan_sha256'], '--validate-only', 'true'))
            assert not checked['ports_opened']
            print(f'PASS {name}: exact calibration, Scene Ignore and modifier compound plan/execution review')
            path_fixture = work / 'fixtures' / f'Path-{name}'
            path_bundle = work / f'path-{name}'
            automatic = json.loads(run('dotnet', str(cli), 'plan-audio-path', '--request', str(path_fixture / 'request.json'),
                                       '--capture', str(path_fixture / 'baseline.syx'), '--destination', str(path_bundle)))
            assert automatic['plan']['executable'] and automatic['added_shunts'] == 3 and automatic['added_edges'] == 4
            prepared = json.loads(run('dotnet', str(hardware), '--hardware', '--apply-preset-edit', '--bundle', str(path_bundle),
                                      '--expected-hash', automatic['plan_sha256'], '--validate-only', 'true'))
            assert not prepared['ports_opened'] and not prepared['plan']['hardware_qualified']
            listener = json.loads(run('dotnet', str(cli), 'listen-midi', '--request', str(path_fixture / 'listener.json'),
                                     '--duration', '1', '--max-events', '16', '--validate-only', 'true'))
            assert listener['model'] == model and listener['sends'] == 0 and not listener['ports_opened']
            run('dotnet', str(cli), 'listen-midi', '--request', str(path_fixture / 'listener.json'), '--duration', '3601', '--max-events', '16', '--validate-only', 'true', expected=2)
            notification = json.loads(run('dotnet', str(cli), 'listen-notifications', '--model', model, '--capacity', str(capacity), '--firmware', '99.07',
                                          '--duration', '1', '--max-events', '16', '--network', '127.0.0.1:5004', '--validate-only', 'true'))
            assert not notification['ports_opened'] and notification['midi_sends'] == 0
            block_read = json.loads(run('dotnet', str(cli), 'read-block-parameters', '--model', model, '--capacity', str(capacity), '--firmware', '99.07',
                                        '--effect-id', '66', '--network', '127.0.0.1:5004', '--destination', str(work / 'block-read'), '--validate-only', 'true'))
            assert not block_read['ports_opened'] and block_read['effect_id'] == 66
            run('dotnet', str(cli), 'read-block-parameters', '--model', model, '--capacity', str(capacity), '--firmware', '99.07',
                '--effect-id', '1001', '--network', '127.0.0.1:5004', '--destination', str(work / 'bad-block-read'), '--validate-only', 'true', expected=2)
            network_review = json.loads(run('dotnet', str(hardware), '--hardware', '--apply-preset-edit', '--bundle', str(path_bundle),
                                           '--expected-hash', automatic['plan_sha256'], '--network', '127.0.0.1:5004', '--validate-only', 'true'))
            assert not network_review['ports_opened']
            run('dotnet', str(cli), 'listen-notifications', '--model', model, '--capacity', str(capacity), '--firmware', '99.07',
                '--duration', '1', '--max-events', '16', '--network', '127.0.0.1:5004', '--input', '0', '--validate-only', 'true', expected=2)
            print(f'PASS {name}: automatic path bundle, execution review and receive-only listener validation')
            batch_source = work / 'fixtures' / f'Batch-{name}'
            batch_output = work / f'batch-{name}'
            batch = json.loads(run('dotnet', str(cli), 'create-edit-batch', '--request', str(batch_source / 'batch.json'), '--destination', str(batch_output)))
            assert len(batch['batch']['ordered_targets']) == 2 and not batch['batch']['ports_opened']
            for mode in ('apply', 'reconcile'):
                checked = json.loads(run('dotnet', str(hardware), '--hardware', '--edit-batch', '--bundle', str(batch_output),
                                         '--expected-hash', batch['batch_sha256'], '--mode', mode, '--validate-only', 'true'))
                assert checked['model'] == model and not checked['hardware_qualified'] and not checked['ports_opened']
            run('dotnet', str(hardware), '--hardware', '--edit-batch', '--bundle', str(batch_output), '--expected-hash', '0' * 64, '--validate-only', 'true', expected=2)
            configured = work / 'fixtures' / f'Configured-{name}' / 'request.json'
            cc = json.loads(run('dotnet', str(hardware), '--hardware', '--live-control', '--request', str(configured),
                                '--expected-hash', hashlib.sha256(configured.read_bytes()).hexdigest(), '--validate-only', 'true'))
            assert cc['transport'] == 'configured-cc' and cc['midi_assignment'] == {'channel': 16, 'controller': 17, 'value': 99}
            recall = work / 'fixtures' / f'Recall-{name}' / 'load-request.json'
            pc = json.loads(run('dotnet', str(hardware), '--hardware', '--load-preset', '--request', str(recall),
                                '--expected-hash', hashlib.sha256(recall.read_bytes()).hexdigest(), '--validate-only', 'true'))
            recall_wire = pc['wire_messages']
            assert recall_wire[:2] == ['BF0001', 'CF01'] and len(recall_wire) == 3 and not pc['ports_opened']
            scene_wire = bytes.fromhex(recall_wire[2])
            assert len(scene_wire) == 9 and scene_wire[5] == 0x0c and scene_wire[6] == pc['target_scene'] - 1
            routed = json.loads(run('dotnet', str(cli), 'route-midi', '--request', str(recall.parent / 'routing.json')))
            assert routed['events'][0]['target']['slot'] == 129 and routed['events'][1]['target'] is None and not routed['ports_opened']
            print(f'PASS {name}: batch bundles, configured CC/bank/PC reviews and note routing, all offline')
            live_request = work / 'fixtures' / f'Live-{name}' / 'request.json'
            live_hash = hashlib.sha256(live_request.read_bytes()).hexdigest()
            live_validation = json.loads(run('dotnet', str(hardware), '--hardware', '--live-control', '--request', str(live_request),
                                            '--expected-hash', live_hash, '--validate-only', 'true'))
            assert live_validation['model'] == model and not live_validation['ports_opened'] and live_validation['persistence'] == 'live-only-no-save'
            run('dotnet', str(hardware), '--hardware', '--live-control', '--request', str(live_request), '--expected-hash', '0' * 64, '--validate-only', 'true', expected=2)
            run('dotnet', str(cli), 'amp-roster', '--model', model, '--capacity', str(capacity), '--firmware', '99.07',
                '--start', '16383', '--count', '2', '--destination', str(work / 'invalid-roster'), expected=2)
            edit_bundle = work / "fixtures" / f"Edit-{name}"
            edit_hash = (edit_bundle / "plan-sha256.txt").read_text()
            checked_edit = json.loads(run("dotnet", str(cli), "inspect-edit-plan", "--bundle", str(edit_bundle), "--expected-hash", edit_hash))
            assert checked_edit['plan']['executable'] and checked_edit['plan']['status'] == 'planned'
            assert checked_edit['plan']['hardware_qualified'] is False and checked_edit['plan']['ports_opened'] is False
            authored_bundle = work / f"authored-{name}"
            replanned_edit = json.loads(run("dotnet", str(cli), "plan-edit", "--request", str(edit_bundle / "request.json"),
                                           "--capture", str(edit_bundle / "baseline.syx"), "--destination", str(authored_bundle)))
            assert replanned_edit['plan_sha256'] == edit_hash
            assert (authored_bundle / 'expected.syx').read_bytes() == (edit_bundle / 'expected.syx').read_bytes()
            validated_edit = json.loads(run("dotnet", str(hardware), "--hardware", "--apply-preset-edit", "--bundle", str(authored_bundle),
                                           "--expected-hash", edit_hash, "--validate-only", "true"))
            assert validated_edit['ports_opened'] is False and validated_edit['plan']['executable']
            run("dotnet", str(cli), "inspect-edit-plan", "--bundle", str(edit_bundle), "--expected-hash", '0' * 64, expected=2)
            run("dotnet", str(cli), "plan-edit", "--request", str(edit_bundle / 'request.json'), "--capture", str(edit_bundle / 'baseline.syx'),
                "--input", "0", expected=2)
            print(f"PASS {name}: compound edit planning, deterministic authored image and offline execution validation")
            inventory = work / "fixtures" / f"Inventory-{name}"
            report = json.loads(run("dotnet", str(cli), "inventory-inspect", "--inventory", str(inventory)))
            assert report["model"] == model and report["capacity"] == capacity
            assert report["status"] == "complete" and report["capture_complete"] and report["decode_complete"]
            assert report["scope"] == "selected-slots" and report["captured_slots"] == 2
            assert report["ports_opened"] is False and report["physical_identity"] == "not-established"
            comparison = json.loads(run("dotnet", str(cli), "inventory-compare", "--before", str(inventory), "--after", str(inventory)))
            assert not comparison['full_capacity_comparison'] and all(c['status'] == 'identical-raw' for c in comparison['changes'])
            usage = json.loads(run("dotnet", str(cli), "inventory-amp-usage", "--inventory", str(inventory), "--amp-type", '20'))
            assert usage['captured_slots'] == 2 and not usage['full_capacity_decoded'] and not usage['ports_opened']
            page = json.loads(run("dotnet", str(cli), "inventory-inspect", "--inventory", str(inventory), "--offset", '1', "--limit", '1'))
            assert len(page['entries']) == 1 and page['entry_count'] == 2 and page['capture_complete']
            roster = work / f'roster-{name}.json'
            roster.write_text(json.dumps({'model': model, 'firmware': '99.07', 'entries': [{'id': 20, 'name': 'Synthetic'}]}))
            roster_usage = json.loads(run("dotnet", str(cli), "inventory-amp-usage", "--inventory", str(inventory), "--roster", str(roster), "--limit", '1'))
            assert roster_usage['absent_ids'] is None and len(roster_usage['uses']) <= 1
            roster.write_text('{')
            run("dotnet", str(cli), "inventory-amp-usage", "--inventory", str(inventory), "--roster", str(roster), expected=2)
            run("dotnet", str(cli), "inventory-compare", "--before", str(inventory), "--after", str(inventory), "--input", "0", expected=2)
            exported_inventory = work / f"inventory-export-{name}"
            exported = json.loads(run("dotnet", str(cli), "inventory-export", "--inventory", str(inventory),
                                      "--destination", str(exported_inventory)))
            assert exported["decoded_presets"] == 2 and exported["decode_errors"] == []
            for entry in report["entries"]:
                capture = entry["capture"]
                assert hashlib.sha256((exported_inventory / f"preset-{entry['slot']:04}.syx").read_bytes()).hexdigest() == capture["sysex_sha256"]
            run("dotnet", str(cli), "inventory-export", "--inventory", str(inventory), "--destination", str(exported_inventory), expected=2)
            run("dotnet", str(cli), "inventory-inspect", "--inventory", str(inventory), "--input", "0", expected=2)
            # Complete checkpoints and conflicting scopes refuse before a platform transport can be opened.
            run("dotnet", str(cli), "inventory", "--resume", str(inventory), "--input", "0", "--output", "0", expected=2)
            run("dotnet", str(cli), "inventory", "--resume", str(inventory), "--all", "--input", "0", "--output", "0", expected=2)
            checkpoint = inventory / "inventory.json"
            original_checkpoint = checkpoint.read_bytes()
            partial = json.loads(original_checkpoint)
            partial["attempts"][-1]["outcome"] = "failed"
            checkpoint.write_text(json.dumps(partial), encoding="utf-8")
            run("dotnet", str(cli), "inventory-export", "--inventory", str(inventory), "--destination", str(work / f"partial-{name}"), expected=2)
            checkpoint.write_bytes(original_checkpoint)
            print(f"PASS {name}: resumable inventory inspection/export, retained hashes and offline refusal guards")
            edit_args = ["--model", model, "--capacity", str(capacity), "--firmware", "99.07"]
            caps = json.loads(run("dotnet", str(cli), "edit-capabilities", *edit_args))
            assert caps["scope"]["model"] == model and caps["scope"]["capacity"] == capacity
            assert caps["ports_opened"] is False and caps["execution_supported"] is True
            assert next(c for c in caps["capabilities"] if c["operation"] == "connected-insertion-removal")["write_supported"] is True
            assert caps["scope"]["catalogue_sha256"] == hashlib.sha256((ROOT / "knowledge-base/catalog/edit-read-profiles.json").read_bytes()).hexdigest()
            catalogue = json.loads(run("dotnet", str(cli), "edit-catalogue", *edit_args, "--family", "Amp"))
            assert catalogue["parameters"][0]["key"] == "amp.type"
            assert len(catalogue["parameters"]) == (1 if model == "Axe-Fx III" else 10)
            editor = catalogue["editor_parameters"]
            assert editor["model"] == model and editor["write_supported"] is False
            assert editor["catalogue_sha256"] == hashlib.sha256((ROOT / "knowledge-base/catalog/audio-parameter-mappings.json").read_bytes()).hexdigest()
            assert next(p for p in editor["entries"] if p["symbol"] == "DISTORT_DRIVE")["parameter_id"] == {"FM9": 11, "FM3": 7, "Axe-Fx III": 1}[model]
            assert json.loads(run("dotnet", str(cli), "edit-catalogue", *edit_args, "--family", "Cab"))["parameters"]
            edit_capture = work / "fixtures" / f"Edit-{name}" / "synthetic.syx"
            original_edit = edit_capture.read_bytes()
            edit_read = json.loads(run("dotnet", str(cli), "decode-edit", *edit_args, "--capture", str(edit_capture)))
            assert edit_read["schema_version"] == 1 and edit_read["status"] == "partial"
            assert edit_read["routing"]["edges"] == [] and edit_read["routing"]["words"][0]["unknown_bits"] == 0x8000
            assert edit_read["scene_names"][0]["text"] == "Synthetic clean"
            amps = [b for b in edit_read["blocks"] if b["effect_id"] in (58, 59)]
            assert len(amps) == (1 if model == "FM3" else 2)
            assert all(b["identity_status"] == "exact-prefix-match" and b["parameters"][0]["raw_value"] == b["effect_id"] * 10 for b in amps)
            assert edit_read["sysex_sha256"] == hashlib.sha256(original_edit).hexdigest()
            assert hashlib.sha256(bytes.fromhex(edit_read["body_hex"])).hexdigest() == edit_read["body_sha256"]
            assert edit_capture.read_bytes() == original_edit and edit_read["ports_opened"] is False
            run("dotnet", str(cli), "decode-edit", *edit_args, "--capture", str(edit_capture), "--capture", str(edit_capture), expected=2)
            run("dotnet", str(cli), "edit-capabilities", *edit_args, "--input", "0", expected=2)
            run("dotnet", str(cli), "edit-capabilities", "--model", model, "--capacity", str(capacity), expected=2)
            audio_capture = work / "fixtures" / f"Audio-{name}" / "synthetic.syx"
            audio_original = audio_capture.read_bytes()
            audio = json.loads(run("dotnet", str(cli), "decode-edit", *edit_args, "--capture", str(audio_capture)))
            assert audio["routing"]["status"] == "decoded-candidate" and len(audio["routing"]["edges"]) == 5
            assert audio["routing"]["edges"][0] == {"source": "cell:1:1", "destination": "cell:2:2"}
            cab = next(b for b in audio["blocks"] if b["effect_id"] == 62)
            assert any(p["channel"] == "B" and p["status"] == "not-applicable" and p["value"] is None for p in cab["parameters"])
            amp = next(b for b in audio["blocks"] if b["effect_id"] == 58)
            if model != "Axe-Fx III":
                assert next(p for p in amp["parameters"] if p["key"] == "amp.drive" and p["channel"] == "C")["value"] == 10
            assert audio_capture.read_bytes() == audio_original and audio["ports_opened"] is False
            print(f"PASS {name}: offline audio mappings, exact instances, routing edges and unchanged captures")
            # A manufacturer-style backup header must pass offline integrity without
            # acquiring a slot or weakening the addressed saved-read contract.
            backup = bytearray((fixture / "preset-0000.syx").read_bytes())
            backup[6:8] = bytes([127, 0])
            checksum = 0
            for value in backup[:11]:
                checksum ^= value
            backup[11] = checksum & 127
            backup_path = work / f"{name}-backup.syx"
            backup_path.write_bytes(backup)
            checked = json.loads(run("dotnet", str(cli), "check-backup", "--model", model, "--capacity", str(capacity),
                                     "--capture", str(backup_path)))
            assert checked["header_payload"][:2] == [127, 0] and checked["restore_verified"] is False
            assert checked["model"] == model and checked["raw_bytes"] == (32768 if model == "Axe-Fx III" else 16384)
            assert checked["destination_semantics"] == "unqualified-opaque-header" and "name" not in checked
            run("dotnet", str(cli), "decode", "--model", model, "--firmware", "99.07", "--capacity", str(capacity),
                "--capture", str(backup_path), "--destination", str(work / f"{name}-invalid-address"), expected=2)
            run("dotnet", str(cli), "check-backup", "--model", model, "--capacity", str(capacity),
                "--capture", str(backup_path), "--capture", str(backup_path), expected=2)
            run("dotnet", str(cli), "decode", "--model", model, "--firmware", "99.07", "--capacity", str(capacity),
                "--capture", str(fixture / f"preset-{capacity - 1:04}.syx"), "--capture", str(fixture / "preset-0000.syx"),
                "--capture", str(fixture / "preset-0001.syx"),
                "--capture", str(fixture / "preset-0002.syx"),
                "--destination", str(folder))
            plan = json.loads(run(sys.executable, "scripts/plan_scene_midi.py", "--snapshot", str(folder / "snapshot.json"),
                                  "--all-populated", "--channel", "1", "--value", "1",
                                  "--block-row", "3", "--block-column", "10"))
            assert plan["device"]["model"] == model and plan["device"]["preset_capacity"] == capacity
            assert plan["summary"] == {"planned": 1, "unchanged": 1, "skipped": 0, "conflict": 2}
            conflict = next(p for p in plan["presets"] if p["slot"] == 1)
            assert conflict["actions"] == [] and conflict["conflicts"][0]["kind"] == "occupied-message-slot"
            assert conflict["conflicts"][0]["existing"]["type"] == "CC"
            unread = next(p for p in plan["presets"] if p["slot"] == 2)
            assert unread["actions"] == [] and unread["conflicts"][0]["kind"] == "unread-scene-midi-table"
            data = json.loads((folder / "snapshot.json").read_text())
            blocks = json.loads(run(sys.executable, "scripts/plan_block_insertion.py", "--snapshot", str(folder / "snapshot.json"),
                                    "--preset", str(capacity - 1), "--effect-id", "190", "--row", "3", "--column", "10"))
            inserted = next(p for p in plan["presets"] if p["slot"] == capacity - 1)
            assert blocks["presets"][0]["actions"] == inserted["actions"][:1]
            assert blocks["validation_scope"] == "grid-placement-only"
            assert data["schema_version"] == 3
            assert all(len(s["messages"]) == 8 for s in data["presets"][0]["scenes"])
            assert data["presets"][0]["scenes"][0]["messages"][1]["raw"]["value"] == 54321
            existing = work / "fixtures" / f"SceneMidi-{name}"
            execute_plan = json.loads(run(sys.executable, "scripts/plan_scene_midi.py", "--snapshot", str(existing / "snapshot.json"),
                                         "--preset", str(capacity - 1), "--scenes", "2", "8", "--message-slot", "3",
                                         "--type", "CC", "--controller", "119", "--channel", "5", "--value", "126"))
            execute_request = work / f"{name}-execute.json"
            execute_request.write_text(json.dumps(execute_plan), encoding="utf-8")
            execute_args = ["dotnet", str(hardware), "--hardware", "--apply-scene-midi", "--validate-only", "true",
                            "--model", model, "--capacity", str(capacity), "--firmware", "99.07", "--backup", str(existing),
                            "--plan", str(execute_request), "--expected-hash", hashlib.sha256(execute_request.read_bytes()).hexdigest(),
                            "--undo-after", "false"]
            validated = json.loads(run(*execute_args))
            assert validated["changed_targets"] == 1 and validated["field_commands"] == 8 and validated["ports_opened"] is False
            recovery = work / "fixtures" / f"Recovery-{name}"
            state = recovery / "state.private.json"
            recovery_args = ["dotnet", str(hardware), "--hardware", "--scene-midi-recovery", "--mode", "inspect",
                             "--recovery", str(recovery), "--expected-hash", hashlib.sha256(state.read_bytes()).hexdigest()]
            inspected = json.loads(run(*recovery_args))
            assert inspected["model"] == model and inspected["items"][0]["status"] == "pending"
            assert inspected["ports_opened"] is False and inspected["fresh_hardware_reconciled"] is False
            recovery_args[recovery_args.index("--mode") + 1] = "undo"
            run(*recovery_args, "--validate-only", "true", expected=2)
            recovery_args[recovery_args.index("--mode") + 1] = "resume"
            run(*recovery_args, "--validate-only", "true")
            recovery_args[recovery_args.index("--expected-hash") + 1] = "0" * 64
            run(*recovery_args, "--input", "9999", "--output", "9999", "--destination", str(work / f"bad-recovery-{name}"), expected=2)
            assert not (work / f"bad-recovery-{name}").exists()
            prior = work / 'fixtures' / f'SelectiveRecovery-{name}' / 'prior-recovery'
            prior_hash = hashlib.sha256((prior / 'state.private.json').read_bytes()).hexdigest()
            migrated_recovery = work / f'migrated-recovery-{name}'
            migrated = json.loads(run('dotnet', str(cli), 'recovery-migrate', '--recovery', str(prior),
                                      '--state-hash', prior_hash, '--destination', str(migrated_recovery)))
            assert migrated['source_protocol_release'] == '0.8.0' and migrated['edited_count'] == 1
            assert migrated['source_state_sha256'] == prior_hash and migrated['ports_opened'] is False
            assert hashlib.sha256((prior / 'state.private.json').read_bytes()).hexdigest() == prior_hash
            assert hashlib.sha256((migrated_recovery / 'state.private.json').read_bytes()).hexdigest() == migrated['target_state_sha256']
            run('dotnet', str(cli), 'recovery-migrate', '--recovery', str(prior), '--state-hash', '0' * 64,
                '--destination', str(work / f'bad-migrated-recovery-{name}'), expected=2)
            assert not (work / f'bad-migrated-recovery-{name}').exists()
            # Retaining a valid request hash cannot make forged before-fields or extra writes executable.
            execute_plan["presets"][0]["actions"][0]["before_message"]["raw"]["controller"] += 1
            execute_request.write_text(json.dumps(execute_plan), encoding="utf-8")
            execute_args[execute_args.index("--expected-hash") + 1] = hashlib.sha256(execute_request.read_bytes()).hexdigest()
            run(*execute_args, expected=2)
            cc = json.loads(run(sys.executable, "scripts/plan_scene_midi.py", "--snapshot", str(folder / "snapshot.json"),
                                "--preset", "1", "--scenes", "1", "--type", "CC", "--controller", "34", "--channel", "1", "--value", "127"))
            assert cc["summary"]["unchanged"] == 1
            run("dotnet", str(cli), "decode", "--model", model, "--firmware", "99.07", "--capacity", str(capacity),
                "--capture", str(fixture / "preset-0000.syx"), "--destination", str(folder), expected=2)
            print(f"PASS CLI backup/decode/plan/executor-validation/preservation/no-overwrite: {name}")
        unknown = work / "unknown"
        base = work / "fixtures/FM9-512/preset-0000.syx"
        base_hash = hashlib.sha256(base.read_bytes()).hexdigest()
        save_options = ["dotnet", str(hardware), "--hardware", "--save-current", "--input", "9999", "--output", "9999",
                        "--model", "FM9", "--preset", "0", "--scene", "1", "--firmware", "99.07",
                        "--expected-channels", "58:1", "--backup", str(base), "--expected-hash", "0" * 64,
                        "--current-backup", str(base), "--current-hash", base_hash]
        # Invalid hash/target must be rejected before attempting to open even nonexistent ports.
        def preflight_failure(options, label, message):
            result = subprocess.run(options + ["--destination", str(work / label)], cwd=ROOT,
                                    capture_output=True, text=True, encoding="utf-8")
            assert result.returncode == 2 and message in result.stderr, result.stdout + result.stderr
        preflight_failure(save_options, "bad-save-hash", "Retained file hash")
        save_options[save_options.index("--expected-hash") + 1] = base_hash
        save_options[save_options.index("--preset") + 1] = "1"
        preflight_failure(save_options, "bad-save-slot", "Retained base is for another slot")
        save_options[save_options.index("--scene") + 1] = "9"
        preflight_failure(save_options, "bad-save-scene", "Scene must be")
        restore_options = ["dotnet", str(hardware), "--hardware", "--restore-preset", "--input", "9999", "--output", "9999",
                           "--model", "FM9", "--preset", "0", "--scene", "1", "--firmware", "99.07",
                           "--backup", str(base), "--expected-hash", "0" * 64,
                           "--current-backup", str(base), "--current-hash", base_hash]
        preflight_failure(restore_options, "bad-restore-hash", "Retained file hash mismatch")
        restore_options[restore_options.index("--expected-hash") + 1] = base_hash
        restore_options[restore_options.index("--preset") + 1] = "1"
        preflight_failure(restore_options, "bad-restore-base-slot", "Retained saved base is for another slot")
        request = work / "insertion-request.json"
        request.write_text("[]", encoding="utf-8")
        insertion_options = ["dotnet", str(hardware), "--hardware", "--insert-blocks", "--input", "9999", "--output", "9999",
                             "--model", "FM9", "--firmware", "99.07", "--backup", str(work / "fixtures/FM9-512"),
                             "--plan", str(request), "--expected-hash", "0" * 64]
        preflight_failure(insertion_options, "bad-insertion-request-hash", "Insertion request hash/size mismatch")
        insertion_options[-1] = hashlib.sha256(request.read_bytes()).hexdigest()
        preflight_failure(insertion_options, "empty-insertion-request", "distinct explicit targets")
        request.write_text(json.dumps([dict(slot=0,row=1,column=1,effect_id=114,saved_scene=1,sysex_sha256=base_hash)] * 2), encoding="utf-8")
        insertion_options[-1] = hashlib.sha256(request.read_bytes()).hexdigest()
        preflight_failure(insertion_options, "duplicate-insertion-request", "distinct explicit targets")
        run("dotnet", str(cli), "decode", "--model", "FM9", "--firmware", "99.07", "--capture",
            str(work / "fixtures/unknown-body/preset-0002.syx"), "--destination", str(unknown), expected=4)
        run(sys.executable, "scripts/plan_scene_midi.py", "--snapshot", str(unknown / "snapshot.json"),
            "--all-populated", "--channel", "1", "--value", "1", expected=2)
        assert (unknown / "manifest.json").is_file()
        run("dotnet", str(cli), "send", expected=2)
        corrupt = work / "corrupt.syx"
        data = bytearray((work / "fixtures/FM9-512/preset-0000.syx").read_bytes())
        data[100] ^= 1
        corrupt.write_bytes(data)
        error = subprocess.run(["dotnet", str(cli), "decode", "--model", "FM9", "--firmware", "12.00", "--capture",
                                str(corrupt), "--destination", str(work / "invalid")], cwd=ROOT, capture_output=True, text=True)
        assert error.returncode == 2 and "Unhandled exception" not in error.stderr and error.stderr.startswith("error:")
        run("dotnet", str(cli), "check-backup", "--model", "FM9", "--capture", str(corrupt), expected=2)
        run("dotnet", str(cli), "decode-edit", "--model", "FM9", "--firmware", "12.00", "--capture", str(corrupt), expected=2)
        run("dotnet", str(cli), "edit-capabilities", "--model", "Axe-Fx III", "--firmware", "99.07", expected=2)
        run("dotnet", str(cli), "edit-catalogue", "--model", "FM9", "--firmware", "99.7", expected=2)
        print("PASS unsupported body retained, planning refused; arbitrary send absent")
    print("CLI pipeline checks passed. No MIDI ports opened.")


if __name__ == "__main__":
    main()
