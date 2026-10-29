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
$targetUriPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureTargetUri)
$previousLocation = Get-Location

try {
    $env:SHAMEEN_SQLSERVER_CONNECTION = $sourceConnection
    $env:DATABASE_URL = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($targetUriPointer)
    Set-Location -LiteralPath $repoRoot
    dotnet run --project backend/Shameen.DataMigration/Shameen.DataMigration.csproj
    if ($LASTEXITCODE -ne 0) {
        throw "The import tool exited with code $LASTEXITCODE. No import was committed unless it reported success."
    }
}
finally {
    Remove-Item Env:SHAMEEN_SQLSERVER_CONNECTION -ErrorAction SilentlyContinue
    Remove-Item Env:DATABASE_URL -ErrorAction SilentlyContinue
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($targetUriPointer)
    $secureTargetUri.Dispose()
    Set-Location -LiteralPath $previousLocation
}
