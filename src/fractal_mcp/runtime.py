"""Typed child runtimes, bounded output/cancellation, and cooperating local MIDI ownership."""
import contextlib
import hashlib
import os
import subprocess
import tempfile
import threading
import time
from pathlib import Path
from .storage import dumps, loads


class Cancelled(Exception):
    pass


class Runtime:
    def __init__(self, config):
        self.config = config

    def command(self, helper, args):
        name = {'cli': 'cli/fractal.dll', 'hardware': 'hardware/Fractal.HardwareChecks.dll'}[helper]
        dll = Path(self.config['runtime_dir']) / name
        if not dll.is_file():
            raise ValueError('Runtime is not built. Run scripts/package_mcp.py and configure runtime_dir.')
        return [self.config.get('dotnet', 'dotnet'), str(dll), *[str(arg) for arg in args]]

    def run(self, helper, args, cancel, *, log=None, maximum_seconds=7200):
        if cancel.is_set():
            raise Cancelled('Cancelled before runtime launch.')
        command = self.command(helper, args)
        with subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                              shell=False, cwd=self.config['runtime_dir']) as process:
            outputs = [bytearray(), bytearray()]
            too_large = threading.Event()
            def drain(stream, index):
                while block := stream.read(4096):
                    if len(outputs[index]) + len(block) <= 2 * 1024 * 1024:
                        outputs[index].extend(block)
                    else:
                        too_large.set()
            readers = [threading.Thread(target=drain, args=(stream, index), daemon=True)
                       for index, stream in enumerate((process.stdout, process.stderr))]
            for reader in readers:
                reader.start()
            start = time.monotonic()
            stopped = None
            while process.poll() is None:
                if cancel.is_set() or too_large.is_set() or time.monotonic() - start > maximum_seconds:
                    stopped = 'cancelled' if cancel.is_set() else 'output/elapsed bound exceeded'
                    # One child process owns the native connection. Stop it; do not resend or roll back.
                    process.kill()
                    break
                time.sleep(.025)
            process.wait()
            for reader in readers:
                reader.join(timeout=5)
            if too_large.is_set():
                stopped = stopped or 'output bound exceeded'
            text = outputs[0].decode('utf-8', errors='replace')
            errors = outputs[1].decode('utf-8', errors='replace')
            if log:
                log.write_text(dumps({'exit_code': process.returncode, 'stdout': text, 'stderr': errors, 'stopped': stopped}) + '\n', encoding='utf-8')
            if stopped:
                raise Cancelled(f'Runtime stopped ({stopped}); device state may be uncertain. Retain evidence and reconcile before any new mutation.')
            if process.returncode != 0:
                raise ValueError(f'Runtime refused or failed ({process.returncode}): {(errors or text)[-3000:]}')
            # Existing hardware adapters can emit progress lines before their final JSON object.
            decoder = __import__('json').JSONDecoder()
            for index, char in enumerate(text):
                if char == '{':
                    try:
                        value, length = decoder.raw_decode(text[index:])
                        if not text[index + length:].strip():
                            return value
                    except ValueError:
                        pass
            raise ValueError('Runtime returned no complete JSON result.')


@contextlib.contextmanager
def device_owner(device):
    # A cooperating OS lease across MCP instances/data roots; other MIDI applications remain external owners.
    identity = "network:" + device["network"] if "network" in device else f'{device["input"]}:{device["output"]}'
    digest = hashlib.sha256(identity.encode()).hexdigest()
    folder = Path(tempfile.gettempdir()) / 'fractal-toolkit-midi-locks'
    folder.mkdir(exist_ok=True)
    path = folder / (digest + '.lock')
    if path.is_symlink():
        raise ValueError('Linked MIDI ownership file refused.')
    stream = path.open('a+b')
    try:
        stream.seek(0)
        stream.write(b'0')
        stream.flush()
        stream.seek(0)
        try:
            if os.name == 'nt':
                import msvcrt
                msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError as error:
            raise ValueError('Another toolkit MCP operation owns these MIDI ports.') from error
        yield
    finally:
        stream.close()


def load_config(path):
    config_path = Path(path).resolve()
    config = loads(config_path.read_text(encoding='utf-8'))
    if not isinstance(config, dict) or config.keys() - {'schema_version', 'data_root', 'runtime_dir', 'dotnet', 'allow_live_reads', 'allow_writes', 'device'}:
        raise ValueError('Invalid server configuration fields.')
    if config.get('schema_version') != 1 or isinstance(config.get('schema_version'), bool):
        raise ValueError('Server configuration requires schema_version 1.')
    for name in ('data_root', 'runtime_dir'):
        if not isinstance(config.get(name), str) or not config[name]:
            raise ValueError(f'Configure {name}.')
        config[name] = str((config_path.parent / config[name]).resolve())
    for flag in ('allow_live_reads', 'allow_writes'):
        if type(config.get(flag, False)) is not bool:
            raise ValueError(f'{flag} must be boolean.')
        config.setdefault(flag, False)
    if config['allow_writes'] and not config['allow_live_reads']:
        raise ValueError('Writes require explicitly enabled live reads as well.')
    if 'dotnet' in config and (not isinstance(config['dotnet'], str) or not config['dotnet']):
        raise ValueError('Configure dotnet as an executable path or name.')
    if config['allow_live_reads'] or 'device' in config:
        device = config.get('device')
        if not isinstance(device, dict) or set(device) not in ({'model', 'capacity', 'firmware', 'input', 'output'}, {'model', 'capacity', 'firmware', 'network'}):
            raise ValueError('Live access requires an exact device and either explicit local ports or one network endpoint.')
        from .schema import obj, MODEL, FIRMWARE, integer, string, validate
        fields = {'model': MODEL, 'firmware': FIRMWARE, 'capacity': integer(512, 1024)}
        if 'network' in device:
            fields['network'] = string()
            validate(obj(fields), device)
            import ipaddress
            try:
                host, port = device['network'].split(':')
                address = ipaddress.IPv4Address(host)
                if str(address) != host or address.is_multicast or address.is_unspecified or host == '255.255.255.255' or not port.isascii() or not port.isdecimal() or not 1 <= int(port) <= 65534:
                    raise ValueError()
            except (ValueError, TypeError) as error:
                raise ValueError('Network requires a unicast IPv4 literal and control port 1–65534.') from error
            device['network'] = f'{host}:{int(port)}'
        else:
            fields.update({'input': integer(0, 65535), 'output': integer(0, 65535)})
            validate(obj(fields), device)
        if device['capacity'] not in (512, 1024) or device['model'] != 'Axe-Fx III' and device['capacity'] != 512:
            raise ValueError('Invalid model/capacity combination.')
    return config
