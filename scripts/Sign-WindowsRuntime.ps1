#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{40}$')][string]$CertificateThumbprint,
    [Parameter(Mandatory)][string]$ReceiptPath,
    [string]$SignToolPath,
    [uri]$TimestampServer = 'http://time.certum.pl'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Sign the runtime on the Windows signing PC.' }
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
if (-not $certificate.HasPrivateKey -or $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date) -or
    @($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' }).Count -eq 0) {
    throw 'Select a currently valid code-signing certificate with its private key available.'
}
if (-not $TimestampServer.IsAbsoluteUri -or $TimestampServer.Scheme -notin @('http','https')) { throw 'Use an HTTP(S) RFC 3161 timestamp service.' }
if (-not $SignToolPath) {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { $SignToolPath = $command.Source }
    else {
        $sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
        $SignToolPath = Get-ChildItem -LiteralPath $sdk -Directory | Where-Object Name -Match '^\d+\.\d+\.\d+\.\d+$' |
            Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'x64/signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (-not $SignToolPath -or -not (Test-Path -LiteralPath $SignToolPath)) { throw 'Windows SDK SignTool is required.' }
$root = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$names = @('fractal.exe','fractal.dll','Fractal.Core.dll','Fractal.HardwareChecks.exe','Fractal.HardwareChecks.dll')
$files = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object Name -In $names)
foreach ($name in $names) { if ($name -notin $files.Name) { throw "Expected published runtime file missing: $name" } }
$temporary = @()
try {
    foreach ($file in $files) {
        $copy = Join-Path $file.DirectoryName ('.sign-' + [guid]::NewGuid().ToString('N') + $file.Extension)
        Copy-Item -LiteralPath $file.FullName -Destination $copy
        $temporary += [pscustomobject]@{ Original=$file.FullName; Copy=$copy }
    }
    & $SignToolPath sign /s My /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampServer.AbsoluteUri /td SHA256 @($temporary.Copy)
    if ($LASTEXITCODE -ne 0) { throw 'Authenticode signing or timestamping failed.' }
    & $SignToolPath verify /pa /all /tw @($temporary.Copy)
    if ($LASTEXITCODE -ne 0) { throw 'Authenticode verification failed.' }
    foreach ($entry in $temporary) {
        $signature = Get-AuthenticodeSignature -LiteralPath $entry.Copy
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint -or
            $null -eq $signature.TimeStamperCertificate) { throw 'Expected trusted signer and timestamp are required for every runtime file.' }
    }
    foreach ($entry in $temporary) { Move-Item -LiteralPath $entry.Copy -Destination $entry.Original -Force }
    $receiptFiles = @($files | ForEach-Object { [ordered]@{
        path=('runtime/' + [IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/'))
        sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    } })
    [ordered]@{ platform='Windows'; architecture='x64'; certificate=$CertificateThumbprint.ToUpperInvariant();
        timestamped=$true; files=$receiptFiles; scope='Signature verification only; no application or hardware acceptance.' } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReceiptPath -Encoding utf8NoBOM
}
finally {
    foreach ($entry in $temporary) { if (Test-Path -LiteralPath $entry.Copy) { Remove-Item -LiteralPath $entry.Copy -Force } }
}
