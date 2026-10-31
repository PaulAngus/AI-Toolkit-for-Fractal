"""Private artifact confinement, exact byte hashes and atomic operation evidence."""
import hashlib
import json
import os
import re
import uuid
import time
from pathlib import Path


def dumps(value):
    return json.dumps(value, ensure_ascii=True, allow_nan=False, separators=(',', ':'))


def loads(text):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError('Duplicate JSON property.')
            result[key] = value
        return result
    def constant(_):
        raise ValueError('Non-finite JSON number.')
    def decimal(value):
        result = float(value)
        if not __import__('math').isfinite(result):
            raise ValueError('JSON number exceeds the finite range.')
        return result
    return json.loads(text, object_pairs_hook=pairs, parse_constant=constant, parse_float=decimal)


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(65536), b''):
            digest.update(block)
    return digest.hexdigest()


def atomic_json(path, value):
    temporary = path.with_name(path.name + '.' + uuid.uuid4().hex + '.pending')
    try:
        with temporary.open('x', encoding='utf-8', newline='\n') as stream:
            stream.write(dumps(value) + '\n')
            stream.flush()
            os.fsync(stream.fileno())
        # Windows readers may briefly deny delete-sharing during status polling.
        # Retry only this flushed file replacement, never a device operation.
        for attempt in range(21):
            try:
                os.replace(temporary, path)
                break
            except PermissionError:
                if attempt == 20:
                    raise
                time.sleep(.01)
    finally:
        temporary.unlink(missing_ok=True)


def confined(root, relative, *, exists=True):
    # Also reject Windows paths on Unix so packaged behavior is consistent.
    if not isinstance(relative, str) or not relative or '\\' in relative or ':' in relative:
        raise ValueError('Use a local relative artifact path.')
    parts = relative.split('/')
    if any(part in ('', '.', '..') for part in parts) or relative.startswith('/'):
        raise ValueError('Artifact path must stay inside its directory.')
    path = root
    for part in parts:
        path = path / part
        if path.is_symlink() or (hasattr(path, 'is_junction') and path.is_junction()):
            raise ValueError('Linked artifact paths are refused.')
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError('Artifact path escapes its directory.')
    if exists and not path.exists():
        raise ValueError('Artifact file is missing.')
    return path


class Artifacts:
    def __init__(self, data_root):
        self.root = Path(data_root).resolve()
        self.root.mkdir(parents=True, exist_ok=True)
        for folder in ('artifacts', 'approvals', 'attempts'):
            confined(self.root, folder, exists=False).mkdir(exist_ok=True)

    def directory(self, identifier):
        if not isinstance(identifier, str) or not re.fullmatch('[a-f0-9]{32}', identifier):
            raise ValueError('Invalid artifact ID.')
        path = confined(self.root, 'artifacts/' + identifier)
        if not path.is_dir():
            raise ValueError('Artifact directory is missing.')
        return path

    def file(self, identifier, relative):
        path = confined(self.directory(identifier), relative)
        if not path.is_file():
            raise ValueError('Expected an artifact file.')
        return path

    def create(self):
        identifier = uuid.uuid4().hex
        folder = self.root / 'artifacts' / identifier
        folder.mkdir()
        return identifier, folder

    def json(self, identifier, relative, maximum=16 * 1024 * 1024):
        path = self.file(identifier, relative)
        if path.stat().st_size > maximum:
            raise ValueError('Artifact exceeds its size bound.')
        return loads(path.read_text(encoding='utf-8'))

    def tree_hashes(self, directory):
        result = {}
        for path in sorted(directory.rglob('*')):
            relative = path.relative_to(directory).as_posix()
            confined(directory, relative)
            if path.is_file() and not relative.endswith(('owner.lock', '.pending')):
                result[relative] = sha(path)
        return result
