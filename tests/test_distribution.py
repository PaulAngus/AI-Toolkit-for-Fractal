"""Distribution checks use synthetic data; no personal files or MIDI access."""
from pathlib import Path
import sys
import tempfile
import io
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from validate_distribution import check_archive, check_directory, check_item


class DistributionChecks(unittest.TestCase):
    def test_machine_paths_are_rejected_in_text_and_binary_encodings(self):
        windows = ('C:' + '\\' + 'Users' + '\\' + 'example' + '\\' + 'project').encode()
        unix = ('/' + 'home' + '/' + 'example' + '/project').encode()
        for data in (windows, windows.replace(b'\\', b'\\\\'), unix, windows.decode().encode('utf-16-le')):
            with self.assertRaisesRegex(ValueError, 'profile path'):
                check_item('runtime/helper.dll', b'prefix\x00' + data)

    def test_private_types_and_chat_records_are_rejected(self):
        for name in ('backups/preset.json', 'preset.syx', 'trace.jsonl', '.env', 'private/settings.json'):
            with self.assertRaisesRegex(ValueError, 'Private artifact'):
                check_item(name, b'{}')
        with self.assertRaisesRegex(ValueError, 'Chat/request'):
            check_item('docs/record.json', b'{"' + b'chat_id' + b'":"synthetic"}')

    def test_dependency_archives_are_checked_inside(self):
        archive = io.BytesIO()
        with zipfile.ZipFile(archive, 'w') as package:
            package.writestr('lib/net10.0/example.dll', ('C:' + '\\' + 'Users' + '\\' + 'example').encode())
        with self.assertRaisesRegex(ValueError, 'profile path'):
            check_item('dependencies/example.nupkg', archive.getvalue())

    def test_public_attribution_and_portable_debug_paths_remain_valid(self):
        check_item('NOTICE', b'Source: https://github.com/example/project')
        check_item('runtime/helper.pdb', b'/_/fractal-ai-toolkit/src/helper.cs')

    def test_archive_checks_embedded_paths_and_source_excludes_build_outputs(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            source = folder / 'src'; source.mkdir()
            (source / 'public.py').write_text('print("synthetic")')
            binary = source / 'bin'; binary.mkdir()
            marker = ('C:' + '\\' + 'Users' + '\\' + 'example').encode()
            (binary / 'helper.dll').write_bytes(marker)
            self.assertEqual(check_directory(folder, source=True), 1)
            with self.assertRaises(ValueError): check_directory(source)
            archive = folder / 'extension.mcpb'
            with zipfile.ZipFile(archive, 'w') as package:
                package.writestr('runtime/helper.dll', marker)
            with self.assertRaises(ValueError): check_archive(archive)
