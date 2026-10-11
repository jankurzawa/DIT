param([string]$OutputPath='artifacts/DIT-Premiere-0.1.21.ccx')
$ErrorActionPreference='Stop'
$ditAdobeSource=Join-Path (Split-Path $PSScriptRoot -Parent) 'adobe'
$ditArchivePath=[System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($ditArchivePath))|Out-Null
$ditManifest=Get-Content -LiteralPath (Join-Path $ditAdobeSource 'manifest.json') -Raw | ConvertFrom-Json
if($ditManifest.host -is [Array]){$ditManifest.host=$ditManifest.host[0]}
# CCX is ZIP; ship runtime files only, with a single production host definition.
$ditArchive=[System.IO.Compression.ZipArchive]::new([System.IO.File]::Create($ditArchivePath),[System.IO.Compression.ZipArchiveMode]::Create)
try {
    $ditEntry=$ditArchive.CreateEntry('manifest.json')
    $ditWriter=[System.IO.StreamWriter]::new($ditEntry.Open(),[System.Text.UTF8Encoding]::new($false))
    try{$ditWriter.Write(($ditManifest | ConvertTo-Json -Depth 20))}finally{$ditWriter.Dispose()}
    foreach($ditFile in @('index.html','panel.js','dist/bridge.js')){
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($ditArchive,(Join-Path $ditAdobeSource $ditFile),$ditFile)|Out-Null
    }
}finally{$ditArchive.Dispose()}
Write-Output $ditArchivePath
