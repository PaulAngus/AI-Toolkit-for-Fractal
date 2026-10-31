import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('claude_desktop_entry', ROOT / 'adapters/claude_desktop/server.py')
entry = importlib.util.module_from_spec(spec)
spec.loader.exec_module(entry)


class DesktopProfileTests(unittest.TestCase):
    def test_status_reports_extension_version_separately_from_core_release(self):
        with tempfile.TemporaryDirectory() as temporary:
            config = entry.configuration(Path(temporary) / 'profile', ROOT)
            toolkit = entry.DesktopToolkit(ROOT, config)
            status = toolkit.call('fractal_status', {})
            manifest = json.loads((ROOT / 'adapters/claude_desktop/manifest.json').read_text(encoding='utf-8'))
            self.assertEqual(status['release'], manifest['version'])
            self.assertEqual(status['extension_version'], manifest['version'])
            self.assertEqual(status['toolkit_release'], toolkit.version)

    def test_startup_accepts_major_minor_and_persists_canonical_firmware(self):
        with tempfile.TemporaryDirectory() as temporary:
            args = self.host_args()
            args.profile = Path(temporary) / 'profile'
            args.firmware = '12.0'
            args.model = 'fm9'
            args.midi_input, args.midi_output = 12345, 12346
            toolkit = entry.startup(args, ROOT)
            self.assertIsNone(toolkit.setup_error)
            self.assertEqual(toolkit.config['device']['firmware'], '12.00')
            self.assertEqual(toolkit.config['device']['model'], 'FM9')
            self.assertTrue(toolkit.call('fractal_status', {})['setup']['ready'])

    def test_invalid_firmware_keeps_stdio_usable_without_stale_device_access(self):
        from unittest.mock import Mock
        with tempfile.TemporaryDirectory() as temporary:
            args = self.host_args()
            args.profile = Path(temporary) / 'profile'
            entry.configuration(args.profile, ROOT, self.settings())
            original = (args.profile / 'server.json').read_bytes()
            args.firmware = 'not a version'
            ports = Mock(side_effect=AssertionError('Invalid firmware must be caught before enumeration.'))
            toolkit = entry.startup(args, ROOT, ports)
            status = toolkit.call('fractal_status', {})
            self.assertFalse(status['setup']['ready'])
            self.assertIn('Firmware', status['setup']['error'])
            self.assertTrue(status['requested_access']['writes'])
            self.assertFalse(status['live_reads_enabled'])
            self.assertFalse(status['writes_enabled'])
            self.assertTrue(toolkit.call('fractal_search_knowledge', {'query': 'Scene MIDI'})['sources'])
            for name, arguments in [('fractal_inspect_device', {}), ('fractal_execute_review', {'review_id': 'a' * 32, 'review_sha256': '0' * 64})]:
                with self.assertRaisesRegex(ValueError, 'Correct Configure'):
                    toolkit.call(name, arguments)
            self.assertEqual((args.profile / 'server.json').read_bytes(), original)
            ports.assert_not_called()

    def test_missing_device_connection_keeps_knowledge_tools_available(self):
        with tempfile.TemporaryDirectory() as temporary:
            args = self.host_args()
            args.profile = Path(temporary) / 'profile'
            toolkit = entry.startup(args, ROOT, lambda: {'inputs': [], 'outputs': []})
            self.assertIn('connection found', toolkit.setup_error)
            self.assertFalse(toolkit.call('fractal_status', {})['setup']['ready'])
            self.assertTrue(toolkit.list_resources())
            self.assertFalse((args.profile / 'server.json').exists())

    def host_args(self, reads='true', writes='true'):
        from types import SimpleNamespace
        return SimpleNamespace(allow_live_reads=reads, allow_writes=writes, model='FM3',
                               capacity=512, firmware='99.07', connection='Automatic',
                               midi_input=None, midi_output=None)

    def test_host_setup_finds_named_connections_without_numeric_user_fields(self):
        from unittest.mock import Mock
        ports = Mock(return_value={'inputs': [{'index': 17, 'name': 'FM3 MIDI In'}],
                                   'outputs': [{'index': 24, 'name': 'FM3 MIDI Out'}]})
        settings = entry.host_settings(self.host_args(), ports)
        self.assertEqual(settings['device']['input'], 17)
        self.assertEqual(settings['device']['output'], 24)
        ports.assert_called_once_with()

    def test_offline_host_setup_needs_no_hardware_and_removes_stale_device(self):
        from unittest.mock import Mock
        ports = Mock(side_effect=AssertionError('Offline setup must not enumerate ports.'))
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            entry.configuration(base / 'profile', base / 'bundle', self.settings())
            settings = entry.host_settings(self.host_args('false', 'false'), ports)
            config = entry.configuration(base / 'profile', base / 'bundle', settings)
            self.assertNotIn('device', config)
            ports.assert_not_called()

    def test_first_launch_is_offline_and_keeps_private_data_outside_bundle(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            config = entry.configuration(base / 'private', base / 'bundle')
            self.assertFalse(config['allow_live_reads'])
            self.assertFalse(config['allow_writes'])
            self.assertEqual(Path(config['data_root']), base / 'private/artifacts')

    def test_reinstallation_preserves_artifacts_and_uses_own_runtime(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            entry.configuration(base / 'profile', base / 'old')
            retained = base / 'profile/retained.txt'
            retained.write_text('retained evidence')
            config = entry.configuration(base / 'profile', base / 'new')
            self.assertEqual(config['runtime_dir'], str(base / 'new/runtime'))
            self.assertEqual(retained.read_text(), 'retained evidence')

    def settings(self, model='FM9', capacity=512, reads=True, writes=True):
        # Synthetic settings only; no ports are opened by these checks.
        return {'allow_live_reads': reads, 'allow_writes': writes,
                'device': {'model': model, 'capacity': capacity, 'firmware': '99.07',
                           'input': 12345, 'output': 12346}}

    def test_all_model_settings_are_persisted_without_opening_ports(self):
        for model, capacity in [('FM9', 512), ('FM3', 512), ('Axe-Fx III', 512), ('Axe-Fx III', 1024)]:
            with self.subTest(model=model, capacity=capacity), tempfile.TemporaryDirectory() as temporary:
                base = Path(temporary)
                config = entry.configuration(base / 'profile', base / 'bundle', self.settings(model, capacity))
                status = entry.Toolkit(ROOT, config).call('fractal_status', {})
                self.assertTrue(status['live_reads_enabled'])
                self.assertTrue(status['writes_enabled'])
                self.assertFalse(status['ports_opened'])
                self.assertEqual(entry.load_config(base / 'profile/server.json'), config)

    def test_configure_can_disable_writes_or_all_live_access(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            for reads, writes in [(True, True), (True, False), (False, False)]:
                config = entry.configuration(base / 'profile', base / 'bundle', self.settings(reads=reads, writes=writes))
                self.assertEqual(config['allow_live_reads'], reads)
                self.assertEqual(config['allow_writes'], writes)

    def test_invalid_settings_do_not_replace_operator_configuration(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            entry.configuration(base / 'profile', base / 'bundle')
            path = base / 'profile/server.json'
            original = path.read_bytes()
            invalid = [self.settings(reads=False), self.settings('FM3', 1024), self.settings('other'),
                       self.settings(reads='false')]
            missing = self.settings(); del missing['device']['output']; invalid.append(missing)
            fractional = self.settings(); fractional['device']['input'] = 1.5; invalid.append(fractional)
            for settings in invalid:
                with self.assertRaises(ValueError):
                    entry.configuration(base / 'profile', base / 'bundle', settings)
                self.assertEqual(path.read_bytes(), original)

    def test_boolean_host_values_are_strict(self):
        self.assertTrue(entry.boolean('true'))
        self.assertFalse(entry.boolean('false'))
        for value in ['False', '0', '', True]:
            with self.assertRaises(ValueError): entry.boolean(value)

    def test_enabled_profile_still_requires_exact_operator_approval(self):
        from unittest.mock import Mock
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            config = entry.configuration(base / 'profile', base / 'bundle', self.settings())
            runtime = Mock()
            runtime.run.return_value = {'mode': 'offline-validation', 'ports_opened': False}
            toolkit = entry.Toolkit(ROOT, config, runtime)
            identifier, directory = toolkit.artifacts.create()
            (directory / 'context').mkdir()
            entry.atomic_json(directory / 'context/load-request.json', {
                'model': 'FM9', 'capacity': 512, 'firmware': '99.07', 'current_slot': 42,
                'target_slot': 43, 'discard_changes': False})
            review = toolkit.call('fractal_prepare_load', {'context_artifact': identifier})
            runtime.reset_mock()
            with self.assertRaises((ValueError, FileNotFoundError)):
                toolkit.call('fractal_execute_review', {'review_id': review['review_id'],
                                                      'review_sha256': review['review_sha256']})
            runtime.run.assert_not_called()
            self.assertEqual(list((toolkit.artifacts.root / 'approvals').iterdir()), [])

    def test_profile_in_package_refused_before_directory_creation(self):
        with tempfile.TemporaryDirectory() as temporary:
            bundle = Path(temporary) / 'bundle'
            with self.assertRaises(ValueError):
                entry.configuration(bundle / 'private', bundle)
            self.assertFalse(bundle.exists())

    def test_existing_artifact_directory_inside_package_refused(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            entry.configuration(base / 'profile', base / 'bundle')
            path = base / 'profile/server.json'
            config = json.loads(path.read_text())
            config['data_root'] = str(base / 'bundle/private')
            path.write_text(json.dumps(config))
            with self.assertRaises(ValueError):
                entry.configuration(base / 'profile', base / 'bundle')


if __name__ == '__main__':
    unittest.main()
