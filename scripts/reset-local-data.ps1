# Run manually by the owner. This resets DIT state only, never media destinations.
[CmdletBinding()]
param([string]$ApplicationPath='C:\Repos\DIT\artifacts\DIT-0.1.18\Dit.Desktop.exe')
$ErrorActionPreference='Stop'
try {
    $ditApplication=(Resolve-Path -LiteralPath $ApplicationPath).Path
    $ditRepo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    if(-not $ditApplication.StartsWith($ditRepo+'\artifacts\',[StringComparison]::OrdinalIgnoreCase)){throw 'Nieoczekiwana lokalizacja programu.'}
    $ditData=[IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DIT'))
    $ditParent=[IO.Path]::GetFullPath([Environment]::GetFolderPath('LocalApplicationData'))
    if([IO.Path]::GetDirectoryName($ditData) -ne $ditParent -or [IO.Path]::GetFileName($ditData) -ne 'DIT'){throw 'Nieoczekiwana lokalizacja danych.'}
    $ditFiles=@()
    if(Test-Path -LiteralPath $ditData){
        if((Get-Item -LiteralPath $ditData).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Katalog danych jest dowiazaniem.'}
        foreach($ditName in @('dit.sqlite','dit.sqlite-shm','dit.sqlite-wal','local.key','adobe.key')){
            $ditPath=Join-Path $ditData $ditName
            if(Test-Path -LiteralPath $ditPath){$ditFiles+=Get-Item -LiteralPath $ditPath}
        }
        foreach($ditSubfolder in @('Backups','Adobe')){
            $ditFolder=Join-Path $ditData $ditSubfolder
            if(-not (Test-Path -LiteralPath $ditFolder)){continue}
            if((Get-Item -LiteralPath $ditFolder).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Katalog pomocniczy jest dowiazaniem.'}
            $ditFiles+=Get-ChildItem -LiteralPath $ditFolder -File | Where-Object {
                ($ditSubfolder -eq 'Backups' -and $_.Name -match '^pre-[a-zA-Z0-9.-]+-\d{8}-\d{6}\.sqlite(?:-shm|-wal)?$') -or
                ($ditSubfolder -eq 'Adobe' -and $_.Name -eq 'DIT.ditconnection')
            }
        }
        foreach($ditFile in $ditFiles){
            if(-not $ditFile.FullName.StartsWith($ditData+'\',[StringComparison]::OrdinalIgnoreCase) -or ($ditFile.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Plik poza katalogiem DIT lub dowiazanie.'}
        }
    }
    $ditProcesses=Get-CimInstance Win32_Process -Filter "name='Dit.Desktop.exe' OR name='Dit.Worker.exe'" | Where-Object {$_.ExecutablePath -and $_.ExecutablePath.StartsWith($ditRepo+'\',[StringComparison]::OrdinalIgnoreCase)}
    foreach($ditProcess in $ditProcesses){Stop-Process -Id $ditProcess.ProcessId -Force}
    foreach($ditProcess in $ditProcesses){Wait-Process -Id $ditProcess.ProcessId -ErrorAction SilentlyContinue}
    foreach($ditFile in $ditFiles){Remove-Item -LiteralPath $ditFile.FullName -Force}
    Write-Host 'Wyczyszczono baze DIT, konta, konfiguracje, historie, lokalne kopie bazy i klucze polaczenia.'
    Write-Host 'Nagrania, backup materialu i projekty Premiere pozostaly na dyskach.'
    Start-Process -FilePath $ditApplication -WorkingDirectory ([IO.Path]::GetDirectoryName($ditApplication))
} catch {
    Write-Host ('Reset nie powiodl sie: '+$_.Exception.Message) -ForegroundColor Red
    Read-Host 'Nacisnij Enter, aby zamknac'
    exit 1
}
