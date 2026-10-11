#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif

[Setup]
AppId={{A2CE854F-DF58-49AC-9A67-4243E974D5A9}
AppName=DIT Media Workspace
AppVersion={#AppVersion}
AppPublisher=DIT Media Workspace
VersionInfoDescription=Instalator DIT Media Workspace
VersionInfoVersion={#AppVersion}.0
DefaultDirName={localappdata}\Programs\DIT
DefaultGroupName=DIT Media Workspace
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=DIT-Setup-{#AppVersion}-win-x64
SetupIconFile={#PayloadDir}\Assets\dit.ico
UninstallDisplayIcon={app}\Dit.Desktop.exe
WizardStyle=modern
Compression=lzma2/normal
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMANumBlockThreads=2
DiskSpanning=no
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
InfoBeforeFile={#PayloadDir}\Pierwsze uruchomienie.txt
UninstallDisplayName=DIT Media Workspace

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Utwórz skrót DIT na pulpicie"; GroupDescription: "Skróty:"
Name: "adobepanel"; Description: "Zainstaluj panel DIT w Adobe Creative Cloud (zamknij Premiere przed instalacją)"; GroupDescription: "Integracja z Premiere Pro:"; Check: HasAdobeInstaller

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DIT"; Filename: "{app}\Dit.Desktop.exe"; WorkingDir: "{app}"
Name: "{group}\Pierwsze uruchomienie"; Filename: "{app}\Pierwsze uruchomienie.txt"
Name: "{group}\Odinstaluj DIT"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DIT"; Filename: "{app}\Dit.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Dit.Desktop.exe"; Description: "Uruchom DIT"; Flags: nowait postinstall skipifsilent
Filename: "{app}\Pierwsze uruchomienie.txt"; Description: "Otwórz instrukcję pierwszego uruchomienia"; Flags: shellexec postinstall skipifsilent unchecked

[Code]
function AdobeInstallerPath(): String;
begin
  Result := ExpandConstant('{commoncf64}\Adobe\Adobe Desktop Common\RemoteComponents\UPI\UnifiedPluginInstallerAgent\UnifiedPluginInstallerAgent.exe');
end;

function HasAdobeInstaller(): Boolean;
begin
  Result := FileExists(AdobeInstallerPath());
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('adobepanel') and HasAdobeInstaller() then
  begin
    if not Exec(AdobeInstallerPath(), '/install "' + ExpandConstant('{app}\adobe\DIT-Premiere.ccx') + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      ResultCode := -1;
    Log('Adobe panel installer exit code: ' + IntToStr(ResultCode));
    if ResultCode <> 0 then
      MsgBox('DIT został zainstalowany. Adobe nie potwierdziło instalacji panelu (kod ' + IntToStr(ResultCode) + '). W DIT wybierz Admin → Premiere i proxy → Skonfiguruj / otwórz Adobe. Instrukcja znajduje się w menu Start.', mbInformation, MB_OK);
  end;
end;
