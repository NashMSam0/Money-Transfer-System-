param([switch]$SupervisorsOnly)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $repoRoot 'backend/Shameen.Api/appsettings.Development.json'
if (-not (Test-Path -LiteralPath $settingsPath)) {
    throw "Local database settings were not found at $settingsPath. Keep the local appsettings.Development.json file in place."
}

$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$sourceConnection = $settings.ConnectionStrings.Transfers
if ([string]::IsNullOrWhiteSpace($sourceConnection)) {
    throw 'ConnectionStrings:Transfers is missing from the local development settings.'
}

$secureTargetUri = Read-Host 'Paste the Supabase Session Pooler URI (port 5432); input is hidden' -AsSecureString
$secureSharedPassword = Read-Host 'Enter the password for the recreated team accounts; input is hidden' -AsSecureString
$targetUriPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureTargetUri)
$sharedPasswordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSharedPassword)
$previousLocation = Get-Location

try {
    $env:SHAMEEN_SQLSERVER_CONNECTION = $sourceConnection
    $env:DATABASE_URL = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($targetUriPointer)
    $env:SHAMEEN_SEED_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($sharedPasswordPointer)
    Set-Location -LiteralPath $repoRoot
    $dotnetArguments = @('run', '--project', 'backend/Shameen.TeamSeeder/Shameen.TeamSeeder.csproj')
    if ($SupervisorsOnly) { $dotnetArguments += @('--', '--supervisors-only') }
    & dotnet @dotnetArguments
    if ($LASTEXITCODE -ne 0) {
        throw "The seeder exited with code $LASTEXITCODE. No write was committed unless it reported success."
    }
}
finally {
    Remove-Item Env:SHAMEEN_SQLSERVER_CONNECTION -ErrorAction SilentlyContinue
    Remove-Item Env:DATABASE_URL -ErrorAction SilentlyContinue
    Remove-Item Env:SHAMEEN_SEED_PASSWORD -ErrorAction SilentlyContinue
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($targetUriPointer)
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($sharedPasswordPointer)
    $secureTargetUri.Dispose()
    $secureSharedPassword.Dispose()
    Set-Location -LiteralPath $previousLocation
}
