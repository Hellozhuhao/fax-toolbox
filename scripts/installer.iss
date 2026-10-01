#define AppName "Fax Toolbox"
#define AppVersion "0.1.0-preview.2"
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
AppVerName={#AppName} 前瞻版
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\Fax Toolbox
DefaultGroupName=Fax Toolbox
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=yes
OutputDir=..\dist
OutputBaseFilename=FaxToolbox-Setup-v{#AppVersion}-win-x64
SetupIconFile=..\apps\windows-shell\assets\fax-toolbox.ico
UninstallDisplayIcon={app}\FaxToolbox.Windows.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=0.1.0.2
VersionInfoDescription=Fax Toolbox 前瞻版安装程序

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[CustomMessages]
zhcn.DesktopIcon=创建桌面快捷方式
en.DesktopIcon=Create a desktop shortcut

[Files]
Source: "..\dist\windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\.cache\installer-tools\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not WebView2Installed

[Icons]
Name: "{autoprograms}\Fax Toolbox"; Filename: "{app}\FaxToolbox.Windows.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Fax Toolbox"; Filename: "{app}\FaxToolbox.Windows.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "{cm:InstallWebView2}"; Flags: runhidden waituntilterminated; Check: not WebView2Installed
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
