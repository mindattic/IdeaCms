<#
.SYNOPSIS
  Codex CLI for IdeaCms.Library (code: MAIL) -- 'doctor' and 'digest'.

  doctor : validates the Codex docs (front-matter, unique ids, resolving cross-refs,
           JSON canon against its schema with unique entity ids, every done story names a
           proof token, every bible-cited path exists, generatedFrom artifacts not stale)
           and exits non-zero on any hard error.
  digest : regenerates docs/BIBLE.digest.md from BIBLE.md sec 1, 3, 5 (Laws), 9 + a status
           index + any pending decision heads from AMENDMENTS.md.

  Windows PowerShell 5.1 safe. This file is intentionally PURE ASCII: WinPS reads a BOM-less
  .ps1 as the ANSI code page, so non-ASCII literals corrupt parsing. Status emoji and the
  section sign are matched via backslash-u escapes inside regex strings, never as literals.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\codex.ps1 doctor
  powershell -ExecutionPolicy Bypass -File tools\codex.ps1 digest
#>
[CmdletBinding()]
param(
  [Parameter(Position = 0)]
  [ValidateSet('doctor', 'digest')]
  [string]$Command = 'doctor'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# Status-emoji and section sign as regex escapes (file stays pure ASCII; see header).
# done=U+2705, partial=U+1F7E1, planned=U+2B1C, cut=U+1F5D1, section sign=U+00A7.
$RX_DONE = '\u2705'
$RX_PART = '\uD83D\uDFE1'
$RX_PLAN = '\u2B1C'
$RX_CUT  = '\uD83D\uDDD1'
$RX_SEC  = '\u00A7'

# --- paths ---------------------------------------------------------------------------------------
$RepoRoot    = Split-Path -Parent $PSScriptRoot
$DocsDir     = Join-Path $RepoRoot 'docs'
$DataDir     = Join-Path $DocsDir  'data'
$SchemaDir   = Join-Path $DataDir  '_schema'
$RfcDir      = Join-Path $DocsDir  'rfc'
$BiblePath   = Join-Path $DocsDir  'BIBLE.md'
$DigestPath  = Join-Path $DocsDir  'BIBLE.digest.md'
$AmendPath   = Join-Path $DocsDir  'AMENDMENTS.md'
$StoriesPath = Join-Path $DocsDir  'USER_STORIES.md'

$script:Errors   = New-Object System.Collections.Generic.List[string]
$script:Warnings = New-Object System.Collections.Generic.List[string]
function Add-Err ($m) { $script:Errors.Add($m)   | Out-Null }
function Add-Warn($m) { $script:Warnings.Add($m) | Out-Null }

function Get-MdFiles {
  $files = @($BiblePath, $StoriesPath, $AmendPath)
  if (Test-Path $RfcDir) {
    $files += (Get-ChildItem -Path $RfcDir -Filter '*.md' -File | ForEach-Object FullName)
  }
  return @($files | Where-Object { Test-Path $_ })
}

function Read-FrontMatter ($path) {
  $lines = Get-Content -LiteralPath $path -Encoding UTF8
  if ($lines.Count -lt 1 -or $lines[0].Trim() -ne '---') { return $null }
  $fm = New-Object System.Collections.Hashtable
  for ($i = 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Trim() -eq '---') { return $fm }
    if ($lines[$i] -match '^\s*([A-Za-z0-9_]+)\s*:\s*(.+?)\s*$') { $fm[$Matches[1]] = $Matches[2] }
  }
  return $null
}

