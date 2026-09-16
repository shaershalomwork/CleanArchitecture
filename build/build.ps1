param([string]$Target = 'Basic', [string]$Configuration = 'Release')
& "$PSScriptRoot/verify.ps1" -Configuration $Configuration -SourceIntegration:($Target -eq 'SourceIntegration')
