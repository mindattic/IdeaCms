#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes a standalone Release copy of IdeaCms to C:\Apps\Ideas\ when the source has
    changed, and optionally launches it. The one script for deploying and launching Ideas.

.DESCRIPTION
    Without -Force this is a fast no-op unless the deployed exe is missing, or a source file under
    this repo is newer than the deployed exe. (serve.bat and run.bat are rewritten every run, so a
    missing one never forces a republish.)

    When it does publish:
    1. Stops any running IdeaCms.Blazor.exe.
    2. With -Force, removes bin/obj for the Blazor, Core and Rendering projects (a clean rebuild).
       BUILD cache only: the LocalDB database (IdeaCms, shared with the dev server), the Data
       Protection key ring under %APPDATA%\MindAttic\DataProtection\Ideas\, and any media already
       uploaded under C:\Apps\Ideas\media\ are never touched, so content and logins survive a redeploy.
    3. Repacks the first-party library (library\tools\pack-all.ps1 -Install) into the host's library\
       folder, then dotnet publish (Release, framework-dependent) -> C:\Apps\Ideas\. Library citizens keep
       their whole-number version while being edited, and boot seeding installs with allowOverride:false,
       so serve.bat also points IDEAS_DROPBOX at the deployed library\ - the Development-only path that
       installs through the admin-upload code path WITH override, so edited packages actually replace
       the installed ones.
    4. Writes C:\Apps\Ideas\serve.bat, which pins ASPNETCORE_URLS to a port distinct from the dev
       server (5229/7207) and sets ASPNETCORE_ENVIRONMENT=Development - Program.cs only runs the
       LocalDB migration and skips its production-only Azure Data Protection (Blob + Key Vault)
       requirement in Development, same convention IdiotProof.Blazor uses for its own deployed copy.
       The Release build itself is unaffected: the debug-only auth bypass is gated on build
       Configuration, not on ASPNETCORE_ENVIRONMENT, so it never ships here regardless.
    5. Writes C:\Apps\Ideas\run.bat (the desktop shortcut target), which opens a menu first:
       1) Deploy & Run runs this script with -Force (a clean republish), then starts serve.bat and
       opens the browser; 2) Run starts the deployed serve.bat without calling this script (deploying
       first if nothing is deployed yet). If the source repo is missing (moved/deleted), Deploy & Run
       warns and launches whatever is already deployed.

    serve.bat and run.bat are rewritten on EVERY run (publishing or not), so an existing install
    picks up launcher changes without a republish. This script never prompts: agents and /deploy
    run it non-interactively; the menu lives only in the generated run.bat.

.PARAMETER Force
    Republish with a clean bin/obj rebuild even if nothing looks changed.

.PARAMETER Launch
    After deploying, start the deployed copy (serve.bat) and open the browser.

.PARAMETER LauncherOnly
    Only (re)write C:\Apps\Ideas\serve.bat and run.bat, then exit: no build, nothing stopped.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File run.ps1
    powershell -ExecutionPolicy Bypass -File run.ps1 -Force
    powershell -ExecutionPolicy Bypass -File run.ps1 -Launch
    powershell -ExecutionPolicy Bypass -File run.ps1 -LauncherOnly
