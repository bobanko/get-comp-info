# Uploads a release archive to the cloud. In GitHub Actions the token is the run's OIDC token;
# a local development server (localhost) accepts uploads without a token.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Package,
    [string]$Token,
    [string]$ApiUrl = 'https://comp-info.chagear.top/api/release'
)

$ErrorActionPreference = 'Stop'
$arguments = @('--silent', '--show-error', '--fail-with-body', '-X', 'POST', '-H', 'Content-Type: application/zip', '--data-binary', "@$Package")
if ($Token) { $arguments += @('-H', "Authorization: Bearer $Token") }
$response = & curl.exe @arguments $ApiUrl
if ($LASTEXITCODE -ne 0) { throw "Публикация не удалась: $response" }
Write-Host "Опубликовано: $response" -ForegroundColor Green
