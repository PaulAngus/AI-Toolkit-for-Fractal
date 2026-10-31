#!/usr/bin/env python3
"""Record/check complete contained protocol-release source hashes; never fetch dependencies."""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "docs/protocol-release.json"


def source_hashes():
    files = [ROOT / "Directory.Build.props", ROOT / "NuGet.Config", ROOT / "LICENSE", ROOT / "NOTICE", ROOT / "docs/big-saurus-license.txt"]
    files += [ROOT / 'knowledge-base/catalog' / name for name in ('block-identities.json', 'edit-read-profiles.json', 'audio-parameter-mappings.json', 'saved-value-scales.json')]
    files += [ROOT / 'scripts' / name for name in ('check_cli_pipeline.py', 'check_shared_midi.py', 'prepare_shared_midi.py', 'validate_knowledge.py', 'protocol_release.py')]
    for folder in (ROOT / "src", ROOT / "tests/Fractal.Checks", ROOT / "tests/Fractal.HardwareChecks"):
        files.extend(p for p in folder.rglob("*") if p.is_file() and p.suffix in (".cs", ".csproj")
                     and not {"bin", "obj"}.intersection(p.relative_to(ROOT).parts))
    files.extend(p for p in (ROOT / "dependencies").rglob("*") if p.is_file())
    return {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(files)}


def planner_hashes():
    files = [ROOT / folder / name for folder in ("scripts", "skills/fractal-fm9/scripts")
             for name in ("plan_block_insertion.py", "plan_block_removal.py", "plan_scene_midi.py")]
    return {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(files)}


def adapter_hashes():
    files = list((ROOT / 'src/fractal_mcp').glob('*.py'))
    files += [ROOT / 'scripts' / name for name in ('fractal_mcp.py', 'package_mcp.py', 'check_mcp_pipeline.py', 'check_mcp_sdk.py', 'search_knowledge.py', 'query_amp.py', 'validate_distribution.py')]
    files += [ROOT / 'tests/test_mcp.py', ROOT / 'tests/test_midi_connections.py', ROOT / 'tests/test_device_settings.py', ROOT / 'tests/test_distribution.py']
    return {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(files)}


def desktop_hashes():
    files = [ROOT / 'adapters/claude_desktop' / name for name in ('server.py', 'manifest.json', 'pyproject.toml')]
    files += [ROOT / 'scripts/package_claude.py', ROOT / 'scripts/check_claude_package.py', ROOT / 'tests/test_claude_desktop.py']
    return {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(files)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--record", action="store_true", help="Deliberately update local release hashes after reviewed changes")
    args = parser.parse_args()
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    hashes = source_hashes()
    planners = planner_hashes()
    adapters = adapter_hashes()
    desktop = desktop_hashes()
    if args.record:
        manifest["contained_source_sha256"] = hashes
        manifest["contained_planner_sha256"] = planners
        manifest["contained_adapter_sha256"] = adapters
        manifest['contained_desktop_packaging_sha256'] = desktop
        MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    elif manifest.get("contained_source_sha256") != hashes:
        raise SystemExit("Protocol release hashes differ; review changes and explicitly record a new source snapshot.")
    elif manifest.get("contained_planner_sha256") != planners:
        raise SystemExit("Planner release hashes differ; review changes and explicitly record the contained planners.")
    elif manifest.get("contained_adapter_sha256") != adapters:
        raise SystemExit("MCP adapter hashes differ; review changes and explicitly record the contained adapter.")
    elif manifest.get('contained_desktop_packaging_sha256') != desktop:
        raise SystemExit('Desktop packaging hashes differ; review and explicitly record the contained packaging.')
    print(f"Contained protocol release {manifest['release']}: {len(hashes)} source/build files, {len(planners)} planner/entry files and {len(adapters)} adapter/check files verified.")
    print(f'Contained Claude Desktop packaging: {len(desktop)} files verified.')


if __name__ == "__main__":
    main()
