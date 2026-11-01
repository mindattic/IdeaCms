# Build the frontpage Data page from mindattic.com/index.htm and write it onto the 'frontpage'
# Page row (BodyHtml / PageCss / PageJs). The REUSABLE ENGINES live in Library .idea widgets the
# page composes by token; the page keeps only its own chrome and CONTENT:
#
#   {{ MindAttic.Ideas.Component.TabBoard alwaysShowTabPage=true }}  tab-board engine  (mindattic-tabs-css + board script)
#   {{ MindAttic.Ideas.Plugin.PinFooter }}    .pin-when-short footer (UiUx PINFOOTER bundle)
#   {{ MindAttic.Ideas.Component.WebSnapshot }}  .web-snapshot viewer (UiUx WEBSNAPSHOT bundle)
#   Theme.Cyberspace + Plugin.AtticFont/OutfitFont/Cyberspace supply theme, fonts, and effects.
#
# Verbatim line ranges copied from index.htm:
#   PageCss  = the page's own <style> (S2 tokens incl. base64 logo/bg, S4 a11y, S5 reset,
#              S6 header, S7 content, S8 logo, S9 footer)            199..647
#   BodyHtml = three widget tokens + the S11 DOM                     2351..2806 + 3302..3338
#              (the inline tab <script> moves to PageJs; the footer's document.write becomes a
#               span filled by PageJs - document.write can't run in a swapped DOM)
#   PageJs   = CONTENT data + converters: BOOK_SYNOPSES/PORTFOLIO_* 2836..2870, URL maps +
#              tabifyPortfolio/tabifyCreative 2997..3119 (verbatim; engine calls resolve to the
#              TabBoard widget), an adapted bootstrap (re-runs after Blazor's prerender->interactive
#              DOM swap), and the S12 IIFEs (shine + a11y)           3341..3588
#
# Idempotent: re-running rewrites the same content. Run after a fresh seed for full fidelity.
[CmdletBinding()]
param(
  [string]$IndexHtm = 'D:\Projects\MindAttic\mindattic.com\index.htm',
  [string]$Conn = 'Server=(localdb)\MSSQLLocalDB;Database=IdeaCms;Trusted_Connection=True'
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$lines = [System.IO.File]::ReadAllLines($IndexHtm, [System.Text.Encoding]::UTF8)
function Slice([int]$from, [int]$to) {   # 1-based inclusive, like the line numbers above
  ($lines[($from - 1)..($to - 1)] -join "`n")
}

# sanity: the anchors this splice depends on (fail loudly if index.htm shifts)
if ($lines[197] -ne '<style>')  { throw "anchor moved: expected <style> at line 198, got '$($lines[197])'" }
if ($lines[647] -ne '</style>') { throw "anchor moved: expected </style> at line 648" }
if ($lines[3280] -notmatch 'function init\(\)') { throw "anchor moved: expected init() at line 3281" }
if ($lines[2835] -notmatch 'var BOOK_SYNOPSES')  { throw "anchor moved: expected BOOK_SYNOPSES at line 2836" }
if ($lines[2996] -notmatch 'var PORTFOLIO_URLS') { throw "anchor moved: expected PORTFOLIO_URLS at line 2997" }

$css = @(
  '/* ============ mindattic.com index.htm - page-own styles, copied verbatim. ============'
  '   Tab-board / pin-footer / web-snapshot css come from the composed .idea widgets;'
  '   theme, fonts, and effects come from the Cyberspace theme .idea. */'
  (Slice 199 647)
) -join "`n"

$tokens = @'
{{ MindAttic.Ideas.Component.TabBoard alwaysShowTabPage=true }}
{{ MindAttic.Ideas.Plugin.PinFooter }}
{{ MindAttic.Ideas.Component.WebSnapshot }}

'@
$body = $tokens + ((Slice 2351 2806) + "`n" + (Slice 3302 3338)).Replace(
  '<span>&copy; <script>document.write(new Date().getFullYear())</script> MindAttic LLC</span>',
  '<span>&copy; <span class="fp-year">2026</span> MindAttic LLC</span>')
if ($body.Contains('<script>document.write')) { throw 'document.write survived the footer adaptation' }

$jsHead = @'
/* ============ frontpage CONTENT script. The board ENGINE is the TabBoard .idea widget; ============
   the converters below are verbatim mindattic.com code whose engine calls bind to it. */
var buildBoardSection, generateProjectArt;   // bound to window.TabBoard inside init()
'@

$bootstrap = @'

    // == CMS ADAPTATION (the only non-verbatim block) ==================================
    // Same flow as index.htm's init(): convert Portfolio links + books grids into boards,
    // then let the TabBoard widget wire everything. Re-runs when Blazor swaps the
    // prerendered DOM for the interactive render (#content's element identity changes).
    function init() {
      if (!window.TabBoard) return false;
      buildBoardSection = window.TabBoard.build;
      generateProjectArt = window.TabBoard.art;
      tabifyPortfolio();
      tabifyCreative();
      window.TabBoard.refresh();
      if (window.WebSnapshot && typeof window.WebSnapshot.autoInit === 'function') {
        window.WebSnapshot.autoInit();
      }
      var year = document.querySelector('.fp-year');
      if (year) year.textContent = String(new Date().getFullYear());
      return true;
    }
    var fpHydratedRoot = null;
    function fpSafeInit() {
      var content = document.getElementById('content');
      if (!content || content === fpHydratedRoot) return;
      if (init()) fpHydratedRoot = content;
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', fpSafeInit);
    else fpSafeInit();
    new MutationObserver(function () { fpSafeInit(); }).observe(document.body, { childList: true, subtree: true });
'@

$js = @(
  $jsHead
  '/* ---- content data (verbatim) ---- */'
  (Slice 2836 2870)
  ''
  '/* ---- per-tile URLs + the tabify converters (verbatim) ---- */'
  (Slice 2997 3119)
  $bootstrap
  ''
  '/* ============ index.htm S12 IIFEs (shine + a11y, verbatim) ============ */'
  (Slice 3341 3588)
) -join "`n"

Write-Host ("assembled: BodyHtml {0:N0} chars, PageCss {1:N0} chars, PageJs {2:N0} chars" -f $body.Length, $css.Length, $js.Length)

Add-Type -AssemblyName System.Data
$cn = New-Object System.Data.SqlClient.SqlConnection($Conn)
$cn.Open()
try {
  $up = $cn.CreateCommand()
  $up.CommandText = "UPDATE Pages SET BodyHtml = @b, PageCss = @c, PageJs = @j, ModifiedUtc = SYSUTCDATETIME() WHERE Slug = 'frontpage'"
  foreach ($p in @(@('@b', $body), @('@c', $css), @('@j', $js))) {
    $param = New-Object System.Data.SqlClient.SqlParameter($p[0], [System.Data.SqlDbType]::NVarChar, -1)
    $param.Value = $p[1]
    $null = $up.Parameters.Add($param)
  }
  $rows = $up.ExecuteNonQuery()
  if ($rows -ne 1) { throw "expected to update 1 row, updated $rows - is the frontpage seeded?" }
  Write-Host 'frontpage row updated.'
}
finally { $cn.Close() }
