#!/usr/bin/env python3
"""Exercise a relocated MCP bundle through real stdio and contained .NET helpers. Never open MIDI."""
import json
import hashlib
from pathlib import Path
import queue
import shutil
import subprocess
import sys
import tempfile
import threading
import argparse
import contextlib
import os

ROOT = Path(__file__).resolve().parents[1]


def run(*arguments):
    result = subprocess.run(arguments, cwd=ROOT, capture_output=True, text=True, encoding='utf-8')
    if result.returncode:
        raise RuntimeError(f'Offline helper failed: {result.stdout}\n{result.stderr}')
    return result.stdout


class Client:
    def __init__(self, bundle, config):
        self.process = subprocess.Popen([sys.executable, '-X', 'utf8', str(bundle / 'scripts/fractal_mcp.py'), '--config', str(config)],
            cwd=config.parent, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8', bufsize=1)
        self.messages = queue.Queue(); self.errors = []; self.next_id = 0
        def read():
            for line in self.process.stdout:
                try: self.messages.put(json.loads(line))
                except ValueError: self.messages.put({'bad_stdout': line})
        def errors():
            self.errors.extend(self.process.stderr.readlines())
        self.reader = threading.Thread(target=read, daemon=True); self.reader.start()
        self.error_reader = threading.Thread(target=errors, daemon=True); self.error_reader.start()
        result = self.request('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'contained-offline-check', 'version': '1'}})
        assert result['protocolVersion'] == '2025-11-25'
        self.notify('notifications/initialized', {})

    def notify(self, method, params):
        self.process.stdin.write(json.dumps({'jsonrpc': '2.0', 'method': method, 'params': params}) + '\n'); self.process.stdin.flush()

    def request(self, method, params):
        self.next_id += 1
        self.process.stdin.write(json.dumps({'jsonrpc': '2.0', 'id': self.next_id, 'method': method, 'params': params}) + '\n'); self.process.stdin.flush()
        reply = self.messages.get(timeout=120)
        assert reply.get('jsonrpc') == '2.0' and reply.get('id') == self.next_id, reply
        if 'error' in reply:
            raise ValueError(reply['error'])
        return reply['result']

    def call(self, name, arguments, error=False):
        result = self.request('tools/call', {'name': 'fractal_' + name, 'arguments': arguments})
        assert result.get('isError', False) == error, result
        if error:
            return result
        value = result['structuredContent']
        assert json.loads(result['content'][0]['text']) == value
        return value

    def close(self):
        self.process.stdin.close(); self.process.wait(timeout=20); self.reader.join(timeout=2); self.error_reader.join(timeout=2)
        assert self.process.returncode == 0 and not self.errors, self.errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--offline-windows', action='store_true', help='Compile the audited offline source allowlist and disable native/network MIDI at the runtime boundary.')
    parser.add_argument('--work-directory', type=Path, help='Retain a new directory with relocated bundle, fixtures and artifacts.')
    options = parser.parse_args()
    if options.offline_windows:
        os.environ['TestLabProfile'] = 'OfflineWindows'
        os.environ['FractalNoMidi'] = 'true'
        print('MCP compiled boundary: OfflineWindows / FractalNoMidi=true', flush=True)
    if options.work_directory:
        options.work_directory.resolve().mkdir(parents=True, exist_ok=False)
    with (contextlib.nullcontext(str(options.work_directory.resolve())) if options.work_directory else tempfile.TemporaryDirectory(prefix='fractal-mcp-contained-')) as temporary:
        work = Path(temporary); bundle = work / 'bundle'; fixtures = work / 'fixtures'
        run(sys.executable, '-X', 'utf8', 'scripts/package_mcp.py', '--destination', str(bundle))
        run(sys.executable, '-X', 'utf8', str(bundle / 'scripts/protocol_release.py'))
        run(sys.executable, '-X', 'utf8', str(bundle / 'scripts/check_shared_midi.py'))
        run('dotnet', 'build', str(bundle / 'src/Fractal.Cli/Fractal.Cli.csproj'), '--configuration', 'Release', '--nologo')
        checks = run('dotnet', 'run', '--project', str(bundle / 'tests/Fractal.Checks/Fractal.Checks.csproj'), '--', str(fixtures))
        assert 'checks passed. Synthetic transport/data only; no hardware ports opened.' in checks, checks[-1000:]
        for model, capacity in [('FM9', 512), ('FM3', 512), ('Axe-Fx III', 512), ('Axe-Fx III', 1024)]:
            data = work / f'data-{model.replace(" ", "-")}-{capacity}'; (data / 'artifacts').mkdir(parents=True)
            config = work / f'{model.replace(" ", "-")}-{capacity}.json'
            config.write_text(json.dumps({'schema_version': 1, 'data_root': str(data), 'runtime_dir': str(bundle / 'runtime'),
                'allow_live_reads': False, 'allow_writes': False,
                'device': {'model': model, 'capacity': capacity, 'firmware': '99.07', 'input': 12345, 'output': 12346}}), encoding='utf-8')
            from uuid import uuid4
            name = f'{model.replace(" ", "-")}-{capacity}'
            if model == 'Axe-Fx III':
                scale_config = work / f'scales-{capacity}.json'
                scale_settings = json.loads(config.read_text(encoding='utf-8'))
                scale_settings['device']['firmware'] = '32.02'
                scale_config.write_text(json.dumps(scale_settings), encoding='utf-8')
                scale_id = uuid4().hex
                scales = fixtures / f'Scales-{capacity}'
                shutil.copytree(scales / 'export', data / 'artifacts' / scale_id / 'export')
                scale_client = Client(bundle, scale_config)
                try:
                    scale_catalogue = scale_client.call('query_parameters', {'model': model, 'capacity': capacity, 'firmware': '32.02'})
                    assert len(scale_catalogue['builtin_scales']['fields']) == 59
                    assert scale_catalogue['builtin_scales']['modifier_normalized_fields']['scale'] == 13
                    assert scale_catalogue['builtin_scales']['modifier_raw_selectors']['damping_type'] == 24
                    scale_request = json.loads((scales / 'request.json').read_text(encoding='utf-8'))
                    scale_operations = [{key: value for key, value in operation.items() if value is not None} for operation in scale_request['operations']]
                    scaled = scale_client.call('plan_preset_edit', {'backup_artifact': scale_id, 'slot': capacity - 1, 'persistence': 'save', 'operations': scale_operations})
                    assert scaled['plan']['executable'] and (data / 'artifacts' / scaled['artifact_id'] / 'bundle/expected.syx').read_bytes() == (scales / 'expected.syx').read_bytes()
                    scale_review = scale_client.call('prepare_preset_edit', {'plan_artifact': scaled['artifact_id']})
                    assert not scale_review['validation']['ports_opened']
                    selector_donor_id = uuid4().hex
                    shutil.copytree(scales / 'selector-donor-export', data / 'artifacts' / selector_donor_id / 'export')
                    selector = scale_client.call('plan_preset_edit', {'backup_artifact': scale_id, 'slot': capacity - 1, 'persistence': 'save', 'operations': [
                        {'kind': 'copy-modifier-selector', 'modifier_slot': 1, 'expected_binding': [14, 70, 4],
                         'key': 'modifier.damping_type', 'template_modifier_slot': 1,
                         'template_artifact': selector_donor_id, 'template_slot': capacity - 1}]})
                    assert selector['plan']['executable'] and (data / 'artifacts' / selector['artifact_id'] / 'bundle/expected.syx').read_bytes() == (scales / 'selector-expected.syx').read_bytes()
                    print(f'PASS {name}: relocated built-in scale discovery, signed values, modifier curves and exact review')
                finally:
                    scale_client.close()
            live_id = uuid4().hex
            path_id = uuid4().hex
            shutil.copytree(fixtures / f'Path-{name}' / 'export', data / 'artifacts' / path_id / 'export')
            shutil.copytree(fixtures / f'Live-{name}', data / 'artifacts' / live_id / 'context')
            performance_id = uuid4().hex
            shutil.copytree(fixtures / f'Performance-{name}', data / 'artifacts' / performance_id / 'performance')
            organization = fixtures / f'Organization-{name}'
            organization_id, organization_context_id, organization_receipt_id, organization_result_id = [uuid4().hex for _ in range(4)]
            shutil.copytree(organization / 'export', data / 'artifacts' / organization_id / 'export')
            (data / 'artifacts' / organization_context_id / 'context').mkdir(parents=True)
            shutil.copyfile(organization / 'context.json', data / 'artifacts' / organization_context_id / 'context/context.json')
            shutil.copytree(organization / 'execution', data / 'artifacts' / organization_receipt_id / 'execution')
            shutil.copytree(organization / 'result-export', data / 'artifacts' / organization_result_id / 'export')
            batch_fixture = fixtures / f'Batch-{name}'
            batch_context = json.loads((batch_fixture / 'batch.json').read_text(encoding='utf-8'))['context']
            batch_context_id = uuid4().hex
            (data / 'artifacts' / batch_context_id / 'context').mkdir(parents=True)
            (data / 'artifacts' / batch_context_id / 'context/context.json').write_text(json.dumps(batch_context), encoding='utf-8')
            batch_plans = []
            for index in range(2):
                identifier = uuid4().hex; batch_plans.append(identifier)
                shutil.copytree(batch_fixture / f'item-{index:03}', data / 'artifacts' / identifier / 'bundle')
            recall_id = uuid4().hex
            shutil.copytree(fixtures / f'Recall-{name}', data / 'artifacts' / recall_id / 'context')
            export_id = uuid4().hex; shutil.copytree(fixtures / f'SceneMidi-{name}', data / 'artifacts' / export_id / 'export')
            edit_export_id = uuid4().hex; shutil.copytree(fixtures / f'Edit-{name}-Export', data / 'artifacts' / edit_export_id / 'export')
            selective_export_id = uuid4().hex
            shutil.copytree(fixtures / f'SelectiveUndo-{name}' / 'export', data / 'artifacts' / selective_export_id / 'export')
            selective_receipt_id = uuid4().hex
            shutil.copytree(fixtures / f'SelectiveUndo-{name}' / 'execution', data / 'artifacts' / selective_receipt_id / 'execution')
            selective_recovery_export_id = uuid4().hex
            shutil.copytree(fixtures / f'SelectiveRecovery-{name}' / 'export', data / 'artifacts' / selective_recovery_export_id / 'export')
            selective_recovery_id = uuid4().hex
            shutil.copytree(fixtures / f'SelectiveRecovery-{name}' / 'recovery', data / 'artifacts' / selective_recovery_id / 'recovery')
            prior_recovery_id = uuid4().hex
            shutil.copytree(fixtures / f'SelectiveRecovery-{name}' / 'prior-recovery', data / 'artifacts' / prior_recovery_id / 'recovery')
            inventory_id = uuid4().hex; shutil.copytree(fixtures / f'Inventory-{name}', data / 'artifacts' / inventory_id / 'inventory')
            recovery_id = uuid4().hex; shutil.copytree(fixtures / f'Recovery-{name}', data / 'artifacts' / recovery_id / 'recovery')
            import hashlib
            state_hash = hashlib.sha256((data / 'artifacts' / recovery_id / 'recovery/state.private.json').read_bytes()).hexdigest()
            snapshot = json.loads((data / 'artifacts' / export_id / 'export/snapshot.json').read_text(encoding='utf-8'))
            slot = snapshot['presets'][0]['slot']
            client = Client(bundle, config)
            try:
                tool_names = {t['name'] for t in client.request('tools/list', {})['tools']}
                assert {'fractal_import_template', 'fractal_list_templates', 'fractal_inspect_template', 'fractal_plan_template'} <= tool_names
                template_fixture = fixtures / f'Templates-{name}'
                template_backup_id = uuid4().hex
                shutil.copytree(template_fixture / 'export', data / 'artifacts' / template_backup_id / 'export')
                assert client.call('list_templates', {})['entries'] == []
                imported = client.call('import_template', {'backup_artifact': template_backup_id, 'slot': 0, 'effect_id': 66,
                    'kind': 'channel', 'channel': 'C', 'name': 'Synthetic channel', 'tags': ['delay', 'example'], 'provenance': 'Synthetic independently filled channel rows'})
                reference = {'entry_id': imported['entry_id'], 'entry_sha256': imported['entry_sha256']}
                listed = client.call('list_templates', {'query': 'CHANNEL', 'tag': 'DELAY', 'limit': 1})
                assert listed['total'] == 1 and listed['entries'][0]['entry_sha256'] == reference['entry_sha256']
                assert client.call('inspect_template', reference)['entry']['channel'] == 'C'
                client.call('inspect_template', {**reference, 'entry_sha256': '0' * 64}, error=True)
                templated = client.call('plan_template', {**reference, 'backup_artifact': template_backup_id, 'slot': 0, 'persistence': 'save',
                    'effect_id': 66, 'kind': 'copy-channel', 'channel': 'A'})
                assert templated['plan']['executable'] and (data / 'artifacts' / templated['artifact_id'] / 'bundle/expected.syx').read_bytes() == (template_fixture / 'copy-expected.syx').read_bytes()
                assert not client.call('prepare_preset_edit', {'plan_artifact': templated['artifact_id']})['ports_opened']
                swapped = client.call('plan_preset_edit', {'backup_artifact': template_backup_id, 'slot': 0, 'persistence': 'save',
                    'operations': [{'kind': 'swap-channels', 'effect_id': 66, 'channel': 'A', 'other_channel': 'C', 'policy': 'follow-channel-settings'}]})
                assert swapped['plan']['executable'] and (data / 'artifacts' / swapped['artifact_id'] / 'bundle/expected.syx').read_bytes() == (template_fixture / 'swap-expected.syx').read_bytes()
                assert not client.call('prepare_preset_edit', {'plan_artifact': swapped['artifact_id']})['ports_opened']
                print(f'PASS {name}: relocated private library and channel swap plans with exact reviews')
                calibration_fixture = fixtures / f'Calibration-{name}'
                def calibration_export(subfolder):
                    identifier = uuid4().hex
                    shutil.copytree(calibration_fixture / subfolder, data / 'artifacts' / identifier / 'export')
                    return identifier
                calibration_baseline = calibration_export('baseline-export')
                donor_id = calibration_export('donor-export')
                points = [calibration_export(f'sample-{i}') for i in range(3)]
                calibration = client.call('calibrate_saved_parameter', {'effect_id': 66, 'channel': 'A', 'key': 'reverb.observed_level', 'unit': 'dB', 'kind': 'parameter',
                    'samples': [{'backup_artifact': identifier, 'slot': capacity - 1, 'value': value} for identifier, value in zip(points, [-12, -6, 0])]})
                ignored = client.call('calibrate_saved_parameter', {'effect_id': 58, 'key': 'amp.scene_ignore', 'unit': 'boolean', 'kind': 'scene-ignore',
                    'samples': [{'backup_artifact': calibration_export(f'ignore-{i}'), 'slot': capacity - 1, 'value': i} for i in range(2)]})
                modifier_calibration = client.call('calibrate_saved_parameter', {'effect_id': 66, 'modifier_slot': 1,
                    'expected_binding': [14, 66, 7], 'key': 'modifier.scale', 'unit': 'display-%', 'kind': 'modifier',
                    'samples': [{'backup_artifact': calibration_export(f'modifier-{i}'), 'slot': capacity - 1, 'value': value}
                                for i, value in enumerate([10, 50, 100])]})
                calibrated_plan = client.call('plan_preset_edit', {'backup_artifact': calibration_baseline, 'slot': capacity - 1, 'persistence': 'save', 'operations': [
                    {'kind': 'set-calibrated-parameter', 'effect_id': 66, 'channel': 'A', 'key': 'reverb.observed_level', 'unit': 'dB', 'calibrated_value': -6, 'calibration_artifact': calibration['artifact_id']},
                    {'kind': 'set-scene-ignore', 'effect_id': 58, 'key': 'amp.scene_ignore', 'unit': 'boolean', 'enabled': True, 'calibration_artifact': ignored['artifact_id']},
                    {'kind': 'set-modifier-source', 'modifier_slot': 1, 'source_id': 15, 'expected_binding': [14, 66, 7]}]})
                assert calibrated_plan['plan']['executable']
                assert (data / 'artifacts' / calibrated_plan['artifact_id'] / 'bundle/expected.syx').read_bytes() == (calibration_fixture / 'expected.syx').read_bytes()
                calibrated_modifier_plan = client.call('plan_preset_edit', {'backup_artifact': calibration_baseline,
                    'slot': capacity - 1, 'persistence': 'save', 'operations': [
                        {'kind': 'set-calibrated-modifier', 'modifier_slot': 1, 'expected_binding': [14, 66, 7],
                         'key': 'modifier.scale', 'unit': 'display-%', 'calibrated_value': 50,
                         'calibration_artifact': modifier_calibration['artifact_id']}]})
                assert calibrated_modifier_plan['plan']['executable'] and calibrated_modifier_plan['plan']['status'] == 'planned'
                calibrated_review = client.call('prepare_preset_edit', {'plan_artifact': calibrated_plan['artifact_id']})
                assert not calibrated_review['validation']['ports_opened']
                modifier_plan = client.call('plan_preset_edit', {'backup_artifact': calibration_baseline, 'slot': capacity - 1, 'persistence': 'save', 'operations': [
                    {'kind': 'copy-modifier', 'modifier_slot': 2, 'template_modifier_slot': 1, 'template_artifact': donor_id, 'template_slot': capacity - 1, 'expected_binding': [0, 0, 0]}]})
                assert modifier_plan['plan']['executable']
                print(f'PASS {name}: relocated calibration witnesses, Scene Ignore, modifier templates and exact review')

                assert {'fractal_plan_preset_edit', 'fractal_plan_selective_scene_midi_undo', 'fractal_plan_selective_recovery_undo', 'fractal_migrate_recovery',
                        'fractal_prepare_preset_edit', 'fractal_prepare_preset_edit_undo', 'fractal_prepare_edit_reconciliation'} <= tool_names
                selective = client.call('plan_selective_scene_midi_undo', {'execution_artifact': selective_receipt_id,
                    'backup_artifact': selective_export_id, 'slot': 0})
                assert selective['plan']['executable'] and selective['source']['owned_rows'] == [9]
                selective_review = client.call('prepare_preset_edit', {'plan_artifact': selective['artifact_id']})
                assert selective_review['summary']['persistence'] == 'save' and not selective_review['ports_opened']
                recovery_head = hashlib.sha256((data / 'artifacts' / selective_recovery_id / 'recovery/state.private.json').read_bytes()).hexdigest()
                recovered = client.call('plan_selective_recovery_undo', {'recovery_artifact': selective_recovery_id,
                    'state_sha256': recovery_head, 'backup_artifact': selective_recovery_export_id, 'slot': 0})
                assert recovered['plan']['executable'] and len(recovered['source']['owned_rows']) == 2
                recovered_review = client.call('prepare_preset_edit', {'plan_artifact': recovered['artifact_id']})
                assert recovered_review['summary']['persistence'] == 'save' and not recovered_review['ports_opened']
                client.call('plan_selective_recovery_undo', {'recovery_artifact': selective_recovery_id,
                    'state_sha256': '0' * 64, 'backup_artifact': selective_recovery_export_id, 'slot': 0}, error=True)
                prior_head = hashlib.sha256((data / 'artifacts' / prior_recovery_id / 'recovery/state.private.json').read_bytes()).hexdigest()
                migrated = client.call('migrate_recovery', {'recovery_artifact': prior_recovery_id, 'state_sha256': prior_head})
                assert migrated['result']['source_protocol_release'] == '0.8.0' and migrated['result']['edited_count'] == 1
                assert not migrated['ports_opened'] and migrated['result']['source_state_sha256'] == prior_head
                migrated_head = migrated['result']['target_state_sha256']
                migrated_inspection = client.call('inspect_recovery', {'recovery_artifact': migrated['artifact_id'], 'state_sha256': migrated_head})
                assert migrated_inspection['items'][0]['status'] == 'edited' and not migrated_inspection['ports_opened']
                migrated_review = client.call('prepare_recovery', {'recovery_artifact': migrated['artifact_id'],
                    'state_sha256': migrated_head, 'mode': 'undo'})
                assert migrated_review['approval_required'] and not migrated_review['ports_opened']
                client.call('migrate_recovery', {'recovery_artifact': prior_recovery_id, 'state_sha256': '0' * 64}, error=True)
                assert {'fractal_read_amp_roster', 'fractal_capture_live_control_context', 'fractal_prepare_live_control'} <= tool_names
                assert {'fractal_plan_preset_organization', 'fractal_inspect_preset_organization', 'fractal_prepare_preset_organization', 'fractal_plan_preset_organization_undo'} <= tool_names
                organization_plan = client.call('plan_preset_organization', {'backup_artifact': organization_id, 'operations': [
                    {'kind': 'reorder', 'assignments': [{'source_slot': 0, 'destination_slot': 1}, {'source_slot': 1, 'destination_slot': 2}, {'source_slot': 2, 'destination_slot': 0}]}]})
                assert organization_plan['plan']['changed_count'] == 3 and not organization_plan['ports_opened']
                inspected = client.call('inspect_preset_organization', {'organization_artifact': organization_plan['artifact_id']})
                assert inspected['plan_sha256'] == organization_plan['plan_sha256']
                organization_review = client.call('prepare_preset_organization', {'organization_artifact': organization_plan['artifact_id'], 'context_artifact': organization_context_id})
                assert organization_review['approval_required'] and len(organization_review['validation']['saved_guards']) == 3
                client.call('execute_review', {'review_id': organization_review['review_id'], 'review_sha256': organization_review['review_sha256']}, error=True)
                reconciliation = client.call('prepare_organization_reconciliation', {'organization_review_artifact': organization_review['review_id']})
                assert not reconciliation['ports_opened']
                progress_hash = hashlib.sha256((organization / 'execution/progress.json').read_bytes()).hexdigest()
                inverse = client.call('plan_preset_organization_undo', {'execution_artifact': organization_receipt_id, 'progress_sha256': progress_hash, 'backup_artifact': organization_result_id})
                assert inverse['eligible_count'] == 3 and not inverse['ports_opened']
                client.call('plan_preset_organization_undo', {'execution_artifact': organization_receipt_id, 'progress_sha256': '0' * 64, 'backup_artifact': organization_result_id}, error=True)
                for action in ({'kind': 'rename', 'slot': 0, 'name': 'Synthetic renamed'}, {'kind': 'copy', 'source_slot': 0, 'destination_slot': 1, 'overwrite': True},
                               {'kind': 'move', 'source_slot': 0, 'destination_slot': 1, 'overwrite': True, 'vacated_policy': 'replace-from-capture', 'replacement_slot': 2},
                               {'kind': 'swap', 'source_slot': 0, 'destination_slot': 1}):
                    plan = client.call('plan_preset_organization', {'backup_artifact': organization_id, 'operations': [action]})
                    assert not plan['ports_opened']
                donor_manifest = json.loads((organization / 'export/manifest.json').read_text(encoding='utf-8'))['captures'][0]
                imported = client.call('plan_preset_organization', {'backup_artifact': organization_id, 'operations': [{'kind': 'import', 'destination_slot': 1, 'overwrite': True,
                    'donor_artifact': organization_id, 'donor_file': 'export/' + donor_manifest['file'], 'donor_sha256': donor_manifest['sysex_sha256'],
                    'donor_model': model, 'donor_capacity': capacity, 'donor_firmware': '99.07'}]})
                assert imported['plan']['changed_count'] == 1
                client.call('plan_preset_organization', {'backup_artifact': organization_id, 'operations': [{'kind': 'copy', 'source_slot': 0, 'destination_slot': 1}]}, error=True)
                client.call('plan_preset_organization', {'backup_artifact': organization_id, 'operations': [{'kind': 'move', 'source_slot': 0, 'destination_slot': 1, 'overwrite': True}]}, error=True)
                print(f'PASS {name}: relocated organization planning, immutable reorder, exact execution/reconciliation reviews, same-context import and eligible undo')
                assert {'fractal_plan_audio_path', 'fractal_listen_controller', 'fractal_listen_device_notifications'} <= tool_names
                client.call('listen_device_notifications', {'duration_seconds': 1, 'max_events': 16}, error=True)
                path_plan = client.call('plan_audio_path', {'backup_artifact': path_id, 'slot': 0, 'persistence': 'save',
                                        'waypoints': [{'row': 1, 'column': 1}, {'row': 1, 'column': 5}], 'shunt_id': 1024})
                assert path_plan['added_shunts'] == 3 and path_plan['added_edges'] == 4
                path_review = client.call('prepare_preset_edit', {'plan_artifact': path_plan['artifact_id']})
                assert path_review['validation']['plan']['executable'] and not path_review['ports_opened']
                listener_request = json.loads((fixtures / f'Path-{name}' / 'listener.json').read_text(encoding='utf-8'))
                for rule in listener_request['rules']:
                    if rule['value'] is None: rule.pop('value')
                    rule['target'] = {k: v for k, v in rule['target'].items() if v is not None}
                client.call('listen_controller', {'input': 12345, 'duration_seconds': 1, 'max_events': 16, 'rules': listener_request['rules']}, error=True)
                assert {'fractal_prepare_edit_batch', 'fractal_prepare_batch_reconciliation', 'fractal_route_midi_events'} <= tool_names
                batch_review = client.call('prepare_edit_batch', {'context_artifact': batch_context_id, 'plan_artifacts': batch_plans})
                assert len(batch_review['summary']['ordered_targets']) == 2 and not batch_review['ports_opened']
                reconcile = client.call('prepare_batch_reconciliation', {'batch_artifact': batch_review['review_id']})
                assert not reconcile['ports_opened']
                try:
                    client.call('prepare_edit_batch', {'context_artifact': batch_context_id, 'plan_artifacts': [batch_plans[0], batch_plans[0]]})
                    raise AssertionError('Duplicate batch plan IDs accepted')
                except ValueError as error:
                    assert 'duplicate items' in str(error)
                cc_review = client.call('prepare_live_control', {'context_artifact': live_id, 'kind': 'channel', 'effect_id': 58, 'channel': 'C', 'midi_assignment': {'channel': 16, 'controller': 17, 'value': 99}})
                assert cc_review['summary']['transport'] == 'configured-cc'
                recall_review = client.call('prepare_load', {'context_artifact': recall_id})
                recall_wire = recall_review['summary']['wire_messages']
                assert recall_wire[:2] == ['BF0001', 'CF01'] and len(recall_wire) == 3
                scene_wire = bytes.fromhex(recall_wire[2])
                assert len(scene_wire) == 9 and scene_wire[5] == 0x0c and scene_wire[6] == 2
                routing = json.loads((fixtures / f'Recall-{name}' / 'routing.json').read_text(encoding='utf-8'))
                routing.pop('schema_version')
                routing['events'].insert(1, routing['events'][0].copy())
                for rule in routing['rules']:
                    if rule['value'] is None: rule.pop('value')
                    rule['target'] = {key: value for key, value in rule['target'].items() if value is not None}
                mapped = client.call('route_midi_events', routing)
                assert mapped['result']['events'][0]['target']['slot'] == 129 and all(e['target'] is None for e in mapped['result']['events'][1:])
                client.call('execute_review', {'review_id': batch_review['review_id'], 'review_sha256': batch_review['review_sha256']}, error=True)
                assert 'fractal_read_block_parameters' in tool_names
                client.call('read_block_parameters', {'effect_id': 66}, error=True)
                client.call('read_amp_roster', {'start': 0, 'count': 2}, error=True)
                client.call('capture_live_control_context', {}, error=True)
                client.call('capture_performance_context', {}, error=True)
                assert {'fractal_capture_performance_context', 'fractal_prepare_performance_control', 'fractal_query_performance_capabilities'} <= tool_names
                performance_caps = client.call('query_performance_capabilities', {'model': model, 'capacity': capacity, 'firmware': '99.07'})
                assert performance_caps['tempo_query_requires_review'] and not performance_caps['tuner_state_query_supported']
                for control in ({'kind': 'tempo-read'}, {'kind': 'tempo-set', 'bpm': 140}, {'kind': 'tempo-tap'}, {'kind': 'tuner-set', 'enabled': True},
                                {'kind': 'looper-button', 'action': 'undo'}, {'kind': 'looper-state', 'action': 'reverse', 'enabled': True}):
                    review = client.call('prepare_performance_control', {'context_artifact': performance_id, **control})
                    assert review['approval_required'] and review['validation']['kind'] == control['kind'] and not review['ports_opened']
                    assert review['validation']['persistence'] == 'performance-only-no-save'
                    client.call('execute_review', {'review_id': review['review_id'], 'review_sha256': review['review_sha256']}, error=True)
                client.call('prepare_performance_control', {'context_artifact': performance_id, 'kind': 'tuner-set', 'bpm': 140, 'enabled': True}, error=True)
                try:
                    client.call('prepare_performance_control', {'context_artifact': performance_id, 'kind': 'tempo-set', 'bpm': 251})
                except ValueError as error:
                    assert 'outside supported range' in str(error)
                else:
                    raise AssertionError('Invalid BPM must be rejected by JSON-RPC argument validation.')
                client.call('prepare_performance_control', {'context_artifact': performance_id, 'kind': 'looper-state', 'action': 'record', 'enabled': True}, error=True)
                client.call('monitor_live_state', {'samples': 2, 'interval_ms': 100}, error=True)
                automatic = client.call('plan_block_insertion', {'snapshot_artifact': export_id, 'slots': [slot], 'effect_id': 114, 'placement_policy': 'first-isolated-empty'})
                automatic_review = client.call('prepare_insertion', {'plan_artifact': automatic['artifact_id'], 'backup_artifact': export_id})
                assert not automatic_review['validation']['ports_opened'] and automatic_review['validation']['targets'] == 1
                client.call('plan_block_insertion', {'snapshot_artifact': export_id, 'slots': [slot], 'effect_id': 114, 'row': 1, 'column': 1, 'placement_policy': 'first-isolated-empty'}, error=True)
                for control in ({'kind': 'scene', 'scene': 5}, {'kind': 'channel', 'effect_id': 58, 'channel': 'C'}, {'kind': 'bypass', 'effect_id': 58, 'bypassed': True}):
                    live_review = client.call('prepare_live_control', {'context_artifact': live_id, **control})
                    assert live_review['validation']['model'] == model and not live_review['validation']['ports_opened']
                    assert live_review['validation']['persistence'] == 'live-only-no-save'
                    client.call('execute_review', {'review_id': live_review['review_id'], 'review_sha256': live_review['review_sha256']}, error=True)
                client.call('prepare_live_control', {'context_artifact': live_id, 'kind': 'channel', 'effect_id': 58, 'channel': 'C', 'scene': 2}, error=True)
                setlist_id = uuid4().hex
                shutil.copytree(fixtures / f'Setlists-{name}' / 'inventory', data / 'artifacts' / setlist_id / 'inventory')
                song_request = json.loads((fixtures / f'Setlists-{name}' / 'request.json').read_text(encoding='utf-8'))
                song_args = {'inventory_artifact': setlist_id, 'songs': song_request['songs'], 'setlists': song_request['setlists'],
                    'active_setlist': 1, 'fc_controller_present': True}
                proposed = client.call('plan_setlists', song_args)
                assert proposed['result']['plan']['dependencies_resolved'] and not proposed['ports_opened']
                assert not proposed['result']['plan']['device_executable'] and not proposed['result']['plan']['editor_importable']
                assert proposed['result']['plan']['songs'][0]['sections'][0]['scene_selection'] == 'default'
                assert proposed['result']['plan']['songs'][0]['sections'][0]['resolved_scene'] == 6
                song_args['songs'][0]['sections'][0]['preset_slot'] = 1
                unresolved = client.call('plan_setlists', song_args)
                assert not unresolved['result']['plan']['dependencies_resolved']
                song_args['songs'][0]['sections'][0]['scene'] = 1
                client.call('plan_setlists', song_args, error=True)
                print(f'PASS {name}: relocated offline setlists/songs and unresolved reference reporting; no execution review or MIDI')
                inventory = client.call('inspect_inventory', {'inventory_artifact': inventory_id})
                assert inventory['capture_complete'] and not inventory['ports_opened']
                compared = client.call('compare_inventories', {'before_artifact': inventory_id, 'after_artifact': inventory_id})
                assert all(c['status'] == 'identical-raw' for c in compared['changes']) and not compared['automatic_reassignment']
                used = client.call('inventory_amp_usage', {'inventory_artifact': inventory_id})
                assert not used['full_capacity_decoded'] and not used['ports_opened']
                exported = client.call('export_inventory', {'inventory_artifact': inventory_id})
                assert exported['result']['decoded_presets'] == 2
                migrated = client.call('migrate_inventory', {'inventory_artifact': inventory_id})
                assert migrated['result']['original_unchanged'] and not migrated['result']['refreshed']
                client.call('capture_inventory', {'all': True}, error=True)
                client.call('resume_inventory', {'inventory_artifact': inventory_id}, error=True)
                edit_caps = client.call('query_edit_capabilities', {'model': model, 'capacity': capacity, 'firmware': '99.07'})
                assert edit_caps['execution_supported'] and not edit_caps['scope']['hardware_qualified']
                parameters = client.call('query_parameters', {'model': model, 'capacity': capacity, 'firmware': '99.07', 'family': 'Amp'})
                assert parameters['parameters'][0]['write_supported']
                edit = client.call('plan_preset_edit', {'backup_artifact': edit_export_id, 'slot': capacity - 1, 'persistence': 'save', 'feedback_policy': 'allow',
                    'operations': [{'kind': 'rename-scene', 'scene': 2, 'name': 'Synthetic MCP edit'},
                                   {'kind': 'disconnect', 'source_row': 1, 'source_column': 1, 'destination_row': 2, 'destination_column': 2}]})
                assert edit['plan']['executable'] and edit['plan']['status'] == 'planned'
                edit_review = client.call('prepare_preset_edit', {'plan_artifact': edit['artifact_id']})
                assert edit_review['summary']['persistence'] == 'save' and not edit_review['validation']['ports_opened']
                assert edit_review['summary']['feedback_policy'] == 'allow' and 'after_special' in edit_review['summary']['routing']
                client.call('execute_review', {'review_id': edit_review['review_id'], 'review_sha256': edit_review['review_sha256']}, error=True)
                reconcile_review = client.call('prepare_edit_reconciliation', {'plan_artifact': edit['artifact_id']})
                assert not reconcile_review['validation']['ports_opened']
                client.call('prepare_preset_edit_undo', {'execution_artifact': edit['artifact_id']}, error=True)
                compound = client.call('plan_preset_edit', {'backup_artifact': export_id, 'slot': slot, 'persistence': 'save',
                    'operations': [{'kind': 'copy-block-settings', 'effect_id': 190, 'template_artifact': export_id, 'template_slot': slot},
                                   {'kind': 'set-scene-midi', 'scene': 2, 'message_slot': 2, 'message_type': 'CC', 'midi_channel': 4, 'controller': 17, 'value': 65}]})
                assert compound['plan']['executable']
                compound_review = client.call('prepare_preset_edit', {'plan_artifact': compound['artifact_id']})
                assert not compound_review['validation']['ports_opened']
                print(f'PASS {name}: relocated compound editing/routing plan and guarded review; no ports opened')
                block = client.call('query_block', {'model': model, 'firmware': '99.07', 'query': 'Reverb 1'})
                assert not block['ports_opened'] and block['matches'][0]['name'] == 'Reverb 1'
                assert block['matches'][0]['availability']['documented_instances'] == (1 if model == 'FM3' else 2)
                unknown = client.call('query_block', {'model': model, 'effect_id': 65535})
                assert unknown['matches'][0]['name'] is None
                assert any(c['name'] == 'Amp 1' for c in snapshot['presets'][0]['grid'])
                assert snapshot['device']['block_catalogue']['model'] == model
                references = client.request('resources/list', {})['resources']
                assert any(r['uri'].endswith('reference-map.md') for r in references)
                assert client.request('resources/read', {'uri': 'fractal://reference/knowledge-base/reference-map.md'})['contents']
                assert client.call('search_knowledge', {'query': 'Scene MIDI recovery'})['sources']
                manifest = json.loads((data / 'artifacts' / export_id / 'export/manifest.json').read_text(encoding='utf-8'))
                entry = manifest['captures'][0]
                integrity = client.call('check_backup', {'artifact_id': export_id, 'file': 'export/' + entry['file'], 'sha256': entry['sysex_sha256'], 'model': model, 'capacity': capacity})
                assert integrity['model'] == model and not integrity['restore_verified']
                decoded = client.call('decode_captures', {'artifact_id': export_id, 'files': ['export/' + entry['file']], 'model': model, 'capacity': capacity, 'firmware': '99.07'})
                assert decoded['result']['decoded_presets'] == 1
                planned = client.call('plan_scene_midi', {'snapshot_artifact': export_id, 'slots': [slot], 'scenes': [1, 8], 'message_slot': 2,
                    'type': 'CC', 'channel': 4, 'controller': 17, 'value': 65})
                review = client.call('prepare_scene_midi', {'plan_artifact': planned['artifact_id'], 'backup_artifact': export_id})
                assert not review['validation']['ports_opened']
                assert review['validation']['field_commands'] > 0
                client.call('execute_review', {'review_id': review['review_id'], 'review_sha256': review['review_sha256']}, error=True)
                pc = client.call('plan_scene_midi', {'snapshot_artifact': export_id, 'slots': [slot], 'scenes': [2], 'message_slot': 3,
                    'type': 'PC', 'channel': 5, 'value': 33})
                pc_review = client.call('prepare_scene_midi', {'plan_artifact': pc['artifact_id'], 'backup_artifact': export_id})
                assert not pc_review['validation']['ports_opened'] and pc_review['validation']['field_commands'] > 0
                grid = snapshot['presets'][0]['grid']; cell = next(c for c in grid if c['effect_id'] == 0)
                insertion = client.call('plan_block_insertion', {'snapshot_artifact': export_id, 'slots': [slot], 'effect_id': 114, 'row': cell['row'], 'column': cell['column']})
                insert_review = client.call('prepare_insertion', {'plan_artifact': insertion['artifact_id'], 'backup_artifact': export_id})
                assert not insert_review['validation']['ports_opened']
                # Independent assembled before/after fixtures; no hardware execution is simulated as qualification.
                original_id = uuid4().hex
                shutil.copytree(fixtures / f'Removal-{name}-Original', data / 'artifacts' / original_id / 'export')
                changed_id = uuid4().hex
                shutil.copytree(fixtures / f'Removal-{name}', data / 'artifacts' / changed_id / 'export')
                removal = client.call('plan_block_removal', {'snapshot_artifact': changed_id, 'slots': [slot], 'effect_id': 114})
                removal_review = client.call('prepare_removal', {'plan_artifact': removal['artifact_id'], 'backup_artifact': changed_id})
                assert not removal_review['validation']['ports_opened'] and removal_review['validation']['targets'] == 1
                client.call('execute_review', {'review_id': removal_review['review_id'], 'review_sha256': removal_review['review_sha256']}, error=True)
                absent = client.call('plan_block_removal', {'snapshot_artifact': original_id, 'slots': [slot], 'effect_id': 114})
                assert absent['plan']['summary']['unchanged'] == 1
                client.call('prepare_removal', {'plan_artifact': absent['artifact_id'], 'backup_artifact': original_id}, error=True)
                add = client.call('plan_block_insertion', {'snapshot_artifact': original_id, 'slots': [slot], 'effect_id': 114, 'row': 2, 'column': 1})
                add_review = client.call('prepare_insertion', {'plan_artifact': add['artifact_id'], 'backup_artifact': original_id})
                # Supply a clearly synthetic retained receipt to exercise real stdio/decode/undo validation.
                evidence_id = uuid4().hex; evidence = data / 'artifacts' / evidence_id
                evidence.mkdir()
                shutil.copytree(fixtures / f'Removal-{name}', evidence / 'execution/item-001/reloaded')
                request = json.loads((data / 'artifacts' / add_review['review_id'] / 'insertion-request.json').read_text())[0]
                changed_manifest = json.loads((evidence / 'execution/item-001/reloaded/manifest.json').read_text())
                changed_hash = changed_manifest['captures'][0]['sysex_sha256']
                result = dict(request, original_hash=request['sysex_sha256'], saved_hash=changed_hash,
                              complete=True, existing_body_and_scene_midi_preserved=True, raw_header_and_padding_preserved=True,
                              repeated_saved_equal=True, reloaded_equal=True)
                (evidence / 'execution/report.private.json').write_text(json.dumps({'complete':True,'model':model,'firmware':'99.07',
                    'completed_count':1,'results':[result]}))
                (evidence / 'result.json').write_text(json.dumps({'review_id':add_review['review_id'],'result':{'complete':True}}))
                (data / 'attempts' / (add_review['review_id']+'.json')).write_text(json.dumps({'status':'verified-complete',
                    'operation':'insert-blocks','execution_artifact':evidence_id,'review_sha256':add_review['review_sha256']}))
                undo = client.call('prepare_insertion_undo', {'execution_artifact':evidence_id})
                assert undo['summary']['undo_insertion'] and not undo['validation']['ports_opened']
                undo_request = json.loads((data / 'artifacts' / undo['review_id'] / 'removal-request.json').read_text())[0]
                assert undo_request['undo_sha256'] == request['sysex_sha256'] and undo_request['sysex_sha256'] == changed_hash
                client.call('execute_review', {'review_id':undo['review_id'],'review_sha256':undo['review_sha256']}, error=True)
                # Damaged receipt, partial completion, and changed retained insertion bytes are refused before MIDI.
                report_file = evidence / 'execution/report.private.json'
                report = json.loads(report_file.read_text()); report['complete']=False; report_file.write_text(json.dumps(report))
                client.call('prepare_insertion_undo', {'execution_artifact':evidence_id}, error=True)
                report['complete']=True; report_file.write_text(json.dumps(report))
                capture_file = evidence / 'execution/item-001/reloaded' / changed_manifest['captures'][0]['file']
                capture_file.write_bytes(capture_file.read_bytes() + b'corrupt')
                client.call('prepare_insertion_undo', {'execution_artifact':evidence_id}, error=True)
                recovery = client.call('inspect_recovery', {'recovery_artifact': recovery_id, 'state_sha256': state_hash})
                assert recovery['capacity'] == capacity and not recovery['ports_opened']
                resumed = client.call('prepare_recovery', {'recovery_artifact': recovery_id, 'state_sha256': state_hash, 'mode': 'resume'})
                assert not resumed['validation']['ports_opened']
                client.call('prepare_recovery', {'recovery_artifact': recovery_id, 'state_sha256': state_hash, 'mode': 'undo'}, error=True)
                client.call('inspect_device', {}, error=True)
                assert client.call('list_artifacts', {})['artifacts']
                # Damaged manifests must stop before even the port-free native validator is launched.
                (data / 'artifacts' / export_id / 'export/snapshot.json').write_text('{}')
                client.call('plan_scene_midi', {'snapshot_artifact': export_id, 'slots': [slot], 'scenes': [1], 'message_slot': 2,
                    'type': 'PC', 'channel': 4, 'value': 65}, error=True)
                print(f'PASS relocated MCP discovery/knowledge/backup/decode/PC-CC/insertion/recovery/live-refusal: {name}', flush=True)
            finally:
                client.close()
        print('MCP pipeline passed: four model/capacity definitions; no MIDI ports opened.', flush=True)


if __name__ == '__main__':
    main()
