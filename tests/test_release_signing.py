"""Unit-only refusal checks for release signing; no keys, tools or devices used."""
import sys
import tempfile
import json
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from release_signing import sign_runtime
from package_claude import build
import protocol_release


class ReleaseSigningTests(unittest.TestCase):
    def test_conflicting_signers_are_refused_before_tools_run(self):
        with patch('release_signing.subprocess.run') as run:
            with self.assertRaisesRegex(ValueError, 'one native'):
                sign_runtime(Path('unused'), windows_certificate='a' * 40,
                             mac_identity='Developer ID Application: Example')
            run.assert_not_called()

    def test_signing_on_the_wrong_platform_is_refused(self):
        with patch('release_signing.platform.system', return_value='Linux'), patch('release_signing.subprocess.run') as run:
            for options in ({'windows_certificate': 'a' * 40}, {'mac_identity': 'Developer ID Application: Example'}):
                with self.assertRaises(ValueError):
                    sign_runtime(Path('unused'), **options)
            run.assert_not_called()

    def test_unsigned_packaging_does_not_invoke_signing_tools(self):
        with patch('release_signing.subprocess.run') as run:
            sign_runtime(Path('unused'))
            run.assert_not_called()

    def test_missing_mach_o_and_ad_hoc_identity_are_refused(self):
        with tempfile.TemporaryDirectory() as temporary, patch('release_signing.platform.system', return_value='Darwin'):
            with self.assertRaisesRegex(ValueError, 'Mach-O'):
                sign_runtime(Path(temporary), mac_identity='Developer ID Application: Example')
            with self.assertRaisesRegex(ValueError, 'Developer ID'):
                sign_runtime(Path(temporary), mac_identity='-')

    def test_notarization_requires_developer_id_before_building(self):
        with patch('package_claude.package') as package:
            with self.assertRaisesRegex(ValueError, 'explicitly selected'):
                build(Path('unused.mcpb'), notary_profile='example-profile')
            package.assert_not_called()

    def test_runtime_distribution_omits_only_exact_build_archives(self):
        expected = {'dependencies/midi/pinned.nupkg': 'archive-hash', 'src/core.cs': 'source-hash'}
        with tempfile.TemporaryDirectory() as temporary, patch.object(protocol_release, 'ROOT', Path(temporary)):
            receipt = Path(temporary) / 'runtime-distribution.json'
            receipt.write_text(json.dumps({'kind': 'runtime', 'omitted_build_inputs': {
                'dependencies/midi/pinned.nupkg': 'archive-hash'}}))
            self.assertEqual(protocol_release.distribution_source_hashes({'contained_source_sha256': expected}),
                             {'src/core.cs': 'source-hash'})
            for omissions in ({'src/core.cs': 'source-hash'}, {'dependencies/midi/pinned.nupkg': 'changed'}, {}):
                receipt.write_text(json.dumps({'kind': 'runtime', 'omitted_build_inputs': omissions}))
                with self.assertRaisesRegex(ValueError, 'exact pinned'):
                    protocol_release.distribution_source_hashes({'contained_source_sha256': expected})
