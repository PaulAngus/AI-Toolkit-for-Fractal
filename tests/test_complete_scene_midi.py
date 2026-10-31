import copy
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from test_multimodel_planner import common_snapshot
from test_scene_midi_planner import planner, ROOT, mark_midi


def complete_snapshot(model="FM9", capacity=512):
    data = common_snapshot(model, capacity)
    data["schema_version"] = 3
    preset = data["presets"][0]
    preset.update(scene_midi_present=True, scene_midi_decode_status="decoded", top_left_block="Amp 1")
    mark_midi(data)
    for scene in preset["scenes"]:
        scene["messages"] = [{"slot": slot, "type": "Off", "channel": None, "value": None, "controller": None,
                              "raw": {"type": 0, "channel": 65535, "controller": 2345, "value": 999}}
                             for slot in range(1, 9)]
    return data


def activate(message, kind="PC", channel=16, value=127, controller=None):
    message.update(type=kind, channel=channel, value=value, controller=controller)
    message["raw"].update(type=2 if kind == "PC" else 1, channel=channel - 1, value=value)
    if controller is not None:
        message["raw"]["controller"] = controller


class CompleteSceneMidiTests(unittest.TestCase):
    def plan(self, data, **settings):
        options = dict(channel=16, value=127, all_populated=True)
        options.update(settings)
        return planner.plan_scene_midi(data, **options)

    def test_all_model_definitions_accept_full_pc_tables_and_inactive_controller_values(self):
        for model, capacity in [("FM9", 512), ("FM3", 512), ("Axe-Fx III", 512), ("Axe-Fx III", 1024)]:
            data = complete_snapshot(model, capacity)
            for scene in data["presets"][0]["scenes"]:
                activate(scene["messages"][7])
            original = copy.deepcopy(data)
            result = self.plan(data, message_slot=8)["presets"][0]
            self.assertEqual("unchanged", result["status"])
            self.assertEqual([], result["actions"])
            self.assertEqual(original, data)

    def test_off_slots_are_available_and_before_values_are_retained_without_aliasing(self):
        data = complete_snapshot()
        original = copy.deepcopy(data)
        result = self.plan(data, scenes=[8, 1], message_slot=8)["presets"][0]
        self.assertEqual("planned", result["status"])
        self.assertEqual([1, 8], [a["scene"] for a in result["actions"]])
        self.assertEqual(original["presets"][0]["scenes"][0]["messages"][7], result["actions"][0]["before_message"])
        result["actions"][0]["before_message"]["raw"]["value"] = 0
        self.assertEqual(original, data)

    def test_cc_match_compares_controller_and_conflicts_suppress_all_actions(self):
        data = complete_snapshot()
        for scene in data["presets"][0]["scenes"]:
            activate(scene["messages"][0], kind="CC", controller=127)
        self.assertEqual("unchanged", self.plan(data, message_type="CC", controller=127)["presets"][0]["status"])
        result = self.plan(data, message_type="CC", controller=0)["presets"][0]
        self.assertEqual("conflict", result["status"])
        self.assertEqual(8, len(result["conflicts"]))
        self.assertEqual([], result["actions"])
        result["conflicts"][0]["existing"]["raw"]["controller"] = 0
        self.assertEqual(127, data["presets"][0]["scenes"][0]["messages"][0]["raw"]["controller"])

    def test_cc_can_be_planned_into_off_slot_while_other_rows_and_scenes_are_preserved(self):
        data = complete_snapshot("FM3")
        activate(data["presets"][0]["scenes"][0]["messages"][0], channel=2, value=12)
        original = copy.deepcopy(data)
        result = self.plan(data, message_type="CC", controller=0, value=0, message_slot=2, scenes=[1])["presets"][0]
        self.assertEqual("planned", result["status"])
        self.assertEqual(1, len(result["actions"]))
        self.assertEqual({"type": "CC", "channel": 16, "controller": 0, "value": 0}, result["actions"][0]["message"])
        self.assertEqual(original, data)

    def test_incomplete_raw_or_semantic_contradictions_are_rejected(self):
        mutations = [
            lambda m: m.pop(),
            lambda m: m[0].pop("raw"),
            lambda m: m[0]["raw"].update(type=3),
            lambda m: m[0]["raw"].update(channel=16),
            lambda m: m[0]["raw"].update(value=0),
            lambda m: m[1].update(channel=1),
            lambda m: m[1]["raw"].update(value=True),
        ]
        for mutate in mutations:
            data = complete_snapshot()
            activate(data["presets"][0]["scenes"][0]["messages"][0])
            mutate(data["presets"][0]["scenes"][0]["messages"])
            with self.assertRaises(ValueError):
                self.plan(data)
        data = complete_snapshot()
        activate(data["presets"][0]["scenes"][0]["messages"][0], kind="CC", controller=7)
        data["presets"][0]["scenes"][0]["messages"][0]["raw"]["controller"] = 8
        with self.assertRaises(ValueError):
            self.plan(data)

    def test_invalid_request_types_and_controller_scope_are_rejected(self):
        for settings in [{"message_type": "Off"}, {"controller": 1}, {"message_type": "CC"},
                         {"message_type": "CC", "controller": -1}, {"message_type": "CC", "controller": True}]:
            with self.subTest(settings=settings), self.assertRaises(ValueError):
                self.plan(complete_snapshot(), **settings)

    def test_common_cli_cc_command_and_missing_controller_error(self):
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / "snapshot.json"
            file.write_text(json.dumps(complete_snapshot("Axe-Fx III", 1024)), encoding="utf-8")
            args = [sys.executable, str(ROOT / "scripts/plan_scene_midi.py"), "--snapshot", str(file),
                    "--all-populated", "--type", "CC", "--channel", "16", "--value", "0"]
            good = subprocess.run(args + ["--controller", "127", "--scenes", "8", "--message-slot", "8"], capture_output=True, text=True)
            self.assertEqual(0, good.returncode, good.stderr)
            self.assertEqual({"planned": 1, "unchanged": 0, "skipped": 0, "conflict": 0}, json.loads(good.stdout)["summary"])
            bad = subprocess.run(args, capture_output=True, text=True)
            self.assertEqual(2, bad.returncode)
            self.assertIn("CC controller", bad.stderr)
            self.assertNotIn("Traceback", bad.stderr)


if __name__ == "__main__":
    unittest.main()
