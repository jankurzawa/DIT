param([switch]$NoBuild)
. "$PSScriptRoot/env.ps1"
Push-Location $ditRepo
try {
    if(-not $NoBuild){& $ditDotnet build DIT.sln -m:1;if($LASTEXITCODE-ne 0){throw 'Kompilacja nie powiodła się.'}}
    $ditData=Join-Path $env:LOCALAPPDATA 'DIT'
    $ditWorker=Join-Path $ditRepo 'src/Dit.Worker/bin/Debug/net10.0/Dit.Worker.exe'
    $ditArgs=@('--data',('"'+$ditData+'"'),'--sevenzip',('"'+(Join-Path $ditRepo '.tools/7zr.exe')+'"'))
    if($ditFfmpeg){$ditArgs+=@('--ffmpeg',('"'+$ditFfmpeg.FullName+'"'),'--ffprobe',('"'+(Join-Path $ditFfmpeg.DirectoryName 'ffprobe.exe')+'"'))}
    Start-Process -FilePath $ditWorker -ArgumentList $ditArgs -WindowStyle Hidden
    Start-Process -FilePath (Join-Path $ditRepo 'src/Dit.Desktop/bin/Debug/net10.0-windows/Dit.Desktop.exe')
} finally {Pop-Location}
