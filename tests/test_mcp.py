"""Adapter boundaries and replay/cancellation invariants; synthetic runtimes only."""
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'src'))
from fractal_mcp.runtime import Cancelled, Runtime, device_owner, load_config
from fractal_mcp.schema import validate
from fractal_mcp.server import Server
from fractal_mcp.storage import Artifacts, atomic_json, confined, dumps, loads, sha
from fractal_mcp.tools import Toolkit, TOOLS, TOOL_MAP


DEVICE = {'model': 'FM9', 'capacity': 512, 'firmware': '99.07', 'input': 12345, 'output': 12346}  # Synthetic, never opened.


class FakeRuntime:
    def __init__(self):
        self.calls = []
        self.fail = False

    def run(self, helper, arguments, cancel, **kw):
        self.calls.append((helper, list(map(str, arguments))))
        if self.fail:
            raise Cancelled('Synthetic uncertain child stop.')
        if '--validate-only' in arguments:
            return {'mode': 'offline-validation', 'ports_opened': False}
        return {'complete': True}


class McpChecks(unittest.TestCase):
    def test_setlist_contracts_are_closed_offline_and_allow_repeated_songs(self):
        schema = TOOL_MAP['fractal_plan_setlists']['inputSchema']
        request = {'inventory_artifact': 'a' * 32, 'songs': [{'number': 1, 'name': 'Song', 'sections': [
            {'number': 1, 'name': 'Intro', 'preset_slot': 0, 'default_scene': True}, {'number': 6, 'name': ''}]}],
            'setlists': [{'number': 1, 'name': 'Set', 'songs': [1, 1]}]}
        validate(schema, request)
        for patch in ({'sys_ex': [1, 2]}, {'active_setlist': 0}, {'fc_controller_present': 1},
                      {'setlists': [{'number': 1, 'name': 'Set', 'songs': [0]}]},
                      {'songs': [{'number': 1, 'name': 'Song', 'sections': [{'number': 1, 'name': '', 'default_scene': False}]}]}):
            with self.assertRaises(ValueError): validate(schema, {**request, **patch})
        self.assertFalse(TOOL_MAP['fractal_plan_setlists']['annotations']['destructiveHint'])
        self.assertFalse(TOOL_MAP['fractal_plan_setlists']['annotations']['readOnlyHint'])
        self.assertEqual(self.runtime.calls, [])

    def test_setlist_planner_binds_inventory_context_and_never_prepares_execution(self):
        identifier, folder = self.toolkit.artifacts.create(); (folder / 'inventory').mkdir()
        def runtime(helper, arguments, cancel, **kw):
            self.runtime.calls.append((helper, list(map(str, arguments))))
            if arguments[0] == 'inventory-inspect': return {**DEVICE, 'ports_opened': False}
            self.assertEqual(arguments[0], 'plan-setlists')
            self.assertNotIn('--input', arguments); self.assertNotIn('--output', arguments)
            request = loads(Path(arguments[arguments.index('--request') + 1]).read_text())
            self.assertEqual(request['firmware'], DEVICE['firmware']); self.assertEqual(request['model'], DEVICE['model'])
            self.assertEqual(request['setlists'][0]['songs'], [1, 1])
            self.assertTrue(request['songs'][0]['sections'][0]['default_scene'])
            self.assertFalse(request['fc_controller_present'])
            return {'plan': {'device_executable': False, 'editor_importable': False, 'dependencies_resolved': False}, 'ports_opened': False}
        self.runtime.run = runtime
        result = self.toolkit.call('fractal_plan_setlists', {'inventory_artifact': identifier, 'songs': [
            {'number': 1, 'name': 'Song', 'sections': [{'number': 1, 'name': '', 'preset_slot': 0, 'default_scene': True}]}],
            'setlists': [{'number': 1, 'name': 'Set', 'songs': [1, 1]}], 'fc_controller_present': False})
        self.assertFalse(result['ports_opened']); self.assertFalse(result['result']['plan']['device_executable'])
        self.assertEqual([call[1][0] for call in self.runtime.calls], ['inventory-inspect', 'plan-setlists'])
        self.assertNotIn('review_id', result)
        self.runtime.run = lambda *args, **kw: {**DEVICE, 'firmware': '99.08'}
        with self.assertRaises(ValueError):
            self.toolkit.call('fractal_plan_setlists', {'inventory_artifact': identifier, 'songs': [], 'setlists': []})

    def test_setlist_offline_planning_needs_no_operator_device_configuration(self):
        identifier, folder = self.toolkit.artifacts.create(); (folder / 'inventory').mkdir()
        self.config.pop('device')
        def runtime(helper, arguments, cancel, **kw):
            self.runtime.calls.append((helper, list(map(str, arguments))))
            if arguments[0] == 'inventory-inspect': return DEVICE
            request = loads(Path(arguments[arguments.index('--request') + 1]).read_text())
            self.assertEqual(request['capacity'], 512); self.assertEqual(request['model'], 'FM9')
            return {'plan': {'device_executable': False}, 'ports_opened': False}
        self.runtime.run = runtime
        result = self.toolkit.call('fractal_plan_setlists', {'inventory_artifact': identifier,
            'songs': [{'number': 1, 'name': '', 'sections': []}], 'setlists': []})
        self.assertFalse(result['ports_opened'])
        self.assertEqual([call[1][0] for call in self.runtime.calls], ['inventory-inspect', 'plan-setlists'])

    def test_organization_contracts_are_closed_and_never_authorize_writes(self):
        schema = TOOL_MAP['fractal_plan_preset_organization']['inputSchema']
        for operation in ({'kind': 'rename', 'slot': 0, 'name': ''}, {'kind': 'copy', 'source_slot': 0, 'destination_slot': 1, 'overwrite': True},
                          {'kind': 'move', 'source_slot': 0, 'destination_slot': 1, 'vacated_policy': 'replace-from-capture', 'replacement_slot': 2},
                          {'kind': 'reorder', 'assignments': [{'source_slot': 0, 'destination_slot': 1}, {'source_slot': 1, 'destination_slot': 0}]}):
            validate(schema, {'backup_artifact': 'a' * 32, 'operations': [operation]})
        for operation in ({'kind': 'delete', 'slot': 0}, {'kind': 'rename', 'slot': 0, 'name': 'x' * 32},
                          {'kind': 'copy', 'source_slot': True, 'destination_slot': 1}, {'kind': 'move', 'vacated_policy': 'invent-empty'},
                          {'kind': 'swap', 'source_slot': 0, 'destination_slot': 1, 'approve': True}):
            with self.assertRaises(ValueError): validate(schema, {'backup_artifact': 'a' * 32, 'operations': [operation]})
        for name in ('plan_preset_organization', 'inspect_preset_organization', 'prepare_preset_organization', 'prepare_organization_reconciliation', 'plan_preset_organization_undo'):
            self.assertFalse(TOOL_MAP['fractal_' + name]['inputSchema']['additionalProperties'])
            self.assertFalse(TOOL_MAP['fractal_' + name]['annotations']['destructiveHint'])
        self.assertEqual(self.runtime.calls, [])

    def test_organization_review_freezes_plan_context_and_guards_offline(self):
        identifier, folder = self.toolkit.artifacts.create(); (folder / 'organization').mkdir()
        atomic_json(folder / 'organization/plan.json', {'synthetic': True}); atomic_json(folder / 'organization/request.json', {'synthetic': True})
        context_id, context_folder = self.toolkit.artifacts.create(); (context_folder / 'context').mkdir()
        atomic_json(context_folder / 'context/context.json', {'synthetic': True})
        def runtime(helper, arguments, cancel, **kw):
            self.runtime.calls.append((helper, list(map(str, arguments))))
            if helper == 'cli':
                output = Path(arguments[arguments.index('--destination') + 1]); output.mkdir()
                atomic_json(output / 'batch.json', {'synthetic': True})
                return {'organization': {'guarded_slots': [0, 1]}}
            return {'model': DEVICE['model'], 'capacity': DEVICE['capacity'], 'firmware': DEVICE['firmware'], 'ports_opened': False}
        self.runtime.run = runtime
        review = self.toolkit.call('fractal_prepare_preset_organization', {'organization_artifact': identifier, 'context_artifact': context_id})
        self.assertTrue(review['approval_required']); self.assertFalse(review['ports_opened'])
        self.assertEqual(review['summary']['organization']['guarded_slots'], [0, 1])
        self.assertIn('--validate-only', self.runtime.calls[-1][1])
        self.assertFalse(any('--input' in args or '--output' in args for _, args in self.runtime.calls))
        frozen = self.toolkit.artifacts.json(review['review_id'], 'review.json')
        self.assertEqual(frozen['operation'], 'preset-organization')
        self.enable()
        with self.assertRaises(ValueError): self.toolkit.call('fractal_execute_review', self.execute_args(review))
        atomic_json(folder / 'organization/request.json', {'altered': True})
        with self.assertRaises(ValueError): self.toolkit.approve(review['review_id'], review['review_sha256'])

    def test_performance_contract_is_closed_and_live_capture_requires_access(self):
        schema = TOOL_MAP['fractal_prepare_performance_control']['inputSchema']
        for request in ({'kind': 'tempo-read'}, {'kind': 'tempo-set', 'bpm': 20}, {'kind': 'tempo-set', 'bpm': 250},
                        {'kind': 'tuner-set', 'enabled': False}, {'kind': 'looper-state', 'action': 'half-speed', 'enabled': True}):
            validate(schema, {'context_artifact': 'a' * 32, **request})
        for bad in ({'kind': 'tempo-set', 'bpm': 19}, {'kind': 'tempo-set', 'bpm': 250.5}, {'kind': 'tempo-tap', 'repeat': 10},
                    {'kind': 'tuner-toggle'}, {'kind': 'looper-button', 'action': 'erase'}):
            with self.assertRaises(ValueError): validate(schema, {'context_artifact': 'a' * 32, **bad})
        with self.assertRaises(ValueError): self.toolkit.call('fractal_capture_performance_context', {})
        self.assertEqual(self.runtime.calls, [])

    def test_performance_event_completion_remains_sent_unverified_and_consumes_review(self):
        identifier, folder = self.toolkit.artifacts.create()
        (folder / 'performance').mkdir()
        atomic_json(folder / 'performance/context.json', {'schema_version': 1, 'preset': {'model': 'FM9', 'capacity': 512, 'firmware': '99.07'}, 'looper': None})
        review = self.toolkit.call('fractal_prepare_performance_control', {'context_artifact': identifier, 'kind': 'tuner-set', 'enabled': True})
        self.enable(); self.toolkit.approve(review['review_id'], review['review_sha256'])
        self.runtime.run = lambda *args, **kwargs: {'complete': True, 'state_verified': False, 'phase': 'sent-unverified'}
        result = self.toolkit.execute(self.execute_args(review), threading.Event())
        attempt = self.toolkit.call('fractal_inspect_attempt', {'review_id': review['review_id']})
        self.assertEqual(attempt['status'], 'sent-unverified')
        self.assertFalse(attempt['completion_verified'])
        self.assertEqual(attempt['result']['artifact_id'], result['artifact_id'])
        self.assertIn('do not replay', attempt['next_action'])
        with self.assertRaises(ValueError): self.toolkit.approve(review['review_id'], review['review_sha256'])
        with self.assertRaises(ValueError): self.toolkit.execute(self.execute_args(review), threading.Event())

    def test_template_library_and_swap_contracts_are_offline_and_closed(self):
        self.assertEqual(self.toolkit.call('fractal_list_templates', {}),
                         {'entries': [], 'total': 0, 'next_offset': None, 'ports_opened': False})
        self.assertEqual(self.runtime.calls, [])
        for name in ('import_template', 'list_templates', 'inspect_template', 'plan_template'):
            definition = TOOL_MAP['fractal_' + name]
            self.assertFalse(definition['inputSchema']['additionalProperties'])
            self.assertFalse(definition['annotations']['destructiveHint'])
        schema = TOOL_MAP['fractal_plan_preset_edit']['inputSchema']
        args = {'backup_artifact': 'a' * 32, 'slot': 0, 'persistence': 'save', 'operations': [
            {'kind': 'swap-channels', 'effect_id': 66, 'channel': 'A', 'other_channel': 'C', 'policy': 'follow-channel-settings'}]}
        validate(schema, args)
        for field, bad in [('other_channel', 'E'), ('policy', 'toggle'), ('approve', True)]:
            invalid = {**args, 'operations': [{**args['operations'][0], field: bad}]}
            with self.assertRaises(ValueError): validate(schema, invalid)
        with self.assertRaises(ValueError):
            self.toolkit.call('fractal_inspect_template', {'entry_id': '../escape', 'entry_sha256': '0' * 64})
        self.assertEqual(self.runtime.calls, [])

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.folder = Path(self.temporary.name)
        self.config = {'schema_version': 1, 'data_root': str(self.folder / 'data'), 'runtime_dir': str(self.folder / 'runtime'),
                       'allow_live_reads': False, 'allow_writes': False, 'device': DEVICE.copy()}
        self.runtime = FakeRuntime()
        self.toolkit = Toolkit(ROOT, self.config, self.runtime)

    def tearDown(self):
        self.toolkit.stop_execution_jobs()
        self.temporary.cleanup()

    def load_review(self):
        identifier, directory = self.toolkit.artifacts.create()
        (directory / 'context').mkdir()
        atomic_json(directory / 'context/load-request.json', {'model': 'FM9', 'capacity': 512, 'firmware': '99.07',
                    'current_slot': 42, 'target_slot': 43, 'discard_changes': False})
        return self.toolkit.call('fractal_prepare_load', {'context_artifact': identifier})

    def enable(self):
        self.config.update(allow_live_reads=True, allow_writes=True)

    def execute_args(self, review):
        return {'review_id': review['review_id'], 'review_sha256': review['review_sha256']}

    def test_status_never_launches_runtime(self):
        self.assertFalse(self.toolkit.call('fractal_status', {})['ports_opened'])
        self.assertEqual(self.runtime.calls, [])

    def test_controller_listener_requires_live_reads_and_never_requests_output(self):
        request = {'input': 12345, 'duration_seconds': 1, 'max_events': 16,
                   'rules': [{'input_kind': 'note', 'input_channel': 2, 'number': 60,
                              'target': {'kind': 'scene', 'scene': 3, 'cc': {'channel': 1, 'controller': 17, 'value': 2}}}]}
        with self.assertRaises(ValueError): self.toolkit.call('fractal_listen_controller', request)
        self.assertEqual(self.runtime.calls, [])
        self.config['allow_live_reads'] = True
        result = self.toolkit.call('fractal_listen_controller', request)
        self.assertIn('artifact_id', result)
        self.assertIn('--validate-only', self.runtime.calls[0][1])
        self.assertNotIn('--output', self.runtime.calls[-1][1])
        self.assertNotIn('--hardware', self.runtime.calls[-1][1])
        self.assertFalse(self.config['allow_writes'])
        with self.assertRaises(ValueError): self.toolkit.call('fractal_listen_controller', {**request, 'duration_seconds': 31})

    def test_network_configuration_replaces_ports_and_binds_review(self):
        network = {'model': 'FM9', 'capacity': 512, 'firmware': '99.07', 'network': '127.0.0.1:5004'}
        self.config['device'] = network
        config_file = self.folder / 'config.json'
        atomic_json(config_file, self.config)
        self.assertEqual(load_config(config_file)['device'], network)
        self.assertEqual(self.toolkit.connection_args(), ['--network', network['network']])
        self.assertNotIn('--input', self.toolkit.device_args(True))
        for endpoint in ('localhost:5004', '127.0.0.1:65535', '0.0.0.0:5004', '224.0.0.1:5004', '127.0.0.1:-1'):
            atomic_json(config_file, {**self.config, 'device': {**network, 'network': endpoint}})
            with self.assertRaises(ValueError): load_config(config_file)
        atomic_json(config_file, {**self.config, 'device': {**network, 'input': 0, 'output': 0}})
        with self.assertRaises(ValueError): load_config(config_file)
        self.enable()
        review = self.load_review()
        retained = self.toolkit.artifacts.json(review['review_id'], 'review.json')
        self.assertEqual(retained['device']['network'], network['network'])

    def test_passive_notifications_require_live_reads_and_use_configured_transport(self):
        request = {'duration_seconds': 1, 'max_events': 4}
        with self.assertRaises(ValueError): self.toolkit.call('fractal_listen_device_notifications', request)
        self.assertFalse(self.runtime.calls)
        self.config['allow_live_reads'] = True
        result = self.toolkit.call('fractal_listen_device_notifications', request)
        self.assertIn('artifact_id', result)
        self.assertNotIn('--output', self.runtime.calls[-1][1])
        self.config['device'] = {'model': 'FM9', 'capacity': 512, 'firmware': '99.07', 'network': '127.0.0.1:5004'}
        self.toolkit.call('fractal_listen_device_notifications', request)
        self.assertIn('--network', self.runtime.calls[-1][1])
        self.assertNotIn('--hardware', self.runtime.calls[-1][1])
        self.assertFalse(self.config['allow_writes'])
        self.runtime.fail = True
        with self.assertRaises(Cancelled): self.toolkit.call('fractal_listen_device_notifications', request)

    def test_block_parameters_use_configured_read_access_and_retain_failure(self):
        request = {'effect_id': 66}
        with self.assertRaises(ValueError): self.toolkit.call('fractal_read_block_parameters', request)
        self.assertFalse(self.runtime.calls)
        self.config['allow_live_reads'] = True
        for invalid in ({}, {'effect_id': 0}, {'effect_id': 1001}, {'effect_id': 66, 'network': '127.0.0.1:5004'}):
            with self.assertRaises(ValueError): self.toolkit.call('fractal_read_block_parameters', invalid)
        for model, capacity in [('FM9', 512), ('FM3', 512), ('Axe-Fx III', 512), ('Axe-Fx III', 1024)]:
            self.config['device'] = {'model': model, 'capacity': capacity, 'firmware': '99.07', 'network': '127.0.0.1:5004'}
            result = self.toolkit.call('fractal_read_block_parameters', request)
            self.assertIn('artifact_id', result)
            command = self.runtime.calls[-1][1]
            self.assertEqual(command[0], 'read-block-parameters')
            self.assertIn('--network', command)
            self.assertEqual(command[command.index('--effect-id') + 1], '66')
            self.assertFalse(self.config['allow_writes'])
        self.runtime.fail = True
        with self.assertRaisesRegex(ValueError, 'retained evidence artifact'):
            self.toolkit.call('fractal_read_block_parameters', request)

    def test_automatic_path_requires_explicit_waypoints_and_shunt_identity(self):
        schema = TOOL_MAP['fractal_plan_audio_path']['inputSchema']
        request = {'backup_artifact': 'a' * 32, 'slot': 0, 'persistence': 'save',
                   'waypoints': [{'row': 1, 'column': 1}, {'row': 1, 'column': 5}], 'shunt_id': 1024}
        validate(schema, request)
        for invalid in ({k: v for k, v in request.items() if k != 'shunt_id'}, {**request, 'waypoints': []}, {**request, 'sharing_policy': 'overwrite'}):
            with self.assertRaises(ValueError): validate(schema, invalid)

    def test_live_tools_respect_configured_access_and_bounds(self):
        for name, arguments in [('read_amp_roster', {'start': 0, 'count': 2}), ('capture_live_control_context', {}), ('monitor_live_state', {'samples': 2, 'interval_ms': 100})]:
            with self.assertRaises(ValueError):
                self.toolkit.call('fractal_' + name, arguments)
        self.assertEqual(self.runtime.calls, [])
        self.enable()
        with self.assertRaises(ValueError):
            self.toolkit.call('fractal_read_amp_roster', {'start': 16383, 'count': 2})
        result = self.toolkit.call('fractal_read_amp_roster', {'start': 128, 'count': 2})
        self.assertIn('artifact_id', result)
        self.assertIn('amp-roster', self.runtime.calls[-1][1])
        self.assertIn('--firmware', self.runtime.calls[-1][1])

    def test_live_control_review_freezes_context_and_exact_intent(self):
        identifier, folder = self.toolkit.artifacts.create()
        (folder / 'context').mkdir()
        context = {'schema_version': 1, 'model': 'FM9', 'capacity': 512, 'firmware': '99.07'}
        atomic_json(folder / 'context/context.json', context)
        review = self.toolkit.call('fractal_prepare_live_control', {'context_artifact': identifier, 'kind': 'bypass', 'effect_id': 58, 'bypassed': False})
        request = self.toolkit.artifacts.json(review['review_id'], 'request.json')
        self.assertEqual(request['context'], context)
        self.assertIs(request['bypassed'], False)
        self.assertIn('--live-control', self.runtime.calls[-1][1])
        self.assertIn('--validate-only', self.runtime.calls[-1][1])
        self.assertTrue(review['approval_required'])
        context['firmware'] = '99.08'
        atomic_json(folder / 'context/context.json', context)
        with self.assertRaises(ValueError):
            self.toolkit.validate_review(review['review_id'], review['review_sha256'])

    def test_calibration_and_modifier_contracts_keep_explicit_evidence(self):
        calibration = TOOL_MAP['fractal_calibrate_saved_parameter']['inputSchema']
        request = {'effect_id': 66, 'channel': 'A', 'key': 'reverb.observed_level', 'unit': 'dB', 'kind': 'parameter',
                   'samples': [{'backup_artifact': 'a' * 32, 'slot': 0, 'value': -12}, {'backup_artifact': 'b' * 32, 'slot': 0, 'value': -6}]}
        validate(calibration, request)
        validate(calibration, {**{k: v for k, v in request.items() if k != 'channel'}, 'kind': 'modifier',
                               'modifier_slot': 1, 'expected_binding': [14, 66, 7], 'key': 'modifier.scale'})
        for invalid in ({**request, 'samples': request['samples'][:1]}, {**request, 'kind': 'arbitrary-offset'}, {**request, 'offset': 200}):
            with self.assertRaises(ValueError): validate(calibration, invalid)
        edit = TOOL_MAP['fractal_plan_preset_edit']['inputSchema']
        for operation in (
            {'kind': 'set-scaled-parameter', 'effect_id': 58, 'channel': 'A', 'key': 'amp.level', 'physical_value': -15, 'unit': 'dB'},
            {'kind': 'set-modifier-parameter', 'modifier_slot': 1, 'expected_binding': [14, 70, 4], 'key': 'modifier.minimum', 'physical_value': -75, 'unit': '%'},
            {'kind': 'set-modifier-parameter', 'modifier_slot': 1, 'expected_binding': [14, 70, 4], 'key': 'modifier.offset', 'physical_value': 0.25, 'unit': 'saved-normalized'},
            {'kind': 'copy-modifier-selector', 'modifier_slot': 1, 'expected_binding': [14, 70, 4], 'key': 'modifier.damping_type', 'template_modifier_slot': 1, 'template_artifact': 'd' * 32, 'template_slot': 0},
            {'kind': 'set-calibrated-parameter', 'effect_id': 66, 'channel': 'A', 'key': 'reverb.observed_level', 'calibrated_value': -6, 'unit': 'dB', 'calibration_artifact': 'c' * 32},
            {'kind': 'set-calibrated-modifier', 'modifier_slot': 1, 'expected_binding': [14, 66, 7], 'key': 'modifier.scale', 'calibrated_value': 50, 'unit': 'display-%', 'calibration_artifact': 'c' * 32},
            {'kind': 'set-scene-ignore', 'effect_id': 58, 'key': 'amp.scene_ignore', 'enabled': True, 'unit': 'boolean', 'calibration_artifact': 'c' * 32},
            {'kind': 'set-modifier-source', 'modifier_slot': 1, 'source_id': 15, 'expected_binding': [14, 66, 7]},
            {'kind': 'copy-modifier', 'modifier_slot': 2, 'template_modifier_slot': 1, 'template_artifact': 'd' * 32, 'template_slot': 0, 'expected_binding': [0, 0, 0]},
        ):
            validate(edit, {'backup_artifact': 'a' * 32, 'slot': 0, 'persistence': 'save', 'operations': [operation]})
        self.assertFalse(TOOL_MAP['fractal_calibrate_saved_parameter']['annotations']['destructiveHint'])

    def test_calibration_preparation_and_planning_are_offline_and_embed_witnesses(self):
        def export(raw):
            identifier, folder = self.toolkit.artifacts.create(); folder = folder / 'export'; folder.mkdir()
            atomic_json(folder / 'snapshot.json', {'device': {'model': 'FM9', 'preset_capacity': 512, 'firmware': '99.07'}})
            (folder / 'witness.syx').write_bytes(raw)
            atomic_json(folder / 'manifest.json', {'snapshot_file': 'snapshot.json', 'snapshot_sha256': sha(folder / 'snapshot.json'),
                        'captures': [{'slot': 0, 'file': 'witness.syx', 'sysex_sha256': sha(folder / 'witness.syx')}]})
            return identifier
        first, second = export(b'synthetic sample one'), export(b'synthetic sample two')
        result = self.toolkit.call('fractal_calibrate_saved_parameter', {'effect_id': 66, 'channel': 'A', 'key': 'reverb.observed_level', 'unit': 'dB', 'kind': 'parameter',
                                  'samples': [{'backup_artifact': first, 'slot': 0, 'value': -12}, {'backup_artifact': second, 'slot': 0, 'value': -6}]})
        request = self.toolkit.artifacts.json(result['artifact_id'], 'request.json')
        self.assertEqual(self.runtime.calls[-1][1][0], 'calibrate-parameter')
        self.assertIn('capture', request['samples'][0])
        folder = self.toolkit.artifacts.directory(result['artifact_id']) / 'calibration'; folder.mkdir()
        atomic_json(folder / 'calibration.json', request)  # Fake runtime output; real byte validation is exercised by the relocated pipeline.
        planned = self.toolkit.call('fractal_plan_preset_edit', {'backup_artifact': first, 'slot': 0, 'persistence': 'save', 'operations': [
            {'kind': 'set-calibrated-parameter', 'effect_id': 66, 'channel': 'A', 'key': 'reverb.observed_level', 'unit': 'dB', 'calibrated_value': -6, 'calibration_artifact': result['artifact_id']}]})
        operation = self.toolkit.artifacts.json(planned['artifact_id'], 'request.json')['operations'][0]
        self.assertEqual(operation['calibration'], request)
        self.assertNotIn('calibration_artifact', operation)
        modifier_result = self.toolkit.call('fractal_calibrate_saved_parameter', {'effect_id': 66, 'modifier_slot': 1,
            'expected_binding': [14, 66, 7], 'key': 'modifier.scale', 'unit': 'display-%', 'kind': 'modifier',
            'samples': [{'backup_artifact': first, 'slot': 0, 'value': 10}, {'backup_artifact': second, 'slot': 0, 'value': 50}]})
        modifier_request = self.toolkit.artifacts.json(modifier_result['artifact_id'], 'request.json')
        self.assertEqual(modifier_request['modifier_slot'], 1)
        self.assertEqual(modifier_request['expected_binding'], [14, 66, 7])
        modifier_folder = self.toolkit.artifacts.directory(modifier_result['artifact_id']) / 'calibration'; modifier_folder.mkdir()
        atomic_json(modifier_folder / 'calibration.json', modifier_request)
        modifier_plan = self.toolkit.call('fractal_plan_preset_edit', {'backup_artifact': first, 'slot': 0, 'persistence': 'save', 'operations': [
            {'kind': 'set-calibrated-modifier', 'modifier_slot': 1, 'expected_binding': [14, 66, 7], 'key': 'modifier.scale',
             'unit': 'display-%', 'calibrated_value': 50, 'calibration_artifact': modifier_result['artifact_id']} ]})
        self.assertEqual(self.toolkit.artifacts.json(modifier_plan['artifact_id'], 'request.json')['operations'][0]['calibration'], modifier_request)
        self.assertFalse(self.config['allow_live_reads']); self.assertFalse(self.config['allow_writes'])
        self.assertTrue(all('--input' not in args and '--network' not in args for _, args in self.runtime.calls))

    def test_edit_values_reject_nonfinite_boolean_and_unknown_fields(self):
        schema = TOOL_MAP['fractal_plan_preset_edit']['inputSchema']
        request = {'backup_artifact': 'a' * 32, 'slot': 0, 'persistence': 'edit-buffer',
                   'operations': [{'kind': 'set-parameter', 'effect_id': 58, 'channel': 'A',
                                   'key': 'amp.drive', 'value': 4.25, 'unit': 'normalized-0-10'}]}
        validate(schema, request)
        for invalid in (float('nan'), float('inf'), True, -1, 65536):
            request['operations'][0]['value'] = invalid
            with self.assertRaises(ValueError):
                validate(schema, request)
        request['operations'][0]['value'] = 4.25
        request['operations'][0]['sysex'] = [240, 247]
        with self.assertRaises(ValueError):
            validate(schema, request)

    def test_configured_midi_is_explicit_and_frozen_in_review(self):
        identifier, folder = self.toolkit.artifacts.create()
        (folder / 'context').mkdir()
        atomic_json(folder / 'context/context.json', {'schema_version': 1, 'model': 'FM9', 'capacity': 512, 'firmware': '99.07'})
        arguments = {'context_artifact': identifier, 'kind': 'scene', 'scene': 3,
                     'midi_assignment': {'channel': 16, 'controller': 17, 'value': 99}}
        review = self.toolkit.call('fractal_prepare_live_control', arguments)
        self.assertEqual(self.toolkit.artifacts.json(review['review_id'], 'request.json')['midi_assignment'], arguments['midi_assignment'])
        arguments['midi_assignment']['channel'] = 0
        with self.assertRaises(ValueError):
            self.toolkit.call('fractal_prepare_live_control', arguments)
        self.assertTrue(all('--validate-only' in call[1] for call in self.runtime.calls))

    def test_route_events_are_offline_and_batch_schema_bounded(self):
        request = {'model': 'FM3', 'capacity': 512, 'firmware': '99.07',
                   'rules': [{'input_kind': 'note', 'input_channel': 2, 'number': 60,
                              'target': {'kind': 'scene', 'scene': 3, 'cc': {'channel': 1, 'controller': 17, 'value': 2}}}],
                   'events': [{'kind': 'note-on', 'channel': 2, 'number': 60, 'value': 127}]}
        result = self.toolkit.call('fractal_route_midi_events', request)
        request['events'].append(request['events'][0].copy())
        self.toolkit.call('fractal_route_midi_events', request)
        self.assertIn('artifact_id', result)
        self.assertEqual(self.runtime.calls[-1][0], 'cli')
        self.assertEqual(self.runtime.calls[-1][1][0], 'route-midi')
        self.assertNotIn('--input', self.runtime.calls[-1][1])
        schema = TOOL_MAP['fractal_prepare_edit_batch']['inputSchema']
        for count in (0, 33):
            with self.assertRaises(ValueError):
                validate(schema, {'context_artifact': 'a' * 32, 'plan_artifacts': ['b' * 32] * count})

    def test_message_preconditions_preserve_repeated_raw_words(self):
        schema = TOOL_MAP['fractal_plan_preset_edit']['inputSchema']
        request = {'backup_artifact': 'a' * 32, 'slot': 0, 'persistence': 'save', 'operations': [
            {'kind': 'set-scene-midi', 'scene': 1, 'message_slot': 1, 'message_type': 'Off', 'expected_raw': [1, 0, 0, 0]}]}
        validate(schema, request)
        for raw in ([1, 0, 0], [1, 0, 0, 65536], [True, 0, 0, 0]):
            request['operations'][0]['expected_raw'] = raw
            with self.assertRaises(ValueError):
                validate(schema, request)

    def test_edit_catalogue_uses_offline_model_context(self):
        self.toolkit.call('fractal_query_parameters', {'model': 'FM3', 'capacity': 512, 'firmware': '99.07', 'family': 'Amp'})
        self.assertEqual(self.runtime.calls, [('cli', ['edit-catalogue', '--model', 'FM3', '--capacity', '512',
                                                     '--firmware', '99.07', '--family', 'Amp'])])

    def test_inventory_live_access_and_scope(self):
        with self.assertRaises(ValueError):
            self.toolkit.call('fractal_capture_inventory', {'all': True})
        self.assertEqual(self.runtime.calls, [])
        self.config['allow_live_reads'] = True
        for arguments in ({}, {'slots': [0], 'all': True}, {'slots': [512]}):
            with self.assertRaises(ValueError):
                self.toolkit.call('fractal_capture_inventory', arguments)
        result = self.toolkit.call('fractal_capture_inventory', {'slots': [0, 511]})
        self.assertIn('artifact_id', result)
        arguments = self.runtime.calls[-1][1]
        self.assertEqual(arguments.count('--preset'), 2)
        self.assertIn('--firmware', arguments)
        self.assertNotIn('--all', arguments)

    def test_feedback_policy_is_explicit_and_closed(self):
        schema = TOOL_MAP['fractal_plan_preset_edit']['inputSchema']
        request = {'backup_artifact': 'a' * 32, 'slot': 0, 'persistence': 'save', 'feedback_policy': 'allow',
                   'operations': [{'kind': 'copy-block-settings', 'effect_id': 186, 'template_artifact': 'b' * 32, 'template_slot': 1}]}
        validate(schema, request)
        request['feedback_policy'] = 'automatic'
        with self.assertRaises(ValueError):
            validate(schema, request)

    def test_status_distinguishes_general_operations_from_hardware_evidence(self):
        resources = {r['uri'] for r in self.toolkit.list_resources()}
        for model, capacity in [('FM9', 512), ('FM3', 512), ('Axe-Fx III', 512), ('Axe-Fx III', 1024)]:
            self.config['device'] = dict(DEVICE, model=model, capacity=capacity)
            status = self.toolkit.call('fractal_status', {})
            self.assertIn(status['capabilities']['catalog'], resources)
            self.assertIn(status['hardware_evidence'], resources)
            self.assertIn('model, firmware and block family', status['capabilities']['block_insertion'])
            self.assertIn('separate reviews', status['capabilities']['combined_operations'])
            self.assertNotIn('Scene MIDI', status['capabilities']['block_insertion'])
        self.assertEqual(self.runtime.calls, [])

    def test_all_tools_have_closed_input_schemas_and_unique_names(self):
        self.assertEqual(len(TOOLS), len(TOOL_MAP))
        for tool in TOOLS:
            self.assertFalse(tool['inputSchema']['additionalProperties'])
        self.assertTrue(TOOL_MAP['fractal_execute_review']['annotations']['destructiveHint'])
        self.assertFalse(TOOL_MAP['fractal_prepare_load']['annotations']['destructiveHint'])

    def test_removal_and_undo_are_closed_preparation_tools(self):
        for name in ('fractal_plan_block_removal','fractal_prepare_removal','fractal_prepare_insertion_undo'):
            self.assertFalse(TOOL_MAP[name]['annotations']['destructiveHint'])
            self.assertFalse(TOOL_MAP[name]['inputSchema']['additionalProperties'])
        self.assertIn('block_removal',self.toolkit.call('fractal_status',{})['capabilities'])
        for args in ({'execution_artifact':'0'*32}, {'execution_artifact':'../unsafe'}, {'execution_artifact':'0'*32,'approve':True}):
            with self.assertRaises(ValueError): self.toolkit.call('fractal_prepare_insertion_undo',args)
        self.assertEqual(self.runtime.calls,[])

    def test_unknown_and_extra_arguments_are_refused(self):
        with self.assertRaises(KeyError): self.toolkit.call('run_shell', {})
        with self.assertRaises(ValueError): self.toolkit.call('fractal_status', {'command': 'anything'})
        self.assertEqual(self.runtime.calls, [])

    def test_integer_parameters_reject_booleans_and_duplicate_slots(self):
        schema = TOOL_MAP['fractal_capture_presets']['inputSchema']
        for value in [{'slots': [True]}, {'slots': [0, 0]}, {'slots': [-1]}, {'slots': [1024]}, {'slots': []}]:
            with self.assertRaises(ValueError): validate(schema, value)

    def test_live_tools_are_disabled_before_any_runtime_launch(self):
        for name, arguments in [('fractal_list_ports', {}), ('fractal_inspect_device', {}), ('fractal_capture_presets', {'slots': [0]}),
                                ('fractal_read_scene_midi', {}), ('fractal_capture_load_context', {'target_slot': 0, 'discard_changes': False})]:
            with self.assertRaises(ValueError): self.toolkit.call(name, arguments)
        self.assertEqual(self.runtime.calls, [])

    def test_search_uses_retained_sources_and_has_no_live_effect(self):
        result = self.toolkit.call('fractal_search_knowledge', {'query': 'Scene MIDI recovery'})
        self.assertTrue(result['sources']); self.assertTrue(result['capabilities'])
        self.assertEqual(self.runtime.calls, [])

    def test_amp_catalogue_does_not_leak_to_other_models(self):
        with self.assertRaises(ValueError): self.toolkit.call('fractal_query_amp', {'model': 'FM3', 'firmware': '12.00', 'model_id': 0})

    def test_block_lookup_uses_common_offline_helper_without_device_configuration(self):
        self.config.pop('device')
        self.toolkit.call('fractal_query_block', {'model': 'FM3', 'query': 'Reverb', 'firmware': '99.07'})
        self.assertEqual(self.runtime.calls, [('cli', ['blocks', '--model', 'FM3', '--firmware', '99.07', '--query', 'Reverb'])])

    def test_block_lookup_rejects_missing_context_and_invalid_ids_before_launch(self):
        for args in [{'model': 'FM9'}, {'model': 'Other', 'query': 'Reverb'}, {'model': 'FM9', 'effect_id': -1},
                     {'model': 'FM9', 'effect_id': True}, {'model': 'FM9', 'effect_id': 65536}, {'model': 'FM9', 'query': 'Reverb', 'input': 0}]:
            with self.assertRaises(ValueError): self.toolkit.call('fractal_query_block', args)
        self.assertEqual(self.runtime.calls, [])

    def test_block_catalogue_resource_is_contained_and_source_attributed(self):
        status = self.toolkit.call('fractal_status', {})
        data = json.loads(self.toolkit.read_resource(status['capabilities']['block_catalogue'])['contents'][0]['text'])
        reverb = next(e for e in data['entries'] if e['effect_id'] == 67)
        self.assertEqual(reverb['name'], 'Reverb 2')
        self.assertFalse(reverb['documented_available']['FM3'])
        self.assertIn('gen3-body', reverb['identity_source_ids'])
        self.assertEqual(self.runtime.calls, [])

    def test_public_resources_exclude_private_data_and_large_catalogues(self):
        identifier, folder = self.toolkit.artifacts.create(); (folder / 'secret.json').write_text('{"secret":true}')
        resources = self.toolkit.list_resources()
        self.assertTrue(any('reference-map.md' in r['uri'] for r in resources))
        self.assertFalse(any(identifier in r['uri'] or 'private' in r['uri'] or 'fm9-12-amp-identities.json' in r['uri'] for r in resources))
        with self.assertRaises(ValueError): self.toolkit.read_resource('file:///outside')

    def test_artifact_paths_cannot_reach_approvals_or_outside_files(self):
        identifier, folder = self.toolkit.artifacts.create()
        for path in ['../../approvals/a.json', '/outside', 'C:/outside', '..\\outside', 'a/../b', './file']:
            with self.assertRaises(ValueError): self.toolkit.artifacts.file(identifier, path)

    def test_artifact_symlinks_are_refused(self):
        identifier, folder = self.toolkit.artifacts.create()
        outside = self.folder / 'outside.json'; outside.write_text('{}')
        try: (folder / 'link.json').symlink_to(outside)
        except OSError:
            if os.name != 'nt':
                self.skipTest('Symlinks unavailable on this host.')
            import subprocess
            directory = self.folder / 'outside-directory'; directory.mkdir(); (directory / 'file.json').write_text('{}')
            junction = folder / 'linked-directory'
            env = {**os.environ, 'FRACTAL_TEST_LINK': str(junction), 'FRACTAL_TEST_TARGET': str(directory)}
            result = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-Command',
                'New-Item -ItemType Junction -Path $env:FRACTAL_TEST_LINK -Target $env:FRACTAL_TEST_TARGET | Out-Null'], env=env, capture_output=True)
            self.assertEqual(result.returncode, 0)
            with self.assertRaises(ValueError): self.toolkit.artifacts.file(identifier, 'linked-directory/file.json')
        else:
            with self.assertRaises(ValueError): self.toolkit.artifacts.file(identifier, 'link.json')

    def test_private_text_artifacts_are_paged_and_hashed(self):
        identifier, folder = self.toolkit.artifacts.create(); (folder / 'text.json').write_text('abcdef')
        r = self.toolkit.call('fractal_read_artifact', {'artifact_id': identifier, 'file': 'text.json', 'offset': 1, 'length': 2})
        self.assertEqual(r['text'], 'bc'); self.assertEqual(r['next_offset'], 3); self.assertEqual(len(r['sha256']), 64)

    def test_duplicate_json_keys_and_nonfinite_values_refused(self):
        for value in ['{"a":1,"a":2}', '{"a":NaN}', '{"a":Infinity}', '{"a":1e1000}']:
            with self.assertRaises(ValueError): loads(value)

    def test_journal_replace_retries_only_bounded_sharing_failures(self):
        from unittest.mock import patch
        path = self.folder / 'journal.json'
        original = os.replace
        with patch('fractal_mcp.storage.os.replace', side_effect=[PermissionError('reader'), None]) as replace:
            atomic_json(path, {'synthetic': True})
            self.assertEqual(replace.call_count, 2)
        with patch('fractal_mcp.storage.os.replace', side_effect=PermissionError('permanent')) as replace:
            with self.assertRaises(PermissionError): atomic_json(path, {})
            self.assertEqual(replace.call_count, 21)
        with patch('fractal_mcp.storage.os.replace', side_effect=OSError('disk')) as replace:
            with self.assertRaises(OSError): atomic_json(path, {})
            self.assertEqual(replace.call_count, 1)

    def test_preview_is_offline_even_when_device_reads_disabled(self):
        review = self.load_review()
        self.assertFalse(review['ports_opened']); self.assertTrue(review['approval_required'])
        self.assertIn('--validate-only', self.runtime.calls[0][1])
        self.assertNotIn('--input', self.runtime.calls[0][1])

    def test_model_cannot_approve_its_own_review(self):
        review = self.load_review(); self.enable()
        with self.assertRaises(ValueError): self.toolkit.call('fractal_execute_review', self.execute_args(review))
        with self.assertRaises(KeyError): self.toolkit.call('fractal_approve', self.execute_args(review))
        self.assertEqual(len(self.runtime.calls), 1)

    def test_operator_approval_requires_enabled_writes(self):
        review = self.load_review()
        with self.assertRaises(ValueError): self.toolkit.approve(review['review_id'], review['review_sha256'])

    def test_stale_review_hash_refused_before_execution(self):
        review = self.load_review(); self.enable()
        with self.assertRaises(ValueError): self.toolkit.approve(review['review_id'], '0' * 64)
        self.assertEqual(len(self.runtime.calls), 1)

    def test_frozen_input_tampering_refused(self):
        review = self.load_review(); self.enable()
        self.toolkit.artifacts.file(review['review_id'], 'context/load-request.json').write_text('{}')
        with self.assertRaises(ValueError): self.toolkit.approve(review['review_id'], review['review_sha256'])

    def test_source_change_after_preview_refused(self):
        review = self.load_review(); self.enable()
        path = self.toolkit.artifacts.file(review['review_id'], 'review.json')
        source = loads(path.read_text())['sources'][0]['path']
        (confined(self.toolkit.artifacts.root, source) / 'load-request.json').write_text('{}')
        with self.assertRaises(ValueError): self.toolkit.approve(review['review_id'], review['review_sha256'])

    def test_port_or_firmware_changes_invalidate_approval(self):
        review = self.load_review(); self.enable()
        self.toolkit.approve(review['review_id'], review['review_sha256'])
        self.config['device']['output'] += 1
        with self.assertRaises(ValueError): self.toolkit.call('fractal_execute_review', self.execute_args(review))
        self.assertEqual(len(self.runtime.calls), 1)

    def test_moved_private_data_requires_a_fresh_review(self):
        import shutil
        review = self.load_review(); self.enable()
        destination = self.folder / 'moved-data'; shutil.copytree(self.toolkit.artifacts.root, destination)
        moved = Toolkit(ROOT, {**self.config, 'data_root': str(destination)}, self.runtime)
        with self.assertRaises(ValueError): moved.approve(review['review_id'], review['review_sha256'])
        self.assertEqual(len(self.runtime.calls), 1)

    def test_execution_consumes_approval_once_and_records_exact_result(self):
        review = self.load_review(); self.enable(); self.toolkit.approve(review['review_id'], review['review_sha256'])
        result = self.toolkit.execute(self.execute_args(review), threading.Event())
        self.assertTrue(result['result']['complete'])
        attempt = self.toolkit.call('fractal_inspect_attempt', {'review_id': review['review_id']})
        self.assertEqual(attempt['status'], 'verified-complete')
        self.assertEqual(attempt['execution_artifact'], result['artifact_id'])
        with self.assertRaises(ValueError): self.toolkit.call('fractal_execute_review', self.execute_args(review))
        with self.assertRaises(ValueError): self.toolkit.approve(review['review_id'], review['review_sha256'])
        self.assertEqual(len(self.runtime.calls), 2)

    def test_cancelled_or_uncertain_execution_cannot_replay(self):
        review = self.load_review(); self.enable(); self.toolkit.approve(review['review_id'], review['review_sha256']); self.runtime.fail = True
        with self.assertRaises(ValueError): self.toolkit.execute(self.execute_args(review), threading.Event())
        self.assertEqual(self.toolkit.call('fractal_inspect_attempt', {'review_id': review['review_id']})['status'], 'stopped-uncertain')
        with self.assertRaises(ValueError): self.toolkit.call('fractal_execute_review', self.execute_args(review))
        self.assertEqual(len(self.runtime.calls), 2)

    def test_cancellation_before_execution_preserves_approval(self):
        review = self.load_review(); self.enable(); self.toolkit.approve(review['review_id'], review['review_sha256'])
        cancel = threading.Event(); cancel.set()
        with self.assertRaises(Cancelled): self.toolkit.call('fractal_execute_review', self.execute_args(review), cancel)
        self.assertTrue((self.toolkit.artifacts.root / 'approvals' / (review['review_id'] + '.json')).exists())
        self.assertEqual(len(self.runtime.calls), 1)

    def test_wrong_approval_hash_cannot_execute(self):
        review = self.load_review(); self.enable(); self.toolkit.approve(review['review_id'], review['review_sha256'])
        path = self.toolkit.artifacts.root / 'approvals' / (review['review_id'] + '.json')
        atomic_json(path, {'review_id': review['review_id'], 'review_sha256': '0' * 64, 'device': DEVICE})
        with self.assertRaises(ValueError): self.toolkit.call('fractal_execute_review', self.execute_args(review))
        self.assertEqual(len(self.runtime.calls), 1)

    def test_two_cooperating_owners_cannot_open_same_ports(self):
        with device_owner(DEVICE):
            with self.assertRaises(ValueError):
                with device_owner(DEVICE): pass
        with device_owner(DEVICE): pass

    def running_job(self):
        self.runtime.run = FakeRuntime.run.__get__(self.runtime)
        review = self.load_review(); self.enable(); self.toolkit.approve(review['review_id'], review['review_sha256'])
        release = threading.Event(); entered = threading.Event()
        def slow(helper, arguments, cancel, **kw):
            entered.set()
            while not release.wait(.01):
                if cancel.is_set():
                    raise Cancelled('Synthetic background stop.')
            return {'complete': True}
        self.runtime.run = slow
        result = self.toolkit.call('fractal_execute_review', self.execute_args(review))
        self.assertTrue(result['accepted']); self.assertTrue(entered.wait(2))
        return review, release

    def test_background_acceptance_is_not_completion_and_cancelled_poll_does_not_stop_job(self):
        review, release = self.running_job()
        self.assertEqual(self.toolkit.call('fractal_inspect_attempt', {'review_id': review['review_id']})['status'], 'attempted')
        with self.assertRaises(ValueError): self.toolkit.call('fractal_execute_review', self.execute_args(review))
        cancel = threading.Event(); timer = threading.Timer(.05, cancel.set); timer.start()
        with self.assertRaises(Cancelled):
            self.toolkit.call('fractal_inspect_attempt', {'review_id': review['review_id'], 'wait_seconds': 20}, cancel)
        timer.join()
        self.assertFalse(self.toolkit.execution_jobs[review['review_id']]['cancel'].is_set())
        release.set()
        result = self.toolkit.call('fractal_inspect_attempt', {'review_id': review['review_id'], 'wait_seconds': 2})
        self.assertTrue(result['completion_verified']); self.assertTrue(result['result']['result']['complete'])

    def test_explicit_job_stop_and_server_shutdown_retain_uncertain_attempt(self):
        for shutdown in (False, True):
            review, release = self.running_job()
            if shutdown:
                Server(self.toolkit, io.BytesIO()).serve(io.BytesIO(b''))
            else:
                result = self.toolkit.call('fractal_cancel_attempt', {'review_id': review['review_id']})
                self.assertTrue(result['stop_requested']); self.assertFalse(result['completion_verified'])
            result = self.toolkit.call('fractal_inspect_attempt', {'review_id': review['review_id'], 'wait_seconds': 2})
            self.assertEqual(result['status'], 'stopped-uncertain')
            with self.assertRaises(ValueError): self.toolkit.call('fractal_execute_review', self.execute_args(review))

    def test_failed_job_surfaces_recovery_head_without_truncated_file_listing(self):
        review = self.load_review(); self.enable(); self.toolkit.approve(review['review_id'], review['review_sha256'])
        def fail(helper, arguments, cancel, **kw):
            folder = Path(arguments[arguments.index('--destination') + 1]) / 'recovery'
            folder.mkdir(parents=True); (folder / 'state.private.json').write_text('{"synthetic":true}')
            raise Cancelled('Synthetic interruption with checkpoint.')
        self.runtime.run = fail
        self.toolkit.call('fractal_execute_review', self.execute_args(review))
        value = self.toolkit.call('fractal_inspect_attempt', {'review_id': review['review_id'], 'wait_seconds': 2})
        self.assertEqual(value['status'], 'stopped-uncertain')
        self.assertEqual(value['recovery_artifact'], value['execution_artifact'])
        self.assertEqual(value['state_sha256'], sha(self.toolkit.artifacts.file(value['execution_artifact'], 'recovery/state.private.json')))

    def test_job_owns_real_child_until_completion_without_holding_tool_request(self):
        review = self.load_review(); self.enable(); self.toolkit.approve(review['review_id'], review['review_sha256'])
        finish = self.folder / 'finish'
        Path(self.config['runtime_dir']).mkdir()
        runtime = Runtime(self.config)
        runtime.command = lambda helper, args: [sys.executable, '-c',
            'import pathlib,sys,time; p=pathlib.Path(sys.argv[1]);\nwhile not p.exists(): time.sleep(.02)\nprint(\'{"complete":true}\')', str(finish)]
        self.toolkit.runtime = runtime
        accepted = self.toolkit.call('fractal_execute_review', self.execute_args(review))
        self.assertEqual(accepted['status'], 'attempted'); self.assertFalse(accepted['completion_verified'])
        # A new short tool call works while the actual child is still blocked.
        self.assertEqual(self.toolkit.call('fractal_status', {})['release'], self.toolkit.version)
        finish.write_text('done')
        terminal = self.toolkit.call('fractal_inspect_attempt', {'review_id': review['review_id'], 'wait_seconds': 2})
        self.assertTrue(terminal['completion_verified'])
        self.assertTrue(terminal['result']['result']['complete'])

    def test_runtime_uses_argument_list_not_shell_commands(self):
        runtime_dir = self.folder / 'runtime'; (runtime_dir / 'cli').mkdir(parents=True)
        (runtime_dir / 'cli/fractal.dll').write_bytes(b'synthetic')
        command = Runtime(self.config).command('cli', ['inspect', '$(not-a-command);&'])
        self.assertEqual(command[-1], '$(not-a-command);&')

    def test_configuration_requires_explicit_ports_and_boolean_flags(self):
        for change in [{'allow_live_reads': 'true'}, {'allow_writes': True}, {'device': {**DEVICE, 'input': True}}, {'device': {**DEVICE, 'capacity': 1024}}]:
            config = {**self.config, **change}; path = self.folder / 'config.json'; atomic_json(path, config)
            with self.assertRaises(ValueError): load_config(path)

    def test_configuration_resolves_paths_against_config_file(self):
        path = self.folder / 'config.json'; atomic_json(path, {'schema_version': 1, 'data_root': 'data', 'runtime_dir': 'runtime'})
        config = load_config(path)
        self.assertFalse(config['allow_live_reads']); self.assertEqual(Path(config['data_root']), self.folder / 'data')

    def test_artifacts_can_be_found_after_reconnection(self):
        review = self.load_review()
        rows = self.toolkit.call('fractal_list_artifacts', {})['artifacts']
        self.assertTrue(any(row['artifact_id'] == review['review_id'] for row in rows))


