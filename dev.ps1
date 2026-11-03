param([ValidateSet('web','android')][string]$Target = 'web')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$localDotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
if ($Target -eq 'web') { & $dotnetCommand run --project StickerApp.Web --launch-profile http }
else { & $dotnetCommand build StickerApp\StickerApp.csproj -c Debug }
exit $LASTEXITCODE
