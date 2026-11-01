<#
.SYNOPSIS
    Applies the EF Core schema to an Azure SQL database.

.DESCRIPTION
    The running site never issues DDL — Program.cs calls MigrateAsync in Development only, and the
    app's SQL identity holds db_datareader/db_datawriter and nothing more. Schema changes come from
    here (or from the equivalent job in .github/workflows/azure-deploy.yml).

    Generates an *idempotent* script, so every migration is guarded by its own
    __EFMigrationsHistory check and re-running is a no-op. Authenticates with an Entra access token,
    so no SQL password exists to leak, and opens a single-IP firewall rule that is always removed.

.PARAMETER ResourceGroup
    Resource group holding the SQL server. Needed to manage the temporary firewall rule.

.PARAMETER SqlServer
    Fully-qualified server name, e.g. sql-mindatticideas-abc123.database.windows.net.

.PARAMETER Database
    Database name. Defaults to MindAtticIdeas.

.PARAMETER ScriptOnly
    Generate the SQL and print where it landed, without connecting to anything.

.EXAMPLE
    ./infra/migrate.ps1 -ResourceGroup rg-mindattic-ideas -SqlServer sql-x.database.windows.net
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ResourceGroup,
    [Parameter(Mandatory)][string] $SqlServer,
    [string] $Database = 'MindAtticIdeas',
    [switch] $ScriptOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$migrationsProject = Join-Path $repoRoot 'src/IdeaCms.Core'
$scriptPath = Join-Path ([IO.Path]::GetTempPath()) "ideas-migrate-$(Get-Date -Format yyyyMMddHHmmss).sql"

function Write-Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }

# See infra/provision.ps1: under $ErrorActionPreference='Stop', PowerShell 5.1 turns any native
# stderr line into a terminating error even when az exited 0. Judge az by its exit code only.
function Invoke-Az {
    $arguments = $args
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & az @arguments 2>&1
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }

    if ($code -ne 0) {
        $text = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        throw "az $($arguments -join ' ') failed (exit $code):`n$text"
    }
    $output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }
}

Write-Step 'Generating the idempotent migration script'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet is not on PATH.' }

# The tools version must track the EF Core runtime the project references, or the generated script
# can miss features the model uses.
$efInstalled = dotnet tool list --global 2>$null | Select-String -SimpleMatch 'dotnet-ef'
if (-not $efInstalled) {
    Write-Host 'Installing dotnet-ef globally...'
    dotnet tool install --global dotnet-ef
}

dotnet ef migrations script --idempotent `
    --project $migrationsProject `
    --startup-project $migrationsProject `
    --configuration Release `
    --output $scriptPath
if ($LASTEXITCODE -ne 0) { throw 'dotnet ef migrations script failed.' }

$size = [math]::Round((Get-Item $scriptPath).Length / 1KB, 1)
Write-Host "Wrote $scriptPath ($size KB)."

if ($ScriptOnly) {
    Write-Host "`n-ScriptOnly: nothing was applied." -ForegroundColor Yellow
    return
}

Write-Step 'Applying to Azure SQL'
$serverShortName = $SqlServer.Split('.')[0]
$myIp = (Invoke-RestMethod -Uri 'https://api.ipify.org?format=json').ip
$ruleName = "migrate-$([Guid]::NewGuid().ToString('N').Substring(0,8))"

Write-Host "Opening the SQL firewall for this machine ($myIp) as '$ruleName'..."
Invoke-Az sql server firewall-rule create --resource-group $ResourceGroup --server $serverShortName `
    --name $ruleName --start-ip-address $myIp --end-ip-address $myIp --output none | Out-Null

try {
    if (-not (Get-Module -ListAvailable -Name SqlServer)) {
        Write-Host 'Installing the SqlServer PowerShell module (current user)...'
        Install-Module SqlServer -Scope CurrentUser -Force -AllowClobber
    }
    Import-Module SqlServer

    $token = Invoke-Az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
    if (-not $token) { throw 'Could not acquire a SQL access token. Run: az login' }

    Invoke-Sqlcmd -ServerInstance $SqlServer -Database $Database -AccessToken $token `
        -InputFile $scriptPath -QueryTimeout 300 -ErrorAction Stop

    Write-Host 'Schema applied.' -ForegroundColor Green
}
finally {
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & az sql server firewall-rule delete --resource-group $ResourceGroup --server $serverShortName `
        --name $ruleName --output none 2>&1 | Out-Null
    $ErrorActionPreference = $previous
    Write-Host "Closed '$ruleName'."
    Remove-Item $scriptPath -ErrorAction SilentlyContinue
}
