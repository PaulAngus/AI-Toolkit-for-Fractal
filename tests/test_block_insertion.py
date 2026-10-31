import copy
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from test_scene_midi_planner import ROOT, planner, snapshot, set_grid_effect
from test_multimodel_planner import common_snapshot

blocks = planner.blocks


class BlockInsertionTests(unittest.TestCase):
    def test_explicit_automatic_policy_preserves_routes_and_reuses_instances(self):
        for model, capacity in [('FM9', 512), ('FM3', 512), ('Axe-Fx III', 512), ('Axe-Fx III', 1024)]:
            data = common_snapshot(model, capacity)
            grid = {(c['row'], c['column']): c for c in data['presets'][0]['grid']}
            grid[1, 1]['effect_id'] = 1024  # Never replace a shunt.
            grid[1, 2]['route_flag'] = 2  # An outgoing edge from row 2 makes that empty cell unsuitable.
            before = copy.deepcopy(data)
            plan = blocks.plan_block_insertion(data, effect_id=190, all_populated=True, placement_policy='first-isolated-empty')
            action = plan['presets'][0]['actions'][0]
            self.assertEqual((3, 1), (action['row'], action['column']))
            self.assertEqual(before, data)
            # Cell order is not a placement preference.
            data['presets'][0]['grid'].reverse()
            self.assertEqual(action, blocks.plan_block_insertion(data, effect_id=190, all_populated=True, placement_policy='first-isolated-empty')['presets'][0]['actions'][0])
            grid[3, 1]['effect_id'] = 190
            repeat = blocks.plan_block_insertion(data, effect_id=190, all_populated=True, placement_policy='first-isolated-empty')
            self.assertEqual('unchanged', repeat['presets'][0]['status'])
            self.assertFalse(repeat['presets'][0]['actions'])

    def test_automatic_policy_conflicts_and_unknown_routing(self):
        data = common_snapshot('FM3')
        for cell in data['presets'][0]['grid']:
            cell.update(effect_id=1024, route_flag=0)
        cell = data['presets'][0]['grid'][0]; cell['effect_id'] = 0
        next(c for c in data['presets'][0]['grid'] if c['column'] == 2)['route_flag'] = 32768
        result = blocks.plan_block_insertion(data, effect_id=190, all_populated=True, placement_policy='first-isolated-empty')['presets'][0]
        self.assertEqual('no-isolated-empty-cell', result['conflicts'][0]['kind'])
        self.assertFalse(result['actions'])
        for extra in ({'row': 1, 'column': 1, 'placement_policy': 'first-isolated-empty'}, {'placement_policy': 'guess'}):
            with self.assertRaises(ValueError):
                blocks.plan_block_insertion(data, effect_id=190, all_populated=True, **extra)

    def test_scene_midi_uses_the_same_explicit_placement_policy(self):
        data = common_snapshot('FM3')
        result = planner.plan_scene_midi(data, channel=1, value=1, all_populated=True, placement_policy='first-isolated-empty')
        expected = blocks.plan_block_insertion(data, effect_id=190, all_populated=True, placement_policy='first-isolated-empty')
        self.assertEqual(expected['presets'][0]['actions'][0], result['presets'][0]['actions'][0])
    def plan(self, data, **kwargs):
        options = dict(effect_id=190, row=3, column=10, all_populated=True)
        options.update(kwargs)
        return blocks.plan_block_insertion(data, **options)

    def test_general_operation_needs_no_scene_midi_fields(self):
        data = common_snapshot("FM3")
        data["device"].pop("capabilities")
        for key in ("scenes", "scene_midi_present", "scene_midi_decode_status", "top_left_block"):
            data["presets"][0].pop(key)
        original = copy.deepcopy(data)
        result = self.plan(data, effect_id=59, row=4, column=1)
        self.assertEqual("grid-placement-only", result["validation_scope"])
        self.assertEqual("insert-block", result["presets"][0]["actions"][0]["kind"])
        self.assertEqual(59, result["presets"][0]["actions"][0]["effect_id"])
        self.assertTrue(result["executor_requirements"])
        self.assertEqual(original, data)

    def test_model_grid_bounds_and_capacity_are_independent(self):
        for model, capacity in [("FM9", 512), ("FM3", 512), ("Axe-Fx III", 512), ("Axe-Fx III", 1024)]:
            data = common_snapshot(model, capacity)
            rows, columns = blocks.GRID_DIMENSIONS[model]
            result = self.plan(data, row=rows, column=1)["presets"][0]
            self.assertEqual("planned", result["status"])
            self.assertEqual(capacity - 1, result["slot"])
            for options in ({"row": rows + 1}, {"column": columns + 1}):
                with self.assertRaises(ValueError):
                    self.plan(data, **options)

    def test_existing_exact_instance_anywhere_is_unchanged_without_relocation(self):
        data = snapshot()
        result = self.plan(data, effect_id=58, row=None, column=None)["presets"][0]
        self.assertEqual("unchanged", result["status"])
        self.assertEqual((6, 14), tuple(result["existing_cell"][k] for k in ("row", "column")))
        self.assertEqual([], result["actions"])
        # A selected cell is a fallback for insertion, not a relocation request.
        self.assertEqual("unchanged", self.plan(data, effect_id=58)["presets"][0]["status"])

    def test_rerun_after_planned_insertion_is_unchanged(self):
        data = snapshot()
        action = self.plan(data)["presets"][0]["actions"][0]
        set_grid_effect(data, action["row"], action["column"], action["effect_id"])
        self.assertEqual("unchanged", self.plan(data)["presets"][0]["status"])

    def test_occupied_cells_and_shunts_are_conflicts(self):
        for effect_id in (58, 65535):
            data = snapshot()
            set_grid_effect(data, 3, 10, effect_id)
            result = self.plan(data)["presets"][0]
            self.assertEqual("occupied-grid-cell", result["conflicts"][0]["kind"])
            self.assertEqual([], result["actions"])

    def test_duplicate_instance_and_unknown_grid_produce_no_actions(self):
        data = snapshot()
        set_grid_effect(data, 2, 2, 190)
        set_grid_effect(data, 3, 3, 190)
        self.assertEqual("ambiguous-block-instance", self.plan(data)["presets"][0]["conflicts"][0]["kind"])
        for incomplete in ([], data["presets"][0]["grid"][:2]):
            data["presets"][0]["grid"] = incomplete
            result = self.plan(data)["presets"][0]
            self.assertEqual("unknown-grid", result["conflicts"][0]["kind"])
            self.assertEqual([], result["actions"])

    def test_routing_and_extra_fields_are_preserved_without_aliasing(self):
        data = snapshot()
        cell = set_grid_effect(data, 3, 10, 0)
        cell.update(route_flag=12345, opaque={"value": 99})
        original = copy.deepcopy(data)
        plan = self.plan(data)
        action = plan["presets"][0]["actions"][0]
        self.assertEqual(cell, action["before_cell"])
        self.assertEqual(12345, action["preserve_route_flag"])
        action["before_cell"]["opaque"]["value"] = 0
        plan["device"]["firmware"] = "changed"
        self.assertEqual(original, data)

    def test_missing_placement_is_a_conflict_with_no_default_cell(self):
        result = self.plan(snapshot(), row=None, column=None)["presets"][0]
        self.assertEqual("missing-block-placement", result["conflicts"][0]["kind"])
        self.assertEqual([], result["actions"])

    def test_explicit_empty_preset_can_receive_a_block_and_all_populated_excludes_it(self):
        data = snapshot()
        data["presets"][0]["populated"] = False
        set_grid_effect(data, 6, 14, 0)
        self.assertEqual([], self.plan(data)["presets"])
        result = self.plan(data, all_populated=False, preset_slots=[0])["presets"][0]
        self.assertEqual("planned", result["status"])

    def test_invalid_grid_and_arguments_are_rejected(self):
        mutations = [lambda p: p["grid"].append(copy.deepcopy(p["grid"][0])),
                     lambda p: p["grid"][0].update(row=True),
                     lambda p: p["grid"][0].update(route_flag=-1),
                     lambda p: p.update(grid={})]
        for mutate in mutations:
            data = snapshot()
            mutate(data["presets"][0])
            with self.assertRaises(ValueError):
                self.plan(data)
        for args in ({"effect_id": 0}, {"effect_id": 1001}, {"effect_id": True}, {"row": None},
                     {"row": 0}, {"column": True}, {"preset_slots": [0]}):
            with self.assertRaises(ValueError):
                self.plan(snapshot(), **args)

    def test_repository_and_isolated_portable_skill_clis(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            shutil.copytree(ROOT / "skills/fractal-fm9", folder / "skill")
            file = folder / "snapshot.json"
            file.write_text(json.dumps(snapshot()), encoding="utf-8")
            common = ["--snapshot", str(file), "--preset", "0"]
            for script in (ROOT / "scripts/plan_block_insertion.py", folder / "skill/scripts/plan_block_insertion.py"):
                result = subprocess.run([sys.executable, str(script), *common, "--effect-id", "190", "--row", "3", "--column", "10"],
                                        cwd=folder, capture_output=True, text=True)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(1, json.loads(result.stdout)["summary"]["planned"])
                auto = subprocess.run([sys.executable, str(script), *common, '--effect-id', '190', '--placement-policy', 'first-isolated-empty'],
                                      cwd=folder, capture_output=True, text=True)
                self.assertEqual(0, auto.returncode, auto.stderr)
                self.assertEqual(1, json.loads(auto.stdout)['summary']['planned'])
            result = subprocess.run([sys.executable, str(folder / "skill/scripts/plan_scene_midi.py"), *common,
                                     "--channel", "1", "--value", "1", "--block-row", "3", "--block-column", "10"],
                                    cwd=folder, capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual("insert-block", json.loads(result.stdout)["presets"][0]["actions"][0]["kind"])


if __name__ == "__main__":
    unittest.main()
