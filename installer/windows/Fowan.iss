#define AppName "Fowan"
#define AppPublisher "Fowan"
#define AppExeName "Fowan.Windows.exe"
#define AppGuid "F6DE2B72-BF97-42CC-AE78-D9A849436768"
#define UninstallSubkey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{" + AppGuid + "}_is1"

#ifndef AppVersion
#define AppVersion "0.1.4"
#endif

#ifndef SourceDir
#define SourceDir "..\..\out\installer\windows\win-x64\app"
#endif

#ifndef OutputDir
#define OutputDir "..\..\out\installer\windows\win-x64"
#endif

#ifndef VcRedistPath
#define VcRedistPath "..\..\out\installer\windows\win-x64\prerequisites\vc_redist.x64.exe"
#endif

#ifndef ReleaseNotesPath
#define ReleaseNotesPath "..\..\out\installer\windows\win-x64\app\ReleaseNotes\release-notes.txt"
#endif

[Setup]
AppId={{F6DE2B72-BF97-42CC-AE78-D9A849436768}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}
DefaultDirName={autopf}\Fowan
DefaultGroupName=Fowan
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
LicenseFile=privacy-zh-CN.txt
OutputDir={#OutputDir}
OutputBaseFilename=FowanSetup-{#AppVersion}-win-x64
SetupIconFile=..\..\assets\brand\windows\fowan.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartIfNeededByRun=no

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式:"; Flags: checkedonce
Name: "autostart"; Description: "登录 Windows 时自动启动 Fowan"; GroupDescription: "启动行为:"; Flags: checkedonce

[Files]
Source: "close-fowan-processes.ps1"; Flags: dontcopy noencryption
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "uninstall-clean-fowan-data.ps1"; DestDir: "{app}\Installer"; Flags: ignoreversion
Source: "{#VcRedistPath}"; DestDir: "{tmp}"; DestName: "vc_redist.x64.exe"; Flags: deleteafterinstall
Source: "{#ReleaseNotesPath}"; Flags: dontcopy

[Icons]
Name: "{commonprograms}\Fowan\Fowan"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{commondesktop}\Fowan"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Fowan"; ValueData: """{app}\{#AppExeName}"" --start-hidden"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Description: "运行 Fowan"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallDelete]
Type: files; Name: "{commondesktop}\Fowan.lnk"
Type: dirifempty; Name: "{commonprograms}\Fowan"

[Code]
var
  CleanUninstallRequested: Boolean;
  ExistingInstallation: Boolean;

function LoadPackagedReleaseNotes(var ReleaseNotes: string): Boolean;
var
  ReleaseNotesFile: string;
  ReleaseNoteLines: TArrayOfString;
  Index: Integer;
begin
  Result := False;
  ExtractTemporaryFile('release-notes.txt');
  ReleaseNotesFile := ExpandConstant('{tmp}\release-notes.txt');
  if not FileExists(ReleaseNotesFile) then
  begin
    exit;
  end;

  Result := LoadStringsFromFile(ReleaseNotesFile, ReleaseNoteLines);
  if Result then
  begin
    ReleaseNotes := '';
    for Index := 0 to GetArrayLength(ReleaseNoteLines) - 1 do
    begin
      if Index > 0 then
      begin
        ReleaseNotes := ReleaseNotes + #13#10;
      end;
      ReleaseNotes := ReleaseNotes + ReleaseNoteLines[Index];
    end;
  end;
end;

procedure ShowUpdateReleaseNotes(InstalledVersion: string);
var
  ReleaseNotes: string;
begin
  if not LoadPackagedReleaseNotes(ReleaseNotes) then
  begin
    exit;
  end;

  if Trim(ReleaseNotes) = '' then
  begin
    exit;
  end;

  MsgBox(
    'Fowan ' + InstalledVersion + ' -> {#AppVersion} 更新日志：' + #13#10 + #13#10 + ReleaseNotes,
    mbInformation,
    MB_OK);
end;

function IsSupportedWindowsBuild(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result :=
    (Version.Major > 10) or
    ((Version.Major = 10) and (Version.Build >= 19041));
end;

function ReadVersionPart(var Text: string): Integer;
var
  DotPos: Integer;
  Part: string;
begin
  DotPos := Pos('.', Text);
  if DotPos = 0 then
  begin
    Part := Text;
    Text := '';
  end
  else
  begin
    Part := Copy(Text, 1, DotPos - 1);
    Delete(Text, 1, DotPos);
  end;

  Result := StrToIntDef(Part, 0);
end;

function CompareVersions(Left: string; Right: string): Integer;
var
  Index: Integer;
  LeftPart: Integer;
  RightPart: Integer;
begin
  Result := 0;
  for Index := 1 to 4 do
  begin
    LeftPart := ReadVersionPart(Left);
    RightPart := ReadVersionPart(Right);

    if LeftPart < RightPart then
    begin
      Result := -1;
      exit;
    end;

    if LeftPart > RightPart then
    begin
      Result := 1;
      exit;
    end;
  end;
end;

function QueryInstalledVersion(var InstalledVersion: string): Boolean;
begin
  Result := RegQueryStringValue(
    HKLM64,
    '{#UninstallSubkey}',
    'DisplayVersion',
    InstalledVersion);

  if not Result then
  begin
    Result := RegQueryStringValue(
      HKLM,
      '{#UninstallSubkey}',
      'DisplayVersion',
      InstalledVersion);
  end;
end;

function InitializeSetup(): Boolean;
var
  InstalledVersion: string;
  VersionComparison: Integer;
  Answer: Integer;
begin
  Result := True;
  ExistingInstallation := False;

  if not IsSupportedWindowsBuild() then
  begin
    MsgBox(
      'Fowan 需要 Windows 10 版本 2004（build 19041）或更高版本。',
      mbCriticalError,
      MB_OK);
    Result := False;
    exit;
  end;

  if QueryInstalledVersion(InstalledVersion) then
  begin
    ExistingInstallation := True;
    VersionComparison := CompareVersions(InstalledVersion, '{#AppVersion}');
    if VersionComparison < 0 then
    begin
      Answer := MsgBox(
        '检测到已安装 Fowan ' + InstalledVersion + '。是否更新到 {#AppVersion}？',
        mbConfirmation,
        MB_YESNO or MB_DEFBUTTON1);
      if Answer <> IDYES then
      begin
        Result := False;
        exit;
      end;

      ShowUpdateReleaseNotes(InstalledVersion);
    end
    else
    begin
      Answer := MsgBox(
        '当前机器已安装 Fowan ' + InstalledVersion + '，该版本不低于当前安装包 {#AppVersion}。是否继续重新安装？',
        mbConfirmation,
        MB_YESNO or MB_DEFBUTTON2);
      if Answer <> IDYES then
      begin
        Result := False;
        exit;
      end;
    end;
  end;
end;

procedure InitializeWizard();
begin
  if ExistingInstallation then
  begin
    if RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Fowan') then
    begin
      WizardSelectTasks('autostart');
    end
    else
    begin
      WizardSelectTasks('!autostart');
    end;
  end;
end;

function ForceCloseInstalledFowanProcesses(): Boolean;
var
  ResultCode: Integer;
  ScriptPath: string;
  Params: string;
begin
  ExtractTemporaryFile('close-fowan-processes.ps1');
  ScriptPath := ExpandConstant('{tmp}\close-fowan-processes.ps1');
  Params :=
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '" ' +
    '-InstallRoot "' + ExpandConstant('{app}') + '"';

  Result := Exec(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Params,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) and (ResultCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if ExistingInstallation and not ForceCloseInstalledFowanProcesses() then
  begin
    Result :=
      '无法关闭当前安装目录中的 Fowan 进程。' + #13#10 +
      '请手动退出 Fowan 工具箱及其工具后重试。';
  end;
end;

function InitializeUninstall(): Boolean;
var
  Answer: Integer;
  Confirm: Integer;
begin
  Result := True;
  CleanUninstallRequested := False;

  Answer := MsgBox(
    '是否执行干净卸载？' + #13#10 + #13#10 +
    '选择“否”将只卸载程序并保留所有 Fowan 用户数据。' + #13#10 +
    '选择“是”将先把用户数据备份到公共桌面，然后删除所有本机用户的 Fowan 数据。',
    mbConfirmation,
    MB_YESNO or MB_DEFBUTTON2);

  if Answer = IDYES then
  begin
    Confirm := MsgBox(
      '干净卸载会处理所有正常用户目录下的 AppData\Local\Fowan。' + #13#10 + #13#10 +
      '卸载器会先生成公共桌面备份 zip；只有备份成功后才删除原始数据。是否继续？',
      mbConfirmation,
      MB_YESNO or MB_DEFBUTTON2);
    CleanUninstallRequested := Confirm = IDYES;
  end;
end;

procedure DeleteKnownDesktopShortcuts();
begin
  DeleteFile(ExpandConstant('{commondesktop}\Fowan.lnk'));
  DeleteFile(ExpandConstant('{userdesktop}\Fowan.lnk'));
end;

procedure RunCleanUninstallBackup();
var
  ResultCode: Integer;
  ScriptPath: string;
  Params: string;
begin
  ScriptPath := ExpandConstant('{app}\Installer\uninstall-clean-fowan-data.ps1');
  if not FileExists(ScriptPath) then
  begin
    MsgBox(
      '未找到 Fowan 用户数据备份脚本，已跳过干净卸载的数据删除步骤。用户数据已保留。',
      mbError,
      MB_OK);
    exit;
  end;

  Params :=
    '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" ' +
    '-BackupRoot "' + ExpandConstant('{commondesktop}') + '"';

  if not Exec(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Params,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    MsgBox(
      '无法启动用户数据备份程序，已跳过干净卸载的数据删除步骤。用户数据已保留。',
      mbError,
      MB_OK);
    exit;
  end;

  if ResultCode = 0 then
  begin
    MsgBox(
      'Fowan 用户数据已备份到公共桌面，并已删除原始用户数据。',
      mbInformation,
      MB_OK);
  end
  else if ResultCode = 2 then
  begin
    MsgBox(
      '未发现可备份的 Fowan 用户数据。卸载将继续。',
      mbInformation,
      MB_OK);
  end
  else
  begin
    MsgBox(
      'Fowan 用户数据备份失败，已跳过干净卸载的数据删除步骤。用户数据已保留。',
      mbError,
      MB_OK);
  end;
end;

function NeedsVcRedist(): Boolean;
var
  Installed: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(
    HKLM64,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64',
    'Installed',
    Installed) then
  begin
    Result := Installed <> 1;
  end;
end;

procedure InstallVcRedistIfNeeded();
var
  ResultCode: Integer;
begin
  if not NeedsVcRedist() then
  begin
    exit;
  end;

  WizardForm.StatusLabel.Caption := '正在安装 Microsoft Visual C++ 运行库...';
  WizardForm.StatusLabel.Update;

  if not Exec(
    ExpandConstant('{tmp}\vc_redist.x64.exe'),
    '/install /quiet /norestart',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    MsgBox(
      '无法启动 Microsoft Visual C++ 运行库安装程序。Fowan 可能无法在这台机器上启动。',
      mbError,
      MB_OK);
    exit;
  end;

  if (ResultCode <> 0) and (ResultCode <> 3010) then
  begin
    MsgBox(
      'Microsoft Visual C++ 运行库安装失败，退出码：' + IntToStr(ResultCode) + '。Fowan 可能无法在这台机器上启动。',
      mbError,
      MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not WizardIsTaskSelected('autostart') then
    begin
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Fowan');
    end;

    InstallVcRedistIfNeeded();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    DeleteKnownDesktopShortcuts();

    if CleanUninstallRequested then
    begin
      RunCleanUninstallBackup();
    end;
  end;
end;
