"""Validate retained pilot manifests/traces and export a report without names, slots or preset bytes."""
import argparse
import base64
import hashlib
import json
from collections import Counter
from pathlib import Path


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def evidence(folder, label):
    report_file, trace_file = folder / "report.private.json", folder / "trace.jsonl"
    report = read(report_file)
    return report, {"label": label, "report_sha256": sha(report_file.read_bytes()),
                    "trace_sha256": sha(trace_file.read_bytes())}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pass-run", type=Path, required=True)
    parser.add_argument("--attempt", type=Path, action="append", default=[])
    parser.add_argument("--recovery", type=Path, action="append", default=[])
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report, provenance = evidence(args.pass_run, "complete-pilot")
    assert report["complete"] and report["restored"] and not report["failure"]
    assert report["state_before"] == report["state_after"]
    assert report["last_observed_saved_sha256"] == report["expected_base_sha256"]
    assert all(r["passed"] for r in report["results"])
    model = report["device"]["definition"]["model"]
    rows = report["device"]["definition"]["grid_rows"]
    model_byte = report["device"]["definition"]["model_byte"]
    position = (report["column"] - 1) * rows + report["row"] - 1
    operations = Counter()
    transmitted, received = 0, 0
    for line in (args.pass_run / "trace.jsonl").read_text().splitlines():
        item = json.loads(line)
        if item["direction"] == "rx":
            received += 1
            continue
        transmitted += 1
        frame = base64.b64decode(item["data_base64"], validate=True)
        assert frame[:4] == b"\xf0\x00\x01\x74" and frame[-1] == 247
        checksum = 0
        for b in frame[:-2]:
            checksum ^= b
        assert frame[-2] == checksum & 127 and all(b < 128 for b in frame[1:-1])
        assert frame[4] in (model_byte, 127)
        if frame[5] == 1:
            assert len(frame) == 23 and frame[4] == model_byte
            action = frame[6]
            label = {9: "set-scene-midi-field", 0x30: "select-cell", 0x32: "insert-scene-midi",
                     0x33: "clear-cell", 0x26: "save-target", 0x27: "reload-target"}[action]
            operations[label] += 1
            if action in (0x26, 0x27):
                assert frame[12] | frame[13] << 7 == report["slot"]
            elif action in (0x30, 0x32, 0x33):
                assert frame[12] | frame[13] << 7 == position
                assert frame[8] | frame[9] << 7 == (190 if action == 0x32 else 0)
            else:
                assert frame[8] | frame[9] << 7 == 190 and frame[10] | frame[11] << 7 < 256
        elif frame[5] == 0x0c and frame[6] != 127:
            assert len(frame) == 9 and frame[6] < 8
            operations["select-scene"] += 1
        else:
            assert frame[5] in (0, 8, 0x0d, 0x0c, 0x0e, 3, 0x13, 0x1f)
            operations["read"] += 1
    assert transmitted == report["transmissions"] and received == report["receives"]
    assert operations["save-target"] == 3 and operations["set-scene-midi-field"] > 0
    exports = 0
    for path in args.pass_run.glob("*/manifest.json"):
        manifest = read(path)
        assert sha((path.parent / manifest["snapshot_file"]).read_bytes()) == manifest["snapshot_sha256"]
        assert not manifest["decode_errors"]
        for capture in manifest["captures"]:
            assert Path(capture["file"]).name == capture["file"]
            assert sha((path.parent / capture["file"]).read_bytes()) == capture["sysex_sha256"]
        exports += 1
    assert exports == 6
    records = [provenance]
    attempts = []
    for index, folder in enumerate(args.attempt, 1):
        trial, prov = evidence(folder, f"partial-attempt-{index}")
        assert not trial["complete"] and trial["failure"]
        attempts.append({"stage": trial["stage"], "failed_checks": [r["name"] for r in trial["results"] if not r["passed"]]})
        records.append(prov)
    recoveries = []
    for index, folder in enumerate(args.recovery, 1):
        recovery, prov = evidence(folder, f"explicit-recovery-{index}")
        assert recovery["complete"] and recovery["restored"] and not recovery["failure"]
        recoveries.append({"complete": True, "original_saved_image_restored": True, "checks_passed": len(recovery["results"])})
        records.append(prov)
    date = json.loads((args.pass_run / "trace.jsonl").read_text().splitlines()[0])["utc"][:10]
    output = {"schema_version": 1, "protocol_release": report["protocol_release"], "date": date,
              "device_model": model, "firmware": report["device"]["firmware"],
              "scope": "Single authorized empty preset; owned Scene MIDI insertion/edit/save/reload/removal",
              "complete": True, "original_saved_image_restored": True, "original_selection_restored": True,
              "checks_passed": len(report["results"]), "transmissions": transmitted, "received_frames": received,
              "operations": dict(operations), "all_saves_and_reloads_scoped_to_target": True,
              "verified_exports": exports, "checks": [r["name"] for r in report["results"]],
              "partial_attempts": attempts, "explicit_recoveries": recoveries, "retained_evidence_sha256": records,
              "limitations": report["limitations"], "other_models": "FM3/Axe-Fx III synthetic checks only; no physical editor test"}
    args.output.write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
    print(f"Editor pilot evidence validated: {output['checks_passed']} checks, {transmitted} sends, {exports} exports; original restored.")


if __name__ == "__main__":
    main()
