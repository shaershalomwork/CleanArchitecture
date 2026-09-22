param(
    [Parameter(Mandatory)][ValidateSet('test', 'production')][string]$Environment,
    [Parameter(Mandatory)][uri]$Endpoint,
    [Parameter(Mandatory)][string]$SecretDirectory
)
$ErrorActionPreference = 'Stop'
if ($Endpoint.Scheme -ne 'https' -or $Endpoint.UserInfo) { throw 'Provide an HTTPS endpoint without credentials.' }
$secrets = (Resolve-Path -LiteralPath $SecretDirectory).Path
foreach ($name in @('bootstrap-api-key', 'elasticsearch-ca.crt')) {
    if (!(Test-Path -LiteralPath (Join-Path $secrets $name) -PathType Leaf)) { throw "Missing bootstrap file: $name" }
}
$scripts = Join-Path $PSScriptRoot 'elasticsearch'
$retention = if ($Environment -eq 'test') { '14' } else { '30' }
docker run --rm --mount "type=bind,source=$scripts,target=/scripts,readonly" --mount "type=bind,source=$secrets,target=/run/secrets,readonly" -e "ELASTICSEARCH_ENDPOINT=$($Endpoint.AbsoluteUri)" -e "LOG_ENVIRONMENT=$Environment" -e "LOG_RETENTION_DAYS=$retention" node:24.13.0-bookworm-slim node /scripts/bootstrap.mjs
if ($LASTEXITCODE -ne 0) { throw 'Elasticsearch bootstrap failed; ingestion must remain disabled.' }