class ProtocolChecks(unittest.TestCase):
    setUp = McpChecks.setUp
    tearDown = McpChecks.tearDown

    def start(self, version='2025-11-25'):
        self.output = io.BytesIO(); self.server = Server(self.toolkit, self.output)
        self.addCleanup(lambda: self.server.jobs.put(None))
        self.server.dispatch({'jsonrpc': '2.0', 'id': 1, 'method': 'initialize', 'params':
                              {'protocolVersion': version, 'capabilities': {}, 'clientInfo': {'name': 'synthetic', 'version': '1'}}})
        self.server.dispatch({'jsonrpc': '2.0', 'method': 'notifications/initialized'})

    def responses(self):
        self.server.jobs.join()
        return [loads(line) for line in self.output.getvalue().decode().splitlines()]

    def test_initialize_negotiates_three_supported_protocol_versions(self):
        for version in ('2025-11-25', '2025-06-18', '2025-03-26', 'unknown-future'):
            self.start(version)
            actual = self.responses()[0]['result']['protocolVersion']
            self.assertEqual(actual, version if version != 'unknown-future' else '2025-11-25')

    def test_calls_before_initialized_are_refused(self):
        self.output = io.BytesIO(); self.server = Server(self.toolkit, self.output); self.addCleanup(lambda: self.server.jobs.put(None))
        self.server.dispatch({'jsonrpc': '2.0', 'id': 0, 'method': 'tools/list'})
        self.assertEqual(self.responses()[0]['error']['code'], -32000)

    def test_discovery_resources_and_structured_tool_results(self):
        self.start()
        self.server.dispatch({'jsonrpc': '2.0', 'id': 2, 'method': 'tools/list'})
        self.server.dispatch({'jsonrpc': '2.0', 'id': 'resource', 'method': 'resources/read', 'params': {'uri': 'fractal://reference/knowledge-base/reference-map.md'}})
        self.server.dispatch({'jsonrpc': '2.0', 'id': 'status', 'method': 'tools/call', 'params': {'name': 'fractal_status', 'arguments': {}}})
        replies = self.responses()
        self.assertEqual(len(replies), 4)
        self.assertTrue(replies[1]['result']['tools'])
        self.assertIn('text', replies[2]['result']['contents'][0])
        self.assertFalse(replies[3]['result']['isError'])
        self.assertEqual(loads(replies[3]['result']['content'][0]['text']), replies[3]['result']['structuredContent'])

    def test_legacy_version_returns_compatible_text_result(self):
        self.start('2025-03-26')
        self.server.dispatch({'jsonrpc': '2.0', 'id': 2, 'method': 'tools/call', 'params': {'name': 'fractal_status'}})
        result = self.responses()[-1]['result']
        self.assertNotIn('structuredContent', result); self.assertFalse(result['isError'])

    def test_method_schema_and_execution_errors_are_distinguished(self):
        self.start()
        self.server.dispatch({'jsonrpc': '2.0', 'id': 2, 'method': 'missing'})
        self.server.dispatch({'jsonrpc': '2.0', 'id': 3, 'method': 'tools/call', 'params': {'name': 'fractal_status', 'arguments': {'shell': 'x'}}})
        self.server.dispatch({'jsonrpc': '2.0', 'id': 4, 'method': 'tools/call', 'params': {'name': 'fractal_inspect_device'}})
        replies = {r['id']: r for r in self.responses()}
        self.assertEqual(replies[2]['error']['code'], -32601); self.assertEqual(replies[3]['error']['code'], -32602)
        self.assertTrue(replies[4]['result']['isError']); self.assertEqual(self.runtime.calls, [])

    def test_invalid_utf8_json_batch_and_bounded_frames(self):
        self.output = io.BytesIO(); self.server = Server(self.toolkit, self.output)
        data = b'\xff\n{broken\n[]\n' + b'x' * (1024 * 1024 + 10) + b'\n' + b'{"jsonrpc":"2.0","id":9,"method":"ping"}\n'
        self.server.serve(io.BytesIO(data))
        replies = [loads(line) for line in self.output.getvalue().decode().splitlines()]
        self.assertEqual([r.get('error', {}).get('code') for r in replies[:-1]], [-32700, -32700, -32600, -32700])
        self.assertEqual(replies[-1]['result'], {})

    def test_cancellation_of_active_call_and_duplicate_id(self):
        self.start(); entered = threading.Event()
        def slow(name, arguments, cancel):
            entered.set(); cancel.wait(5)
            if cancel.is_set(): raise Cancelled('Synthetic cancellation observed.')
            return {}
        self.toolkit.call = slow
        call = {'jsonrpc': '2.0', 'id': 'job', 'method': 'tools/call', 'params': {'name': 'fractal_status'}}
        self.server.dispatch(call); self.assertTrue(entered.wait(2))
        self.server.dispatch(call)
        self.server.dispatch({'jsonrpc': '2.0', 'method': 'notifications/cancelled', 'params': {'requestId': 'job'}})
        replies = self.responses()
        self.assertEqual(replies[-2]['error']['code'], -32600)
        self.assertTrue(replies[-1]['result']['isError'])

    def test_client_shutdown_cancels_owned_call(self):
        self.start(); observed = threading.Event()
        def slow(name, arguments, cancel):
            cancel.wait(5)
            if cancel.is_set(): observed.set(); raise Cancelled('EOF')
            return {}
        self.toolkit.call = slow
        self.server.dispatch({'jsonrpc': '2.0', 'id': 2, 'method': 'tools/call', 'params': {'name': 'fractal_status'}})
        self.server.serve(io.BytesIO(b''))
        self.assertTrue(observed.is_set())