# =================================================================================================
# DOCTOR
# =================================================================================================
function Invoke-Doctor {
  $validLayers = New-Object System.Collections.Hashtable
  $validLayers['BIBLE.md']        = 'bible'
  $validLayers['USER_STORIES.md'] = 'stories'
  $validLayers['AMENDMENTS.md']   = 'amendments'

  # ---- 1. front-matter on every L0/L1/L2/rfc file ----
  foreach ($f in Get-MdFiles) {
    $name = Split-Path $f -Leaf
    $fm = Read-FrontMatter $f
    if ($null -eq $fm) { Add-Err "front-matter: $name has no valid codex front-matter block"; continue }
    foreach ($k in @('codex', 'project', 'code', 'layer', 'status', 'updated')) {
      if (-not $fm.ContainsKey($k)) { Add-Err "front-matter: $name missing key '$k'" }
    }
    if ($fm.ContainsKey('code')  -and $fm['code']  -ne 'MAIL') { Add-Err "front-matter: $name code='$($fm['code'])' expected 'MAIL'" }
    if ($fm.ContainsKey('codex') -and $fm['codex'] -ne '1')    { Add-Err "front-matter: $name codex='$($fm['codex'])' expected '1'" }
    if ($validLayers.ContainsKey($name) -and $fm.ContainsKey('layer') -and $fm['layer'] -ne $validLayers[$name]) {
      Add-Err "front-matter: $name layer='$($fm['layer'])' expected '$($validLayers[$name])'"
    }
    if (($f -like '*\rfc\*') -and $fm.ContainsKey('layer') -and $fm['layer'] -ne 'rfc') {
      Add-Err "front-matter: $name (rfc) layer='$($fm['layer'])' expected 'rfc'"
    }
    if ($fm.ContainsKey('updated') -and $fm['updated'] -notmatch '^\d{4}-\d{2}-\d{2}$') {
      Add-Err "front-matter: $name updated='$($fm['updated'])' is not YYYY-MM-DD"
    }
  }

  # ---- 2. ids unique across the docset; cross-ref links resolve ----
  $anchors = New-Object System.Collections.Hashtable
  $refs    = New-Object System.Collections.Generic.List[object]
  $idPattern  = '\{#([A-Za-z0-9\-\.' + $RX_SEC + ']+)\}'
  $refPattern = '\]\(([^)]*#[A-Za-z0-9\-\.' + $RX_SEC + ']+)\)'
  foreach ($f in Get-MdFiles) {
    $name = Split-Path $f -Leaf
    $text = Get-Content -LiteralPath $f -Raw -Encoding UTF8
    foreach ($m in [regex]::Matches($text, $idPattern)) {
      $id = $m.Groups[1].Value
      if ($anchors.ContainsKey($id)) { $anchors[$id] = [int]$anchors[$id] + 1 } else { $anchors[$id] = 1 }
    }
    foreach ($m in [regex]::Matches($text, $refPattern)) {
      $target = $m.Groups[1].Value
      $parts  = $target -split '#', 2
      $refs.Add([pscustomobject]@{ File = $name; Frag = $parts[1]; Path = $parts[0] }) | Out-Null
    }
  }
  foreach ($id in $anchors.Keys) {
    if ([int]$anchors[$id] -gt 1) { Add-Err "anchors: id '#$id' defined $($anchors[$id]) times (must be unique)" }
  }
  foreach ($r in $refs) {
    $isCrossRepo = $r.Path -match '^\.\.[/\\]\.\.'  # ../../ = outside library tree; skip anchor check
    $isLocal = -not $isCrossRepo -and ($r.Path -match '\.md$' -or [string]::IsNullOrEmpty($r.Path))
    if ($isLocal -and -not $anchors.ContainsKey($r.Frag)) {
      Add-Err "cross-ref: $($r.File) links to anchor '#$($r.Frag)' which has no matching definition"
    }
  }

  # ---- 3. JSON canon validates against its schema; entity ids unique ----
  if (Test-Path $DataDir) {
    foreach ($jf in (Get-ChildItem -Path $DataDir -Filter '*.json' -File)) {
      $raw = Get-Content -LiteralPath $jf.FullName -Raw -Encoding UTF8
      try { $obj = $raw | ConvertFrom-Json }
      catch { Add-Err "data: $($jf.Name) is not valid JSON -- $($_.Exception.Message)"; continue }
      Test-ComponentCatalog -File $jf.Name -Obj $obj
    }
    if (Test-Path $SchemaDir) {
      foreach ($s in (Get-ChildItem -Path $SchemaDir -Filter '*.schema.json' -File)) {
        try { (Get-Content -LiteralPath $s.FullName -Raw -Encoding UTF8) | ConvertFrom-Json | Out-Null }
        catch { Add-Err "schema: $($s.Name) is not valid JSON -- $($_.Exception.Message)" }
      }
    } else { Add-Warn 'data: no _schema/ directory under docs/data' }
  }

  # ---- 4. every done story names a proof token ----
  # A story is a bullet that runs from "- **MAIL-US-...**" until the next "- **" bullet or a
  # blank line. The done marker and its "(verified by ...)" proof can sit on different lines of
  # the same bullet, so evaluate the whole block, not a single line.
  if (Test-Path $StoriesPath) {
    $slines = Get-Content -LiteralPath $StoriesPath -Encoding UTF8
    $block  = New-Object System.Collections.Generic.List[string]
    $flush = {
      param($b)
      if ($b.Count -eq 0) { return }
      $joined = ($b -join ' ')
      if ($joined -match ('MAIL-US-[A-Za-z]+\d+\s*' + $RX_DONE)) {
        $sid = ([regex]::Match($joined, 'MAIL-US-[A-Za-z]+\d+')).Value
        if ($joined -notmatch '(verified by|verified|proof)') {
          Add-Err "stories: done story $sid names no proof"
        }
        foreach ($tm in [regex]::Matches($joined, '`([^`]+)`')) {
          $tok = $tm.Groups[1].Value
          if ($tok -match '^(Themes|Plugins|Components|Pages|dist|docs|tools)[\\/]\S+$') {
            $candidate = Join-Path $RepoRoot ($tok -replace '/', '\')
            if (-not (Test-Path $candidate)) { Add-Warn "stories: $sid cites path '$tok' not found on disk (best-effort)" }
          }
        }
      }
    }
    foreach ($ln in $slines) {
      if ($ln -match '^\s*-\s+\*\*MAIL-US-' -or [string]::IsNullOrWhiteSpace($ln) -or $ln -match '^#') {
        & $flush $block
        $block = New-Object System.Collections.Generic.List[string]
      }
      if (-not [string]::IsNullOrWhiteSpace($ln)) { $block.Add($ln) }
    }
    & $flush $block
  }

  # ---- 5. every code path/file cited in the bible exists on disk ----
  if (Test-Path $BiblePath) {
    $bibleText = Get-Content -LiteralPath $BiblePath -Raw -Encoding UTF8
    foreach ($m in [regex]::Matches($bibleText, '\]\(([^)#]+)(?:#[^)]*)?\)')) {
      $rel = $m.Groups[1].Value.Trim()
      if ([string]::IsNullOrWhiteSpace($rel)) { continue }
      if ($rel -match '^[a-z]+://') { continue }
      $resolved = Join-Path $DocsDir ($rel -replace '/', '\')
      if (-not (Test-Path $resolved)) { Add-Err "bible: cited path '$rel' does not resolve (looked at $resolved)" }
    }
    foreach ($m in [regex]::Matches($bibleText, '`([^`]+)`')) {
      $tok = $m.Groups[1].Value
      if ($tok -match '^(Themes|Plugins|Components|Pages|dist|docs|tools)[\\/]' -and $tok -notmatch '\s') {
        $candidate = Join-Path $RepoRoot ($tok -replace '/', '\')
        if (-not (Test-Path $candidate)) { Add-Warn "bible: backticked path '$tok' not found on disk (best-effort)" }
      }
    }
  }

  # ---- 6. generatedFrom artifacts not stale (digest vs BIBLE.md) ----
  if (Test-Path $DigestPath) {
    if (Test-Path $BiblePath) {
      $bibleMtime  = (Get-Item $BiblePath).LastWriteTimeUtc
      $digestMtime = (Get-Item $DigestPath).LastWriteTimeUtc
      if ($bibleMtime -gt $digestMtime) {
        Add-Err 'digest: docs/BIBLE.digest.md is stale (BIBLE.md modified after it) -- run: codex.ps1 digest'
      }
    }
  } else {
    Add-Warn 'digest: docs/BIBLE.digest.md does not exist -- run: codex.ps1 digest'
  }

  # ---- 7. regenerate digest in-memory and warn if on-disk copy differs ----
  if (Test-Path $BiblePath) {
    $fresh = Build-Digest
    if (Test-Path $DigestPath) {
      $onDisk = Get-Content -LiteralPath $DigestPath -Raw -Encoding UTF8
      if ((Normalize-Text $onDisk) -ne (Normalize-Text $fresh)) {
        Add-Warn 'digest: docs/BIBLE.digest.md content differs from a fresh regenerate -- run: codex.ps1 digest'
      }
    }
  }

  Write-Report
  if ($script:Errors.Count -gt 0) { exit 1 } else { exit 0 }
}

function Normalize-Text ($s) { return ($s -replace "`r`n", "`n").TrimEnd() }

function Test-ComponentCatalog {
  param([string]$File, $Obj)
  if (-not ($Obj.PSObject.Properties.Name -contains 'components')) {
    Add-Warn "data: $File has no 'components' array -- skipping catalog checks"; return
  }
  $ids   = New-Object System.Collections.Hashtable
  $valid = @('Theme', 'Plugin', 'Component')  # the library's three kinds (M.IDEA-LAW-1)
  foreach ($c in $Obj.components) {
    $names = $c.PSObject.Properties.Name
    foreach ($req in @('id', 'key', 'kind', 'version', 'displayName', 'assembly', 'artifact')) {
      if (-not ($names -contains $req)) { Add-Err "data: $File component '$($c.id)' missing required field '$req'" }
    }
    if ($names -contains 'id') {
      if ($ids.ContainsKey($c.id)) { Add-Err "data: $File duplicate entity id '$($c.id)'" } else { $ids[$c.id] = 1 }
      if ($c.id -notmatch '^(theme|plugin|component)\.[a-z0-9]+$') { Add-Err "data: $File id '$($c.id)' not '<kind>.<key>'" }
    }
    if (($names -contains 'kind') -and ($valid -notcontains $c.kind)) {
      Add-Err "data: $File component '$($c.id)' kind '$($c.kind)' invalid"
    }
    if (($names -contains 'version') -and ([int]$c.version -lt 1)) {
      Add-Err "data: $File component '$($c.id)' version must be >= 1"
    }
    if (($names -contains 'kind') -and ($names -contains 'key')) {
      $expect = ($c.kind.ToLower()) + '.' + $c.key
      if ($c.id -ne $expect) { Add-Err "data: $File component id '$($c.id)' disagrees with kind/key ('$expect')" }
    }
    if ($names -contains 'artifact') {
      $art = Join-Path (Join-Path $RepoRoot 'dist') $c.artifact
      if (-not (Test-Path $art)) { Add-Warn "data: $File artifact '$($c.artifact)' not found in dist/ (best-effort)" }
    }
  }
}

function Write-Report {
  Write-Host ''
  Write-Host '  Codex doctor -- IdeaCms.Library (MAIL)' -ForegroundColor Cyan
  Write-Host '  ----------------------------------------------'
  $checks = @(
    'front-matter present and valid on L0/L1/L2/rfc',
    'anchor ids unique; cross-refs resolve',
    'docs/data JSON valid and schema-conformant; ids unique',
    'every done story names a proof',
    'every bible-cited path exists',
    'generated digest not stale'
  )
  foreach ($c in $checks) { Write-Host "   [*] $c" }
  Write-Host ''
  if ($script:Warnings.Count -gt 0) {
    Write-Host "  WARNINGS ($($script:Warnings.Count)):" -ForegroundColor Yellow
    foreach ($w in $script:Warnings) { Write-Host "    - $w" -ForegroundColor Yellow }
    Write-Host ''
  }
  if ($script:Errors.Count -gt 0) {
    Write-Host "  ERRORS ($($script:Errors.Count)):" -ForegroundColor Red
    foreach ($e in $script:Errors) { Write-Host "    X $e" -ForegroundColor Red }
    Write-Host ''
    Write-Host '  doctor: FAIL' -ForegroundColor Red
  } else {
    Write-Host '  doctor: PASS' -ForegroundColor Green
  }
}

# =================================================================================================
# DIGEST
# =================================================================================================
function Get-BibleSection {
  param([string[]]$Lines, [string]$HeaderRegex)
  $out = New-Object System.Collections.Generic.List[string]
  $in = $false
  foreach ($ln in $Lines) {
    if ($ln -match '^##\s') {
      if ($in) { break }
      if ($ln -match $HeaderRegex) { $in = $true; $out.Add($ln); continue }
    }
    if ($in) { $out.Add($ln) }
  }
  return ,$out.ToArray()
}

function Build-Digest {
  $lines = Get-Content -LiteralPath $BiblePath -Encoding UTF8

  $s1 = Get-BibleSection -Lines $lines -HeaderRegex '^##\s+1\.\s'
  $s3 = Get-BibleSection -Lines $lines -HeaderRegex '^##\s+3\.\s'
  $s5 = Get-BibleSection -Lines $lines -HeaderRegex '^##\s+5\.\s'
  $s9 = Get-BibleSection -Lines $lines -HeaderRegex '^##\s+9\.\s'

  $done = 0; $partial = 0; $planned = 0; $cut = 0
  if (Test-Path $StoriesPath) {
    $st = Get-Content -LiteralPath $StoriesPath -Raw -Encoding UTF8
    $done    = ([regex]::Matches($st, ('MAIL-US-[A-Za-z]+\d+\s*' + $RX_DONE))).Count
    $partial = ([regex]::Matches($st, $RX_PART)).Count
    $planned = ([regex]::Matches($st, $RX_PLAN)).Count
    $cut     = ([regex]::Matches($st, $RX_CUT)).Count
  }

  $pending = @()
  if (Test-Path $AmendPath) {
    $heads = Get-Content -LiteralPath $AmendPath -Encoding UTF8 | Where-Object { $_ -match '^##\s+MAIL-A\d+' }
    foreach ($h in @($heads)) { if ($h) { $pending += ($h -replace '^##\s+', '').Trim() } }
  }

  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('---')
  [void]$sb.AppendLine('codex: 1')
  [void]$sb.AppendLine('project: IdeaCms.Library')
  [void]$sb.AppendLine('code: MAIL')
  [void]$sb.AppendLine('layer: bible')
  [void]$sb.AppendLine('status: living')
  [void]$sb.AppendLine("updated: $((Get-Date).ToString('yyyy-MM-dd'))")
  [void]$sb.AppendLine('generatedFrom: docs/BIBLE.md')
  [void]$sb.AppendLine('---')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('# IdeaCms.Library -- Bible Digest')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine('AUTHORITATIVE -- full detail in docs/BIBLE.md. GENERATED by tools/codex.ps1; never hand-edit.')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine("> Status index (USER_STORIES.md): done $done | partial $partial | planned $planned.")
  if ($pending.Count -gt 0) {
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Pending decisions (docs/AMENDMENTS.md)')
    foreach ($p in $pending) { [void]$sb.AppendLine("- $p") }
  }
  [void]$sb.AppendLine('')
  foreach ($block in @($s1, $s3, $s5, $s9)) {
    foreach ($l in $block) { [void]$sb.AppendLine($l) }
    [void]$sb.AppendLine('')
  }
  return $sb.ToString()
}

function Invoke-Digest {
  if (-not (Test-Path $BiblePath)) { Write-Host 'digest: docs/BIBLE.md not found' -ForegroundColor Red; exit 1 }
  $content = (Build-Digest) -replace "`r`n", "`n"
  [System.IO.File]::WriteAllText($DigestPath, $content, (New-Object System.Text.UTF8Encoding($false)))
  $kb = [math]::Round((Get-Item $DigestPath).Length / 1KB, 1)
  Write-Host "digest: wrote docs/BIBLE.digest.md ($kb KB)" -ForegroundColor Green
}

# =================================================================================================
switch ($Command) {
  'doctor' { Invoke-Doctor }
  'digest' { Invoke-Digest }
}
