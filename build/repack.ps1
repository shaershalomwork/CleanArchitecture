param([ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')][string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$metadata = Join-Path $root '.template.config/template.json'
$original = [IO.File]::ReadAllText($metadata)
try {
    $config = $original | ConvertFrom-Json
    $config.symbols.caPackageVersion.parameters.value = $Version
    [IO.File]::WriteAllText($metadata, ($config | ConvertTo-Json -Depth 30))
    dotnet pack "$root/build/Template.csproj" -p:PackageVersion=$Version -o "$root/artifacts/template-packages"
    if ($LASTEXITCODE -ne 0) { throw 'Template packaging failed.' }
} finally { [IO.File]::WriteAllText($metadata, $original) }
Write-Host "Candidate package is in artifacts/template-packages. Run build/test.ps1 before installing or releasing."
