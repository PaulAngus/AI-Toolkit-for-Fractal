#!/usr/bin/env python3
"""Run local MCP; operator-only import and exact one-use review approval."""
import argparse
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'src'))
from fractal_mcp.runtime import load_config
from fractal_mcp.storage import dumps
from fractal_mcp.tools import Toolkit
from fractal_mcp.server import Server


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('serve', 'approve', 'import'), nargs='?', default='serve')
    parser.add_argument('--config', required=True, type=Path)
    parser.add_argument('--review-id')
    parser.add_argument('--review-sha256')
    parser.add_argument('--source', type=Path)
    parser.add_argument('--kind', choices=('export', 'recovery', 'files'), default='files')
    args = parser.parse_args()
    try:
        toolkit = Toolkit(ROOT, load_config(args.config))
        if args.mode == 'serve':
            if args.review_id or args.review_sha256 or args.source:
                raise ValueError('Server mode does not accept approval/import options.')
            Server(toolkit, sys.stdout.buffer).serve(sys.stdin.buffer)
        elif args.mode == 'approve':
            if not args.review_id or not args.review_sha256 or args.source:
                raise ValueError('Approve needs the concrete review ID and SHA-256, with no import source.')
            print(dumps(toolkit.approve(args.review_id, args.review_sha256)))
        else:
            if not args.source or args.review_id or args.review_sha256:
                raise ValueError('Import needs a local --source file/directory.')
            source = args.source.resolve()
            identifier, destination = toolkit.artifacts.create()
            if source.is_dir() and destination.is_relative_to(source):
                raise ValueError('Import destination cannot be inside its source directory.')
            if args.kind in ('export', 'recovery'):
                toolkit.artifacts.tree_hashes(source)  # Refuse links before copying a complete retained export/record.
                shutil.copytree(source, destination / args.kind)
            elif source.is_dir():
                toolkit.artifacts.tree_hashes(source)
                shutil.copytree(source, destination / 'files')
            else:
                if not source.is_file():
                    raise ValueError('Import source is missing.')
                shutil.copyfile(source, destination / source.name)
            print(dumps({'artifact_id': identifier, 'kind': args.kind}))
        return 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f'error: {error}', file=sys.stderr)
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
