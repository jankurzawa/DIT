$ErrorActionPreference='Stop'
$ditRepo=Split-Path $PSScriptRoot -Parent
Push-Location $ditRepo
try {
    New-Item -ItemType Directory -Force .tools | Out-Null
    if(-not(Test-Path .tools/dotnet/dotnet.exe)){
        Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile .tools/dotnet-install.ps1
        & ./.tools/dotnet-install.ps1 -Channel 10.0 -InstallDir "$ditRepo/.tools/dotnet" -NoPath
    }
    if(-not(Test-Path .tools/7zr.exe)){Invoke-WebRequest https://github.com/ip7z/7zip/releases/download/26.04/7zr.exe -OutFile .tools/7zr.exe}
    if(-not(Test-Path .tools/ffmpeg)){
        Invoke-WebRequest https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip -OutFile .tools/ffmpeg.zip
        Invoke-WebRequest https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256 -OutFile .tools/ffmpeg.zip.sha256
        $ditExpected=(Get-Content .tools/ffmpeg.zip.sha256).Split(' ')[0].Trim()
        if((Get-FileHash .tools/ffmpeg.zip -Algorithm SHA256).Hash-ne $ditExpected){throw 'Niezgodna suma FFmpeg.'}
        Expand-Archive .tools/ffmpeg.zip -DestinationPath .tools/ffmpeg
    }
    if(-not(Test-Path .tools/typescript/package/bin/tsc)){
        Invoke-WebRequest https://registry.npmjs.org/typescript/-/typescript-5.9.3.tgz -OutFile .tools/typescript.tgz
        New-Item -ItemType Directory -Force .tools/typescript | Out-Null
        tar -xzf .tools/typescript.tgz -C .tools/typescript
    }
    . "$PSScriptRoot/env.ps1"
    & $ditDotnet restore DIT.sln
    if($LASTEXITCODE-ne 0){throw 'Przywracanie NuGet nie powiodło się.'}
    node .tools/typescript/package/bin/tsc -p adobe/tsconfig.json
    if($LASTEXITCODE-ne 0){throw 'Kompilacja panelu nie powiodła się. Wymagany jest Node.js 22+.'}
} finally {Pop-Location}
