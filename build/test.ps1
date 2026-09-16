param(
    [string[]]$ClientFramework = @('None', 'Angular'),
    [string]$Version = '0.1.0',
    [ValidateSet('SqlServer', 'SQLite', 'Oracle')][string]$CustomerProvider = 'SqlServer',
    [switch]$BrowserTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('artifacts/template-tests/' + [Guid]::NewGuid().ToString('N'))
$hive = Join-Path $run 'hive'
New-Item -ItemType Directory -Force -Path $run | Out-Null
& "$PSScriptRoot/repack.ps1" -Version $Version
dotnet new install "$root/artifacts/template-packages/DataCentric.Integration.Solution.Template.$Version.nupkg" --debug:custom-hive $hive
if ($LASTEXITCODE -ne 0) { throw 'Package installation failed.' }
$secretIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($client in $ClientFramework) {
    $path = Join-Path $run $client
    dotnet new di-sln -cf $client --CustomerProvider $CustomerProvider -n IntegrationSmoke -o $path --debug:custom-hive $hive
    if ($LASTEXITCODE -ne 0) { throw "Generation failed: $client" }
    $settings = Get-Content (Join-Path $path 'src/Web/appsettings.json') -Raw | ConvertFrom-Json
    if ($settings.Sources.CustomerRegistry.Provider -ne $CustomerProvider) { throw 'Generated source provider is incorrect.' }
    [xml]$webProject = Get-Content (Join-Path $path 'src/Web/Web.csproj') -Raw
    $secretId = [string]$webProject.Project.PropertyGroup.UserSecretsId
    if ([string]::IsNullOrWhiteSpace($secretId) -or $secretId.Contains('7a87b1cc-f5d8-4bcf-b960-398d18d386af') -or !$secretIds.Add($secretId)) {
        throw 'Generated applications must have distinct user-secrets IDs.'
    }
    Push-Location $path
    try {
        Push-Location src/Application
        try {
            dotnet new di-usecase -n Probe -fn Diagnostics -ut query -rt NoData --RootNamespace IntegrationSmoke.Application --debug:custom-hive $hive
            if ($LASTEXITCODE -ne 0) { throw 'Query generation failed.' }
            dotnet new di-usecase -n Submit -fn Diagnostics -ut command --RootNamespace IntegrationSmoke.Application --parent-namespace Nested -o Nested --debug:custom-hive $hive
            if ($LASTEXITCODE -ne 0) { throw 'Command generation failed.' }
        } finally { Pop-Location }
        & ./build/verify.ps1
        if ($LASTEXITCODE -ne 0) { throw "Verification failed: $client" }
        dotnet publish src/Web/Web.csproj -c Release -o artifacts/published
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $client" }
        if ($BrowserTests) {
            # Issue short-lived tokens after publishing so dependency installation cannot consume their lifetime.
            $env:TEST_READER_TOKEN = (dotnet user-jwts create --project src/Web --name smoke-reader --claim permissions=customers.read --audience integration-smoke --valid-for 1h --output token)
            if ($LASTEXITCODE -ne 0) { throw 'Local reader token creation failed.' }
            $env:TEST_WRITER_TOKEN = (dotnet user-jwts create --project src/Web --name smoke-writer --claim permissions=customers.write --audience integration-smoke --valid-for 1h --output token)
            if ($LASTEXITCODE -ne 0) { throw 'Local writer token creation failed.' }
            Copy-Item -LiteralPath src/Web/appsettings.Development.json -Destination artifacts/published/appsettings.Development.json
        }
        if ($BrowserTests) {
            if ($client -ne 'None' -and !$env:PLAYWRIGHT_BROWSER_CHANNEL) {
                & ./artifacts/bin/Web.AcceptanceTests/release/playwright.ps1 install chromium --with-deps
                if ($LASTEXITCODE -ne 0) { throw 'Browser installation failed.' }
            }
            $env:ASPNETCORE_ENVIRONMENT = 'Development'
            $env:ASPNETCORE_URLS = 'http://localhost:5187'
            $env:TEST_BASE_URL = 'http://localhost:5187'
            $start = @{ FilePath='dotnet'; ArgumentList=@('IntegrationSmoke.Web.dll'); WorkingDirectory=(Join-Path $path 'artifacts/published'); PassThru=$true; RedirectStandardOutput=(Join-Path $path 'artifacts/server.log'); RedirectStandardError=(Join-Path $path 'artifacts/server-error.log') }
            if ($env:OS -eq 'Windows_NT') { $start.WindowStyle = 'Hidden' }
            $server = Start-Process @start
            try {
                $ready = $false
                for ($attempt=0; $attempt -lt 30; $attempt++) {
                    try { $response=Invoke-WebRequest "$env:TEST_BASE_URL/alive" -UseBasicParsing; if ($response.StatusCode -eq 200) { $ready=$true; break } } catch { Start-Sleep -Seconds 1 }
                }
                if (-not $ready) { throw 'Published application did not become healthy.' }
                dotnet test tests/Application.FunctionalTests --no-build -c Release --filter TestCategory=Published
                if ($LASTEXITCODE -ne 0) { throw "Published API tests failed: $client" }
                if ($client -ne 'None') {
                    Push-Location src/Web/ClientApp
                    try {
                        npm test -- --watch=false --browsers=ChromeHeadless
                        if ($LASTEXITCODE -ne 0) { throw 'Angular unit tests failed.' }
                    } finally { Pop-Location }
                    dotnet test tests/Web.AcceptanceTests --no-build -c Release
                    if ($LASTEXITCODE -ne 0) { throw "Browser tests failed: $client" }
                }
            } finally {
                if (-not $server.HasExited) { Stop-Process -Id $server.Id }
                Remove-Item Env:TEST_BASE_URL -ErrorAction SilentlyContinue
                Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
                Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
            }
        }
    } finally {
        if ($BrowserTests) {
            dotnet user-jwts clear --project src/Web --force | Out-Null
            dotnet user-secrets remove 'Authentication:Schemes:Bearer:SigningKeys' --project src/Web | Out-Null
            Remove-Item Env:TEST_READER_TOKEN -ErrorAction SilentlyContinue
            Remove-Item Env:TEST_WRITER_TOKEN -ErrorAction SilentlyContinue
        }
        Pop-Location
    }
}
