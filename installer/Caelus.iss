; Caelus v1.9.0 Inno Setup 安装脚本
; 使用方法：安装 Inno Setup 6（https://jrsoftware.org/isinfo.php），然后用 ISCC.exe 编译此脚本

[Setup]
AppId={{A7B8C9D0-E1F2-4A5B-8C9D-0E1F2A3B4C5D}
AppName=Caelus
AppVersion=1.9.0
AppPublisher=zenjiro
AppPublisherURL=https://github.com/wxj-1019/Caelus
AppCopyright=Copyright 2026 zenjiro
DefaultDirName={autopf}\Caelus
DefaultGroupName=Caelus
DisableProgramGroupPage=yes
OutputDir=..\release
OutputBaseFilename=Caelus-1.9.0-Setup
Compression=lzma2/ultra64
SolidCompression=yes
; 安装需要管理员权限（Caelus 需要管理员权限运行）
PrivilegesRequired=admin
SetupIconFile=..\Caelus.ico
UninstallDisplayIcon={app}\Caelus.exe
; 中文支持
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=no
; 现代外观
WizardStyle=modern
WizardSizePercent=100

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："
Name: "startupicon"; Description: "开机时自动启动 Caelus"; GroupDescription: "附加选项："

[Files]
Source: "..\Caelus.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Caelus.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Caelus"; Filename: "{app}\Caelus.exe"; IconFilename: "{app}\Caelus.ico"
Name: "{group}\卸载 Caelus"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Caelus"; Filename: "{app}\Caelus.exe"; IconFilename: "{app}\Caelus.ico"; Tasks: desktopicon
; 开机自启通过计划任务实现，而非启动文件夹
Name: "{userstartup}\Caelus"; Filename: "{app}\Caelus.exe"; IconFilename: "{app}\Caelus.ico"; Tasks: startupicon

[Run]
; 安装完成后启动 Caelus
Filename: "{app}\Caelus.exe"; Description: "启动 Caelus"; Flags: nowait postinstall skipifsilent runascurrentuser

[Registry]
; 注册表根键（与 Caelus 代码一致）
Root: HKCU; Subkey: "Software\Caelus"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey

[Code]
// 安装前检查是否有运行中的 Caelus 实例
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  // 尝试发送退出信号
  Exec('powershell', '-NoProfile -Command "try{[System.Threading.EventWaitHandle]::OpenExisting(''Global\Caelus_Exit'').Set()}catch{}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
end;

// 卸载前停止运行中的实例
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('powershell', '-NoProfile -Command "try{[System.Threading.EventWaitHandle]::OpenExisting(''Global\Caelus_Exit'').Set()}catch{}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
  Exec('taskkill', '/f /im Caelus.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
