<#
.SYNOPSIS
    Provisions the Azure estate for IdeaCms — the company site and the public demo — and
    wires up everything Bicep cannot.

.DESCRIPTION
    Phases:

      1. Deploy infra/main.bicep — one plan, two sites (company + demo), SQL, Storage, two Key Vaults,
         managed identities, RBAC.
      2. Seed secrets. The company vault gets the auth Security bucket; the demo vault gets its OWN
         pepper/keys. Values are generated with a CSPRNG, never printed, never written to disk.
      3. Create the SQL contained users, by object id (exact, not a display-name lookup):
           company site  -> MindAtticIdeas              datareader + datawriter
           demo site     -> MindAtticIdeasDemoTemplate  datareader + datawriter (copied hourly)
           CI principal  -> both                        ddladmin + datareader + datawriter
      4. Apply the schema to the demo template (the company database is migrated by CI).
      5. Restart the company site; trigger the demo reset, which makes the demo's first database.

    Safe to re-run: Bicep is declarative, secrets are only generated when absent, and SQL users are
    created behind existence checks.

.PARAMETER ResourceGroup
    Resource group to deploy into. Created if it does not exist.

.PARAMETER TurnstileSiteKey
    Public Cloudflare Turnstile site key for the demo-login reveal. Omit to keep the reveal off. The
    matching secret key goes in the company vault as `turnstile-secret` (see docs/DEPLOYMENT.md).

.PARAMETER WhatIf
    Show what the Bicep deployment would change, then stop without touching anything.

.EXAMPLE
    ./infra/provision.ps1 -ResourceGroup rg-mindattic-ideas -WhatIf
    ./infra/provision.ps1 -ResourceGroup rg-mindattic-ideas -TurnstileSiteKey 0x4AAAA...
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string] $ResourceGroup,
    [string] $Location = 'centralus',
    [string] $SiteAppName = 'mindattic',
    [string] $DemoAppName = 'mindattic-ideas-demo',
    [string] $CiAppDisplayName = 'gh-mindattic-ideas-deploy',
    [string] $TurnstileSiteKey = '',
    [ValidateSet('B1', 'B2', 'S1', 'P0v3')][string] $AppServicePlanSku = 'B1',
    [string] $SqlDatabaseSku = 'Basic'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$bicep = Join-Path $PSScriptRoot 'main.bicep'

function Write-Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }

# az writes warnings and "not found" messages to stderr. Under $ErrorActionPreference='Stop',
# PowerShell 5.1 wraps every native stderr line in an ErrorRecord and turns it into a TERMINATING
# error -- even when az exited 0. So never judge az by stderr; judge it by its exit code.
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

# Same, but a non-zero exit is an answer rather than a failure (existence probes).
function Test-Az {
    $arguments = $args
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & az @arguments 2>&1
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }

    if ($code -ne 0) { return $null }
    ($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join ''
}

function New-RandomBase64([int] $bytes) {
    # RandomNumberGenerator.Fill is .NET Core only; Create()/GetBytes works on 5.1 and 7 alike.
    $buffer = New-Object byte[] $bytes
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    [Convert]::ToBase64String($buffer)
}

# Seeds the named secrets into a vault, generating each one only if absent.
function Initialize-VaultSecrets([string] $vault, [array] $secrets) {
    $existing = @(Invoke-Az keyvault secret list --vault-name $vault --query '[].name' -o tsv)
    foreach ($secret in $secrets) {
        if ($existing -contains $secret.Name) { Write-Host "  = $vault/$($secret.Name) already present, left alone"; continue }
        $value = & $secret.Generate
        if (-not $value) { Write-Warning "  ! $vault/$($secret.Name): no value available, skipped"; continue }
        Invoke-Az keyvault secret set --vault-name $vault --name $secret.Name --value $value --output none | Out-Null
        Write-Host "  + $vault/$($secret.Name) set"
        $value = $null
    }
}

# The package-signing trust cert is the PUBLIC half of the publisher's signing cert, copied from this
# machine's Vault PackageSigning bucket. Without it every .idea install fails closed (M.IDEA-§4.8).
function Get-PublicSigningCert {
    $signingProviders = Join-Path $env:APPDATA 'MindAttic\PackageSigning\providers.json'
    if (-not (Test-Path $signingProviders)) { return $null }
    (Get-Content $signingProviders -Raw | ConvertFrom-Json).'signing-cert-public'
}

