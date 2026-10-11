param([string]$OutputDirectory)
. "$PSScriptRoot/env.ps1"
Push-Location $ditRepo
try {
    $ditOutput=if($OutputDirectory){[System.IO.Path]::GetFullPath($OutputDirectory,$ditRepo)}else{Join-Path $ditRepo 'artifacts/DIT'}
    function Copy-DitTree([string]$Source,[string]$Destination) {
        $sourceRoot=(Resolve-Path -LiteralPath $Source).Path
        foreach($item in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
            $target=Join-Path $Destination ([System.IO.Path]::GetRelativePath($sourceRoot,$item.FullName))
            [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target))|Out-Null
            Copy-Item -LiteralPath $item.FullName -Destination $target -Force
        }
    }
    & $ditDotnet publish src/Dit.Desktop -c Release -r win-x64 --self-contained true -o $ditOutput
    if($LASTEXITCODE-ne 0){throw 'Publikacja interfejsu nie powiodła się.'}
    & $ditDotnet publish src/Dit.Worker -c Release -r win-x64 --self-contained true -o (Join-Path $ditOutput 'worker')
    if($LASTEXITCODE-ne 0){throw 'Publikacja procesu roboczego nie powiodła się.'}
    foreach($ditPresetRoot in @($ditOutput,(Join-Path $ditOutput 'worker'))){if(-not(Test-Path -LiteralPath (Join-Path $ditPresetRoot 'Presets/DIT Proxy 720p.epr'))){throw 'Brak domyslnego presetu proxy w pakiecie.'}}
    & $ditDotnet publish src/Dit.Restore -c Release -r win-x64 --self-contained true -o (Join-Path $ditOutput 'restore')
    if($LASTEXITCODE-ne 0){throw 'Publikacja narzędzia odtwarzania nie powiodła się.'}
    node .tools/typescript/package/bin/tsc -p adobe/tsconfig.json
    if($LASTEXITCODE-ne 0){throw 'Kompilacja panelu Adobe nie powiodła się.'}
    Copy-DitTree (Join-Path $ditRepo 'adobe') (Join-Path $ditOutput 'adobe')
    & (Join-Path $PSScriptRoot 'package-adobe.ps1') -OutputPath (Join-Path $ditOutput 'adobe/DIT-Premiere.ccx')
    if($ditFfmpeg){Copy-Item -LiteralPath $ditFfmpeg.FullName,(Join-Path $ditFfmpeg.DirectoryName 'ffprobe.exe') -Destination (Join-Path $ditOutput 'worker') -Force}
    Copy-Item -LiteralPath (Join-Path $ditRepo '.tools/7zr.exe') -Destination (Join-Path $ditOutput 'worker/7z.exe') -Force
    Copy-DitTree (Join-Path $ditRepo 'docs') (Join-Path $ditOutput 'docs')
    Copy-Item -LiteralPath (Join-Path $ditRepo 'README.md') -Destination $ditOutput -Force
    Write-Output "Pakiet: $ditOutput"
} finally {Pop-Location}
