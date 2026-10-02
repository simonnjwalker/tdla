param([int]$Port = 5080)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $projectRoot '.local/tdla.private.json'
if (-not (Test-Path -LiteralPath $configPath)) {
    throw 'Local configuration is missing. Follow docs/WINDOWS-PUBLISH.md to configure MySQL and private settings.'
}
$env:TDLA_CONFIG_FILE = $configPath
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Site__PublicUrl = "http://localhost:$Port"
dotnet run --project (Join-Path $projectRoot 'src/Tdla.Web') --no-launch-profile -- --urls "http://localhost:$Port"
exit $LASTEXITCODE
