"""Sign normal published runtime files before packaging; never open MIDI ports."""
import hashlib
import json
import platform
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MACHO = {b'\xcf\xfa\xed\xfe', b'\xfe\xed\xfa\xcf', b'\xca\xfe\xba\xbe', b'\xbe\xba\xfe\xca', b'\xca\xfe\xba\xbf'}


def sign_runtime(bundle, *, windows_certificate=None, mac_identity=None):
    bundle = Path(bundle)
    if windows_certificate and mac_identity:
        raise ValueError('Select one native signing platform per package.')
    if windows_certificate:
        if platform.system() != 'Windows':
            raise ValueError('Authenticode signing requires the Windows signing PC.')
        subprocess.run(['pwsh', '-NoProfile', '-File', str(ROOT / 'scripts/Sign-WindowsRuntime.ps1'),
                        '-RuntimeDirectory', str(bundle / 'runtime'), '-CertificateThumbprint', windows_certificate,
                        '-ReceiptPath', str(bundle / 'release-signing.json')], check=True)
    elif mac_identity:
        if platform.system() != 'Darwin' or not mac_identity.startswith('Developer ID Application:'):
            raise ValueError('Developer ID signing requires macOS and a Developer ID Application identity.')
        binaries = []
        for path in sorted((bundle / 'runtime').rglob('*')):
            if path.is_file():
                with path.open('rb') as stream:
                    if stream.read(4) in MACHO:
                        binaries.append(path)
        if not binaries:
            raise ValueError('No published Mach-O runtime binaries to sign.')
        files = []
        for path in binaries:
            args = ['codesign', '--force', '--sign', mac_identity, '--options', 'runtime', '--timestamp']
            if path.suffix != '.dylib':
                args += ['--entitlements', str(ROOT / 'scripts/release-signing.entitlements.plist')]
            subprocess.run([*args, str(path)], check=True)
            subprocess.run(['codesign', '--verify', '--strict', '--verbose=2', str(path)], check=True)
            details = subprocess.run(['codesign', '-d', '--verbose=4', str(path)],
                                     check=True, capture_output=True, text=True).stderr
            if 'Authority=' + mac_identity not in details or 'Timestamp=' not in details:
                raise ValueError('Expected timestamped Developer ID signature was not found.')
            architectures = subprocess.run(['lipo', '-archs', str(path)], check=True,
                                            capture_output=True, text=True).stdout.split()
            files.append({'path': path.relative_to(bundle).as_posix(),
                          'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                          'architectures': architectures})
        (bundle / 'release-signing.json').write_text(json.dumps({
            'platform': 'macOS', 'signing_host_architecture': platform.machine(), 'identity': mac_identity,
            'hardened_runtime': True, 'timestamped': True, 'files': files,
            'scope': 'Signature verification only; no application or hardware acceptance.'}, indent=2) + '\n',
            encoding='utf-8')


def notarize_archive(archive, profile):
    """Notarize the exact ZIP bytes in an MCPB; standalone binaries cannot be stapled."""
    import shutil
    import tempfile
    if platform.system() != 'Darwin':
        raise ValueError('Notarization requires macOS.')
    archive = Path(archive)
    with tempfile.TemporaryDirectory(prefix='fractal-notarize-') as temporary:
        upload = Path(temporary) / 'AI-Toolkit-for-Fractal.zip'
        shutil.copyfile(archive, upload)
        result = subprocess.run(['xcrun', 'notarytool', 'submit', str(upload), '--keychain-profile', profile,
                                 '--wait', '--output-format', 'json'], check=True, capture_output=True, text=True)
        receipt = json.loads(result.stdout)
        receipt['archive_sha256'] = hashlib.sha256(archive.read_bytes()).hexdigest()
        receipt['stapled'] = False
        receipt['stapling_scope'] = 'Apple does not support stapling standalone executables or ZIP/MCPB containers.'
        archive.with_suffix(archive.suffix + '.notarization.json').write_text(
            json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
        subprocess.run(['xcrun', 'notarytool', 'log', receipt['id'], '--keychain-profile', profile,
                        str(archive.with_suffix(archive.suffix + '.notarization-log.json'))], check=True)
        if receipt.get('status') != 'Accepted':
            raise ValueError('Apple did not accept this exact package; retain its notarization log.')


def add_signing_arguments(parser):
    group = parser.add_mutually_exclusive_group()
    group.add_argument('--windows-certificate', help='Exact CurrentUser/My code-signing certificate thumbprint')
    group.add_argument('--mac-identity', help='Exact Developer ID Application identity on the signing Mac')
