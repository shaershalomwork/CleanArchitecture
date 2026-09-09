param([string]$Configuration = 'Release', [switch]$SourceIntegration)
$ErrorActionPreference = 'Stop'
$originalSourceIntegration = $env:RUN_SOURCE_INTEGRATION_TESTS
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet build --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $filter = 'TestCategory!=SourceIntegration&TestCategory!=Browser&TestCategory!=Published'
    if ($SourceIntegration) { $env:RUN_SOURCE_INTEGRATION_TESTS = '1'; $filter = 'TestCategory!=Browser&TestCategory!=Published' }
    dotnet test --no-build --configuration $Configuration --filter $filter
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }
} finally {
    $env:RUN_SOURCE_INTEGRATION_TESTS = $originalSourceIntegration
    Pop-Location
}
