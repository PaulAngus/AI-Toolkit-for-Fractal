import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import unittest


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "skills/fractal-fm9/scripts/plan_scene_midi.py"
spec = importlib.util.spec_from_file_location("plan_scene_midi", SCRIPT)
planner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(planner)


def snapshot():
    return {
        "schema_version": 1, "device": {"model": "FM9", "firmware": "12.00"},
        "presets": [{
            "slot": 0, "content_sha256": "a" * 64, "populated": True,
            "top_left_block": None, "scene_midi_present": False,
            "grid": [{"row": row, "column": column, "effect_id": 58 if (row, column) == (6, 14) else 0,
                      "route_flag": 0} for column in range(1, 15) for row in range(1, 7)],
            "scenes": [{"number": scene, "messages": []} for scene in range(1, 9)],
        }],
    }


def set_grid_effect(data, row, column, effect_id):
    cell = next(c for c in data["presets"][0]["grid"] if (c["row"], c["column"]) == (row, column))
    cell["effect_id"] = effect_id
    return cell


def mark_midi(data, row=2, column=2):
    set_grid_effect(data, row, column, 190)


class SceneMidiPlannerTests(unittest.TestCase):
    def plan(self, data, **kwargs):
        kwargs.setdefault("block_row", 4)
        kwargs.setdefault("block_column", 10)
        return planner.plan_scene_midi(data, channel=1, value=1, preset_slots=[0], **kwargs)

    def test_new_block_is_one_insertion_and_eight_scene_settings(self):
        data = snapshot()
        original = copy.deepcopy(data)
        result = self.plan(data)["presets"][0]
        self.assertEqual("planned", result["status"])
        self.assertEqual(1, sum(action["kind"] == "insert-block" for action in result["actions"]))
        self.assertEqual((190, 4, 10), tuple(result["actions"][0][k] for k in ("effect_id", "row", "column")))
        settings = [action for action in result["actions"] if action["kind"] == "set-scene-midi-message"]
        self.assertEqual(list(range(1, 9)), [action["scene"] for action in settings])
        self.assertEqual(original, data)

    def test_reapplying_desired_settings_has_no_actions(self):
        data = snapshot()
        preset = data["presets"][0]
        preset.update(scene_midi_present=True, top_left_block="Scene MIDI")
        mark_midi(data)
        for scene in preset["scenes"]:
            scene["messages"] = [{"slot": 1, "type": "PC", "channel": 1, "value": 1}]
        result = self.plan(data)["presets"][0]
        self.assertEqual("unchanged", result["status"])
        self.assertEqual([], result["actions"])

    def test_existing_message_conflict_suppresses_entire_preset(self):
        data = snapshot()
        preset = data["presets"][0]
        preset.update(scene_midi_present=True, top_left_block="Scene MIDI")
        mark_midi(data)
        preset["scenes"][3]["messages"] = [{"slot": 1, "type": "CC", "controller": 34, "channel": 1, "value": 2}]
        original = copy.deepcopy(data)
        result = self.plan(data)["presets"][0]
        self.assertEqual("conflict", result["status"])
        self.assertEqual([], result["actions"])
        self.assertEqual(4, result["conflicts"][0]["scene"])
        self.assertEqual(original, data)

    def test_unselected_scenes_and_other_message_slots_are_preserved(self):
        data = snapshot()
        preset = data["presets"][0]
        preset.update(scene_midi_present=True, top_left_block="Scene MIDI")
        mark_midi(data)
        preset["scenes"][0]["messages"] = [{"slot": 2, "type": "CC", "controller": 7, "channel": 2, "value": 64}]
        preset["scenes"][7]["messages"] = [{"slot": 1, "type": "PC", "channel": 2, "value": 12}]
        original = copy.deepcopy(data)
        result = self.plan(data, scenes=[2, 1])["presets"][0]
        self.assertEqual("planned", result["status"])
        self.assertEqual([1, 2], [action["scene"] for action in result["actions"]])
        self.assertEqual(original, data)

    def test_occupied_insertion_cell_is_a_conflict(self):
        data = snapshot()
        set_grid_effect(data, 4, 10, 58)
        result = self.plan(data)["presets"][0]
        self.assertEqual("conflict", result["status"])
        self.assertEqual("occupied-grid-cell", result["conflicts"][0]["kind"])
        self.assertEqual([], result["actions"])

    def test_existing_block_at_another_position_is_used_without_relocation(self):
        data = snapshot()
        data["presets"][0].update(scene_midi_present=True, top_left_block="Amp 1")
        mark_midi(data)
        result = self.plan(data)["presets"][0]
        self.assertEqual("planned", result["status"])
        self.assertTrue(all(a["kind"] == "set-scene-midi-message" for a in result["actions"]))

    def test_empty_preset_is_skipped_and_excluded_from_all_populated(self):
        data = snapshot()
        data["presets"][0]["populated"] = False
        self.assertEqual("skipped", self.plan(data)["presets"][0]["status"])
        result = planner.plan_scene_midi(data, channel=1, value=1, all_populated=True)
        self.assertEqual([], result["presets"])

    def test_snapshot_hash_is_canonical_but_content_sensitive(self):
        data = snapshot()
        reordered = dict(reversed(list(data.items())))
        digest = self.plan(data)["source_snapshot_sha256"]
        self.assertEqual(digest, self.plan(reordered)["source_snapshot_sha256"])
        data["presets"][0]["content_sha256"] = "b" * 64
        self.assertNotEqual(digest, self.plan(data)["source_snapshot_sha256"])

    def test_missing_duplicate_or_ambiguous_scope_is_rejected(self):
        data = snapshot()
        for args in [{}, {"preset_slots": [1]}, {"preset_slots": [0, 0]}, {"preset_slots": [0], "all_populated": True}]:
            with self.subTest(args=args), self.assertRaises(ValueError):
                planner.plan_scene_midi(data, channel=1, value=1, **args)

    def test_invalid_snapshot_fields_are_rejected(self):
        mutations = [
            lambda d: d["device"].update(model="FM3"),
            lambda d: d["device"].update(firmware=None),
            lambda d: d["presets"][0].update(slot=True),
            lambda d: d["presets"][0].update(content_sha256="invalid"),
            lambda d: d["presets"].append(copy.deepcopy(d["presets"][0])),
            lambda d: d["presets"][0]["scenes"].pop(),
            lambda d: d["presets"][0]["scenes"][0].update(number=2),
            lambda d: d["presets"][0].update(top_left_block="Scene MIDI"),
            lambda d: d["presets"][0]["scenes"][0].update(messages=[{"slot": 1, "type": "PC", "channel": 1, "value": 1}]),
        ]
        for mutate in mutations:
            with self.subTest(mutation=mutate), self.assertRaises(ValueError):
                data = snapshot()
                mutate(data)
                self.plan(data)

    def test_invalid_requested_settings_are_rejected(self):
        for args in [{"channel": 0}, {"channel": True}, {"value": 128}, {"scenes": []}, {"scenes": [1, 1]}, {"message_slot": 9}]:
            settings = {"channel": 1, "value": 1, "preset_slots": [0]}
            settings.update(args)
            with self.subTest(args=args), self.assertRaises(ValueError):
                planner.plan_scene_midi(snapshot(), **settings)

    def test_invalid_existing_messages_are_rejected(self):
        for message in [
            {"slot": 1, "type": "CC", "channel": 1, "value": 1},
            {"slot": 1, "type": "PC", "channel": 17, "value": 1},
            {"slot": 1, "type": "PC", "channel": 1, "value": False},
        ]:
            with self.subTest(message=message), self.assertRaises(ValueError):
                data = snapshot()
                data["presets"][0].update(scene_midi_present=True, top_left_block="Scene MIDI")
                mark_midi(data)
                data["presets"][0]["scenes"][0]["messages"] = [message]
                self.plan(data)

    def test_example_cli_produces_expected_independent_outcomes(self):
        result = subprocess.run([sys.executable, str(SCRIPT), "--snapshot", str(ROOT / "examples/fm9-snapshot.json"),
                                 "--all-populated", "--channel", "1", "--value", "1",
                                 "--block-row", "4", "--block-column", "10"], capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        plan = json.loads(result.stdout)
        self.assertEqual({"planned": 1, "unchanged": 1, "skipped": 0, "conflict": 1}, plan["summary"])
        self.assertEqual("offline-plan-only", plan["mode"])

    def test_cli_rejects_missing_file_without_traceback(self):
        result = subprocess.run([sys.executable, str(SCRIPT), "--snapshot", str(ROOT / "missing.json"),
                                 "--preset", "0", "--channel", "1", "--value", "1"], capture_output=True, text=True)
        self.assertEqual(2, result.returncode)
        self.assertIn("error:", result.stderr)
        self.assertNotIn("Traceback", result.stderr)

    def test_upper_left_occupancy_does_not_prevent_selected_cell_insertion(self):
        data = snapshot()
        data["presets"][0]["top_left_block"] = "Amp 1"
        set_grid_effect(data, 1, 1, 58)
        result = self.plan(data)["presets"][0]
        self.assertEqual("planned", result["status"])
        self.assertEqual((4, 10), (result["actions"][0]["row"], result["actions"][0]["column"]))

    def test_absent_block_requires_placement_and_complete_grid(self):
        data = snapshot()
        result = planner.plan_scene_midi(data, channel=1, value=1, preset_slots=[0])["presets"][0]
        self.assertEqual("missing-block-placement", result["conflicts"][0]["kind"])
        self.assertEqual([], result["actions"])
        data["presets"][0].pop("grid")
        result = self.plan(data)["presets"][0]
        self.assertEqual("unknown-grid", result["conflicts"][0]["kind"])
        self.assertEqual([], result["actions"])

    def test_existing_legacy_block_does_not_require_placement_or_grid_summary(self):
        data = snapshot()
        data["presets"][0].update(scene_midi_present=True)
        data["presets"][0].pop("grid")
        data["presets"][0].pop("top_left_block")
        result = planner.plan_scene_midi(data, channel=1, value=1, preset_slots=[0])["presets"][0]
        self.assertEqual("planned", result["status"])
        self.assertTrue(all(a["kind"] == "set-scene-midi-message" for a in result["actions"]))


if __name__ == "__main__":
    unittest.main()
