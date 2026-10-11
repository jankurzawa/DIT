param([string]$InstallerPath='artifacts/installers/DIT-Setup-0.1.20-win-x64.exe')
$ErrorActionPreference='Stop'
$ditRepo=Split-Path $PSScriptRoot -Parent
$ditInstaller=[IO.Path]::GetFullPath($InstallerPath,$ditRepo)
$ditTestRoot=Join-Path $ditRepo ('artifacts/installer-smoke-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$ditTestApp=Join-Path $ditTestRoot 'Program'
[IO.Directory]::CreateDirectory($ditTestRoot)|Out-Null
$ditChecks=[Collections.Generic.List[string]]::new()
$ditWorkerProcess=$null
function Run-DitTestProcess([string]$Executable,[string[]]$Arguments,[int]$Timeout=90000) {
    $ditStart=[Diagnostics.ProcessStartInfo]::new($Executable)
    $ditStart.UseShellExecute=$false;$ditStart.CreateNoWindow=$true;$ditStart.WindowStyle='Hidden'
    $ditStart.RedirectStandardOutput=$true;$ditStart.RedirectStandardError=$true
    $ditStart.Environment['DOTNET_ROOT']=Join-Path $ditTestRoot 'no-external-dotnet'
    $ditStart.Environment['PATH']="$env:WINDIR\System32;$env:WINDIR"
    foreach($ditArg in $Arguments){$ditStart.ArgumentList.Add($ditArg)}
    $ditProcess=[Diagnostics.Process]::Start($ditStart)
    $ditOut=$ditProcess.StandardOutput.ReadToEndAsync();$ditErr=$ditProcess.StandardError.ReadToEndAsync()
    if(-not $ditProcess.WaitForExit($Timeout)){$ditProcess.Kill($true);throw "Przekroczony czas: $Executable"}
    $ditOutput=$ditOut.GetAwaiter().GetResult();$ditError=$ditErr.GetAwaiter().GetResult()
    if($ditProcess.ExitCode-ne 0){throw "Proces zwrócił $($ditProcess.ExitCode): $Executable`n$ditOutput`n$ditError"}
    return $ditOutput
}
try {
    # No desktop shortcut, Adobe panel or automatic launch; the install path is isolated.
    Run-DitTestProcess $ditInstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS=','/MERGETASKS=!desktopicon,!adobepanel',"/DIR=$ditTestApp","/LOG=$ditTestRoot/install.log") 240000|Out-Null
    $ditChecks.Add('Silent per-user installation succeeds without desktop shortcut or Adobe changes')
    $ditManifest=Get-Content -LiteralPath (Join-Path $ditTestApp 'payload-manifest.json') -Raw|ConvertFrom-Json
    foreach($ditFile in $ditManifest) {
        $ditPath=Join-Path $ditTestApp $ditFile.path
        if(-not(Test-Path -LiteralPath $ditPath) -or (Get-FileHash -LiteralPath $ditPath -Algorithm SHA256).Hash.ToLowerInvariant()-ne $ditFile.sha256){throw "Niezgodny plik po instalacji: $($ditFile.path)"}
    }
    $ditChecks.Add("All $($ditManifest.Count) installed files match the SHA-256 payload manifest")
    if(Get-ChildItem -LiteralPath $ditTestApp -Recurse -File | Where-Object {$_.Name -match '(\.key$|\.sqlite(?:-wal|-shm)?$|\.ditconnection$|\.prproj$)'}){throw 'Dane użytkownika w instalatorze.'}
    $ditChecks.Add('No project database, credentials, connection keys or Premiere projects included')
    $ditWorkerStart=[Diagnostics.ProcessStartInfo]::new((Join-Path $ditTestApp 'worker/Dit.Worker.exe'))
    $ditWorkerStart.UseShellExecute=$false;$ditWorkerStart.CreateNoWindow=$true;$ditWorkerStart.WindowStyle='Hidden'
    $ditWorkerStart.Environment['DOTNET_ROOT']=Join-Path $ditTestRoot 'no-external-dotnet';$ditWorkerStart.Environment['PATH']="$env:WINDIR\System32;$env:WINDIR"
    foreach($ditArg in @('--data',(Join-Path $ditTestRoot 'Data'),'--listen','http://127.0.0.1:47839','--adobe-auto-launch','false')){$ditWorkerStart.ArgumentList.Add($ditArg)}
    $ditWorkerProcess=[Diagnostics.Process]::Start($ditWorkerStart)
    $ditHeaders=$null
    for($ditAttempt=0;$ditAttempt-lt 60;$ditAttempt++) {
        Start-Sleep -Milliseconds 250
        $ditKeyPath=Join-Path $ditTestRoot 'Data/local.key'
        if(-not(Test-Path -LiteralPath $ditKeyPath)){continue}
        $ditHeaders=@{'X-DIT-Key'=(Get-Content -LiteralPath $ditKeyPath -Raw).Trim()}
        try{$ditHealth=Invoke-RestMethod http://127.0.0.1:47839/health -Headers $ditHeaders -TimeoutSec 2;break}catch{}
    }
    if($ditHealth.status-ne 'ok'){throw 'Nie działa proces roboczy z nowej instalacji.'}
    $ditProjects=Invoke-RestMethod http://127.0.0.1:47839/projects -Headers $ditHeaders
    if($ditProjects.Count-ne 0){throw 'Nowa baza zawiera cudze projekty.'}
    $ditChecks.Add('Self-contained worker starts with fresh empty SQLite data and new local keys')
    Run-DitTestProcess (Join-Path $ditTestApp 'Dit.Desktop.exe') @('--check-recordings',(Join-Path $ditTestRoot 'recordings.json'))|Out-Null
    if(-not(Test-Path -LiteralPath (Join-Path $ditTestRoot 'recordings.json'))){throw 'Brak wyniku kontroli WPF.'}
    Run-DitTestProcess (Join-Path $ditTestApp 'Dit.Desktop.exe') @('--check-startup',(Join-Path $ditTestRoot 'startup.json'))|Out-Null
    if(-not(Test-Path -LiteralPath (Join-Path $ditTestRoot 'startup.json'))){throw 'Brak wyniku startu podglądu LibVLC.'}
    $ditChecks.Add('Installed WPF interface, filters and native LibVLC initialize without external SDK/Node/VLC paths')
    $ditSample=Join-Path $ditTestRoot 'sample.mp4'
    Run-DitTestProcess (Join-Path $ditTestApp 'worker/ffmpeg.exe') @('-v','error','-f','lavfi','-i','color=c=blue:s=128x72:r=25','-t','1','-c:v','libx264','-pix_fmt','yuv420p',$ditSample)|Out-Null
    $ditMedia=Run-DitTestProcess (Join-Path $ditTestApp 'worker/ffprobe.exe') @('-v','error','-show_streams','-of','json',$ditSample)|ConvertFrom-Json
    if($ditMedia.streams[0].width-ne 128){throw 'FFmpeg/ffprobe nie działają.'}
    $ditChecks.Add('Bundled FFmpeg encodes real H.264 video and ffprobe reads it')
    Run-DitTestProcess (Join-Path $ditTestApp 'worker/7z.exe') @('a','-t7z',(Join-Path $ditTestRoot 'sample.7z'),$ditSample)|Out-Null
    Run-DitTestProcess (Join-Path $ditTestApp 'worker/7z.exe') @('t',(Join-Path $ditTestRoot 'sample.7z'))|Out-Null
    $ditChecks.Add('Bundled 7zr creates and verifies an archive')
    $ditWorkerProcess.Kill();$ditWorkerProcess.WaitForExit();$ditWorkerProcess=$null
    # Windows uninstall should preserve all user data outside the installation directory.
    $ditDataHash=(Get-FileHash -LiteralPath (Join-Path $ditTestRoot 'Data/dit.sqlite') -Algorithm SHA256).Hash
    Run-DitTestProcess (Join-Path $ditTestApp 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=$ditTestRoot/uninstall.log") 90000|Out-Null
    for($ditAttempt=0;$ditAttempt-lt 40 -and (Test-Path -LiteralPath (Join-Path $ditTestApp 'Dit.Desktop.exe'));$ditAttempt++){Start-Sleep -Milliseconds 250}
    if(Test-Path -LiteralPath (Join-Path $ditTestApp 'Dit.Desktop.exe')){throw 'Deinstalator pozostawił program.'}
    if((Get-FileHash -LiteralPath (Join-Path $ditTestRoot 'Data/dit.sqlite') -Algorithm SHA256).Hash-ne $ditDataHash){throw 'Deinstalator zmienił dane użytkownika.'}
    $ditChecks.Add('Uninstall removes the program and preserves the database and keys')
    @{installer=$ditInstaller;testDirectory=$ditTestRoot;passed=$ditChecks;externalDevelopmentDependencies=$false}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $ditRepo 'artifacts/installers/installer-smoke-result.json') -Encoding utf8
    $ditChecks|ForEach-Object {Write-Output "PASS: $_"}
} finally {
    if($ditWorkerProcess -and -not $ditWorkerProcess.HasExited){$ditWorkerProcess.Kill();$ditWorkerProcess.WaitForExit()}
}