class ChildRuntimeChecks(unittest.TestCase):
    def test_cancellation_stops_synthetic_child_and_retains_log(self):
        with tempfile.TemporaryDirectory() as folder:
            runtime = Runtime({'runtime_dir': folder})
            runtime.command = lambda helper, args: [sys.executable, '-c', 'import time; time.sleep(20)']
            cancel = threading.Event(); timer = threading.Timer(.1, cancel.set); timer.start()
            log = Path(folder) / 'runtime.json'
            with self.assertRaises(Cancelled): runtime.run('cli', [], cancel, log=log)
            timer.join(); self.assertEqual(loads(log.read_text())['stopped'], 'cancelled')

    def test_progress_lines_do_not_contaminate_structured_result(self):
        with tempfile.TemporaryDirectory() as folder:
            runtime = Runtime({'runtime_dir': folder})
            runtime.command = lambda helper, args: [sys.executable, '-c', 'print("Progress 1/2"); print(\'{"complete":true}\')']
            self.assertEqual(runtime.run('cli', [], threading.Event()), {'complete': True})

    def test_excessive_child_output_is_not_accepted(self):
        with tempfile.TemporaryDirectory() as folder:
            runtime = Runtime({'runtime_dir': folder})
            runtime.command = lambda helper, args: [sys.executable, '-c', 'print("x" * 3000000); print(\'{"complete":true}\')']
            with self.assertRaises(Cancelled): runtime.run('cli', [], threading.Event())
