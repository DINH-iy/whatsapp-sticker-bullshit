param([ValidateSet('web','android','ios')][string]$Target = 'web')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$localDotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
if ($Target -eq 'web') { & $dotnetCommand run --project StickerApp.Web --launch-profile http }
elseif ($Target -eq 'android') { & $dotnetCommand build StickerApp\StickerApp.csproj -f net10.0-android -c Debug }
else { & $dotnetCommand build StickerApp/StickerApp.csproj -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64 }
exit $LASTEXITCODE
