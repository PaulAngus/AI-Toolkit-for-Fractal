"""Verify contained packages and build with an empty NuGet cache, without MIDI."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import zipfile
import struct

ROOT = Path(__file__).resolve().parents[1]


def main():
    feed = ROOT / 'dependencies/midi'
    manifest = json.loads((feed / 'manifest.json').read_text())
    expected = {entry['file']: entry['sha256'] for entry in manifest['packages']}
    actual = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in feed.glob('*.nupkg')}
    if actual != expected:
        raise RuntimeError('Contained MIDI package hashes differ from reviewed manifest.')
    with tempfile.TemporaryDirectory(prefix='fractal-midi-offline-') as work:
        env = dict(os.environ, NUGET_PACKAGES=str(Path(work) / 'packages'), NUGET_HTTP_CACHE_PATH=str(Path(work) / 'http'))
        # A single repository-local source and empty cache prove the dependency closure.
        for portable in (False, True) if os.name == 'nt' else (True,):
            build = Path(work) / ('portable-build' if portable else 'windows-build')
            properties = [f'-p:FractalPortableBuild={str(portable).lower()}', '-p:NuGetAudit=false',
                          '-p:UseArtifactsOutput=true', '-p:ArtifactsPath=' + str(build)]
            project = 'tests/Fractal.Checks/Fractal.Checks.csproj'
            subprocess.run(['dotnet', 'restore', project, '--force', '--no-cache', '--source', str(feed), *properties], cwd=ROOT, env=env, check=True)
            subprocess.run(['dotnet', 'build', project, '--no-restore', '--nologo', *properties], cwd=ROOT, env=env, check=True)
            executables = list((build / 'bin/Fractal.Checks').rglob('Fractal.Checks.dll'))
            if len(executables) != 1:
                raise RuntimeError('Expected one freshly built shared-MIDI check assembly.')
            subprocess.run(['dotnet', str(executables[0]), '--shared-midi-only'], cwd=ROOT, env=env, check=True)
        published = Path(work) / 'portable-publish'
        subprocess.run(['dotnet', 'publish', 'src/Fractal.Cli/Fractal.Cli.csproj', '--no-self-contained',
                        '-p:FractalPortableBuild=true', '-p:UseAppHost=false', '-p:NuGetAudit=false',
                        '-p:DebugType=None', '-p:DebugSymbols=false',
                        '-p:UseArtifactsOutput=true', '-p:ArtifactsPath=' + str(Path(work) / 'publish-build'),
                        '--source', str(feed), '--output', str(published)], cwd=ROOT, env=env, check=True)
        if (published / 'MightBeUseful.Midi.Windows.dll').exists():
            raise RuntimeError('Portable Mac distribution unexpectedly contains the Windows backend.')
        if list(published.rglob('*.pdb')):
            raise RuntimeError('A stale debug symbol file entered the clean no-symbol publication.')
        core_package = f"mightbeuseful.midi.coremidi.{manifest['shared_midi_version']}.nupkg"
        with zipfile.ZipFile(feed / core_package) as package:
            for rid, cpu in [('osx-arm64', 0x0100000c), ('osx-x64', 0x01000007)]:
                relative = f'runtimes/{rid}/native/Melanchall_DryWetMidi_Native64.dylib'
                native = (published / relative).read_bytes()
                if native != package.read(relative) or native[:4] != b'\xca\xfe\xba\xbe':
                    raise RuntimeError('Mac native asset missing, modified or not the reviewed universal binary.')
                slices = struct.unpack_from('>I', native, 4)[0]
                architectures = {struct.unpack_from('>I', native, 8 + 20 * i)[0] for i in range(slices)}
                if cpu not in architectures:
                    raise RuntimeError('Mac native asset lacks its declared architecture.')
        from validate_distribution import check_directory
        check_directory(published)
    print('PASS Mac arm64/x64 native asset packaging; no native library loaded.')
    print('PASS contained package hashes, cold-cache builds and fake-port checks; no hardware accessed.')


if __name__ == '__main__':
    main()
