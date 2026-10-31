#!/usr/bin/env python3
"""Build a relocatable complete local MCP bundle from this repository alone."""
import argparse
import shutil
import subprocess
from pathlib import Path
from validate_distribution import PUBLIC_DIRECTORIES, PUBLIC_FILES, check_directory

ROOT = Path(__file__).resolve().parents[1]


def package(destination):
    check_directory(ROOT, source=True)
    destination = destination.resolve()
    if destination.exists():
        raise ValueError('Choose a new package directory; existing artifacts are never replaced.')
    if any(destination.is_relative_to(ROOT / source) for source in ('src', 'knowledge-base', 'docs', 'skills', 'scripts')):
        raise ValueError('Package destination cannot be inside one of its source directories.')
    build_directory = destination.with_name(destination.name + '-build')
    if build_directory.exists():
        raise ValueError('Choose a fresh build directory; existing intermediate outputs are never reused.')
    build_directory.mkdir()
    destination.mkdir(parents=True)
    for project, folder in [('src/Fractal.Cli/Fractal.Cli.csproj', 'cli'), ('tests/Fractal.HardwareChecks/Fractal.HardwareChecks.csproj', 'hardware')]:
        subprocess.run(['dotnet', 'publish', project, '--configuration', 'Release', '--no-self-contained', '--nologo',
                        '--artifacts-path', str(build_directory), '-p:UseArtifactsOutput=true',
                        '-p:DebugType=None', '-p:DebugSymbols=false',
                        '--output', str(destination / 'runtime' / folder)], cwd=ROOT, check=True)
    for folder in PUBLIC_DIRECTORIES:
        if not (ROOT / folder).is_dir():
            continue
        shutil.copytree(ROOT / folder, destination / folder, ignore=shutil.ignore_patterns('__pycache__', '*.pyc', 'bin', 'obj'))
    for name in PUBLIC_FILES:
        shutil.copyfile(ROOT / name, destination / name)
    check_directory(destination)
    print(f'Contained MCP bundle created: {destination}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--destination', type=Path, required=True)
    try:
        package(parser.parse_args().destination)
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        parser.exit(2, f'error: {error}\n')
