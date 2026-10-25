; Personal only. Stable identity; never share the Network AppId.
#ifndef AppVersion
  #define AppVersion "0.1.1-preview"
#endif
#ifndef FileVersion
  #define FileVersion "0.1.1.0"
#endif
#ifndef PersonalAppId
  #define PersonalAppId "C6DB8A6A-11F0-46EA-90DF-8F73D735B0B4"
#endif
#ifndef PersonalAppName
  #define PersonalAppName "VATowiec Personal"
#endif
#ifndef SourceDir
  #error SourceDir is required
#endif
#ifndef ReleaseDir
  #error ReleaseDir is required
#endif

[Setup]
AppId={{{#PersonalAppId}}
AppName={#PersonalAppName}
AppVersion={#AppVersion}
AppVerName={#PersonalAppName} {#AppVersion}
AppPublisher=Otwarty następca VATowca
VersionInfoVersion={#FileVersion}
DefaultDirName={localappdata}\Programs\{#PersonalAppName}
DefaultGroupName={#PersonalAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
OutputDir={#ReleaseDir}
OutputBaseFilename=Vatowiec-Personal-{#AppVersion}-win-x64-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#PersonalAppName} {#AppVersion}
UninstallDisplayIcon={app}\Vatowiec.Desktop.exe
CloseApplications=no
RestartApplications=no
SetupLogging=yes
InfoBeforeFile={#SourceDir}\PERSONAL-README.txt

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "Utwórz skrót VATowiec Personal na pulpicie"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#PersonalAppName}"; Filename: "{app}\Vatowiec.Desktop.exe"; Parameters: "--personal"; WorkingDir: "{app}"
Name: "{userdesktop}\{#PersonalAppName}"; Filename: "{app}\Vatowiec.Desktop.exe"; Parameters: "--personal"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Vatowiec.Desktop.exe"; Parameters: "--personal"; Description: "Uruchom VATowiec Personal"; Flags: nowait postinstall skipifsilent

; No UninstallDelete, SQL, key-ring, preferences or Credential Manager cleanup.
[Code]
const
  BootstrapUrl = 'https://download.microsoft.com/download/2e652cd0-da06-4fc0-836a-b75ec45035a9/SQL2025-SSEI-Expr.exe';
  BootstrapHash = 'fa7e1fabc9a2e9c9cdab0d1512bcb30d2949133147db057ac530f510e5270680';
var
  DependencyPage: TWizardPage;
  DependencyText: TNewStaticText;
  DependencyButton: TNewButton;
  ClosingFailed: Boolean;

function Sql2025File(const Path: String): Boolean;
var MS, LS: Cardinal;
begin
  Result := GetVersionNumbers(Path, MS, LS);
  if Result then
    { SQL 2025 file version is 2025.170.*, product version is 17.0.*. }
    Result := ((MS shr 16) = 17) or (((MS shr 16) = 2025) and ((MS and $FFFF) = 170));
end;

function LocalDbPresent: Boolean;
var ApiPath, ToolsPath: String;
begin
  Result := False;
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions\17.0', 'InstanceAPIPath', ApiPath) then exit;
  if not Sql2025File(ApiPath) then exit;
  ToolsPath := ExpandFileName(ExtractFileDir(ApiPath) + '\..\..\Tools\Binn\SqlLocalDB.exe');
  Result := Sql2025File(ToolsPath);
end;

procedure DownloadLocalDb(Sender: TObject);
var Code: Integer; DownloadPage: TDownloadWizardPage;
begin
  if LocalDbPresent then begin
    MsgBox('SQL Server 2025 LocalDB jest już dostępny. Nie będzie instalowany ponownie.', mbInformation, MB_OK); exit;
  end;
  if MsgBox('Wymagane jest połączenie z Internetem. Pobrać i uruchomić oficjalny instalator Microsoft SQL Server 2025 Express?' + #13#10#13#10 +
    'Wybierz Pobierz nośnik (Download Media), następnie LocalDB. Otwórz pobrany plik SqlLocalDB.msi i zapoznaj się z warunkami Microsoftu. Tylko instalacja zależności może wymagać zgody administratora.' + #13#10#13#10 +
    'Microsoft zgłasza błąd licencji CTP w części pakietów LocalDB. Nie akceptujemy jej automatycznie. W razie nieprawidłowych warunków przerwij instalację i skontaktuj się z Microsoftem.' + #13#10#13#10 +
    'Nie wybieraj instalacji pełnego SQL Server Express ani zmiany istniejących instancji. Po zakończeniu wróć tutaj i kliknij Dalej.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then exit;
  DownloadPage := CreateDownloadPage('SQL Server 2025 LocalDB', 'Pobieranie z Microsoftu i kontrola SHA-256.', nil);
  try
    DownloadPage.Add(BootstrapUrl, 'SQL2025-SSEI-Expr.exe', BootstrapHash);
    DownloadPage.Show;
    try
      DownloadPage.Download;
    finally
      DownloadPage.Hide;
    end;
    if not Exec(ExpandConstant('{tmp}\SQL2025-SSEI-Expr.exe'), '', '', SW_SHOWNORMAL, ewWaitUntilTerminated, Code) then
      RaiseException('Nie uruchomiono instalatora Microsoft. VATowiec nie został jeszcze zainstalowany.');
    if Code <> 0 then MsgBox('Instalator Microsoft zakończył się kodem ' + IntToStr(Code) + '. Gotowość LocalDB zostanie sprawdzona ponownie.', mbError, MB_OK);
  except
    MsgBox('Nie przygotowano LocalDB. Pobranie lub weryfikacja instalatora Microsoft nie powiodły się. ' + GetExceptionMessage, mbError, MB_OK);
  end;
end;

procedure InitializeWizard;
begin
  DependencyPage := CreateCustomPage(wpInfoBefore, 'SQL Server 2025 LocalDB', 'Sprawdzenie wymaganej wersji 17.x');
  DependencyText := TNewStaticText.Create(DependencyPage);
  DependencyText.Parent := DependencyPage.Surface;
  DependencyText.SetBounds(0, 0, DependencyPage.SurfaceWidth, ScaleY(170));
  DependencyText.AutoSize := False; DependencyText.WordWrap := True;
  DependencyText.Caption := 'VATowiec Personal wymaga SQL Server 2025 LocalDB (17.x).' + #13#10#13#10 +
    'Jeżeli go brakuje, przygotuj go przyciskiem poniżej, a następnie kliknij Dalej. Pobieranie wymaga Internetu. Sam program VATowiec instaluje się bez uprawnień administratora.' + #13#10#13#10 +
    'Zastany LocalDB nie będzie ponownie instalowany. Instalator nie zmienia żadnych baz ani instancji.';
  DependencyButton := TNewButton.Create(DependencyPage); DependencyButton.Parent := DependencyPage.Surface;
  DependencyButton.SetBounds(0, ScaleY(180), DependencyPage.SurfaceWidth, ScaleY(30));
  DependencyButton.Caption := 'Pobierz i uruchom instalator Microsoft…'; DependencyButton.OnClick := @DownloadLocalDb;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = DependencyPage.ID) and LocalDbPresent;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = DependencyPage.ID) and not LocalDbPresent then begin
    MsgBox('Nie wykryto gotowego SQL Server 2025 LocalDB (17.x). Nie można jeszcze zainstalować VATowca Personal. Przygotuj LocalDB i spróbuj ponownie.', mbError, MB_OK);
    Result := False;
  end;
end;

function EnumWindows(Callback: LongWord; Param: Integer): Boolean; external 'EnumWindows@user32.dll stdcall';
function GetWindowThreadProcessId(Window: HWND; var ProcessId: Cardinal): Cardinal; external 'GetWindowThreadProcessId@user32.dll stdcall';
function OpenProcess(Access: Cardinal; Inherit: Boolean; ProcessId: Cardinal): THandle; external 'OpenProcess@kernel32.dll stdcall';
function QueryFullProcessImageName(Process: THandle; Flags: Cardinal; Path: String; var Size: Cardinal): Boolean; external 'QueryFullProcessImageNameW@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean; external 'CloseHandle@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: Cardinal): Cardinal; external 'WaitForSingleObject@kernel32.dll stdcall';
function PostMessage(Window: HWND; Msg: Cardinal; WParam, LParam: Integer): Boolean; external 'PostMessageW@user32.dll stdcall';

function CloseOwnedWindow(Window: HWND; Param: Integer): Boolean;
var ProcessId, Size: Cardinal; Process: THandle; Path: String;
begin
  Result := True;
  GetWindowThreadProcessId(Window, ProcessId);
  Process := OpenProcess($101000, False, ProcessId); { SYNCHRONIZE + QUERY_LIMITED_INFORMATION }
  if Process = 0 then exit;
  try
    Size := 32768; SetLength(Path, Size);
    if QueryFullProcessImageName(Process, 0, Path, Size) then begin
      SetLength(Path, Size);
      if CompareText(Path, ExpandConstant('{app}\Vatowiec.Desktop.exe')) = 0 then begin
        { WM_CLOSE respects the existing unsaved-changes guard and waits for its own API. Never kill. }
        PostMessage(Window, $0010, 0, 0);
        if WaitForSingleObject(Process, 65000) <> 0 then ClosingFailed := True;
      end;
    end;
  finally
    CloseHandle(Process);
  end;
end;

function ClosePersonal: Boolean;
begin
  ClosingFailed := False;
  EnumWindows(CreateCallback(@CloseOwnedWindow), 0);
  Result := not ClosingFailed;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var NetworkDirectory, Target, DataDirectory: String;
begin
  Result := '';
  Target := AddBackslash(ExpandFileName(ExpandConstant('{app}')));
  DataDirectory := AddBackslash(ExpandConstant('{localappdata}\Vatowiec\Personal'));
  if (Pos(Lowercase(DataDirectory), Lowercase(Target)) = 1) or
     (Pos(Lowercase(Target), Lowercase(DataDirectory)) = 1) then begin
    Result := 'Katalog programu musi być oddzielony od katalogu danych Personal.'; exit;
  end;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9F727694-49DB-480A-966A-BB51688C57E5}_is1', 'InstallLocation', NetworkDirectory) then
    if CompareText(Target, AddBackslash(ExpandFileName(NetworkDirectory))) = 0 then begin
      Result := 'Nie można zastąpić instalacji VATowiec Network instalacją Personal. Wybierz osobny katalog.'; exit;
    end;
  if not LocalDbPresent then begin Result := 'Brak SQL Server 2025 LocalDB (17.x). Nie zainstalowano programu. Przygotuj zależność i ponów instalację.'; exit; end;
  if not ClosePersonal then Result := 'Zapisz niezapisane zmiany i zamknij VATowiec Personal. Nie zakończono procesów siłą. Ponów instalację po zamknięciu programu.';
end;

function InitializeUninstall: Boolean;
begin
  Result := ClosePersonal;
  if not Result then MsgBox('Zapisz zmiany i zamknij VATowiec Personal, a następnie ponów odinstalowanie. Dane i procesy pozostają zachowane.', mbError, MB_OK);
end;
