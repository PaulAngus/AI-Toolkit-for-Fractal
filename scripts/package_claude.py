#!/usr/bin/env python3
"""Build a configurable Claude Desktop MCPB from contained source; no MIDI or AI calls."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile
from package_mcp import package, ROOT
from release_signing import add_signing_arguments, notarize_archive


def build(destination, *, windows_certificate=None, mac_identity=None, notary_profile=None, runtime=None):
    if notary_profile and not mac_identity:
        raise ValueError('Notarization requires an explicitly selected Developer ID identity.')
    destination = Path(destination).resolve()
    if destination.exists() or destination.with_suffix('.mcpb.sha256').exists() or destination.suffix != '.mcpb':
        raise ValueError('Choose a new .mcpb output file; existing packages are never replaced.')
    destination.parent.mkdir(parents=True, exist_ok=True)
    # Build into a temporary directory, never pack the working tree or private inputs.
    with tempfile.TemporaryDirectory(prefix='fractal-claude-build-') as temporary:
        bundle = Path(temporary) / 'bundle'
        package(bundle, windows_certificate=windows_certificate, mac_identity=mac_identity, runtime=runtime)
        shutil.copyfile(ROOT / 'adapters/claude_desktop/manifest.json', bundle / 'manifest.json')
        shutil.copyfile(ROOT / 'adapters/claude_desktop/pyproject.toml', bundle / 'pyproject.toml')
        files = sorted(path for path in bundle.rglob('*') if path.is_file())
        for path in files:
            parts = path.relative_to(bundle).parts
            if {'private', 'backups', '__pycache__', 'bin', 'obj'}.intersection(parts) or path.suffix in ('.syx', '.raw', '.body', '.pyc'):
                raise ValueError('Non-distributable package input refused.')
        manifest = json.loads((bundle / 'manifest.json').read_text(encoding='utf-8'))
        release = json.loads((bundle / 'docs/protocol-release.json').read_text(encoding='utf-8'))
        desktop = json.loads((bundle / 'docs/claude-desktop-validation.json').read_text(encoding='utf-8'))
        if manifest['version'] != desktop.get('packaged_extension_version', desktop['extension_version']) or release['release'] != desktop.get('packaged_toolkit_release', desktop['toolkit_release']):
            raise ValueError('Extension or contained toolkit version differs from its packaging record.')
        with zipfile.ZipFile(destination, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
            for path in files:
                archive.write(path, path.relative_to(bundle))
        with zipfile.ZipFile(destination) as archive:
            if archive.testzip() is not None:
                raise ValueError('Package archive integrity failed.')
    digest = hashlib.sha256(destination.read_bytes()).hexdigest()
    destination.with_suffix('.mcpb.sha256').write_text(f'{digest}  {destination.name}\n', encoding='utf-8')
    if notary_profile:
        notarize_archive(destination, notary_profile)
    print(f'Claude Desktop package created: {destination} ({destination.stat().st_size / 1024 / 1024:.1f} MiB)')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--destination', type=Path, required=True)
    add_signing_arguments(parser)
    parser.add_argument('--runtime', choices=('win-x64', 'osx-arm64', 'osx-x64'))
    parser.add_argument('--notary-profile', help='Existing notarytool keychain profile on the signing Mac')
    try:
        args = parser.parse_args()
        build(args.destination, windows_certificate=args.windows_certificate,
              mac_identity=args.mac_identity, notary_profile=args.notary_profile, runtime=args.runtime)
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        parser.exit(2, f'error: {error}\n')
