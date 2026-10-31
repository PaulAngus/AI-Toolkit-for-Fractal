import copy
import importlib.util
import unittest
from test_multimodel_planner import common_snapshot
from test_scene_midi_planner import ROOT

spec = importlib.util.spec_from_file_location('removal', ROOT / 'skills/fractal-fm9/scripts/plan_block_removal.py')
removal = importlib.util.module_from_spec(spec)
spec.loader.exec_module(removal)


class RemovalTests(unittest.TestCase):
    def test_models_exact_instance_repeat_and_preservation(self):
        for model, capacity in [('FM9',512), ('FM3',512), ('Axe-Fx III',512), ('Axe-Fx III',1024)]:
            data = common_snapshot(model,capacity)
            preset = data['presets'][0]
            cell = next(c for c in preset['grid'] if c['column']==1 and c['effect_id']==0)
            cell['effect_id']=114
            before=copy.deepcopy(data)
            plan=removal.plan_block_removal(data,effect_id=114,preset_slots=[preset['slot']])
            self.assertEqual('planned',plan['presets'][0]['status'])
            self.assertEqual('remove-block',plan['presets'][0]['actions'][0]['kind'])
            self.assertEqual(before,data)
            cell['effect_id']=0
            repeat=removal.plan_block_removal(data,effect_id=114,preset_slots=[preset['slot']])
            self.assertEqual('unchanged',repeat['presets'][0]['status'])
            self.assertFalse(repeat['presets'][0]['actions'])

    def test_connected_ambiguous_and_unknown_grid(self):
        for kind in ('connected','outgoing','duplicate','missing'):
            data=common_snapshot('FM9'); preset=data['presets'][0]
            cell=next(c for c in preset['grid'] if c['column']==2 and c['effect_id']==0)
            cell['effect_id']=114
            if kind=='connected': cell['route_flag']=1
            if kind=='outgoing': next(c for c in preset['grid'] if c['column']==3)['route_flag']=1 << (cell['row'] - 1)
            if kind=='duplicate': next(c for c in preset['grid'] if c['column']==3)['effect_id']=114
            if kind=='missing': preset['grid'].pop()
            plan=removal.plan_block_removal(data,effect_id=114,all_populated=True)
            self.assertEqual('conflict',plan['presets'][0]['status'])

    def test_unrelated_routes_do_not_disable_isolated_removal(self):
        data = common_snapshot('FM9'); preset = data['presets'][0]
        grid = {(c['row'], c['column']): c for c in preset['grid']}
        grid[1, 3]['effect_id'] = 114
        grid[2, 2]['route_flag'] = 2
        grid[2, 4]['route_flag'] = 2
        original = copy.deepcopy(data)
        result = removal.plan_block_removal(data, effect_id=114, all_populated=True)
        self.assertEqual('planned', result['presets'][0]['status'])
        self.assertEqual(original, data)

    def test_invalid_identity_and_scope(self):
        for value in (True,0,1001):
            with self.assertRaises(ValueError): removal.plan_block_removal(common_snapshot('FM3'),effect_id=value,all_populated=True)
