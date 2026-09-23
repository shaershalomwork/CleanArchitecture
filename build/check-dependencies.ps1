# Run after restore/build. Check the restored graph, including transitive test dependencies.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$properties = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$selected = [string]$properties.Project.PropertyGroup.CustomerProvider
$expected = switch ($selected) {
    'SqlServer' { 'Microsoft.Data.SqlClient' }
    'SQLite' { 'Microsoft.Data.Sqlite' }
    'Oracle' { 'Oracle.ManagedDataAccess.Core' }
    default { '' }
}
$drivers = @('Microsoft.Data.SqlClient', 'Microsoft.Data.Sqlite', 'Oracle.ManagedDataAccess.Core')
$solution = Get-ChildItem -LiteralPath $root -Filter *.slnx | Select-Object -First 1
[xml]$projects = Get-Content -LiteralPath $solution.FullName -Raw
foreach ($entry in $projects.SelectNodes('//Project')) {
    $name = [IO.Path]::GetFileNameWithoutExtension([string]$entry.Path)
    $assetsPath = Join-Path $root "artifacts/obj/$name/project.assets.json"
    if (!(Test-Path -LiteralPath $assetsPath)) { throw "Missing dependency graph: $assetsPath" }
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $packages = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ } | ForEach-Object { $_.Split('/')[0] })
    foreach ($driver in $drivers) {
        if ($driver -ne $expected -and $packages -contains $driver) { throw "Unexpected database dependency $driver in $name." }
    }
    if (!$expected -and $packages -contains 'Dapper') { throw "Unexpected Dapper dependency in $name." }
    if ($name -eq 'Web' -and $expected -and $packages -notcontains $expected) { throw "Selected provider $selected is missing from Web." }
}
Write-Host 'Default solution dependency graph matches the selected provider.'
