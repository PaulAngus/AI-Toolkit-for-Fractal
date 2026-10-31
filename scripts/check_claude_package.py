#!/usr/bin/env python3
"""Test an extracted Claude Desktop bundle offline, outside either repository."""
import argparse
from validate_distribution import check_archive
import json
from pathlib import Path
import queue
import subprocess
import sys
import tempfile
import threading
import zipfile


def check(archive, uv=None):
    check_archive(archive)
    with tempfile.TemporaryDirectory(prefix='fractal-desktop-check-') as temporary:
        work = Path(temporary)
        bundle = work / 'installed package'
        bundle.mkdir()
        with zipfile.ZipFile(archive) as packed:
            for name in packed.namelist():
                if not (bundle / name).resolve().is_relative_to(bundle.resolve()) or '\\' in name or ':' in name:
                    raise ValueError('Unconfined archive path refused.')
            assert packed.testzip() is None
            packed.extractall(bundle)
        subprocess.run([sys.executable, '-X', 'utf8', str(bundle / 'scripts/protocol_release.py')], cwd=work, check=True)
        manifest = json.loads((bundle / 'manifest.json').read_text(encoding='utf-8'))
        expected_tools = json.loads(subprocess.check_output([
            sys.executable, '-X', 'utf8', '-c',
            'import json,sys;sys.path.insert(0,sys.argv[1]);from fractal_mcp.tools import TOOLS;print(json.dumps(TOOLS))',
            str(bundle / 'src')], text=True, encoding='utf-8'))
        assert manifest['server']['type'] == 'uv'
        assert manifest['user_config']['allow_live_reads']['default'] is True
        assert manifest['user_config']['allow_writes']['default'] is True
        assert manifest['user_config']['midi_connection']['default'] == 'Automatic'
        assert 'midi_input' not in manifest['user_config'] and 'midi_output' not in manifest['user_config']
        cases = [('FM9', 512, False, False, '99.07'), ('FM9', 512, True, False, '99.07'),
                 ('FM9', 512, True, True, '99.07'), ('FM3', 512, True, True, '99.07'),
                 ('Axe-Fx III', 512, True, True, '99.07'), ('Axe-Fx III', 1024, True, True, '99.07'),
                 ('FM9', 512, True, True, '12.0'), ('FM9', 512, True, True, 'invalid'),
                 ('fm9', 512, True, True, '12.0')]
        for index, (model, capacity, reads, writes, firmware) in enumerate(cases):
            ready = firmware != 'invalid'
            values = {'device_model': model, 'preset_capacity': capacity, 'firmware': firmware,
                      'midi_connection': 'Automatic',
                      'allow_live_reads': reads, 'allow_writes': writes}
            profile = work / ('private profile ' + str(index))
            args = []
            for arg in manifest['server']['mcp_config']['args']:
                arg = arg.replace('${__dirname}', str(bundle))
                for key, value in values.items():
                    rendered = str(value).lower() if type(value) is bool else str(value)
                    arg = arg.replace('${user_config.' + key + '}', rendered)
                assert '${' not in arg, arg
                args.append(arg)
            command = [str(uv), *args] if uv else [sys.executable, '-X', 'utf8', str(bundle / manifest['server']['entry_point']), *args[4:]]
            command += ['--profile', str(profile)]
            # Explicit operator CLI overrides keep enabled-mode probes synthetic and port-free.
            # Automatic/name resolution is independently checked with synthetic rosters.
            if reads:
                command += ['--midi-input', '12345', '--midi-output', '12346']
            process = subprocess.Popen(command, cwd=work, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                       text=True, encoding='utf-8', bufsize=1)
            replies = queue.Queue()
            errors = []
            def read():
                for line in process.stdout:
                    try:
                        replies.put(json.loads(line))
                    except ValueError:
                        replies.put({'bad_stdout': line})
            def read_errors():
                errors.extend(process.stderr.readlines())
            readers = [threading.Thread(target=read, daemon=True), threading.Thread(target=read_errors, daemon=True)]
            for reader in readers:
                reader.start()
            number = 0
            def request(method, parameters):
                nonlocal number
                number += 1
                process.stdin.write(json.dumps({'jsonrpc': '2.0', 'id': number, 'method': method, 'params': parameters}) + '\n')
                process.stdin.flush()
                reply = replies.get(timeout=60)
                assert reply.get('id') == number and 'error' not in reply, reply
                return reply['result']
            try:
                initialized = request('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                                      'clientInfo': {'name': 'packaged-desktop-probe', 'version': '1'}})
                assert initialized['protocolVersion'] == '2025-11-25'
                assert initialized['serverInfo']['version'] == manifest['version']
                process.stdin.write(json.dumps({'jsonrpc': '2.0', 'method': 'notifications/initialized'}) + '\n')
                process.stdin.flush()
                assert request('tools/list', {})['tools'] == expected_tools
                blocks = request('tools/call', {'name': 'fractal_query_block', 'arguments': {'model': 'FM3', 'query': 'Reverb'}})['structuredContent']
                assert not blocks['ports_opened'] and blocks['matches'][0]['name'] == 'Reverb 1'
                assert blocks['matches'][1]['availability']['documented_available'] is False
                status = request('tools/call', {'name': 'fractal_status', 'arguments': {}})['structuredContent']
                assert not status['ports_opened']
                assert status['release'] == manifest['version']
                assert status['extension_version'] == manifest['version']
                assert status['toolkit_release'] == json.loads((bundle / 'docs/protocol-release.json').read_text(encoding='utf-8'))['release']
                assert status['writes_enabled'] == (values['allow_writes'] and ready)
                assert status['live_reads_enabled'] == (values['allow_live_reads'] and ready)
                assert status['setup']['ready'] == ready
                if not ready:
                    assert 'Firmware' in status['setup']['error']
                    assert status['requested_access']['writes'] is True
                knowledge = request('tools/call', {'name': 'fractal_search_knowledge', 'arguments': {'query': 'Scene MIDI'}})
                assert not knowledge.get('isError') and knowledge['structuredContent']['sources']
                resources = request('resources/list', {})['resources']
                uris = {item['uri'] for item in resources}
                assert status['capabilities']['catalog'] in uris
                assert status['hardware_evidence'] in uris
                assert 'separate reviews' in status['capabilities']['combined_operations']
                reference = next(item for item in resources if item['uri'].endswith('reference-map.md'))
                assert request('resources/read', {'uri': reference['uri']})['contents']
                if not values['allow_live_reads'] or not ready:
                    refusal = request('tools/call', {'name': 'fractal_inspect_device', 'arguments': {}})
                    assert refusal['isError'] is True
                # Enabled modes deliberately call no live tool. Missing review cannot launch hardware.
                refusal = request('tools/call', {'name': 'fractal_execute_review', 'arguments': {
                    'review_id': 'a' * 32, 'review_sha256': '0' * 64}})
                assert refusal['isError'] is True
                # Prove the package's helper can actually launch; help is port-free.
                subprocess.run(['dotnet', str(bundle / 'runtime/cli/fractal.dll'), '--help'], cwd=work, check=True, capture_output=True)
                if ready:
                    settings = json.loads((profile / 'server.json').read_text(encoding='utf-8'))
                    assert settings['allow_live_reads'] == values['allow_live_reads']
                    assert settings['allow_writes'] == values['allow_writes']
                    if reads:
                        assert settings['device']['model'] == ('FM9' if model == 'fm9' else model)
                        assert settings['device']['capacity'] == values['preset_capacity']
                        assert settings['device']['firmware'] == ('12.00' if firmware == '12.0' else firmware)
                    else:
                        assert 'device' not in settings
                    assert settings['runtime_dir'] == str(bundle / 'runtime')
                else:
                    assert not (profile / 'server.json').exists()
            finally:
                process.stdin.close()
                try:
                    process.wait(timeout=20)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
                for reader in readers:
                    reader.join(timeout=3)
            assert process.returncode == 0, errors
            # UV's environment diagnostics belong on stderr, never in protocol output.
            if not ready:
                assert any('Setup needs attention:' in line for line in errors), errors
            elif not uv:
                assert not errors, errors
        print(f'Extracted desktop package passed with {"manifest UV launch" if uv else "Python entry point"}: nine configurations including model/version normalization, distinct reported releases and recoverable setup errors, 26 tools, sourced block lookup, references, own helper, execution refusal and clean EOF. No MIDI ports opened.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('--uv', type=Path)
    args = parser.parse_args()
    check(args.archive, args.uv)
