"""Prepare unsigned 0.3.4 MIDI archives by removing only CodeView build directories.

This is an explicit dependency preparation step, never run during normal builds.
The manifest retains upstream and prepared SHA-256 values. IL/native code, assembly
identity, references and every other archive entry remain unchanged.
"""
import argparse
from datetime import datetime, timedelta
import copy
import hashlib
import io
import json
from pathlib import Path
import struct
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def sanitize(data):
    result = bytearray(data)
    pe = struct.unpack_from('<I', data, 60)[0]
    optional = pe + 24
    magic = struct.unpack_from('<H', data, optional)[0]
    directory = optional + (112 if magic == 0x20b else 96) + 6 * 8
    rva, size = struct.unpack_from('<II', data, directory)
    section = optional + struct.unpack_from('<H', data, pe + 20)[0]
    offset = None
    for i in range(struct.unpack_from('<H', data, pe + 6)[0]):
        virtual_size, address, raw_size, raw_pointer = struct.unpack_from('<IIII', data, section + i * 40 + 8)
        if address <= rva < address + max(virtual_size, raw_size):
            offset = raw_pointer + rva - address
            break
    if offset is None:
        raise ValueError('Missing PE debug directory')
    changes = 0
    for entry in range(offset, offset + size, 28):
        kind, length, _, pointer = struct.unpack_from('<IIII', data, entry + 12)
        if kind != 2:
            continue
        if data[pointer:pointer + 4] != b'RSDS':
            raise ValueError('Unexpected CodeView format')
        start, end = pointer + 24, pointer + length
        original = data[start:end].split(b'\0', 1)[0]
        basename = original.replace(b'\\', b'/').rsplit(b'/', 1)[-1]
        if not basename.endswith(b'.pdb'):
            raise ValueError('Unexpected CodeView filename')
        result[start:end] = basename + b'\0' * (end - start - len(basename))
        changes += 1
    if changes != 1:
        raise ValueError('Expected one CodeView record')
    return bytes(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-package', type=Path, action='append', required=True)
    args = parser.parse_args()
    feed = ROOT / 'dependencies/midi'
    manifest_path = feed / 'manifest.json'
    manifest = json.loads(manifest_path.read_text())
    for package in args.source_package:
        if package.name not in ('mightbeuseful.midi.0.3.4.nupkg', 'mightbeuseful.midi.windows.0.3.4.nupkg', 'mightbeuseful.midi.coremidi.0.3.4.nupkg'):
            raise ValueError('Only the reviewed unsigned MIDI 0.3.4 packages may be prepared')
        record = next(r for r in manifest['packages'] if r['file'] == package.name)
        original = package.read_bytes()
        upstream = record.get('upstream_sha256', record['sha256'])
        if hashlib.sha256(original).hexdigest() != upstream:
            raise ValueError('Upstream package hash mismatch')
        output = io.BytesIO()
        with zipfile.ZipFile(io.BytesIO(original)) as source, zipfile.ZipFile(output, 'w') as target:
            if '.signature.p7s' in source.namelist():
                raise ValueError('Signed packages must not be modified')
            for entry in source.infolist():
                content = source.read(entry)
                prepared_entry = copy.copy(entry)
                if entry.filename.endswith('.dll'):
                    # Ensure same-size upstream DLLs are replaced by incremental builds.
                    prepared_entry.date_time = (datetime(*entry.date_time) + timedelta(seconds=2)).timetuple()[:6]
                    content = sanitize(content)
                target.writestr(prepared_entry, content)
        prepared = output.getvalue()
        (feed / package.name).write_bytes(prepared)
        record.update(upstream_sha256=upstream, sha256=hashlib.sha256(prepared).hexdigest(),
                      preparation='CodeView PDB basename only; executable code and assembly identity unchanged')
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')


if __name__ == '__main__':
    main()
