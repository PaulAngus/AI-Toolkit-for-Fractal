#!/usr/bin/env python3
"""Plan Scene MIDI PC/CC settings from a normalized snapshot; never opens MIDI."""

from __future__ import annotations

import argparse
import copy
import importlib.util
import json
import sys
from pathlib import Path
from typing import Any


_spec = importlib.util.spec_from_file_location("fractal_block_planning", Path(__file__).with_name("plan_block_insertion.py"))
blocks = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(blocks)
integer, object_value = blocks.integer, blocks.object_value


def validate_snapshot(snapshot: Any) -> dict[str, Any]:
    snapshot = blocks.validate_context(snapshot)
    device = snapshot["device"]
    if snapshot["schema_version"] >= 2:
        capabilities = object_value(device.get("capabilities"), "device capabilities")
        if capabilities.get("scene_midi") is not True:
            raise ValueError("snapshot does not establish Scene MIDI capability")
        integer(capabilities.get("scene_count"), 8, 8, "scene count")
        integer(capabilities.get("message_slots"), 8, 8, "message slot count")
    for preset in snapshot["presets"]:
        slot = preset["slot"]
        if type(preset.get("scene_midi_present")) is not bool:
            raise ValueError(f"preset {slot}: scene_midi_present must be a boolean")
        block = preset.get("top_left_block")
        if block is not None and (not isinstance(block, str) or not block.strip()):
            raise ValueError(f"preset {slot}: top_left_block must be null or a nonempty label")
        if block == "Scene MIDI" and not preset["scene_midi_present"]:
            raise ValueError(f"preset {slot}: Scene MIDI cell contradicts scene_midi_present")
        cells = blocks.grid_cells(preset, device)
        rows, columns = blocks.GRID_DIMENSIONS[device["model"]]
        if len(cells) == rows * columns:
            midi_count = sum(cell["effect_id"] == 190 for cell in cells.values())
            if midi_count > 1 or bool(midi_count) != preset["scene_midi_present"]:
                raise ValueError(f"preset {slot}: grid contradicts Scene MIDI presence")
        decode_status = preset.get("scene_midi_decode_status", "decoded" if preset["scene_midi_present"] else "absent")
        if snapshot["schema_version"] >= 2 and "scene_midi_decode_status" not in preset:
            raise ValueError(f"preset {slot}: scene_midi_decode_status is required")
        if decode_status not in ("decoded", "absent", "unsupported-saved-table-layout"):
            raise ValueError(f"preset {slot}: invalid Scene MIDI decode status")
        if (decode_status == "absent") == preset["scene_midi_present"]:
            raise ValueError(f"preset {slot}: inconsistent Scene MIDI decode status")
        scenes = preset.get("scenes")
        if not isinstance(scenes, list) or len(scenes) != 8:
            raise ValueError(f"preset {slot}: exactly eight scenes are required")
        scene_numbers = set()
        for scene in scenes:
            scene = object_value(scene, "scene")
            number = integer(scene.get("number"), 1, 8, "scene number")
            if number in scene_numbers:
                raise ValueError(f"preset {slot}: duplicate scene {number}")
            scene_numbers.add(number)
            messages = scene.get("messages")
            if not isinstance(messages, list):
                raise ValueError(f"preset {slot}, scene {number}: messages must be an array")
            if messages and not preset["scene_midi_present"]:
                raise ValueError(f"preset {slot}: messages require an existing Scene MIDI block")
            if messages and decode_status == "unsupported-saved-table-layout":
                raise ValueError(f"preset {slot}: unread table cannot contain decoded messages")
            message_slots = set()
            for message in messages:
                message = object_value(message, "message")
                message_slot = integer(message.get("slot"), 1, 8, "message slot")
                if message_slot in message_slots:
                    raise ValueError(f"preset {slot}, scene {number}: duplicate message slot {message_slot}")
                message_slots.add(message_slot)
                kind = message.get("type")
                if kind not in ("Off", "PC", "CC"):
                    raise ValueError("message type must be Off, PC or CC")
                if kind == "Off":
                    if any(message.get(key) is not None for key in ("channel", "value", "controller")):
                        raise ValueError("Off slots have null semantic fields; inactive values belong in raw")
                else:
                    integer(message.get("channel"), 1, 16, "MIDI channel")
                    integer(message.get("value"), 0, 127, "message value")
                if kind == "CC":
                    integer(message.get("controller"), 0, 127, "CC controller")
                elif message.get("controller") is not None:
                    raise ValueError("only CC messages have a semantic controller")
                if snapshot["schema_version"] == 3:
                    raw = object_value(message.get("raw"), "raw Scene MIDI fields")
                    for key in ("type", "channel", "controller", "value"):
                        integer(raw.get(key), 0, 65535, f"raw {key}")
                    if raw["type"] != {"Off": 0, "CC": 1, "PC": 2}[kind]:
                        raise ValueError("raw type contradicts normalized message")
                    if kind != "Off" and (raw["channel"] + 1 != message["channel"] or raw["value"] != message["value"]):
                        raise ValueError("raw channel/value contradict normalized message")
                    if kind == "CC" and raw["controller"] != message["controller"]:
                        raise ValueError("raw controller contradicts normalized CC")
            if snapshot["schema_version"] == 3 and decode_status == "decoded" and message_slots != set(range(1, 9)):
                raise ValueError(f"preset {slot}, scene {number}: a decoded table requires all eight message slots")
    return snapshot