# --- Preflight -------------------------------------------------------------------------------

Write-Step 'Preflight'
if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw 'Azure CLI (az) is not on PATH.' }

$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) { throw 'Not logged in. Run: az login' }
Write-Host "Subscription : $($account.name) ($($account.id))"
Write-Host "Signed in as : $($account.user.name)"

$signedInId = Invoke-Az ad signed-in-user show --query id -o tsv
if (-not $signedInId) { throw 'Could not resolve the signed-in user object id.' }
$signedInUpn = Test-Az ad signed-in-user show --query userPrincipalName -o tsv
if (-not $signedInUpn) { $signedInUpn = $account.user.name }
Write-Host "Object id    : $signedInId"

$ciAppId = Invoke-Az ad app list --display-name $CiAppDisplayName --query '[0].appId' -o tsv
if (-not $ciAppId) { throw "No app registration named '$CiAppDisplayName' (the GitHub OIDC principal). See docs/DEPLOYMENT.md." }
$ciObjectId = Invoke-Az ad sp show --id $ciAppId --query id -o tsv
Write-Host "CI principal : $CiAppDisplayName ($ciObjectId)"

# --- Phase 1: infrastructure ------------------------------------------------------------------

Write-Step "Resource group '$ResourceGroup'"
Invoke-Az group create --name $ResourceGroup --location $Location --output none | Out-Null

$deployMode = if ($WhatIfPreference) { 'what-if' } else { 'create' }
$deployArgs = @(
    'deployment', 'group', $deployMode,
    '--resource-group', $ResourceGroup,
    '--template-file', $bicep,
    '--parameters',
    "siteAppName=$SiteAppName",
    "demoAppName=$DemoAppName",
    "location=$Location",
    "appServicePlanSku=$AppServicePlanSku",
    "sqlDatabaseSku=$SqlDatabaseSku",
    "sqlAdminObjectId=$signedInId",
    "sqlAdminLogin=$signedInUpn",
    "ciPrincipalObjectId=$ciObjectId",
    "turnstileSiteKey=$TurnstileSiteKey"
)

if ($WhatIfPreference) {
    Write-Step 'What-if (no changes will be made)'
    az @deployArgs
    Write-Host "`nWhat-if complete. Re-run without -WhatIf to apply." -ForegroundColor Yellow
    return
}

Write-Step 'Deploying infrastructure (a few minutes)'
$deployArgs += @('--name', "ideas-$(Get-Date -Format yyyyMMddHHmmss)", '--output', 'json')
$result = az @deployArgs | ConvertFrom-Json
if (-not $result) { throw 'Bicep deployment failed.' }

$out = $result.properties.outputs
$siteName = $out.webAppName.value
$siteHost = $out.webAppHostName.value
$sitePrincipal = $out.webAppPrincipalId.value
$demoName = $out.demoAppName.value
$demoHost = $out.demoAppHostName.value
$demoPrincipal = $out.demoAppPrincipalId.value
$keyVaultName = $out.keyVaultName.value
$demoKeyVaultName = $out.demoKeyVaultName.value
$sqlServerName = $out.sqlServerName.value
$sqlServerFqdn = $out.sqlServerFqdn.value
$sqlDatabase = $out.sqlDatabaseName.value
$templateDatabase = $out.demoTemplateDatabaseName.value

Write-Host "Company site : https://$siteHost"
Write-Host "Demo site    : https://$demoHost"
Write-Host "SQL          : $sqlServerFqdn ($sqlDatabase, $templateDatabase)"

# --- Phase 2: secrets ---------------------------------------------------------------------------

# MindAttic.Authentication fail-closes without these (ConfigAuthSecrets.GetRequired). Key Vault secret
# names allow only alphanumerics and hyphens, so 'pepper.v1' is stored as 'pepper-v1'.
$authSecrets = @(
    @{ Name = 'pepper-v1';       Generate = { New-RandomBase64 32 } }
    @{ Name = 'reset-token-key'; Generate = { New-RandomBase64 32 } }
    @{ Name = 'dp-kek';          Generate = { New-RandomBase64 32 } }
    @{ Name = 'signing-cert-public'; Generate = { Get-PublicSigningCert } }
)

Write-Step 'Seeding the company vault'
Initialize-VaultSecrets $keyVaultName ($authSecrets + @(
    # Typed by a human once, at first sign-in, then rotated. Padding stripped so it reads as a string.
    @{ Name = 'bootstrap-token'; Generate = { (New-RandomBase64 24) -replace '[+/=]', '' } }
))

