# Signed release packages

Build from clean committed source on the native signing machine. Keep the runtime
version in `Directory.Build.props`, `docs/protocol-release.json` and the extension
manifest/pyproject aligned with the packaging version fields in
`docs/claude-desktop-validation.json`. The historical validation fields retain
their original scope. A signature or successful archive check is not hardware
or application acceptance. GitHub Actions remains disabled.

The normal toolkit remains framework dependent: .NET 10 and Python are runtime
prerequisites. Publishing does not replace dependencies or introduce a test host.
Use a fresh destination for every attempt; artifacts are never overwritten.

MCPB releases contain the published runtime, references and source used at runtime.
Build-only NuGet archives are distributed separately in the matching complete
source archive. Apple scans nested NuGet ZIPs during notarization; upstream
development packages contain unsigned native binaries that are not loaded by the
published runtime. Keep those original packages and hashes unchanged in source.
`runtime-distribution.json` declares the exact omitted build inputs, and source
verification checks all remaining files against the full protocol manifest.
No other source omission or changed dependency hash is permitted.

Create the complete source archive from the exact clean release commit:

```sh
git archive --format=zip --output AI-Toolkit-for-Fractal-0.9.0-source.zip HEAD
```

The source archive is development input, not a notarized runtime distribution.

## Windows x64

```powershell
python scripts/package_claude.py --destination dist/AI-Toolkit-for-Fractal-0.9.0-win-x64.mcpb --runtime win-x64 --windows-certificate YOUR_EXACT_40_CHARACTER_THUMBPRINT
```

SignTool signs the toolkit's two apphosts and its own managed assemblies before
archiving, using SHA-256 and an RFC 3161 SHA-256 timestamp. Every file must verify
as trusted with the selected signer and a timestamp. The private key stays in
the Windows certificate/token provider; enter a requested PIN only in its UI.
Third-party signatures and source/dependency archives are retained unchanged.

## macOS

```sh
python3 scripts/package_claude.py --destination dist/AI-Toolkit-for-Fractal-0.9.0-osx-arm64.mcpb --runtime osx-arm64 --mac-identity 'Developer ID Application: YOUR NAME (TEAMID)' --notary-profile EXISTING_KEYCHAIN_PROFILE
python3 scripts/package_claude.py --destination dist/AI-Toolkit-for-Fractal-0.9.0-osx-x64.mcpb --runtime osx-x64 --mac-identity 'Developer ID Application: YOUR NAME (TEAMID)' --notary-profile EXISTING_KEYCHAIN_PROFILE
```

All published Mach-O files receive a timestamped Developer ID signature and strict
verification. Framework-dependent apphosts use hardened runtime with JIT and
library-validation exceptions to load the installed .NET runtime from its own
publisher. Debugging entitlements are not enabled. The exact MCPB ZIP bytes are
submitted to Apple's service; acceptance and detailed logs are retained beside
the package. Standalone executable tickets cannot be stapled, so Gatekeeper uses
Apple's online ticket lookup. ZIP extraction must preserve executable permissions.

The same `--runtime` and signing switches are supported by `package_mcp.py` for
an unpacked provider-independent MCP bundle; add `--runtime-only` for the normal
runtime release, or retain the default complete build bundle for development.
`release-signing.json` records
signed runtime hashes without machine paths. Archive `.sha256` sidecars pin the
final bytes. Keep the unchanged candidate for subsequent platform acceptance.

Primary guidance: [Microsoft SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool),
[Microsoft .NET macOS notarization](https://learn.microsoft.com/en-us/dotnet/core/install/macos-notarization-issues),
[Apple notarization workflow and standalone binary limits](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow).
