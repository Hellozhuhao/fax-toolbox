#define AppName "Fax Toolbox"
#ifndef AppVersion
  #define AppVersion "0.2.0-beta.1"
#endif
#ifndef AppFileVersion
  #define AppFileVersion "0.2.0.1"
#endif
#ifndef PublishedDirectory
  #define PublishedDirectory "..\dist\windows-beta"
#endif
#define AppPublisher "Fax Toolbox"
#define AppUrl "https://github.com/Hellozhuhao/fax-toolbox"

[Setup]
#ifdef SmokeBuild
AppId={{F2E76E33-7D1C-4546-AF50-973A7AE62818}
UsePreviousAppDir=no
#else
AppId={{AC155AED-3AC0-43D2-A3B5-14A641539D5D}
#endif
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} Beta 版
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\Fax Toolbox
DefaultGroupName=Fax Toolbox
DisableDirPage=no
DisableProgramGroupPage=yes
DisableWelcomePage=yes
OutputDir=..\dist
OutputBaseFilename=FaxToolbox-Payload-v{#AppVersion}-win-x64
SetupIconFile=..\apps\windows-shell\assets\fax-toolbox.ico
UninstallDisplayIcon={app}\FaxToolbox.Windows.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
RestartApplications=no
VersionInfoVersion={#AppFileVersion}
VersionInfoDescription=Fax Toolbox Beta 安装引擎

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[CustomMessages]
zhcn.DesktopIcon=创建桌面快捷方式
en.DesktopIcon=Create a desktop shortcut

[Files]
Source: "{#PublishedDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Fax Toolbox"; Filename: "{app}\FaxToolbox.Windows.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Fax Toolbox"; Filename: "{app}\FaxToolbox.Windows.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\FaxToolbox.Windows.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
zhcn.InstallWebView2=正在安装 WebView2 运行环境…
en.InstallWebView2=Installing the WebView2 Runtime...
zhcn.LaunchApp=启动 Fax Toolbox
en.LaunchApp=Launch Fax Toolbox

[Code]
function WebView2Installed: Boolean;
var
  Version: String;
  Key: String;
begin
  Key := 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result :=
    (RegQueryStringValue(HKLM32, Key, 'pv', Version) and
     (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, Key, 'pv', Version) and
     (Version <> '') and (Version <> '0.0.0.0'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not WebView2Installed then
    Result := 'WebView2 Runtime is required. Please run the Fax Toolbox Beta setup bootstrapper.';
end;
