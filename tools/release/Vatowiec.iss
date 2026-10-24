; VATowiec client only. Keep AppId stable across releases.
; Compile with tools/release/build-release.ps1 (Inno Setup >= 6.3).
#ifndef AppVersion
  #define AppVersion "0.1.0-preview"
#endif
#ifndef FileVersion
  #define FileVersion "0.1.0.0"
#endif
#ifndef SourceDir
  #error SourceDir must point to the verified client publish directory
#endif
#ifndef ReleaseDir
  #error ReleaseDir must point to the release artifacts directory
#endif

[Setup]
AppId={{9F727694-49DB-480A-966A-BB51688C57E5}
AppName=VATowiec
AppVersion={#AppVersion}
AppVerName=VATowiec {#AppVersion}
AppPublisher=Otwarty następca VATowca
VersionInfoVersion={#FileVersion}
VersionInfoProductVersion={#FileVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoDescription=Instalator klienta VATowiec
DefaultDirName={localappdata}\Programs\VATowiec
DefaultGroupName=VATowiec
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
OutputDir={#ReleaseDir}
OutputBaseFilename=Vatowiec-{#AppVersion}-win-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=VATowiec {#AppVersion}
UninstallDisplayIcon={app}\Vatowiec.Desktop.exe
CloseApplications=yes
CloseApplicationsFilter=Vatowiec.Desktop.exe,*.dll
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "Utwórz skrót na pulpicie"; GroupDescription: "Skróty:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\VATowiec"; Filename: "{app}\Vatowiec.Desktop.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\VATowiec"; Filename: "{app}\Vatowiec.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Vatowiec.Desktop.exe"; Description: "Uruchom VATowiec"; Flags: nowait postinstall skipifsilent

; No UninstallDelete / registry or Credential Manager cleanup: personal data
; lives outside {app}, in LocalAppData\Vatowiec\Desktop and Credential Manager.
