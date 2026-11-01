#requires -Version 5.1
<#
.SYNOPSIS
  Build the public demo's provisioning idealist: every first-party .idea + ONE hello-world page.

.DESCRIPTION
  The demo (mindattic-ideas-demo) is a vanilla Ideas install that boots from this file
  (IdeaCms:Idealist = seed/demo.idealist). It is generated, not committed, so its Packages[] is always
  exactly the set of packages in the build that ships — adding a package to library/ needs no edit here.
  The page itself lives in seed/demo/frontpage.html.

.EXAMPLE
  ./tools/build-demo-idealist.ps1 -Out publish/seed/demo.idealist
#>
[CmdletBinding()]
param(
    [string] $LibraryDir,
    [string] $PagePath,
    [Parameter(Mandatory)] [string] $Out
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot is empty inside param() defaults on Windows PowerShell 5.1, so resolve them here.
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $LibraryDir) { $LibraryDir = Join-Path $repoRoot 'src\IdeaCms.Blazor\library' }
if (-not $PagePath)   { $PagePath   = Join-Path $repoRoot 'seed\demo\frontpage.html' }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$packages = foreach ($file in Get-ChildItem -Path $LibraryDir -Filter '*.idea' -File | Sort-Object Name) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        $entry = $zip.GetEntry('idea.json')
        if (-not $entry) { throw "$($file.Name) has no idea.json" }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        "$($manifest.category).$($manifest.key)@$($manifest.version)"
    } finally { $zip.Dispose() }
}
if (-not $packages) { throw "No .idea packages found in $LibraryDir" }

$plugins = '["Plugin.navmenu","Plugin.themetoggle","Plugin.footer","Plugin.poweredby"]'
$list = [ordered]@{
    formatVersion = 1
    exportedUtc   = (Get-Date).ToUniversalTime().ToString('o')
    exportedFrom  = 'tools/build-demo-idealist.ps1'
    packages      = @($packages)
    site          = [ordered]@{
        key = 'default'; name = 'Ideas demo'; hostBindings = ''
        defaultThemeKey = 'ideas'; defaultThemeVersion = 1; defaultThemeMode = 'dark'; settingsJson = $null
    }
    settings      = @(
        [ordered]@{ scope = 'Site'; key = 'nav.brand';       value = 'Ideas demo' }
        [ordered]@{ scope = 'Site'; key = 'nav.brandhref';   value = '/' }
        [ordered]@{ scope = 'Site'; key = 'nav.links';       value = 'Home=/;Sign in=/admin;About Ideas=https://mindattic.azurewebsites.net/ideas' }
        [ordered]@{ scope = 'Site'; key = 'footer.text';     value = 'A public demo of IdeaCms. Wiped and re-provisioned every hour.' }
        [ordered]@{ scope = 'Site'; key = 'plugins.default'; value = $plugins }
    )
    pages         = @(
        [ordered]@{
            # A fixed uid, so every hourly re-provision reconciles onto the same page identity.
            uid = '5d0c9a3e-4f2b-4c8e-9a61-1d7e2b3c4a50'; slug = 'frontpage'; title = 'Ideas demo'
            # [IO.File], not Get-Content: on 5.1 Get-Content's string carries PSDrive/PSProvider note
            # properties that ConvertTo-Json would walk at -Depth 10 (effectively forever).
            kind = 'Data'; bodyHtml = [System.IO.File]::ReadAllText((Resolve-Path $PagePath)); bodyTrust = 'Author'
            themeKey = 'ideas'; themeVersion = 1; isPublished = $true; enabled = $true
            uses = @('Theme.ideas@1')
        }
    )
    componentMetadata = @()
    media             = @()
}

$outPath = [System.IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outPath) | Out-Null
if (Test-Path $outPath) { Remove-Item $outPath -Force }

$archive = [System.IO.Compression.ZipFile]::Open($outPath, 'Create')
try {
    $writer = New-Object System.IO.StreamWriter($archive.CreateEntry('idealist.json').Open(), (New-Object System.Text.UTF8Encoding($false)))
    try { $writer.Write(($list | ConvertTo-Json -Depth 10)) } finally { $writer.Dispose() }
} finally { $archive.Dispose() }

Write-Host "demo idealist: $($packages.Count) package(s), 1 page -> $outPath"
