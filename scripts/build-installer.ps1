param(
    [string]$PackageDirectory,
    [string]$OutputDirectory,
    [string]$CompilerPath,
    [switch]$SkipPublish
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/env.ps1"
Push-Location $ditRepo
try {
    [xml]$ditProperties=Get-Content -LiteralPath (Join-Path $ditRepo 'Directory.Build.props') -Raw
    $ditVersion=[string]$ditProperties.Project.PropertyGroup.Version
    $ditPackage=if($PackageDirectory){[IO.Path]::GetFullPath($PackageDirectory,$ditRepo)}else{Join-Path $ditRepo "artifacts/DIT-$ditVersion"}
    $ditInstallerOutput=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory,$ditRepo)}else{Join-Path $ditRepo 'artifacts/installers'}
    if(-not $SkipPublish){& "$PSScriptRoot/package.ps1" -OutputDirectory $ditPackage}
    $ditCompiler=if($CompilerPath){[IO.Path]::GetFullPath($CompilerPath,$ditRepo)}else{Join-Path $ditRepo '.tools/inno/ISCC.exe'}
    if(-not(Test-Path -LiteralPath $ditCompiler)) {throw 'Brak ISCC.exe. Zainstaluj Inno Setup 6.7+ i podaj -CompilerPath.'}
    foreach($ditRequired in @('Dit.Desktop.exe','coreclr.dll','worker/Dit.Worker.exe','worker/coreclr.dll','worker/ffmpeg.exe','worker/ffprobe.exe','worker/7z.exe','restore/Dit.Restore.exe','libvlc/win-x64/libvlc.dll','Presets/DIT Proxy 720p.epr','worker/Presets/DIT Proxy 720p.epr','adobe/DIT-Premiere.ccx')) {
        if(-not(Test-Path -LiteralPath (Join-Path $ditPackage $ditRequired))){throw "Niepełny pakiet: $ditRequired"}
    }
    # A fresh staging folder excludes other architectures, developer files and all user data.
    $ditStage=Join-Path $ditRepo ('artifacts/installer-payload-'+$ditVersion+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
    [IO.Directory]::CreateDirectory($ditStage)|Out-Null
    foreach($ditFile in Get-ChildItem -LiteralPath $ditPackage -File -Recurse) {
        $ditRelative=[IO.Path]::GetRelativePath($ditPackage,$ditFile.FullName).Replace('\','/')
        if($ditRelative -match '^libvlc/(win-x86|win-arm64)/' -or $ditRelative -match '\.(pdb|lib)$' -or $ditRelative -match '^(adobe|docs)/' -or $ditRelative -eq 'README.md'){continue}
        if($ditRelative -match '(?i)(\.sqlite(?:-wal|-shm)?$|\.ditconnection$|\.key$|\.prproj$|\.bak$|\.log$)'){throw "Plik użytkownika w pakiecie: $ditRelative"}
        $ditTarget=Join-Path $ditStage $ditRelative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ditTarget))|Out-Null
        Copy-Item -LiteralPath $ditFile.FullName -Destination $ditTarget
    }
    foreach($ditRelative in @('adobe/DIT-Premiere.ccx','adobe/manifest.json','adobe/index.html','adobe/panel.js','adobe/dist/bridge.js')) {
        $ditTarget=Join-Path $ditStage $ditRelative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ditTarget))|Out-Null
        Copy-Item -LiteralPath (Join-Path $ditPackage $ditRelative) -Destination $ditTarget
    }
    [IO.Directory]::CreateDirectory((Join-Path $ditStage 'docs'))|Out-Null
    Copy-Item -LiteralPath 'docs/INSTALACJA-WINDOWS.md','docs/ADOBE.md','docs/POLACZENIE-PREMIERE.md' -Destination (Join-Path $ditStage 'docs')
    $ditGuide=(Get-Content -LiteralPath 'docs/INSTALACJA-WINDOWS.md' -Raw).Replace('`','').Replace('# DIT','DIT')
    [IO.File]::WriteAllText((Join-Path $ditStage 'Pierwsze uruchomienie.txt'),$ditGuide,[Text.UTF8Encoding]::new($true))
    $ditLicenses=Join-Path $ditStage 'Licenses';[IO.Directory]::CreateDirectory($ditLicenses)|Out-Null
    Copy-Item -LiteralPath (Join-Path $ditFfmpeg.Directory.Parent.FullName 'LICENSE') -Destination (Join-Path $ditLicenses 'FFmpeg-GPLv3.txt')
    Copy-Item -LiteralPath (Join-Path $ditFfmpeg.Directory.Parent.FullName 'README.txt') -Destination (Join-Path $ditLicenses 'FFmpeg-build.txt')
    Copy-Item -LiteralPath (Join-Path $env:DOTNET_ROOT 'LICENSE.txt') -Destination (Join-Path $ditLicenses 'DotNet-LICENSE.txt')
    foreach($ditNotice in Get-ChildItem -LiteralPath $env:DOTNET_ROOT -Filter 'ThirdPartyNotices.txt' -File){Copy-Item -LiteralPath $ditNotice.FullName -Destination (Join-Path $ditLicenses 'DotNet-ThirdPartyNotices.txt')}
    Copy-Item -LiteralPath 'installer/THIRD-PARTY-NOTICES.txt' -Destination $ditLicenses
    foreach($ditLicense in @('LibVLC-LGPL-2.1.txt','LibVLCSharp-LGPL-2.1.txt')){Copy-Item -LiteralPath (Join-Path $ditRepo ('installer/licenses/'+$ditLicense)) -Destination $ditLicenses}
    $ditManifest=@(Get-ChildItem -LiteralPath $ditStage -Recurse -File | Sort-Object FullName | ForEach-Object {
        @{path=[IO.Path]::GetRelativePath($ditStage,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
    $ditManifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $ditStage 'payload-manifest.json') -Encoding utf8
    [IO.Directory]::CreateDirectory($ditInstallerOutput)|Out-Null
    & $ditCompiler '/Qp' "/DPayloadDir=$ditStage" "/DOutputDir=$ditInstallerOutput" "/DAppVersion=$ditVersion" (Join-Path $ditRepo 'installer/DIT.iss')
    if($LASTEXITCODE-ne 0){throw 'Kompilacja instalatora nie powiodła się.'}
    $ditSetup=Join-Path $ditInstallerOutput "DIT-Setup-$ditVersion-win-x64.exe"
    $ditHash=(Get-FileHash -LiteralPath $ditSetup -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($ditSetup+'.sha256',"$ditHash  $([IO.Path]::GetFileName($ditSetup))`n")
    @{version=$ditVersion;installer=$ditSetup;sha256=$ditHash;payload=$ditStage;files=$ditManifest.Count;payloadBytes=($ditManifest|Measure-Object bytes -Sum).Sum;installerBytes=(Get-Item -LiteralPath $ditSetup).Length}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $ditInstallerOutput "build-$ditVersion.json") -Encoding utf8
    Write-Output "Instalator: $ditSetup"
} finally {Pop-Location}
