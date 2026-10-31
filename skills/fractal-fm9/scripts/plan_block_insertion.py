#!/usr/bin/env python3
"""Plan placement of a caller-resolved block instance; never opens MIDI."""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any

# Separate model definitions, matching the contained DeviceDefinition.cs.
GRID_DIMENSIONS = {"FM9": (6, 14), "FM3": (4, 12), "Axe-Fx III": (6, 14)}
VALIDATION_SCOPE = "grid-placement-only"
EXECUTOR_REQUIREMENTS = [
    "Verify effect instance availability for the actual model/firmware and DSP resources",
    "Establish new block defaults, channels and scene settings",
    "Validate target-specific insertion, persistence and restore operations",
    "Recheck saved base hash and live unsaved state before applying",
]


def integer(value: Any, low: int, high: int, label: str) -> int:
    if type(value) is not int or not low <= value <= high:
        raise ValueError(f"{label} must be an integer from {low} to {high}")
    return value


def object_value(value: Any, label: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be an object")
    return value


def grid_cells(preset: dict[str, Any], device: dict[str, Any]) -> dict[tuple[int, int], dict[str, Any]]:
    rows, columns = GRID_DIMENSIONS[device["model"]]
    grid = preset.get("grid", [])
    if not isinstance(grid, list):
        raise ValueError("preset grid must be an array")
    cells = {}
    for cell in grid:
        cell = object_value(cell, "grid cell")
        row = integer(cell.get("row"), 1, rows, "grid row")
        column = integer(cell.get("column"), 1, columns, "grid column")
        integer(cell.get("effect_id"), 0, 65535, "grid effect ID")
        integer(cell.get("route_flag"), 0, 65535, "grid route flag")
        if (row, column) in cells:
            raise ValueError(f"duplicate grid cell ({row}, {column})")
        cells[row, column] = cell
    return cells


def validate_context(snapshot: Any) -> dict[str, Any]:
    snapshot = object_value(snapshot, "snapshot")
    if snapshot.get("decode_errors"):
        raise ValueError("snapshot contains unsupported preset bodies; resolve decode_errors before planning")
    if type(snapshot.get("schema_version")) is not int or snapshot["schema_version"] not in (1, 2, 3):
        raise ValueError("snapshot schema_version must be 1, 2 or 3")
    device = object_value(snapshot.get("device"), "device")
    if device.get("model") not in GRID_DIMENSIONS:
        raise ValueError("device model must be FM9, FM3 or Axe-Fx III")
    if not isinstance(device.get("firmware"), str) or not device["firmware"].strip():
        raise ValueError("device firmware must be a recorded nonempty string")
    legacy = snapshot["schema_version"] == 1 and device["model"] == "FM9"
    capacity = integer(device.get("preset_capacity", 512 if legacy else None), 1, 16384, "preset capacity")
    if capacity not in ((512, 1024) if device["model"] == "Axe-Fx III" else (512,)):
        raise ValueError("preset capacity does not match the device definition")
    if not isinstance(snapshot.get("presets"), list):
        raise ValueError("presets must be an array")
    slots = set()
    for preset in snapshot["presets"]:
        preset = object_value(preset, "preset")
        slot = integer(preset.get("slot"), 0, capacity - 1, "preset slot")
        if slot in slots:
            raise ValueError(f"duplicate preset slot {slot}")
        slots.add(slot)
        digest = preset.get("content_sha256")
        if not isinstance(digest, str) or re.fullmatch(r"[0-9a-fA-F]{64}", digest) is None:
            raise ValueError(f"preset {slot}: content_sha256 must contain 64 hexadecimal characters")
        if type(preset.get("populated")) is not bool:
            raise ValueError(f"preset {slot}: populated must be a boolean")
        grid_cells(preset, device)
    return snapshot


def select_slots(snapshot: dict[str, Any], preset_slots: list[int] | None, all_populated: bool) -> list[int]:
    if type(all_populated) is not bool:
        raise ValueError("all_populated must be a boolean")
    if all_populated == (preset_slots is not None):
        raise ValueError("choose preset_slots or all_populated, exclusively")
    by_slot = {p["slot"]: p for p in snapshot["presets"]}
    if all_populated:
        return sorted(slot for slot, preset in by_slot.items() if preset["populated"])
    if not isinstance(preset_slots, list) or not preset_slots:
        raise ValueError("choose at least one preset slot")
    for slot in preset_slots:
        integer(slot, 0, snapshot["device"].get("preset_capacity", 512) - 1, "requested preset slot")
    if len(set(preset_slots)) != len(preset_slots):
        raise ValueError("requested preset slots must be unique")
    missing = sorted(set(preset_slots) - by_slot.keys())
    if missing:
        raise ValueError(f"requested preset slots absent from snapshot: {missing}")
    return sorted(preset_slots)


def validate_placement(device: dict[str, Any], row: int | None, column: int | None, placement_policy: str | None = None) -> None:
    if placement_policy not in (None, 'first-isolated-empty'):
        raise ValueError('Unknown placement policy')
    if placement_policy is not None and (row is not None or column is not None):
        raise ValueError('Choose explicit coordinates or a placement policy, not both')
    if (row is None) != (column is None):
        raise ValueError("block row and column must be supplied together")
    if row is not None:
        rows, columns = GRID_DIMENSIONS[device["model"]]
        integer(row, 1, rows, "block row")
        integer(column, 1, columns, "block column")


def result_for(preset: dict[str, Any]) -> dict[str, Any]:
    return {"slot": preset["slot"], "base_content_sha256": preset["content_sha256"].lower(),
            "status": "unchanged", "reason": "block instance already present",
            "actions": [], "conflicts": []}


def isolated(cells: dict, device: dict, row: int, column: int) -> bool:
    """A destination mask selects source rows in the previous column; preserve other routes."""
    rows, columns = GRID_DIMENSIONS[device['model']]
    if len(cells) != rows * columns or cells[row, column]['route_flag'] != 0:
        return False
    allowed, source = (1 << rows) - 1, 1 << (row - 1)
    return not any(cell['column'] == column + 1 and (cell['route_flag'] & ~allowed or cell['route_flag'] & source)
                   for cell in cells.values())


def insertion_for(preset: dict[str, Any], device: dict[str, Any], effect_id: int,
                  row: int | None, column: int | None, placement_policy: str | None = None) -> dict[str, Any]:
    """Ensure one exact instance exists; never relocate, replace or wire a block."""
    result = result_for(preset)
    cells = grid_cells(preset, device)
    rows, columns = GRID_DIMENSIONS[device["model"]]
    if len(cells) != rows * columns:
        result["conflicts"].append({"kind": "unknown-grid", "required_cells": rows * columns,
                                    "observed_cells": len(cells)})
    else:
        existing = [cell for cell in cells.values() if cell["effect_id"] == effect_id]
        if len(existing) > 1:
            result["conflicts"].append({"kind": "ambiguous-block-instance", "effect_id": effect_id})
        elif existing:
            result["existing_cell"] = copy.deepcopy(existing[0])
        else:
            if placement_policy == 'first-isolated-empty':
                candidates = sorted((c for c in cells.values() if c['effect_id'] == 0 and isolated(cells, device, c['row'], c['column'])),
                                    key=lambda c: (c['column'], c['row']))
                if candidates:
                    row, column = candidates[0]['row'], candidates[0]['column']
                    result['placement_policy'] = placement_policy
                else:
                    result['conflicts'].append({'kind': 'no-isolated-empty-cell', 'effect_id': effect_id})
            if row is None and not result['conflicts']:
                result["conflicts"].append({"kind": "missing-block-placement", "effect_id": effect_id})
            cell = cells.get((row, column))
            if result['conflicts']:
                pass
            elif cell["effect_id"] != 0:
                result["conflicts"].append({"kind": "occupied-grid-cell", "row": row, "column": column,
                                            "existing": copy.deepcopy(cell)})
            else:
                result.update(status="planned", reason="requested block instance is absent")
                result["actions"].append({"kind": "insert-block", "effect_id": effect_id,
                                          "row": row, "column": column, "before_cell": copy.deepcopy(cell),
                                          "preserve_route_flag": cell["route_flag"]})
    if result["conflicts"]:
        result.update(status="conflict", reason="block placement requires resolution")
    return result


def plan_envelope(snapshot: dict[str, Any], results: list[dict[str, Any]], schema_version: int = 1) -> dict[str, Any]:
    canonical = json.dumps(snapshot, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    return {"schema_version": schema_version, "mode": "offline-plan-only",
            "device": copy.deepcopy(snapshot["device"]), "source_schema_version": snapshot["schema_version"],
            "source_snapshot_sha256": hashlib.sha256(canonical).hexdigest(),
            "summary": {status: sum(item["status"] == status for item in results)
                        for status in ("planned", "unchanged", "skipped", "conflict")}, "presets": results}


def plan_block_insertion(snapshot: dict[str, Any], *, effect_id: int, row: int | None = None,
                         column: int | None = None, preset_slots: list[int] | None = None,
                         all_populated: bool = False, placement_policy: str | None = None) -> dict[str, Any]:
    validate_context(snapshot)
    # This is a block-instance range, not a model-specific availability catalogue.
    integer(effect_id, 1, 1000, "block effect ID")
    validate_placement(snapshot["device"], row, column, placement_policy)
    selected = select_slots(snapshot, preset_slots, all_populated)
    by_slot = {p["slot"]: p for p in snapshot["presets"]}
    results = [insertion_for(by_slot[slot], snapshot["device"], effect_id, row, column, placement_policy) for slot in selected]
    plan = plan_envelope(snapshot, results)
    plan.update(operation="ensure-block-instance", effect_id=effect_id,
                requested_placement={"policy": placement_policy} if placement_policy else None if row is None else {"row": row, "column": column},
                validation_scope=VALIDATION_SCOPE, executor_requirements=list(EXECUTOR_REQUIREMENTS))
    return plan


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--snapshot", required=True, type=Path)
    scope = parser.add_mutually_exclusive_group(required=True)
    scope.add_argument("--preset", type=int, action="append", dest="preset_slots")
    scope.add_argument("--all-populated", action="store_true")
    parser.add_argument("--effect-id", required=True, type=int, help="Exact block instance ID from a qualified model catalogue")
    parser.add_argument("--row", type=int, help="1-based chosen cell; required when the instance is absent")
    parser.add_argument("--column", type=int)
    parser.add_argument("--placement-policy", choices=['first-isolated-empty'])
    args = parser.parse_args()
    try:
        snapshot = json.loads(args.snapshot.read_text(encoding="utf-8"))
        plan = plan_block_insertion(snapshot, effect_id=args.effect_id, row=args.row, column=args.column,
                                    preset_slots=args.preset_slots, all_populated=args.all_populated, placement_policy=args.placement_policy)
    except (OSError, ValueError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2
    print(json.dumps(plan, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
