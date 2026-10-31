"""Configured Claude Desktop entry point; delegates to the provider-independent server."""
import argparse
import json
from pathlib import Path
import sys
import tempfile
import threading

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'src'))
from fractal_mcp.runtime import Cancelled, Runtime, load_config
from fractal_mcp.connections import resolve_connection
from fractal_mcp.device_settings import normalize_firmware, normalize_model
from fractal_mcp.server import Server
from fractal_mcp.storage import atomic_json
from fractal_mcp.tools import Toolkit


def boolean(value):
    if value not in ('true', 'false'):
        raise ValueError('Access settings must be true or false.')
    return value == 'true'


def enumerate_ports(bundle=ROOT):
    # This closed CLI command lists the local platform catalogue; it sends no MIDI.
    return Runtime({'runtime_dir': str(Path(bundle) / 'runtime')}).run(
        'cli', ['ports'], threading.Event(), maximum_seconds=15)


def host_settings(args, ports=enumerate_ports):
    reads, writes = boolean(args.allow_live_reads), boolean(args.allow_writes)
    if writes and not reads:
        raise ValueError('Enable device reads as well as writes, or turn writes off.')
    settings = {'allow_live_reads': reads, 'allow_writes': writes}
    if reads:
        model = normalize_model(args.model)
        firmware = normalize_firmware(args.firmware)
        if (args.midi_input is None) != (args.midi_output is None):
            raise ValueError('Explicit CLI port overrides must supply both directions.')
        indices = {'input': args.midi_input, 'output': args.midi_output} if args.midi_input is not None else resolve_connection(ports(), model, args.connection)
        settings['device'] = {'model': model, 'capacity': args.capacity,
                              'firmware': firmware, **indices}
    else:
        # Offline use needs no attached hardware or connection enumeration.
        settings['device'] = None
    return settings


def configuration(profile, bundle=ROOT, settings=None, *, persist=True):
    profile = Path(profile).resolve()
    bundle = Path(bundle).resolve()
    if profile.is_relative_to(bundle):
        raise ValueError('Private extension data must be outside the installed package.')
    profile.mkdir(parents=True, exist_ok=True)
    path = profile / 'server.json'
    raw = json.loads(path.read_text(encoding='utf-8')) if path.exists() else {
        'schema_version': 1, 'data_root': 'artifacts',
        'allow_live_reads': False, 'allow_writes': False}
    if not isinstance(raw, dict):
        raise ValueError('Invalid private configuration.')
    if settings is not None:
        raw.update(settings)
        if raw.get('device') is None:
            raw.pop('device', None)
    # Host settings are validated before replacing the operator-visible configuration.
    raw['runtime_dir'] = str(bundle / 'runtime')
    if isinstance(raw.get('data_root'), str):
        raw['data_root'] = str((profile / raw['data_root']).resolve())
    with tempfile.TemporaryDirectory(dir=profile) as temporary:
        candidate = Path(temporary) / 'candidate.json'
        atomic_json(candidate, raw)
        config = load_config(candidate)
    if Path(config['data_root']).is_relative_to(bundle):
        raise ValueError('Private artifacts must be outside the installed package.')
    # Persist the exact host configuration so the operator approval CLI binds to it.
    if persist:
        atomic_json(path, config)
    return config


class DesktopToolkit(Toolkit):
    """A setup error disables device operations while leaving references usable."""
    def __init__(self, bundle, config, setup_error=None, requested_access=None):
        super().__init__(bundle, config)
        self.server_version = self._json_public('adapters/claude_desktop/manifest.json')['version']
        self.setup_error = setup_error
        self.requested_access = requested_access

    def call(self, name, arguments, cancel=None):
        if self.setup_error and name in {
            'fractal_list_ports', 'fractal_inspect_device', 'fractal_capture_presets',
            'fractal_read_scene_midi', 'fractal_capture_load_context', 'fractal_execute_review',
            'fractal_prepare_scene_midi', 'fractal_prepare_insertion', 'fractal_prepare_load',
            'fractal_prepare_recovery'}:
            raise ValueError(self.setup_error + ' Correct Configure settings and restart the extension. No device operation started.')
        result = super().call(name, arguments, cancel)
        if name == 'fractal_status':
            result['release'] = self.server_version
            result['extension_version'] = self.server_version
            result['toolkit_release'] = self.version
            result['setup'] = {'ready': self.setup_error is None, 'error': self.setup_error,
                               'action': 'Correct the extension Configure settings and restart.' if self.setup_error else None}
            if self.requested_access is not None:
                result['requested_access'] = self.requested_access
        return result


def startup(args, bundle=ROOT, ports=enumerate_ports):
    values = (args.allow_live_reads, args.allow_writes, args.model, args.capacity, args.firmware)
    requested = {'live_reads': args.allow_live_reads == 'true', 'writes': args.allow_writes == 'true'} if any(value is not None for value in values) else None
    try:
        settings = None
        if any(value is not None for value in values):
            if any(value is None for value in values):
                raise ValueError('Configure both access switches and all device fields.')
            settings = host_settings(args, ports)
        config = configuration(args.profile, bundle, settings)
        return DesktopToolkit(bundle, config, requested_access=requested)
    except (OSError, ValueError, KeyError, TypeError, Cancelled) as error:
        # Never reuse stale live access after a setup failure or replace its valid file.
        config = configuration(args.profile, bundle, {
            'allow_live_reads': False, 'allow_writes': False, 'device': None}, persist=False)
        return DesktopToolkit(bundle, config, setup_error=str(error), requested_access=requested)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--profile', type=Path,
                        default=Path.home() / '.fractal-ai-toolkit' / 'claude-desktop')
    parser.add_argument('--allow-live-reads', choices=('true', 'false'))
    parser.add_argument('--allow-writes', choices=('true', 'false'))
    parser.add_argument('--model')
    parser.add_argument('--capacity', type=int)
    parser.add_argument('--firmware')
    parser.add_argument('--connection', default='Automatic')
    parser.add_argument('--midi-input', type=int)
    parser.add_argument('--midi-output', type=int)
    try:
        args = parser.parse_args()
        toolkit = startup(args)
        if toolkit.setup_error:
            print('Setup needs attention: ' + toolkit.setup_error + ' Knowledge tools remain available; device access is disabled.', file=sys.stderr)
        Server(toolkit, sys.stdout.buffer).serve(sys.stdin.buffer)
        return 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f'error: {error}', file=sys.stderr)
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
