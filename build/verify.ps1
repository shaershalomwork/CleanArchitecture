param([string]$Configuration = 'Release', [switch]$DatabaseExamples, [switch]$SourceIntegration)
$ErrorActionPreference = 'Stop'
$originalSourceIntegration = $env:RUN_SOURCE_INTEGRATION_TESTS
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet build --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & "$PSScriptRoot/check-dependencies.ps1"
    $filter = 'TestCategory!=SourceIntegration&TestCategory!=Browser&TestCategory!=Published&TestCategory!=LoggingIntegration'
    dotnet test --no-build --configuration $Configuration --filter $filter
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }
    if ($DatabaseExamples -or $SourceIntegration) {
        if ($SourceIntegration) {
            $env:RUN_SOURCE_INTEGRATION_TESTS = '1'
            $filter = 'TestCategory!=Browser&TestCategory!=Published&TestCategory!=LoggingIntegration'
        }
        dotnet test examples/Databases/Database.Tests/Database.Tests.csproj --configuration $Configuration --filter $filter
        if ($LASTEXITCODE -ne 0) { throw 'Optional database verification failed.' }
    }
} finally {
    $env:RUN_SOURCE_INTEGRATION_TESTS = $originalSourceIntegration
    Pop-Location
}