Write-Step 'Seeding the demo vault (its own pepper and keys; the admin password is rotated by the reset)'
Initialize-VaultSecrets $demoKeyVaultName $authSecrets

# --- Phase 3: SQL contained users ----------------------------------------------------------------

function Get-UserSql([string] $name, [string] $objectId, [string[]] $roles) {
    $grants = ($roles | ForEach-Object { "ALTER ROLE $_ ADD MEMBER [$name];" }) -join "`n"
    @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$name')
BEGIN
    CREATE USER [$name] FROM EXTERNAL PROVIDER WITH OBJECT_ID = '$objectId';
END;
$grants
"@
}

$reader = @('db_datareader', 'db_datawriter')
$migrator = @('db_ddladmin', 'db_datareader', 'db_datawriter')

$myIp = (Invoke-RestMethod -Uri 'https://api.ipify.org?format=json').ip
Write-Step "SQL contained users (temporary firewall rule for $myIp)"
Invoke-Az sql server firewall-rule create --resource-group $ResourceGroup --server $sqlServerName `
    --name 'provision-script' --start-ip-address $myIp --end-ip-address $myIp --output none | Out-Null
try {
    if (-not (Get-Module -ListAvailable -Name SqlServer)) {
        Write-Host 'Installing the SqlServer PowerShell module (current user)...'
        Install-Module SqlServer -Scope CurrentUser -Force -AllowClobber
    }
    Import-Module SqlServer
    $token = Invoke-Az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv

    Invoke-Sqlcmd -ServerInstance $sqlServerFqdn -Database $sqlDatabase -AccessToken $token -Query (
        (Get-UserSql $siteName $sitePrincipal $reader) + "`n" + (Get-UserSql $CiAppDisplayName $ciObjectId $migrator))
    Write-Host "  $sqlDatabase : [$siteName] read/write, [$CiAppDisplayName] migrate"

    # The demo identity exists ONLY in the template, so it exists only in the demo's copies of it.
    Invoke-Sqlcmd -ServerInstance $sqlServerFqdn -Database $templateDatabase -AccessToken $token -Query (
        (Get-UserSql $demoName $demoPrincipal $reader) + "`n" + (Get-UserSql $CiAppDisplayName $ciObjectId $migrator))
    Write-Host "  $templateDatabase : [$demoName] read/write, [$CiAppDisplayName] migrate"
}
finally {
    Test-Az sql server firewall-rule delete --resource-group $ResourceGroup --server $sqlServerName `
        --name 'provision-script' --output none | Out-Null
}

# --- Phase 4: the demo template's schema ---------------------------------------------------------

Write-Step "Applying the schema to $templateDatabase"
& (Join-Path $PSScriptRoot 'migrate.ps1') -ResourceGroup $ResourceGroup -SqlServer $sqlServerFqdn -Database $templateDatabase

# --- Phase 5: bring both up -----------------------------------------------------------------------

Write-Step 'Restarting the company site'
Invoke-Az webapp restart --resource-group $ResourceGroup --name $siteName --output none | Out-Null

Write-Step 'Resetting the demo (creates its database, password and login)'
$dispatched = $false
if (Get-Command gh -ErrorAction SilentlyContinue) {
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { & gh workflow run demo-reset.yml --repo mindattic/IdeaCms --ref master 2>&1 | Out-Null; $dispatched = $LASTEXITCODE -eq 0 }
    finally { $ErrorActionPreference = $previous }
}
if ($dispatched) { Write-Host 'Dispatched demo-reset.yml — watch it with: gh run watch --repo mindattic/IdeaCms' }
else { Write-Warning 'Could not dispatch demo-reset.yml (not pushed yet, or no gh). The next deploy runs it; or dispatch it from Actions.' }

Write-Step 'Provisioned'
Write-Host @"
Company site : https://$siteHost   (deployed by azure-deploy.yml on every push to master)
Demo site    : https://$demoHost   (wiped and re-provisioned hourly by demo-reset.yml)

First sign-in to the company site, if its database has no users yet:
    az keyvault secret show --vault-name $keyVaultName --name bootstrap-token --query value -o tsv
You will be forced to change it. ROTATE the Key Vault secret afterwards.

Full runbook: docs/DEPLOYMENT.md
"@ -ForegroundColor Green
