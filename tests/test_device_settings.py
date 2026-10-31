"""Version formatting checks only; no device firmware is queried or guessed."""
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src'))
from fractal_mcp.device_settings import normalize_firmware, normalize_model


class FirmwareSettingTests(unittest.TestCase):
    def test_model_case_spacing_and_punctuation_are_normalized(self):
        for value, expected in [('fm9', 'FM9'), (' FM 3 ', 'FM3'), ('axe fx iii', 'Axe-Fx III'),
                                ('AXE-FX 3', 'Axe-Fx III'), ('AxeFxIII', 'Axe-Fx III')]:
            with self.subTest(value=value):
                self.assertEqual(normalize_model(value), expected)

    def test_unsupported_models_are_not_guessed(self):
        for value in ['FM30', 'Axe-Fx II', '', None, True]:
            with self.assertRaises(ValueError): normalize_model(value)

    def test_single_digit_minor_component_is_zero_padded(self):
        self.assertEqual(normalize_firmware('12.0'), '12.00')
        self.assertEqual(normalize_firmware('7.2'), '7.02')

    def test_canonical_version_is_unchanged(self):
        for value in ['12.00', '7.02', '1.10', '999.99']:
            self.assertEqual(normalize_firmware(value), value)

    def test_human_formatting_is_normalized(self):
        self.assertEqual(normalize_firmware('  v012.0  '), '12.00')
        self.assertEqual(normalize_firmware('V7.02'), '7.02')

    def test_missing_unsupported_and_ambiguous_versions_are_rejected(self):
        for value in ['', '12', '12.', '12.000', '12.00 beta', 'Automatic', '12.0.1', '١٢.٠', '-1.00']:
            with self.subTest(value=value), self.assertRaisesRegex(ValueError, 'Firmware'):
                normalize_firmware(value)

    def test_non_text_values_are_not_guessed(self):
        for value in [12, 12.0, True, None]:
            with self.assertRaises(ValueError): normalize_firmware(value)


if __name__ == '__main__':
    unittest.main()
