param([switch]$Outage)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
function Compose {
    & docker compose @args
    if ($LASTEXITCODE -ne 0) { throw 'Compose verification command failed.' }
}
try {
    Compose config --quiet
    Compose run --rm --no-deps --entrypoint node elastic-setup /setup/verify.mjs smoke
    $stdout = & docker compose logs --no-log-prefix web
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read application stdout.' }
    if ($stdout -match 'private-smoke-marker') { throw 'Sensitive marker leaked to Console.' }
    if (!($stdout -match 'HTTP request completed')) { throw 'Application Console logs were not found.' }
    if ($Outage) {
        try {
            Compose stop elasticsearch
            Compose run --rm --no-deps --entrypoint node elastic-setup /setup/verify.mjs emit
            # Give the SDK time to hand its records to the persistent Collector queue.
            Start-Sleep -Seconds 5
            Compose kill -s SIGKILL otel-collector
            Compose up -d --no-deps otel-collector
        } finally { Compose start elasticsearch }
        Compose run --rm --no-deps --entrypoint node elastic-setup /setup/verify.mjs check
    }
    Write-Host 'Logging verification passed. No volumes or credentials were removed.'
} finally { Pop-Location }