def plan_scene_midi(
    snapshot: dict[str, Any], *, channel: int, value: int,
    preset_slots: list[int] | None = None, all_populated: bool = False,
    scenes: list[int] | None = None, message_slot: int = 1,
    message_type: str = "PC", controller: int | None = None,
    block_row: int | None = None, block_column: int | None = None,
    placement_policy: str | None = None,
) -> dict[str, Any]:
    validate_snapshot(snapshot)
    integer(channel, 1, 16, "MIDI channel")
    integer(value, 0, 127, "message value")
    if message_type not in ("PC", "CC"):
        raise ValueError("requested message type must be PC or CC")
    if message_type == "CC":
        integer(controller, 0, 127, "CC controller")
    elif controller is not None:
        raise ValueError("controller applies only to CC messages")
    integer(message_slot, 1, 8, "message slot")
    blocks.validate_placement(snapshot["device"], block_row, block_column, placement_policy)
    selected_scenes = list(range(1, 9)) if scenes is None else scenes
    if not isinstance(selected_scenes, list) or not selected_scenes:
        raise ValueError("choose at least one scene")
    for scene in selected_scenes:
        integer(scene, 1, 8, "requested scene")
    if len(set(selected_scenes)) != len(selected_scenes):
        raise ValueError("requested scenes must be unique")
    selected_scenes = sorted(selected_scenes)
    by_slot = {preset["slot"]: preset for preset in snapshot["presets"]}
    selected_slots = blocks.select_slots(snapshot, preset_slots, all_populated)

    desired = {"type": message_type, "channel": channel, "value": value}
    if message_type == "CC":
        desired["controller"] = controller
    results = []
    for slot in selected_slots:
        preset = by_slot[slot]
        result: dict[str, Any] = {
            "slot": slot, "base_content_sha256": preset["content_sha256"].lower(),
            "status": "unchanged", "reason": "requested messages already configured",
            "actions": [], "conflicts": [],
        }
        if not preset["populated"]:
            result.update(status="skipped", reason="empty preset")
            results.append(result)
            continue
        if preset.get("scene_midi_decode_status") == "unsupported-saved-table-layout":
            result.update(status="conflict", reason="existing Scene MIDI settings have not been decoded")
            result["conflicts"].append({"kind": "unread-scene-midi-table"})
            results.append(result)
            continue
        insertion = None
        if not preset["scene_midi_present"]:
            insertion = blocks.insertion_for(preset, snapshot["device"], 190, block_row, block_column, placement_policy)
            result["conflicts"].extend(insertion["conflicts"])
        by_scene = {scene["number"]: scene for scene in preset["scenes"]}
        changes = []
        for number in selected_scenes:
            existing = next((message for message in by_scene[number]["messages"]
                             if message["slot"] == message_slot), None)
            if existing is not None and existing["type"] != "Off":
                if all(existing.get(key) == setting for key, setting in desired.items()):
                    continue
                result["conflicts"].append({
                    "kind": "occupied-message-slot", "scene": number,
                    "message_slot": message_slot, "existing": copy.deepcopy(existing),
                })
            else:
                changes.append({
                    "kind": "set-scene-midi-message", "scene": number,
                    "message_slot": message_slot, "message": dict(desired),
                    "before_message": copy.deepcopy(existing),
                })
        if result["conflicts"]:
            result.update(status="conflict", reason="existing settings require resolution")
        elif changes:
            result.update(status="planned", reason="requested messages require changes")
            if insertion is not None:
                result["actions"].extend(insertion["actions"])
            result["actions"].extend(changes)
        results.append(result)

    plan = blocks.plan_envelope(snapshot, results, schema_version=2)
    plan.update(message=desired, message_slot=message_slot, scenes=selected_scenes,
                requested_block_placement={"policy": placement_policy} if placement_policy else None if block_row is None else {"row": block_row, "column": block_column},
                insertion_validation_scope=blocks.VALIDATION_SCOPE,
                executor_requirements=list(blocks.EXECUTOR_REQUIREMENTS))
    return plan


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--snapshot", required=True, type=Path)
    scope = parser.add_mutually_exclusive_group(required=True)
    scope.add_argument("--preset", type=int, action="append", dest="preset_slots")
    scope.add_argument("--all-populated", action="store_true")
    parser.add_argument("--channel", required=True, type=int)
    parser.add_argument("--value", required=True, type=int)
    parser.add_argument("--type", choices=("PC", "CC"), default="PC", dest="message_type")
    parser.add_argument("--controller", type=int)
    parser.add_argument("--scenes", nargs="+", type=int)
    parser.add_argument("--message-slot", type=int, default=1)
    parser.add_argument("--block-row", type=int, help="Chosen 1-based cell when a new block is required")
    parser.add_argument("--block-column", type=int)
    parser.add_argument("--placement-policy", choices=['first-isolated-empty'])
    args = parser.parse_args()
    try:
        snapshot = json.loads(args.snapshot.read_text(encoding="utf-8"))
        plan = plan_scene_midi(snapshot, channel=args.channel, value=args.value,
                               preset_slots=args.preset_slots, all_populated=args.all_populated,
                               scenes=args.scenes, message_slot=args.message_slot,
                               message_type=args.message_type, controller=args.controller,
                               block_row=args.block_row, block_column=args.block_column, placement_policy=args.placement_policy)
    except (OSError, ValueError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2
    print(json.dumps(plan, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
