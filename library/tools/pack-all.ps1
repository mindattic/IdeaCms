#requires -Version 5.1
<#
.SYNOPSIS
  Build every library citizen and pack each one to dist/*.idea.

.DESCRIPTION
  The per-citizen pack command is long and easy to get wrong by hand, so dist/ drifts from source: a
  component gets edited, never repacked, and the CMS keeps serving the stale package. (That is how the
  Ideas brochure went on teaching a retired token grammar long after its source was corrected.)

  This walks Themes/, Plugins/ and Components/, builds them all in one pass, then packs each project
  whose output assembly is newer than its packed .idea — or every project when -Force is given.

.PARAMETER Force
  Repack every citizen, even when its .idea is already newer than its assembly.

.PARAMETER Sign
  After packing, content-sign every dist/*.idea (M.IDEA-§4.8) with the signing pfx + password from the
  MindAttic.Vault PackageSigning bucket (%APPDATA%\MindAttic\PackageSigning\providers.json). The host
  rejects an unsigned package on EVERY install path (and fails closed when no trust cert is configured),
  so pair -Install with -Sign.

.PARAMETER Install
  After packing, copy the packed .idea files into the CMS host's library/ folder, so a FRESH database
  seeds them. Note that startup seeding installs with allowOverride:false, so an already-installed
  version is a no-op — copying alone will NOT refresh a package whose version did not change. To pick
  up rebuilt packages during development, start the host with IDEAS_DROPBOX pointed at dist/, which
  installs through the same path with allowOverride:true:

      $env:IDEAS_DROPBOX = 'D:\Projects\MindAttic\IdeaCms\library\dist'
      dotnet run --project src\IdeaCms.Blazor

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\pack-all.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\pack-all.ps1 -Force -Install
#>
[CmdletBinding()]
param(
    [switch] $Force,
    [switch] $Sign,
    [switch] $Install
)

$ErrorActionPreference = 'Stop'

$libraryRoot = Split-Path -Parent $PSScriptRoot
$repoRoot    = Split-Path -Parent $libraryRoot
$distDir     = Join-Path $libraryRoot 'dist'
$sdkProject  = Join-Path $repoRoot 'src\IdeaCms.Sdk'
$refsDir     = Join-Path $repoRoot 'src\MindAttic.Ideas.Abstractions\bin\Debug\net10.0'
$hostLibrary = Join-Path $repoRoot 'src\IdeaCms.Blazor\library'

if (-not (Test-Path $refsDir)) {
    throw "Abstractions reference output not found at '$refsDir'. Build the CMS solution first: dotnet build IdeaCms.slnx -c Debug"
}
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }

Write-Host 'Building the library (Release)...' -ForegroundColor Cyan
$slnx = Join-Path $libraryRoot 'IdeaCms.Library.slnx'
& dotnet build -c Release $slnx --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Library build failed (exit $LASTEXITCODE)." }

$packed = 0; $skipped = 0; $failed = @()

foreach ($kindDir in @('Themes', 'Plugins', 'Components')) {
    $kindPath = Join-Path $libraryRoot $kindDir
    if (-not (Test-Path $kindPath)) { continue }

    foreach ($project in Get-ChildItem -Path $kindPath -Directory | Sort-Object Name) {
        $csproj = Get-ChildItem -Path $project.FullName -Filter '*.csproj' -File | Select-Object -First 1
        if (-not $csproj) { continue }

        $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($csproj.Name)
        $dll = Join-Path $project.FullName "bin\Release\net10.0\$assemblyName.dll"
        if (-not (Test-Path $dll)) {
            $failed += "$($project.Name): no Release output at $dll"
            continue
        }

        # Identity is convention-based, so the artifact name is the assembly name plus the V{n} class.
        # Match on the assembly stem rather than guessing the version.
        $existing = Get-ChildItem -Path $distDir -Filter "$assemblyName.V*.idea" -File -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTime -Descending | Select-Object -First 1

        if (-not $Force -and $existing -and $existing.LastWriteTime -ge (Get-Item $dll).LastWriteTime) {
            $skipped++
            continue
        }

        $assets = Join-Path $project.FullName 'assets'
        $packArgs = @('run', '--project', $sdkProject, '--', 'pack',
                      '--assembly', $dll, '--out', $distDir, '--refs', $refsDir)
        if (Test-Path $assets) { $packArgs += @('--wwwroot', $assets) }

        $output = & dotnet @packArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            $failed += "$($project.Name): $($output -join ' ')"
        } else {
            $packed++
            Write-Host "  packed $assemblyName" -ForegroundColor Green
        }
    }
}

Write-Host ''
Write-Host "packed=$packed skipped=$skipped failed=$($failed.Count)" -ForegroundColor Cyan
foreach ($f in $failed) { Write-Host "  FAILED $f" -ForegroundColor Red }

if ($Sign -and $failed.Count -eq 0) {
    $signingProviders = Join-Path $env:APPDATA 'MindAttic\PackageSigning\providers.json'
    if (-not (Test-Path $signingProviders)) { throw "No PackageSigning Vault bucket at '$signingProviders'." }
    $signing = Get-Content $signingProviders -Raw | ConvertFrom-Json
    if (-not $signing.'signing-cert-pfx') { throw "PackageSigning bucket has no 'signing-cert-pfx' entry." }
    $tmpPfx = Join-Path ([System.IO.Path]::GetTempPath()) "ma-idea-signing-$([Guid]::NewGuid()).pfx"
    [System.IO.File]::WriteAllBytes($tmpPfx, [Convert]::FromBase64String($signing.'signing-cert-pfx'))
    try {
        & dotnet build $sdkProject -c Debug -v q --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "ma-idea build failed (exit $LASTEXITCODE)." }
        $signed = 0
        foreach ($idea in Get-ChildItem -Path $distDir -Filter '*.idea' -File) {
            & dotnet run --no-build --project $sdkProject -- sign $idea.FullName --pfx $tmpPfx --password $signing.'signing-cert-password' | Out-Null
            if ($LASTEXITCODE -ne 0) { $failed += "$($idea.Name): ma-idea sign exited $LASTEXITCODE" } else { $signed++ }
        }
        Write-Host "signed=$signed" -ForegroundColor Cyan
    }
    finally { Remove-Item $tmpPfx -Force -ErrorAction SilentlyContinue }
}

if ($Install) {
    if (-not (Test-Path $hostLibrary)) { New-Item -ItemType Directory -Path $hostLibrary | Out-Null }
    Copy-Item -Path (Join-Path $distDir '*.idea') -Destination $hostLibrary -Force
    Write-Host "copied $((Get-ChildItem $distDir -Filter '*.idea').Count) .idea files into the CMS host library" -ForegroundColor Cyan
}

if ($failed.Count -gt 0) { exit 1 }
