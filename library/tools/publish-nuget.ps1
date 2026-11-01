#requires -Version 5.1
<#
.SYNOPSIS
  Pack, sign, wrap and push every library citizen to the configured NuGet feed (GitHub Packages).

.DESCRIPTION
  NuGet distribution (M.IDEA-§4.8): each `.idea` citizen ships as its own package
  (id `IdeaCms.{Category}.{Key}`, version `{n}.0.0`), content-signed independently of NuGet's
  own signing feature (see PackageSigner) so every install path — NuGet fetch, `library/` folder,
  `--install`, admin upload — verifies the same signature the same way.

  Chain: pack-all.ps1 (build + pack to dist/*.idea) -> for each .idea, pull the signing pfx+password out
  of the MindAttic.Vault PackageSigning bucket -> `ma-idea sign` -> `ma-idea nupkg` -> `dotnet nuget push`.
  GitHub Packages' own version immutability (rejects re-pushing the same id+version with different
  bytes) is a second, independent layer on top of the host's own hash-conflict check at install time.

.PARAMETER FeedUrl
  The NuGet v3 feed to push to, e.g. https://nuget.pkg.github.com/MindAttic/index.json.

.PARAMETER Force
  Forwarded to pack-all.ps1 — repack every citizen even if its .idea is already newer than its assembly.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\publish-nuget.ps1 -FeedUrl https://nuget.pkg.github.com/MindAttic/index.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $FeedUrl,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

$libraryRoot = Split-Path -Parent $PSScriptRoot
$repoRoot    = Split-Path -Parent $libraryRoot
$distDir     = Join-Path $libraryRoot 'dist'
$nupkgDir    = Join-Path $libraryRoot 'nupkg'
$sdkProject  = Join-Path $repoRoot 'src\IdeaCms.Sdk'

# ---- pull the signing pfx + password and the feed PAT out of MindAttic.Vault's on-disk JSON. Vault
# buckets are just JSON files under %APPDATA%\MindAttic\<bucket>\ — no library call needed for a script. ----
$vaultRoot = Join-Path $env:APPDATA 'MindAttic'
$signingProviders = Join-Path $vaultRoot 'PackageSigning\providers.json'
if (-not (Test-Path $signingProviders)) {
    throw "No PackageSigning Vault bucket at '$signingProviders'. Provision signing-cert-pfx + " +
          "signing-cert-password there first (base64-encoded pfx bytes + its password)."
}
$signing = Get-Content $signingProviders -Raw | ConvertFrom-Json
$pfxBase64 = $signing.'signing-cert-pfx'
$pfxPassword = $signing.'signing-cert-password'
if (-not $pfxBase64) { throw "PackageSigning bucket has no 'signing-cert-pfx' entry." }

$patSource = Join-Path $vaultRoot 'Tokens\tokens.json'
if (-not (Test-Path $patSource)) { $patSource = Join-Path $vaultRoot 'Tokens\providers.json' }
if (-not (Test-Path $patSource)) {
    throw "No Tokens Vault bucket found under '$vaultRoot\Tokens'. Provision 'github-packages-pat' there first."
}
$tokens = Get-Content $patSource -Raw | ConvertFrom-Json
$pat = $tokens.'github-packages-pat'
if (-not $pat) { throw "Tokens bucket has no 'github-packages-pat' entry." }

$tmpPfx = Join-Path ([System.IO.Path]::GetTempPath()) "ma-idea-signing-$([Guid]::NewGuid()).pfx"
[System.IO.File]::WriteAllBytes($tmpPfx, [Convert]::FromBase64String($pfxBase64))

try {
    Write-Host 'Packing the library...' -ForegroundColor Cyan
    $packArgs = @('-File', (Join-Path $PSScriptRoot 'pack-all.ps1'))
    if ($Force) { $packArgs += '-Force' }
    & powershell -ExecutionPolicy Bypass @packArgs
    if ($LASTEXITCODE -ne 0) { throw "pack-all.ps1 failed (exit $LASTEXITCODE)." }

    if (-not (Test-Path $nupkgDir)) { New-Item -ItemType Directory -Path $nupkgDir | Out-Null }

    $signed = 0; $pushed = 0; $failed = @()
    foreach ($idea in Get-ChildItem -Path $distDir -Filter '*.idea' -File) {
        try {
            & dotnet run --project $sdkProject -- sign $idea.FullName --pfx $tmpPfx --password $pfxPassword
            if ($LASTEXITCODE -ne 0) { throw "ma-idea sign failed (exit $LASTEXITCODE)." }
            $signed++

            & dotnet run --project $sdkProject -- nupkg --idea $idea.FullName --out $nupkgDir
            if ($LASTEXITCODE -ne 0) { throw "ma-idea nupkg failed (exit $LASTEXITCODE)." }
        }
        catch {
            $failed += "$($idea.Name): $_"
        }
    }

    foreach ($nupkg in Get-ChildItem -Path $nupkgDir -Filter '*.nupkg' -File) {
        # GitHub Packages rejects re-pushing the same id+version with different bytes — --skip-duplicate
        # makes an identical re-push a no-op instead of an error, matching the install-time hash-conflict
        # posture (same version, same content = fine; same version, different content = a real conflict).
        & dotnet nuget push $nupkg.FullName --source $FeedUrl --api-key $pat --skip-duplicate
        if ($LASTEXITCODE -eq 0) { $pushed++ }
        else { $failed += "$($nupkg.Name): dotnet nuget push exited $LASTEXITCODE" }
    }

    Write-Host ''
    Write-Host "signed=$signed pushed=$pushed failed=$($failed.Count)" -ForegroundColor Cyan
    foreach ($f in $failed) { Write-Host "  FAILED $f" -ForegroundColor Red }
    if ($failed.Count -gt 0) { exit 1 }
}
finally {
    Remove-Item $tmpPfx -Force -ErrorAction SilentlyContinue
}
