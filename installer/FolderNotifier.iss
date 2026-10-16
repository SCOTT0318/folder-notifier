; Build with build-installer.ps1. The payload includes the Windows Desktop runtime.
#ifndef AppVersion
  #define AppVersion "1.8.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\dist\FolderNotifier-" + AppVersion
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{892C5268-9038-48B6-B340-3A3614A73E0F}
AppName=폴더 알림
AppVersion={#AppVersion}
AppVerName=폴더 알림 {#AppVersion}
AppPublisher=FolderNotifier
VersionInfoDescription=폴더 알림 설치 프로그램
VersionInfoProductName=폴더 알림
VersionInfoProductVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\FolderNotifier
DefaultGroupName=폴더 알림
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupArchitecture=x64
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=FolderNotifier-Setup
SetupIconFile=..\FolderNotifier\Assets\folder-notifier.ico
WizardSmallImageFile=..\FolderNotifier\Assets\folder-notifier.png
UninstallDisplayIcon={app}\folder-notifier-v2.ico
UninstallDisplayName=폴더 알림
Uninstallable=yes
CreateUninstallRegKey=not IsSmokeTest
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dynamic windows11
WizardSizePercent=115
WizardResizable=no
ShowLanguageDialog=no
CloseApplications=no
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
ChangesAssociations=no
ChangesEnvironment=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Messages]
korean.WelcomeLabel1=폴더 알림 설치
korean.WelcomeLabel2=새 파일이 들어오면 바로 알려 드립니다.%n%n설치 후 감시할 폴더를 선택하면 사용할 수 있습니다.%n.NET을 따로 설치할 필요가 없습니다.
korean.FinishedHeadingLabel=폴더 알림 설치 완료
korean.FinishedLabel=폴더 알림을 사용할 준비가 되었습니다.%n%n시작 메뉴에서 다시 열 수 있습니다.
korean.SetupAppRunningError=폴더 알림이 실행 중입니다.%n%n작업 표시줄의 숨겨진 아이콘에서 폴더 알림을 마우스 오른쪽 버튼으로 클릭한 뒤 '종료'를 선택해 주세요.%n%n종료 후 확인을 누르면 설치를 계속합니다.
korean.UninstallAppRunningError=폴더 알림이 실행 중입니다.%n%n작업 표시줄의 숨겨진 아이콘에서 폴더 알림을 마우스 오른쪽 버튼으로 클릭한 뒤 '종료'를 선택해 주세요.%n%n종료 후 확인을 누르면 제거를 계속합니다.

[CustomMessages]
korean.DesktopShortcut=바탕 화면에 바로가기 만들기
korean.LaunchApp=폴더 알림 실행
korean.UninstallShortcut=폴더 알림 제거
korean.CloseRunningApp=폴더 알림이 실행 중입니다.%n%n작업 표시줄의 숨겨진 아이콘에서 폴더 알림을 마우스 오른쪽 버튼으로 클릭한 뒤 '종료'를 선택해 주세요.%n%n종료 후 확인을 누르면 계속합니다.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\폴더 알림"; Filename: "{app}\FolderNotifier.exe"; WorkingDir: "{app}"; IconFilename: "{app}\folder-notifier-v2.ico"; AppUserModelID: "FolderNotifier.Desktop"; Check: not IsSmokeTest
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"; Check: not IsSmokeTest
Name: "{autodesktop}\폴더 알림"; Filename: "{app}\FolderNotifier.exe"; WorkingDir: "{app}"; IconFilename: "{app}\folder-notifier-v2.ico"; AppUserModelID: "FolderNotifier.Desktop"; Tasks: desktopicon; Check: not IsSmokeTest

[Run]
Filename: "{app}\FolderNotifier.exe"; WorkingDir: "{app}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent; Check: not IsSmokeTest

[Code]
function IsSmokeTest: Boolean;
begin
  { The build verification uses an isolated /DIR and leaves no user shortcuts
    or installed-app registration. Normal installation always registers removal. }
  Result := ExpandConstant('{param:SMOKETEST|0}') = '1';
end;

function EnsureAppClosed: Boolean;
begin
  Result := True;
  if IsSmokeTest then
    Exit;
  while CheckForMutexes('Local\FolderNotifier.Desktop') do
    if SuppressibleMsgBox(ExpandConstant('{cm:CloseRunningApp}'), mbError,
      MB_OKCANCEL, IDCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
end;

function InitializeSetup: Boolean;
begin
  Result := EnsureAppClosed;
end;

function InitializeUninstall: Boolean;
begin
  Result := EnsureAppClosed;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if (CurUninstallStep = usPostUninstall) and (not IsSmokeTest) then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'FolderNotifier', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\FolderNotifier.exe') + '" --startup') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'FolderNotifier');
end;

{ User preferences live in LocalAppData\FolderNotifier and are never included
  in the payload or removed during upgrades or uninstallation. }
