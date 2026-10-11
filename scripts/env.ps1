$ErrorActionPreference='Stop'
$ditRepo=Split-Path $PSScriptRoot -Parent
$env:DOTNET_ROOT=Join-Path $ditRepo '.tools/dotnet'
$env:DOTNET_CLI_HOME=Join-Path $ditRepo '.tools/cli'
$env:NUGET_PACKAGES=Join-Path $ditRepo '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$ditDotnet=Join-Path $env:DOTNET_ROOT 'dotnet.exe'
if(-not(Test-Path -LiteralPath $ditDotnet)){throw 'Brak SDK .NET. Uruchom scripts/bootstrap.ps1.'}
$ditFfmpeg=Get-ChildItem (Join-Path $ditRepo '.tools/ffmpeg') -Filter ffmpeg.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if($ditFfmpeg){$env:PATH=$env:DOTNET_ROOT+';'+$ditFfmpeg.DirectoryName+';'+$env:PATH}else{$env:PATH=$env:DOTNET_ROOT+';'+$env:PATH}
