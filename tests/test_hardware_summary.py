import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("hardware_summary", ROOT / "scripts/summarize_hardware_validation.py")
summary = importlib.util.module_from_spec(spec)
spec.loader.exec_module(summary)


class HardwareSummaryTests(unittest.TestCase):
    def setUp(self):
        self.work = tempfile.TemporaryDirectory(prefix="fractal-summary-")
        self.folder = Path(self.work.name)
        self.snapshot = self.folder / "full-snapshot"
        self.snapshot.mkdir()
        self.data = {"presets": [{"slot": 0, "name": "Private preset secret", "content_sha256": "a" * 64,
                                    "body_sha256": "b" * 64, "populated": True, "amps": [],
                                    "scene_midi_present": False}], "decode_errors": []}
        self.report = {"device": {"definition": {"model": "FM3", "preset_capacity": 512}, "firmware": "10.00",
                                  "name": "Private device secret"}, "state_before": {"name": "Private preset secret"},
                       "state_after": {"name": "Private preset secret"}, "protocol_release": "0.2.1", "started_utc": "2026-10-08",
                       "completed_utc": "2026-10-08", "complete": True, "failure": None, "captured_presets": 1,
                       "results": [{"name": "identity", "passed": True}], "repeats": [{"slot": 0, "identical": True}],
                       "trace_sends": 1, "trace_receives": 1, "all_transmissions_read_allowlisted": True,
                       "elapsed_seconds": 1, "preset_read_ms_min": 1, "preset_read_ms_max": 1, "preset_read_ms_mean": 1,
                       "limitations": ["synthetic summary fixture only"]}
        (self.snapshot / "preset-0000.syx").write_bytes(b"synthetic-hash-fixture")
        (self.folder / "trace.jsonl").write_text('private raw trace placeholder')
        self.flush()

    def flush(self, filename="preset-0000.syx"):
        (self.folder / "report.private.json").write_text(json.dumps(self.report))
        path = self.snapshot / "snapshot.json"
        path.write_text(json.dumps(self.data))
        capture = self.snapshot / "preset-0000.syx"
        (self.snapshot / "manifest.json").write_text(json.dumps({"snapshot_sha256": summary.sha256(path), "captures": [
            {"slot": 0, "file": filename, "sysex_sha256": summary.sha256(capture), "raw_sha256": "a" * 64, "body_sha256": "b" * 64}]}))

    def tearDown(self):
        self.work.cleanup()

    def test_public_summary_omits_personal_names_and_raw_data(self):
        value = summary.summarize(self.folder, "full-snapshot")
        text = json.dumps(value)
        self.assertNotIn("Private preset secret", text)
        self.assertNotIn("Private device secret", text)
        self.assertNotIn("private raw trace placeholder", text)
        self.assertEqual("FM3", value["device"]["model"])

    def test_missing_capacity_cannot_be_reported_as_pass(self):
        self.assertFalse(summary.summarize(self.folder, "full-snapshot")["physical_read_validation_passed"])

    def test_changed_capture_hash_is_rejected(self):
        (self.snapshot / "preset-0000.syx").write_bytes(b"changed")
        with self.assertRaises(ValueError):
            summary.summarize(self.folder, "full-snapshot")

    def test_capture_path_cannot_escape_evidence(self):
        self.flush("../../outside.syx")
        with self.assertRaises(ValueError):
            summary.summarize(self.folder, "full-snapshot")
