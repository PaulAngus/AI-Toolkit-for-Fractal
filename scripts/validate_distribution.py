"""Reject private data and machine paths in distributable source and compiled output."""
import argparse
import io
from pathlib import Path
import re
import zipfile

PUBLIC_DIRECTORIES = ('dependencies', 'src', 'tests', 'scripts', 'knowledge-base', 'docs', 'skills', 'examples', 'assets', 'adapters')
PUBLIC_FILES = ('LICENSE', 'NOTICE', 'AGENTS.md', 'README.md', 'Directory.Build.props', 'NuGet.Config', '.gitignore')
EXCLUDED_DIRECTORIES = {'__pycache__', 'bin', 'obj', '.venv'}
PRIVATE_DIRECTORIES = {'private', 'backups', 'tokens', 'config-backups'}
PRIVATE_SUFFIXES = {'.syx', '.raw', '.body', '.pcap', '.pcapng', '.jsonl', '.log', '.pyc'}
PROFILE_PATH = re.compile(rb'(?:[A-Za-z]:[/\\]{1,2}Users[/\\]{1,2}|/(?:Users|home)/)[^\s/\\\x00]+', re.I)
CHAT_RECORD = re.compile(rb'"(?:chat_id|chat_title|turn_id|specific_request)"\s*:')


def check_item(name, content):
    if name.lower().endswith('.nupkg'):
        with zipfile.ZipFile(io.BytesIO(content)) as archive:
            for member in archive.namelist():
                if not member.endswith('/'):
                    check_item(name + '/' + member, archive.read(member))
    path = Path(name)
    if PRIVATE_DIRECTORIES.intersection(path.parts) or path.suffix.lower() in PRIVATE_SUFFIXES or path.name == '.env' or path.name.startswith('.env.'):
        raise ValueError(f'Private artifact type in distribution: {name}')
    # PE metadata and portable PDBs can retain paths even when source files are clean.
    for encoded in (content, content.replace(b'\x00', b'')):
        if PROFILE_PATH.search(encoded):
            raise ValueError(f'Machine-specific profile path in distribution: {name}')
    if CHAT_RECORD.search(content):
        raise ValueError(f'Chat/request record in distribution: {name}')


def check_directory(folder, *, source=False):
    folder = Path(folder)
    roots = [folder / name for name in (*PUBLIC_DIRECTORIES, *PUBLIC_FILES)] if source else [folder]
    count = 0
    for root in roots:
        paths = root.rglob('*') if root.is_dir() else [root]
        for path in paths:
            if not path.is_file() or (source and EXCLUDED_DIRECTORIES.intersection(path.relative_to(folder).parts)):
                continue
            check_item(path.relative_to(folder).as_posix(), path.read_bytes())
            count += 1
    return count


def check_archive(archive):
    with zipfile.ZipFile(archive) as package:
        for name in package.namelist():
            if not name.endswith('/'):
                check_item(name, package.read(name))
        return len(package.namelist())


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('path', type=Path, nargs='?', default=Path(__file__).resolve().parents[1])
    parser.add_argument('--source', action='store_true', help='Check only public source inputs, excluding local build directories')
    args = parser.parse_args()
    count = check_directory(args.path, source=args.source) if args.path.is_dir() else check_archive(args.path)
    print(f'Distribution privacy checks passed: {count} files; private artifacts, profile paths and chat records excluded.')