#>
param(
    [switch]$Force,
    [switch]$Launch,
    [switch]$LauncherOnly,
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$LaunchArgs = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoDir    = $PSScriptRoot
$proj       = Join-Path $repoDir 'src\IdeaCms.Blazor\IdeaCms.Blazor.csproj'
$out        = 'C:\Apps\Ideas'
$exeName    = 'IdeaCms.Blazor.exe'
$exe        = Join-Path $out $exeName
$url        = 'http://localhost:5280'
$serveBat    = Join-Path $out 'serve.bat'
$launcherBat = Join-Path $out 'run.bat'

$sourceExtensions = @('.cs', '.csproj', '.xaml', '.razor', '.cshtml', '.css', '.js', '.json',
                      '.props', '.targets', '.resx')
# publish\ is a gitignored local publish output, not source.
$skipDirs = @('bin', 'obj', '.git', '.vs', 'node_modules', 'publish', 'TestResults')

# Walk that never descends into build output or VCS folders; stops at the first newer source file.
# cmd reads a running .bat by byte offset, and run.bat runs this script, so only rewrite a launcher
# whose text actually changed, and always with CRLF so its goto labels resolve.
function Write-BatIfChanged([string]$Path, [string]$Text) {
    $Text = ($Text -replace "`r?`n", "`r`n").TrimEnd() + "`r`n"
    if ((Test-Path -LiteralPath $Path) -and ([IO.File]::ReadAllText($Path) -ceq $Text)) { return }
    [IO.File]::WriteAllText($Path, $Text, [Text.Encoding]::ASCII)
}

function Find-NewerSource([string]$dir, [datetime]$since) {
    foreach ($item in Get-ChildItem -LiteralPath $dir -Force -ErrorAction SilentlyContinue) {
        if ($item.PSIsContainer) {
            if ($skipDirs -contains $item.Name) { continue }
            if (Find-NewerSource $item.FullName $since) { return $true }
        } elseif (($sourceExtensions -contains $item.Extension.ToLowerInvariant()) -and
                  $item.LastWriteTimeUtc -gt $since) {
            Write-Host "  Changed: $($item.FullName)" -ForegroundColor DarkGray
            return $true
        }
    }
    return $false
}

function Test-NeedsPublish {
    if (-not (Test-Path $exe)) { return $true }
    return (Find-NewerSource $repoDir (Get-Item $exe).LastWriteTimeUtc)
}

# ── Run wrapper + menu launcher (desktop shortcut target) ──────────────────
# Written on every run, publishing or not. Deploy & Run calls this script with -Force; Run starts the
# deployed copy directly. Falls back to the existing deployed copy (with a warning) if the repo has
# moved, rather than refusing to start.
function Write-Launchers {
    @(
        '@echo off',
        'cd /d "%~dp0"',
        "set ASPNETCORE_URLS=$url",
        'set ASPNETCORE_ENVIRONMENT=Development',
        'set "IDEAS_DROPBOX=%~dp0library"',
        "`"%~dp0$exeName`""
    ) -join "`r`n" | ForEach-Object { Write-BatIfChanged $serveBat $_ }

    $runPs1Path = Join-Path $repoDir 'run.ps1'
    @(
        '@echo off',
        'title IdeaCms',
        "rem Generated by $runPs1Path - edits here are overwritten.",
        "set `"DEPLOY_PS1=$runPs1Path`"",
        'echo.',
        'echo   IdeaCms',
        'echo     1^) Deploy ^& Run',
        'echo     2^) Run',
        'echo.',
        'choice /c 12 /n /m "  Choose 1 or 2: "',
        'if errorlevel 2 goto run',
        ':deploy',
        'if exist "%DEPLOY_PS1%" (',
        '    powershell -NoProfile -ExecutionPolicy Bypass -File "%DEPLOY_PS1%" -Force',
        '    if errorlevel 1 echo Redeploy failed - launching whatever is already in this folder instead.',
        ') else (',
        '    echo Source repo not found at "%DEPLOY_PS1%" - launching existing build without redeploying.',
        ')',
        'goto launch',
        ':run',
        "if exist `"%~dp0$exeName`" goto launch",
        'echo  Nothing is deployed yet - deploying first.',
        'goto deploy',
        ':launch',
        "echo  Starting IdeaCms ($url)...",
        "start `"IdeaCms`" `"%~dp0serve.bat`"",
        'timeout /t 3 /nobreak >nul',
        "start `"`" `"$url`""
    ) -join "`r`n" | ForEach-Object { Write-BatIfChanged $launcherBat $_ }
    # run.bat replaces the old generated launch.bat and deploy.bat. (The old run.bat, the server
    # runner, is now serve.bat; writing the launcher above overwrites it in place.)
    foreach ($old in 'launch.bat', 'deploy.bat') {
        $oldPath = Join-Path $out $old
        if (Test-Path $oldPath) { Remove-Item $oldPath -Force -ErrorAction SilentlyContinue }
    }
}

New-Item -ItemType Directory -Force -Path $out | Out-Null

if ($LauncherOnly) {
    Write-Launchers
    Write-Host "  Wrote $launcherBat" -ForegroundColor Green
    exit 0
}

if (-not $Force -and -not (Test-NeedsPublish)) {
    Write-Host '  Up to date - nothing to publish.' -ForegroundColor DarkGray
} else {
    # ── Stop running instance ──────────────────────────────────────────────
    Write-Host ''
    Write-Host '  Stopping running instance...' -ForegroundColor Yellow
    $procs = Get-Process 'IdeaCms.Blazor' -ErrorAction SilentlyContinue
    if ($procs) {
        $procs | Stop-Process -Force
        Write-Host "    Stopped ($(@($procs).Count) process(es))" -ForegroundColor DarkYellow
        Start-Sleep -Milliseconds 800
    } else {
        Write-Host '    Nothing running.' -ForegroundColor DarkGray
    }

    # ── Clear build cache (-Force only) ────────────────────────────────────
    # bin/obj only - never the LocalDB database, the Data Protection key ring under
    # %APPDATA%\MindAttic\DataProtection\Ideas\, or media already deployed under $out\media\.
    if ($Force) {
        Write-Host ''
        Write-Host '  Clearing build cache (bin/obj)...' -ForegroundColor Cyan
        foreach ($rel in @('src\IdeaCms.Blazor', 'src\IdeaCms.Core', 'src\IdeaCms.Rendering')) {
            $projRoot = Join-Path $repoDir $rel
            if (-not (Test-Path $projRoot)) { continue }
            foreach ($d in @((Join-Path $projRoot 'bin'), (Join-Path $projRoot 'obj'))) {
                if (Test-Path $d) {
                    try { Remove-Item -Recurse -Force $d -ErrorAction Stop }
                    catch { Write-Host "    (could not fully remove $d : $($_.Exception.Message))" -ForegroundColor DarkYellow }
                }
            }
        }
    }

    # ── Repack the library into the host ───────────────────────────────────
    Write-Host ''
    Write-Host '  Packing library citizens into the host...' -ForegroundColor Cyan
    powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoDir 'library\tools\pack-all.ps1') -Sign -Install | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Library pack failed (exit $LASTEXITCODE)." -ForegroundColor Red
        exit $LASTEXITCODE
    }

    # ── Publish ────────────────────────────────────────────────────────────
    Write-Host ''
    Write-Host "  Publishing $proj" -ForegroundColor Cyan
    Write-Host "    -> $exe"

    dotnet publish $proj `
        --configuration Release `
        --no-self-contained `
        --output $out | Out-Host

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publish failed (exit $LASTEXITCODE)." -ForegroundColor Red
        exit $LASTEXITCODE
    }
    if (-not (Test-Path $exe)) {
        Write-Host "Publish completed but $exeName not found at $exe." -ForegroundColor Red
        exit 1
    }
    # An incremental publish can copy an unchanged apphost with its old timestamp; stamp it so the
    # next run's change check compares against this deploy.
    (Get-Item $exe).LastWriteTime = Get-Date

    Write-Host ''
    Write-Host '  Published successfully.' -ForegroundColor Green
    Write-Host "    Exe    : $exe" -ForegroundColor Gray
    Write-Host "    Run    : $serveBat ($url)" -ForegroundColor Gray
    Write-Host "    Launch : $launcherBat" -ForegroundColor Gray
    Write-Host ''
}

# LAMP step 7 (M.IDEA-T33): import any first-run values into MindAttic Vault and write the settings manifest
# Vault.Desktop reads, as the deployed copy runs it (serve.bat's Development environment), on every run.
if (Test-Path $exe) {
    $prevEnv = $env:ASPNETCORE_ENVIRONMENT
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    try { Push-Location $out; & $exe --write-settings-manifest | Out-Null } finally { Pop-Location; $env:ASPNETCORE_ENVIRONMENT = $prevEnv }
}

Write-Launchers

if ($Launch) {
    Write-Host '  Launching...' -ForegroundColor Cyan
    Start-Process $serveBat
    Start-Sleep -Seconds 3
    Start-Process $url
}
exit 0
