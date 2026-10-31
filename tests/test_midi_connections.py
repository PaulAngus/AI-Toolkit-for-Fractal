"""Synthetic port rosters only; name resolution never sends MIDI."""
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src'))
from fractal_mcp.connections import resolve_connection


def roster(name):
    return {'inputs': [{'index': 5, 'name': name + ' In'}],
            'outputs': [{'index': 2, 'name': 'Unrelated synthesizer'}, {'index': 8, 'name': name + ' Out'}]}


class ConnectionTests(unittest.TestCase):
    def test_lowercase_model_resolves_the_named_usb_device(self):
        self.assertEqual(resolve_connection(roster('FM9 MIDI'), 'fm9'), {'input': 5, 'output': 8})

    def test_usb_names_resolve_for_all_models_without_assuming_port_order(self):
        for model, name in [('FM9', 'FM9 MIDI'), ('FM3', 'Fractal FM3 MIDI'), ('Axe-Fx III', 'Axe-Fx III MIDI'), ('Axe-Fx III', 'Axe Fx 3 MIDI')]:
            with self.subTest(model=model, name=name):
                self.assertEqual(resolve_connection(roster(name), model), {'input': 5, 'output': 8})

    def test_separate_interface_can_be_selected_by_name(self):
        self.assertEqual(resolve_connection(roster('USB MIDI Interface'), 'FM3', 'usb midi interface'), {'input': 5, 'output': 8})

    def test_missing_or_wrong_model_connections_are_refused(self):
        for model in ['FM3', 'Axe-Fx III', 'unknown']:
            with self.assertRaises(ValueError): resolve_connection(roster('FM9 MIDI'), model)
        with self.assertRaises(ValueError): resolve_connection({'inputs': [], 'outputs': []}, 'FM9')

    def test_multiple_devices_need_an_explicit_name(self):
        ports = roster('FM9 First MIDI')
        ports['inputs'].append({'index': 6, 'name': 'FM9 Second MIDI In'})
        ports['outputs'].append({'index': 9, 'name': 'FM9 Second MIDI Out'})
        with self.assertRaisesRegex(ValueError, 'More than one'): resolve_connection(ports, 'FM9')
        self.assertEqual(resolve_connection(ports, 'FM9', 'FM9 Second MIDI'), {'input': 6, 'output': 9})

    def test_duplicate_names_are_not_silently_resolved(self):
        ports = roster('FM3 MIDI')
        ports['inputs'].append({'index': 6, 'name': 'FM3 MIDI In'})
        with self.assertRaises(ValueError): resolve_connection(ports, 'FM3', 'FM3 MIDI')

    def test_automatic_pair_cannot_combine_different_devices(self):
        ports = roster('FM9 First MIDI')
        ports['outputs'][-1]['name'] = 'FM9 Second MIDI Out'
        with self.assertRaisesRegex(ValueError, 'same device'): resolve_connection(ports, 'FM9')

    def test_malformed_rosters_and_blank_selection_are_refused(self):
        for ports in [{}, {'inputs': 'ports'}, {'inputs': [{'index': True, 'name': 'FM9 MIDI'}], 'outputs': []}]:
            with self.assertRaises(ValueError): resolve_connection(ports, 'FM9')
        with self.assertRaises(ValueError): resolve_connection(roster('FM9 MIDI'), 'FM9', '')


if __name__ == '__main__':
    unittest.main()
