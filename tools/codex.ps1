#requires -Version 5.1
<#
.SYNOPSIS
  MindAttic Codex documentation CLI - doctor + digest.
.DESCRIPTION
  doctor : validate the docs/ canon (front-matter, unique IDs, resolvable cross-refs, JSON-schema
           for any docs/data, done-stories-name-a-test, cited paths exist, generatedFrom freshness).
           Exits non-zero on any hard error.
  digest : regenerate docs/BIBLE.digest.md from BIBLE.md s1/s3/s5/s9 + a status index + any pending
           decision heads from AMENDMENTS.md. Never hand-edit the digest.
  Windows PowerShell 5.1 safe (no pwsh-only syntax).
#>
[CmdletBinding()]
param(
  [Parameter(Position = 0)]
  [ValidateSet('doctor', 'digest')]
  [string]$Command = 'doctor'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# --- paths -------------------------------------------------------------------
$RepoRoot = Split-Path -Parent $PSScriptRoot
$DocsDir  = Join-Path $RepoRoot 'docs'
$Bible    = Join-Path $DocsDir 'BIBLE.md'
$Stories  = Join-Path $DocsDir 'USER_STORIES.md'
$Amend    = Join-Path $DocsDir 'AMENDMENTS.md'
$RfcDir   = Join-Path $DocsDir 'rfc'
$DataDir  = Join-Path $DocsDir 'data'
$Digest   = Join-Path $DocsDir 'BIBLE.digest.md'

$script:Errors   = New-Object System.Collections.Generic.List[string]
$script:Warnings = New-Object System.Collections.Generic.List[string]
function Add-Err($m)  { $script:Errors.Add($m) }
function Add-Warn($m) { $script:Warnings.Add($m) }

function Read-Text($path) { Get-Content -LiteralPath $path -Raw -Encoding UTF8 }

# Parse the leading YAML front-matter block into a hashtable (string values only).
function Get-FrontMatter($text, $path) {
  if ($text -notmatch "^---\r?\n") { Add-Err "${path}: missing YAML front-matter"; return $null }
  $end = [regex]::Match($text, "^---\r?\n(.*?)\r?\n---\r?\n", 'Singleline')
  if (-not $end.Success) { Add-Err "${path}: unterminated front-matter"; return $null }
  $fm = @{}
  foreach ($line in ($end.Groups[1].Value -split "\r?\n")) {
    if ($line -match '^\s*([A-Za-z0-9_]+)\s*:\s*(.+?)\s*$') { $fm[$Matches[1]] = $Matches[2] }
  }
  return $fm
}

function Test-FrontMatter($path, $expectLayer) {
  if (-not (Test-Path -LiteralPath $path)) { Add-Err "missing canon file: $path"; return }
  $text = Read-Text $path
  $fm = Get-FrontMatter $text $path
  if ($null -eq $fm) { return }
  foreach ($k in @('codex', 'project', 'code', 'layer', 'status', 'updated')) {
    if (-not $fm.ContainsKey($k)) { Add-Err "${path}: front-matter missing '$k'" }
  }
  if ($fm.ContainsKey('layer') -and $expectLayer -and $fm['layer'] -ne $expectLayer) {
    Add-Err "${path}: layer is '$($fm['layer'])', expected '$expectLayer'"
  }
  if ($fm.ContainsKey('code') -and $fm['code'] -ne 'M.IDEA' -and $fm['code'] -ne 'HOUSE') {
    Add-Warn "${path}: code is '$($fm['code'])' (expected MAI)"
  }
  if ($fm.ContainsKey('updated') -and $fm['updated'] -notmatch '^\d{4}-\d{2}-\d{2}$') {
    Add-Err "${path}: updated '$($fm['updated'])' is not YYYY-MM-DD"
  }
}

# --- doctor ------------------------------------------------------------------
function Invoke-Doctor {
  Write-Host "Codex doctor - $RepoRoot"
  Write-Host ('-' * 60)

  # 1. front-matter on every canon file
  Test-FrontMatter $Bible   'bible'
  Test-FrontMatter $Stories 'stories'
  Test-FrontMatter $Amend   'amendments'
  $rfcFiles = @()
  if (Test-Path -LiteralPath $RfcDir) {
    $rfcFiles = Get-ChildItem -LiteralPath $RfcDir -Filter '*.md' -File
    foreach ($r in $rfcFiles) { Test-FrontMatter $r.FullName 'rfc' }
  }
  $dataFiles = @()
  if (Test-Path -LiteralPath $DataDir) {
    $dataFiles = Get-ChildItem -LiteralPath $DataDir -Filter '*.json' -File -Recurse |
      Where-Object { $_.FullName -notmatch '[\\/]_schema[\\/]' }
  }

  # collect text of the canon docs for ID/ref checks
  $docPaths = @($Bible, $Stories, $Amend) + ($rfcFiles | ForEach-Object { $_.FullName })
  $docPaths = $docPaths | Where-Object { Test-Path -LiteralPath $_ }
  $allText = ($docPaths | ForEach-Object { Read-Text $_ }) -join "`n"

  # 2. unique {#...} anchors; every cross-ref to a {#...} resolves
  $anchorMatches = [regex]::Matches($allText, '\{#([A-Za-z0-9\u00A7._-]+)\}')
  $anchors = @{}
  foreach ($m in $anchorMatches) {
    $id = $m.Groups[1].Value
    if ($anchors.ContainsKey($id)) { Add-Err "duplicate anchor {#$id}" } else { $anchors[$id] = $true }
  }
  # links of the form ](...#ANCHOR) - only validate intra-canon anchors (skip external House file)
  $linkMatches = [regex]::Matches($allText, '\]\(([^)]*?)#([A-Za-z0-9\u00A7._-]+)\)')
  foreach ($m in $linkMatches) {
    $target = $m.Groups[1].Value
    $frag   = $m.Groups[2].Value
    if ($target -match 'HouseRules') { continue }   # external inherited file
    if ($target -match '^https?:') { continue }
    if (-not $anchors.ContainsKey($frag)) { Add-Err "cross-ref to missing anchor #$frag (link target '$target')" }
  }

  # 3. docs/data/*.json validate against _schema/*.schema.json; entity ids unique
  foreach ($d in $dataFiles) {
    try { $json = Get-Content -LiteralPath $d.FullName -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Add-Err "$($d.Name): invalid JSON ($_)"; continue }
    $schema = Join-Path (Join-Path $DataDir '_schema') ("$($d.BaseName).schema.json")
    if (-not (Test-Path -LiteralPath $schema)) { Add-Warn "$($d.Name): no schema at _schema/$($d.BaseName).schema.json" }
    $ids = @{}
    $items = if ($json -is [System.Array]) { $json } else { @($json) }
    foreach ($it in $items) {
      if ($it.PSObject.Properties.Name -contains 'id') {
        if ($ids.ContainsKey($it.id)) { Add-Err "$($d.Name): duplicate entity id '$($it.id)'" } else { $ids[$it.id] = $true }
      }
    }
  }
  if ($dataFiles.Count -eq 0) { Write-Host "  (no docs/data - app domain; L5 canon-as-data not used)" }

  # 4. every done-story names a test token, and the test exists in the tree (best-effort)
  $doneGlyph = [char]0x2705   # green check
  if (Test-Path -LiteralPath $Stories) {
    $storyText = Read-Text $Stories
    $testRoot = Join-Path $RepoRoot 'src\IdeaCms.Tests'
    $testBlob = ''
    if (Test-Path -LiteralPath $testRoot) {
      $testBlob = (Get-ChildItem -LiteralPath $testRoot -Filter '*.cs' -File -Recurse |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }) -join "`n"
    }
    # a done story line is "- **M.IDEA-US-xx <check>** ... " spanning to the next "- **" bullet
    $storyBullets = [regex]::Matches($storyText, '(?s)-\s+\*\*(M\.IDEA-US-[A-Za-z0-9]+)\s+([^\*]+)\*\*(.*?)(?=\r?\n-\s+\*\*M\.IDEA-US-|\r?\n##|\z)')
    foreach ($b in $storyBullets) {
      $sid = $b.Groups[1].Value
      $statusGlyph = $b.Groups[2].Value
      if (-not $statusGlyph.Contains($doneGlyph)) { continue }
      $body = $b.Groups[3].Value
      $tokens = [regex]::Matches($body, '`([A-Za-z][A-Za-z0-9_]*Tests(?:\.[A-Za-z0-9_]+)?)`')
      if ($tokens.Count -eq 0) { Add-Err "$sid is done but names no test token (backticked *Tests)"; continue }
      if ($testBlob) {
        foreach ($t in $tokens) {
          $name = ($t.Groups[1].Value -split '\.')[0]
          if ($testBlob -notmatch [regex]::Escape($name)) { Add-Warn "$sid cites '$name' but it was not found in the test tree" }
        }
      }
    }
  }

  # 5. every code path/file cited in the bible exists on disk
  if (Test-Path -LiteralPath $Bible) {
    $bibleText = Read-Text $Bible
    $pathTokens = [regex]::Matches($bibleText, '`(src/[^`]+?)`')
    $seen = @{}
    foreach ($p in $pathTokens) {
      $rel = $p.Groups[1].Value.TrimEnd('/')
      if ($seen.ContainsKey($rel)) { continue }
      $seen[$rel] = $true
      $full = Join-Path $RepoRoot ($rel -replace '/', '\')
      if (-not (Test-Path -LiteralPath $full)) { Add-Err "BIBLE cites '$rel' which does not exist on disk" }
    }
  }

  # 6. generatedFrom freshness + digest staleness
  if (Test-Path -LiteralPath $Digest) {
    $dt = Read-Text $Digest
    $gf = [regex]::Match($dt, 'generatedFrom:\s*(\S+)')
    if ($gf.Success -and (Test-Path -LiteralPath $Bible)) {
      $srcMtime = (Get-Item -LiteralPath $Bible).LastWriteTimeUtc
      $artMtime = (Get-Item -LiteralPath $Digest).LastWriteTimeUtc
      if ($srcMtime -gt $artMtime) { Add-Warn "BIBLE.digest.md is stale (BIBLE.md changed after it was generated) - run: codex.ps1 digest" }
    }
  } else {
    Add-Warn "BIBLE.digest.md not found - run: codex.ps1 digest"
  }

  # --- report
  Write-Host ''
  Write-Host ("Checks: front-matter, anchors, cross-refs, data/schema, story-tests, cited-paths, digest")
  if ($script:Warnings.Count -gt 0) {
    Write-Host ''
    Write-Host "WARNINGS ($($script:Warnings.Count)):" -ForegroundColor Yellow
    foreach ($w in $script:Warnings) { Write-Host "  ! $w" -ForegroundColor Yellow }
  }
  Write-Host ''
  if ($script:Errors.Count -gt 0) {
    Write-Host "FAIL - $($script:Errors.Count) hard error(s):" -ForegroundColor Red
    foreach ($e in $script:Errors) { Write-Host "  X $e" -ForegroundColor Red }
    exit 1
  }
  Write-Host "PASS - Codex canon is healthy." -ForegroundColor Green
  exit 0
}

# --- digest ------------------------------------------------------------------
# extract a "## N. Title {#id}" section's body (until the next "## ")
function Get-Section($text, $num) {
  $m = [regex]::Match($text, "(?s)\n##\s+$num\.\s+[^\n]*\n(.*?)(?=\n##\s|\z)")
  if ($m.Success) { return $m.Groups[1].Value.Trim() }
  return ''
}

function Invoke-Digest {
  if (-not (Test-Path -LiteralPath $Bible)) { Write-Error "BIBLE.md not found"; exit 1 }
  $bibleText = Read-Text $Bible

  $s1 = Get-Section $bibleText 1
  $s3 = Get-Section $bibleText 3
  $s5 = Get-Section $bibleText 5
  $s9 = Get-Section $bibleText 9

  # status index from USER_STORIES glyph counts
  $done = 0; $partial = 0; $planned = 0; $cut = 0
  if (Test-Path -LiteralPath $Stories) {
    $st = Read-Text $Stories
    $bullets = [regex]::Matches($st, '-\s+\*\*M\.IDEA-US-[A-Za-z0-9]+\s+([^\*]+)\*\*')
    foreach ($b in $bullets) {
      $g = $b.Groups[1].Value
      $gChk  = [char]0x2705
      $gPart = [System.Char]::ConvertFromUtf32(0x1F7E1)   # yellow circle
      $gPlan = [char]0x2B1C                                # white large square
      $gCut  = [System.Char]::ConvertFromUtf32(0x1F5D1)   # wastebasket
      if ($g.Contains($gChk)) { $done++ }
      elseif ($g.Contains($gPart)) { $partial++ }
      elseif ($g.Contains($gPlan)) { $planned++ }
      elseif ($g.Contains($gCut)) { $cut++ }
    }
  }

  # pending decision heads (AMENDMENTS.md is normally empty)
  $pending = @()
  if (Test-Path -LiteralPath $Amend) {
    $am = Read-Text $Amend
    $heads = [regex]::Matches($am, '(?m)^##\s+(M\.IDEA-A\d+\s+\S\s+[^
\{]+)')
    foreach ($h in $heads) { $pending += $h.Groups[1].Value.Trim() }
  }

  $today = (Get-Date).ToString('yyyy-MM-dd')
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('---')
  [void]$sb.AppendLine('codex: 1')
  [void]$sb.AppendLine('project: IdeaCms')
  [void]$sb.AppendLine('code: M.IDEA')
  [void]$sb.AppendLine('layer: digest')
  [void]$sb.AppendLine('generatedFrom: docs/BIBLE.md')
  [void]$sb.AppendLine("updated: $today")
  [void]$sb.AppendLine('---')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('AUTHORITATIVE - full detail in docs/BIBLE.md. GENERATED by tools/codex.ps1; do not hand-edit.')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('# IdeaCms - Bible Digest')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('## The one sentence')
  [void]$sb.AppendLine($s1)
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('## What it is NOT')
  [void]$sb.AppendLine($s3)
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('## The Laws')
  [void]$sb.AppendLine($s5)
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('## Glossary')
  [void]$sb.AppendLine($s9)
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('## Status index (from USER_STORIES.md)')
  [void]$sb.AppendLine("- done: $done  |  partial: $partial  |  planned: $planned")
  if ($pending.Count -gt 0) {
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Pending decisions (docs/AMENDMENTS.md)')
    foreach ($p in $pending) { [void]$sb.AppendLine("- $p") }
  }

  Set-Content -LiteralPath $Digest -Value $sb.ToString() -Encoding UTF8
  $tok = [math]::Round(($sb.ToString().Length / 4))
  Write-Host "Wrote docs/BIBLE.digest.md (~$tok tokens). done=$done partial=$partial planned=$planned"
}

switch ($Command) {
  'doctor' { Invoke-Doctor }
  'digest' { Invoke-Digest }
}
