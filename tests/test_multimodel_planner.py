import copy
import unittest

from test_scene_midi_planner import planner, snapshot, mark_midi


def common_snapshot(model, capacity=512):
    data = snapshot()
    data["schema_version"] = 2
    data["device"] = {"model": model, "firmware": "99.07", "preset_capacity": capacity,
                      "capabilities": {"scene_midi": True, "scene_count": 8, "message_slots": 8}}
    preset = data["presets"][0]
    rows, columns = (4, 12) if model == "FM3" else (6, 14)
    preset.update(slot=capacity - 1, scene_midi_decode_status="absent", body_header_words=[0, 6],
                  block_records=[{"parameter_words": [321, 654]}],
                  grid=[{"row": r, "column": c, "effect_id": 58 if (r, c) == (rows, columns) else 0,
                         "route_flag": 0} for c in range(1, columns + 1) for r in range(1, rows + 1)])
    return data


class CommonPlannerTests(unittest.TestCase):
    def plan(self, data):
        return planner.plan_scene_midi(data, channel=16, value=127, all_populated=True, block_row=3, block_column=10)

    def test_every_model_and_capacity_preserves_context_and_unrelated_content(self):
        for model, capacity in [("FM9", 512), ("FM3", 512), ("Axe-Fx III", 512), ("Axe-Fx III", 1024)]:
            with self.subTest(model=model, capacity=capacity):
                data = common_snapshot(model, capacity)
                original = copy.deepcopy(data)
                result = self.plan(data)
                self.assertEqual("planned", result["presets"][0]["status"])
                self.assertEqual(capacity - 1, result["presets"][0]["slot"])
                self.assertEqual(data["device"], result["device"])
                self.assertEqual(9, len(result["presets"][0]["actions"]))
                self.assertEqual(original, data)
                result["device"]["capabilities"]["scene_midi"] = False
                self.assertEqual(original, data)

    def test_unread_existing_table_is_a_conflict_without_writes(self):
        for model in ("FM9", "FM3", "Axe-Fx III"):
            data = common_snapshot(model)
            data["presets"][0].update(scene_midi_present=True, top_left_block="Scene MIDI",
                                       scene_midi_decode_status="unsupported-saved-table-layout")
            mark_midi(data)
            result = self.plan(data)["presets"][0]
            self.assertEqual("conflict", result["status"])
            self.assertEqual([], result["actions"])
            self.assertEqual("unread-scene-midi-table", result["conflicts"][0]["kind"])

    def test_capacity_capability_and_decode_contract_reject_invalid_context(self):
        mutations = [
            lambda d: d["device"].pop("preset_capacity"),
            lambda d: d["device"].update(preset_capacity=1024),
            lambda d: d["device"].update(preset_capacity=True),
            lambda d: d["device"]["capabilities"].update(scene_midi=False),
            lambda d: d["device"]["capabilities"].update(scene_count=True),
            lambda d: d["presets"][0].pop("scene_midi_decode_status"),
            lambda d: d["presets"][0].update(scene_midi_decode_status="decoded"),
            lambda d: d["presets"][0].update(slot=512),
        ]
        for mutate in mutations:
            data = common_snapshot("FM3")
            mutate(data)
            with self.subTest(mutation=mutate), self.assertRaises(ValueError):
                self.plan(data)

    def test_axe_high_slot_selection_and_boundary(self):
        data = common_snapshot("Axe-Fx III", 1024)
        result = planner.plan_scene_midi(data, channel=1, value=1, preset_slots=[1023], block_row=6, block_column=1)
        self.assertEqual("planned", result["presets"][0]["status"])
        with self.assertRaises(ValueError):
            planner.plan_scene_midi(data, channel=1, value=1, preset_slots=[1024])

    def test_normalized_decoded_tables_work_for_all_models_and_firmware(self):
        for model in ("FM9", "FM3", "Axe-Fx III"):
            data = common_snapshot(model)
            data["presets"][0].update(scene_midi_present=True, top_left_block="Scene MIDI", scene_midi_decode_status="decoded")
            mark_midi(data)
            for scene in data["presets"][0]["scenes"]:
                scene["messages"] = [{"slot": 1, "type": "PC", "channel": 16, "value": 127}]
            self.assertEqual("unchanged", self.plan(data)["presets"][0]["status"])


if __name__ == "__main__":
    unittest.main()
