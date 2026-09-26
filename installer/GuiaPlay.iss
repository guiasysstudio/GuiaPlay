#ifndef MyAppVersion
  #error MyAppVersion must be provided by build-release.ps1
#endif
#ifndef NumericVersion
  #error NumericVersion must be provided by build-release.ps1
#endif
#ifndef SourceDir
  #error SourceDir must be provided by build-release.ps1
#endif
#ifndef MarkerPath
  #error MarkerPath must be provided by build-release.ps1
#endif
#ifndef ReleaseDir
  #error ReleaseDir must be provided by build-release.ps1
#endif

#define MyAppName "GuiaPlay"
#define MyAppPublisher "GuiaSys Studio"
#define MyAppExeName "GuiaPlay.exe"

[Setup]
AppId={{3F70953D-F837-4EC7-925F-592392C39E0A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppComments=GuiaPlay é uma solução de reprodução de mídia para múltiplas telas, desenvolvida para operação rápida e organizada.
DefaultDirName={localappdata}\Programs\GuiaPlay
DefaultGroupName=GuiaPlay
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#ReleaseDir}
OutputBaseFilename=GuiaPlay-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=Assets\GuiaPlay-Setup.ico
WizardImageFile=Assets\WizardImageFile.bmp
WizardSmallImageFile=Assets\WizardSmallImageFile.bmp
WizardImageStretch=no
SetupLogging=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#NumericVersion}
VersionInfoDescription=GuiaPlay Setup
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#NumericVersion}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos adicionais:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MarkerPath}"; DestDir: "{app}"; DestName: "install.json"; Flags: ignoreversion

[Icons]
Name: "{group}\GuiaPlay"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\GuiaPlay"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Executar GuiaPlay"; Flags: nowait postinstall skipifsilent unchecked
