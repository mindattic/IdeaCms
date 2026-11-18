# Sync the packed library .idea files into the web app's library/ seed folder and scrub stale
# Widget-category DB rows so the next app start (or a running app's restart) picks up the
# Plugin/Component split cleanly.
#
# What this does:
#   1. Wipes src/IdeaCms.Web/library/ and copies all 43 dist/*.idea files in.
#   2. Removes InstalledPackage rows with Category = 'Widget' (they no longer parse against the
#      updated ContentKind enum — Widget was renamed to Plugin at ordinal 1).
#   3. Removes ContentDefinition Package-origin rows with Category = 'Widget' (same reason).
#   4. Optionally rebuilds dist/ first if you pass -Rebuild.
#
# Usage:
#   powershell -File tools\install-library.ps1
#   powershell -File tools\install-library.ps1 -Rebuild         # pack first, then sync
#   powershell -File tools\install-library.ps1 -Conn "..."      # override the connection string
#
# The default database is the development one, IdeaCmsDev in Azure, opened with your `az login`.
#
# After this script finishes, restart the web app.  The startup sequence installs every .idea in
# library/ via PackageInstallService (allowOverride:false — idempotent; already-installed versions
# are no-ops; NEW Plugin/Component packages install fresh).

[CmdletBinding()]
param(
    [switch]$Rebuild,
    [string]$Conn = 'Server=tcp:sql-mindattic-f65744.database.windows.net,1433;Database=IdeaCmsDev;Authentication=Active Directory Default;Encrypt=True;Connect Timeout=60'
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$root    = Split-Path $PSScriptRoot -Parent
$distDir = Join-Path $root 'library\dist'
$webLib  = Join-Path $root 'src\IdeaCms.Web\library'

# Opens $ConnectionString. For Azure SQL without a password it signs in with the developer's own
# `az login`: Windows PowerShell's System.Data.SqlClient cannot parse `Authentication=...`, so that
# keyword is dropped and an access token from the Azure CLI is put on the connection instead.
function Open-SqlConnection([string]$ConnectionString) {
    Add-Type -AssemblyName 'System.Data'
    $useToken = $ConnectionString -match 'database\.windows\.net' -and $ConnectionString -notmatch '(?i)(^|;)\s*(Password|Pwd)\s*='
    if ($useToken) {
        $ConnectionString = (($ConnectionString -split ';') | Where-Object { $_.Trim() -and $_ -notmatch '(?i)^\s*Authentication\s*=' }) -join ';'
    }
    $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    if ($useToken) {
        $token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
        if ($LASTEXITCODE -ne 0 -or -not $token) { throw 'No Azure SQL access token: run "az login" first.' }
        $connection.AccessToken = $token
    }
    $connection.Open()
    $connection
}

# ── 1. Optionally rebuild the dist/ packages ────────────────────────────────────────────────────
if ($Rebuild) {
    Write-Host '[install-library] Building library...' -ForegroundColor Cyan
    $slnx = Join-Path $root 'library\IdeaCms.Library.slnx'
    dotnet build $slnx -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Library build failed.' }

    Write-Host '[install-library] Packing...' -ForegroundColor Cyan
    $packScript = Join-Path $root 'library\tools\pack-all.ps1'
    if (Test-Path $packScript) {
        powershell -ExecutionPolicy Bypass -File $packScript
        if ($LASTEXITCODE -ne 0) { throw 'Pack-all failed.' }
    } else {
        Write-Warning "pack-all.ps1 not found at $packScript; skipping auto-pack. Run pack manually."
    }
}

# ── 2. Sync dist/ → web/library/ ────────────────────────────────────────────────────────────────
Write-Host '[install-library] Syncing packages...' -ForegroundColor Cyan

$ideas = Get-ChildItem $distDir -Filter '*.idea' -ErrorAction Stop
if ($ideas.Count -eq 0) { throw "No .idea files found in $distDir. Run -Rebuild or pack manually first." }

# Remove all existing .idea files (stale Widget.* packages get removed here).
$old = Get-ChildItem $webLib -Filter '*.idea' -ErrorAction SilentlyContinue
foreach ($f in $old) { Remove-Item $f.FullName -Force }

# Copy new packages.
foreach ($f in $ideas) {
    Copy-Item $f.FullName (Join-Path $webLib $f.Name) -Force
    Write-Host "  + $($f.Name)"
}
Write-Host "[install-library] $($ideas.Count) package(s) synced to $webLib" -ForegroundColor Green

# ── 3. Clean up stale Widget rows from the database ─────────────────────────────────────────────
Write-Host '[install-library] Cleaning up Widget DB rows...' -ForegroundColor Cyan

$sqlConn = $null
try {
    $sqlConn = Open-SqlConnection $Conn

    $delPkg = $sqlConn.CreateCommand()
    $delPkg.CommandText = "DELETE FROM InstalledPackages WHERE Category = 'Widget'"
    $pkgRows = $delPkg.ExecuteNonQuery()

    $delDef = $sqlConn.CreateCommand()
    $delDef.CommandText = "DELETE FROM ContentDefinitions WHERE Category = 'Widget'"
    $defRows = $delDef.ExecuteNonQuery()

    Write-Host "[install-library] Removed $pkgRows InstalledPackage row(s), $defRows ContentDefinition row(s) with Category='Widget'" -ForegroundColor Green
}
catch {
    Write-Warning "DB cleanup skipped (could not connect or table missing): $_"
    Write-Warning "This is harmless if the database has never been seeded; old Widget rows will simply fail to resolve at runtime."
}
finally {
    if ($sqlConn) { $sqlConn.Dispose() }
}

# ── 4. Done ─────────────────────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '[install-library] Done. Restart the web app to apply changes.' -ForegroundColor Green
Write-Host "  The startup sequence will install all $($ideas.Count) packages via PackageInstallService."
