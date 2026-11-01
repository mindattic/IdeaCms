#requires -Version 5.1
# Codex SessionStart hook: inject docs/BIBLE.digest.md as authoritative context.
# Emits Claude Code hook JSON. If the digest is missing/empty, emits {}.
# Windows PowerShell 5.1 / Win-1252 safe: all non-ASCII is escaped to \uXXXX.

$ErrorActionPreference = 'Stop'

function ConvertTo-JsonString([string]$s) {
  $sb = New-Object System.Text.StringBuilder
  foreach ($ch in $s.ToCharArray()) {
    $code = [int]$ch
    switch ($ch) {
      '"'  { [void]$sb.Append('\"') }
      '\'  { [void]$sb.Append('\\') }
      "`b" { [void]$sb.Append('\b') }
      "`f" { [void]$sb.Append('\f') }
      "`n" { [void]$sb.Append('\n') }
      "`r" { [void]$sb.Append('\r') }
      "`t" { [void]$sb.Append('\t') }
      default {
        if ($code -lt 32 -or $code -gt 126) {
          [void]$sb.Append('\u' + $code.ToString('x4'))
        } else {
          [void]$sb.Append($ch)
        }
      }
    }
  }
  return $sb.ToString()
}

try {
  $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
  $digestPath = Join-Path $repoRoot 'docs\BIBLE.digest.md'

  if (-not (Test-Path -LiteralPath $digestPath)) { Write-Output '{}'; exit 0 }
  $digest = Get-Content -LiteralPath $digestPath -Raw -Encoding UTF8
  if ([string]::IsNullOrWhiteSpace($digest)) { Write-Output '{}'; exit 0 }

  $preamble = @"
[IdeaCms Codex] The following digest of docs/BIBLE.md is the AUTHORITATIVE source of truth
for this project (what it IS, is NOT, its Laws, and current status). Treat it as binding context.
Full detail lives in docs/BIBLE.md; stories and their verifying tests are in
docs/USER_STORIES.md. Org-wide laws are inherited from
MindAttic.HouseRules.md. Do not contradict this digest; if a change is needed, amend the canon.

"@

  $payload = $preamble + $digest
  $escaped = ConvertTo-JsonString $payload
  $json = '{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"' + $escaped + '"}}'
  Write-Output $json
  exit 0
}
catch {
  Write-Output '{}'
  exit 0
}
