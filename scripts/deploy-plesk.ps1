param([Parameter(Mandatory = $true)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'

foreach ($key in @('PLESK_SERVER', 'PLESK_SITE', 'PLESK_USERNAME', 'PLESK_PASSWORD', 'TDLA_CONFIG_FILE')) {
    $value = [Environment]::GetEnvironmentVariable($key)
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Missing $key. See docs/WINDOWS-PUBLISH.md." }
    if ($value.Contains("'") -or $value.Contains("`r") -or $value.Contains("`n")) { throw "$key contains characters unsupported by this Web Deploy script." }
}
if (-not $env:PLESK_SERVER.StartsWith('https://')) { throw 'PLESK_SERVER must use HTTPS.' }
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
$configPath = Join-Path $publishPath 'web.config'
[xml]$config = Get-Content -LiteralPath $configPath -Raw
$aspnet = $config.SelectSingleNode('//aspNetCore')
if ($null -eq $aspnet) { throw 'Published web.config does not contain aspNetCore.' }
$variables = $aspnet.SelectSingleNode('environmentVariables')
if ($null -eq $variables) { $variables = $config.CreateElement('environmentVariables'); [void]$aspnet.AppendChild($variables) }
foreach ($entry in @{ TDLA_CONFIG_FILE = $env:TDLA_CONFIG_FILE; ASPNETCORE_ENVIRONMENT = 'Production' }.GetEnumerator()) {
    $node = $variables.SelectSingleNode("environmentVariable[@name='$($entry.Key)']")
    if ($null -eq $node) { $node = $config.CreateElement('environmentVariable'); [void]$variables.AppendChild($node) }
    $node.SetAttribute('name', $entry.Key)
    $node.SetAttribute('value', $entry.Value)
}
$config.Save($configPath)
$msdeploy = Join-Path ${env:ProgramFiles} 'IIS/Microsoft Web Deploy V3/msdeploy.exe'
if (-not (Test-Path -LiteralPath $msdeploy)) { throw 'Install Microsoft Web Deploy 3.6 or later.' }
# Quote provider values and pass an argument array; never evaluate credentials as shell code.
# DoNotDeleteRule preserves any pre-existing server files. Review obsolete files separately.
$arguments = @(
    '-verb:sync',
    "-source:iisApp='$publishPath'",
    "-dest:iisApp='$env:PLESK_SITE',computerName='$env:PLESK_SERVER',userName='$env:PLESK_USERNAME',password='$env:PLESK_PASSWORD',authType='Basic'",
    '-enableRule:AppOffline',
    '-enableRule:DoNotDeleteRule',
    '-retryAttempts:3'
)
& $msdeploy @arguments
if ($LASTEXITCODE -ne 0) { throw "Web Deploy failed (exit $LASTEXITCODE)." }
