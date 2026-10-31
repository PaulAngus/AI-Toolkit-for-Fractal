#!/usr/bin/env python3
"""Plan removal of one isolated, exact block instance per preset; never opens MIDI."""
import argparse
import importlib.util
import json
import sys
from pathlib import Path

spec = importlib.util.spec_from_file_location('contained_grid_planner', Path(__file__).with_name('plan_block_insertion.py'))
blocks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(blocks)


def plan_block_removal(snapshot, *, effect_id, preset_slots=None, all_populated=False):
    blocks.validate_context(snapshot)
    blocks.integer(effect_id, 1, 1000, 'block effect ID')
    selected = blocks.select_slots(snapshot, preset_slots, all_populated)
    presets = {p['slot']: p for p in snapshot['presets']}
    results = []
    rows, columns = blocks.GRID_DIMENSIONS[snapshot['device']['model']]
    for slot in selected:
        preset = presets[slot]
        result = blocks.result_for(preset)
        result['reason'] = 'block instance already absent'
        cells = blocks.grid_cells(preset, snapshot['device'])
        found = [c for c in cells.values() if c['effect_id'] == effect_id]
        if len(cells) != rows * columns:
            result['conflicts'].append({'kind': 'unknown-grid'})
        elif len(found) > 1:
            result['conflicts'].append({'kind': 'ambiguous-block-instance', 'effect_id': effect_id})
        elif found:
            cell = found[0]
            if not blocks.isolated(cells, snapshot['device'], cell['row'], cell['column']):
                result['conflicts'].append({'kind': 'connected-or-uncertain-routing', 'effect_id': effect_id})
            else:
                result.update(status='planned', reason='remove exact isolated block instance')
                result['actions'] = [{'kind': 'remove-block', 'effect_id': effect_id, 'row': cell['row'], 'column': cell['column'], 'before_cell': dict(cell)}]
        if result['conflicts']:
            result.update(status='conflict', reason='removal requires resolution')
        results.append(result)
    plan = blocks.plan_envelope(snapshot, results)
    plan.update(operation='remove-block-instance', effect_id=effect_id, validation_scope='isolated-grid-removal-only',
                executor_requirements=['Recheck exact saved baseline and preserve unsaved work',
                                       'Identify one complete instance record and preserve all other bytes',
                                       'Verify loaded contents before Save, then repeated saved reads and Reload'])
    return plan


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--snapshot', required=True, type=Path)
    scope = parser.add_mutually_exclusive_group(required=True)
    scope.add_argument('--preset', type=int, action='append', dest='preset_slots')
    scope.add_argument('--all-populated', action='store_true')
    parser.add_argument('--effect-id', required=True, type=int)
    args = parser.parse_args()
    try:
        print(json.dumps(plan_block_removal(json.loads(args.snapshot.read_text(encoding='utf-8')), effect_id=args.effect_id,
                                           preset_slots=args.preset_slots, all_populated=args.all_populated), indent=2))
        return 0
    except (ValueError, OSError) as error:
        print(f'error: {error}', file=sys.stderr)
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
