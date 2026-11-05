$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$secureTargetUri = Read-Host 'Paste the Supabase Session Pooler URI (port 5432); input is hidden' -AsSecureString
$secureSharedPassword = Read-Host 'Enter the shared password for new agent accounts; input is hidden' -AsSecureString
$targetUriPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureTargetUri)
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSharedPassword)
$previousLocation = Get-Location
try {
    $env:DATABASE_URL = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($targetUriPointer)
    $env:SHAMEEN_SEED_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    Set-Location -LiteralPath $repoRoot
    & dotnet run --configuration Release --project 'backend/Shameen.TeamSeeder/Shameen.TeamSeeder.csproj' -- --city-agents-only
    if ($LASTEXITCODE -ne 0) { throw "The city-agent seeder exited with code $LASTEXITCODE." }
}
finally {
    Remove-Item Env:DATABASE_URL -ErrorAction SilentlyContinue
    Remove-Item Env:SHAMEEN_SEED_PASSWORD -ErrorAction SilentlyContinue
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($targetUriPointer)
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    $secureTargetUri.Dispose()
    $secureSharedPassword.Dispose()
    Set-Location -LiteralPath $previousLocation
}
