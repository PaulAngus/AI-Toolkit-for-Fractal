#!/usr/bin/env python3
"""Verify a private hardware run's hashes and emit a public summary without preset/device names."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def summarize(evidence, snapshot_folder):
    report_file = evidence / "report.private.json"
    report = json.loads(report_file.read_text(encoding="utf-8"))
    folder = evidence / snapshot_folder
    manifest_file = folder / "manifest.json"
    manifest = json.loads(manifest_file.read_text(encoding="utf-8"))
    snapshot_file = folder / "snapshot.json"
    snapshot = json.loads(snapshot_file.read_text(encoding="utf-8"))
    if sha256(snapshot_file) != manifest["snapshot_sha256"]:
        raise ValueError("Snapshot file hash mismatch")
    captures = manifest["captures"]
    seen = set()
    wire_lengths = Counter()
    for capture in captures:
        slot = capture["slot"]
        if slot in seen:
            raise ValueError("Duplicate capture slot")
        seen.add(slot)
        path = (folder / capture["file"]).resolve()
        if folder.resolve() not in path.parents:
            raise ValueError("Capture path escapes evidence directory")
        if sha256(path) != capture["sysex_sha256"]:
            raise ValueError(f"Capture hash mismatch: slot {slot}")
        wire_lengths[path.stat().st_size] += 1
    definition = report["device"]["definition"]
    presets = snapshot["presets"]
    for preset in presets:
        source = next(c for c in captures if c["slot"] == preset["slot"])
        if source["raw_sha256"] != preset["content_sha256"] or source["body_sha256"] != preset["body_sha256"]:
            raise ValueError("Snapshot/manifest content hashes disagree")
    complete_capacity = seen == set(range(definition["preset_capacity"]))
    decoded_complete = {p["slot"] for p in presets} == seen and not snapshot.get("decode_errors")
    passed = (report["complete"] and report["failure"] is None and complete_capacity and decoded_complete
              and report["all_transmissions_read_allowlisted"] and not any(r.get("passed") is False for r in report["results"]))
    return {
        "schema_version": 1,
        "protocol_release": report["protocol_release"],
        "started_utc": report["started_utc"], "completed_utc": report["completed_utc"],
        "device": {"model": definition["model"], "firmware": report["device"]["firmware"],
                   "preset_capacity": definition["preset_capacity"]},
        "physical_read_validation_passed": passed,
        "captured_presets": report["captured_presets"], "decoded_presets": len(presets),
        "complete_capacity_covered": complete_capacity,
        "populated_presets": sum(p["populated"] for p in presets),
        "empty_presets": sum(not p["populated"] for p in presets),
        "amp_blocks": sum(len(p["amps"]) for p in presets),
        "scene_midi_blocks": sum(p["scene_midi_present"] for p in presets),
        "opaque_terminal_tails": sum(p.get("opaque_tail_offset") is not None for p in presets),
        "selection_preserved_at_observed_checks": report["state_before"] == report["state_after"],
        "checks": report["results"], "checks_passed": sum(r.get("passed") is True for r in report["results"]),
        "repeat_samples": len(report["repeats"]),
        "all_repeat_samples_byte_identical": all(r["identical"] for r in report["repeats"]),
        "transmitted_requests": report["trace_sends"], "received_frames": report["trace_receives"],
        "all_transmissions_read_allowlisted": report["all_transmissions_read_allowlisted"],
        "elapsed_seconds": report["elapsed_seconds"],
        "preset_read_ms": {"min": report["preset_read_ms_min"], "max": report["preset_read_ms_max"], "mean": report["preset_read_ms_mean"]},
        "capture_file_length_counts": dict(wire_lengths),
        "independent_manifest_hash_verification": "passed",
        "evidence_sha256": {"private_report": sha256(report_file), "private_trace": sha256(evidence / "trace.jsonl"),
                            "capture_manifest": sha256(manifest_file), "snapshot_file": sha256(snapshot_file)},
        "limitations": report["limitations"],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--snapshot-folder", default="full-snapshot")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    value = summarize(args.evidence.resolve(), args.snapshot_folder)
    output = json.dumps(value, indent=2) + "\n"
    if args.output:
        with args.output.open("x", encoding="utf-8") as file:
            file.write(output)
    else:
        print(output, end="")


if __name__ == "__main__":
    main()
