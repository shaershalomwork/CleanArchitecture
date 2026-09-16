$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$local = Join-Path $root '.local/logging'
New-Item -ItemType Directory -Force -Path $local | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$passwordFile = Join-Path $local 'elastic-password'
if (!(Test-Path $passwordFile)) {
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    [IO.File]::WriteAllText($passwordFile, [Convert]::ToBase64String($bytes), $utf8)
}
$kibanaFile = Join-Path $local 'kibana.yml'
if (!(Test-Path $kibanaFile)) { [IO.File]::WriteAllText($kibanaFile, "server.name: kibana-local`n", $utf8) }
$scripts = Join-Path $PSScriptRoot 'elasticsearch'
docker run --rm --user 0 --entrypoint bash --mount "type=bind,source=$local,target=/setup" --mount "type=bind,source=$scripts,target=/scripts,readonly" docker.elastic.co/elasticsearch/elasticsearch:9.5.4 /scripts/certificates.sh
if ($LASTEXITCODE -ne 0) { throw 'Local certificate initialization failed.' }
Write-Host 'Local secrets initialized in .local/logging. Run docker compose up --build -d.'
Write-Host 'Trust .local/logging/certs/ca.crt in your browser for local HTTPS. Kibana login: elastic; password in .local/logging/elastic-password.'
