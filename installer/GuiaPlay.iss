#ifndef MyAppVersion
  #error MyAppVersion must be provided by build-release.ps1
#endif
#ifndef NumericVersion
  #error NumericVersion must be provided by build-release.ps1
#endif
#ifndef SourceDir
  #error SourceDir must be provided by build-release.ps1
#endif
#ifndef ReleaseDir
  #error ReleaseDir must be provided by build-release.ps1
#endif
#ifndef TargetRid
  #error TargetRid must be provided by build-release.ps1
#endif
#ifndef AllowedArchitectures
  #error AllowedArchitectures must be provided by build-release.ps1
#endif
#ifndef SetupBaseName
  #error SetupBaseName must be provided by build-release.ps1
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
MinVersion=10.0.17763
ArchitecturesAllowed={#AllowedArchitectures}
#ifdef InstallIn64BitMode
ArchitecturesInstallIn64BitMode={#InstallIn64BitMode}
#endif
OutputDir={#ReleaseDir}
OutputBaseFilename={#SetupBaseName}
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
ChangesAssociations=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos adicionais:"; Flags: unchecked
Name: "windowsintegration"; Description: "Integrar GuiaPlay ao menu Abrir com do Windows"; GroupDescription: "Integração opcional com o Windows:"; Flags: unchecked
Name: "contextmenu"; Description: "Adicionar Abrir com GuiaPlay ao menu de contexto"; GroupDescription: "Integração opcional com o Windows:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GuiaPlay"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\GuiaPlay"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "GuiaPlay"; Flags: uninsdeletekey; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe"; ValueType: string; ValueName: "ApplicationCompany"; ValueData: "GuiaSys Studio"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Reprodutor de mídia para operação em múltiplas telas."; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".mp4"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".mkv"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".avi"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".mov"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".wmv"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".webm"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".m4v"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".mpg"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".mpeg"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".ts"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".m2ts"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".3gp"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".ogv"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".mp3"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".wav"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".ogg"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".flac"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".aac"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".m4a"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".wma"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".opus"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".aiff"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\Applications\GuiaPlay.exe\SupportedTypes"; ValueType: none; ValueName: ".alac"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\GuiaPlay.Video"; ValueType: string; ValueData: "Mídia de vídeo do GuiaPlay"; Flags: uninsdeletekey; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\GuiaPlay.Video"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "Mídia de vídeo do GuiaPlay"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\GuiaPlay.Video\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\GuiaPlay.Video\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\GuiaPlay.Audio"; ValueType: string; ValueData: "Mídia de áudio do GuiaPlay"; Flags: uninsdeletekey; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\GuiaPlay.Audio"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "Mídia de áudio do GuiaPlay"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\GuiaPlay.Audio\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\GuiaPlay.Audio\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay"; Flags: uninsdeletekey; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "GuiaPlay"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Reprodutor de mídia para operação em múltiplas telas."; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.mp4\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Video"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mp4"; ValueData: "GuiaPlay.Video"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.mkv\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Video"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mkv"; ValueData: "GuiaPlay.Video"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.avi\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Video"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".avi"; ValueData: "GuiaPlay.Video"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.mov\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Video"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mov"; ValueData: "GuiaPlay.Video"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.wmv\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Video"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wmv"; ValueData: "GuiaPlay.Video"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.webm\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Video"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".webm"; ValueData: "GuiaPlay.Video"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.m4v\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Video"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m4v"; ValueData: "GuiaPlay.Video"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.mpg\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Video"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mpg"; ValueData: "GuiaPlay.Video"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.mpeg\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Video"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mpeg"; ValueData: "GuiaPlay.Video"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.ts\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Video"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ts"; ValueData: "GuiaPlay.Video"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.m2ts\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Video"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m2ts"; ValueData: "GuiaPlay.Video"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.3gp\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Video"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".3gp"; ValueData: "GuiaPlay.Video"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.ogv\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Video"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ogv"; ValueData: "GuiaPlay.Video"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Audio"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mp3"; ValueData: "GuiaPlay.Audio"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Audio"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wav"; ValueData: "GuiaPlay.Audio"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Audio"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ogg"; ValueData: "GuiaPlay.Audio"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Audio"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".flac"; ValueData: "GuiaPlay.Audio"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.aac\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Audio"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".aac"; ValueData: "GuiaPlay.Audio"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Audio"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".m4a"; ValueData: "GuiaPlay.Audio"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.wma\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Audio"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wma"; ValueData: "GuiaPlay.Audio"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.opus\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Audio"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".opus"; ValueData: "GuiaPlay.Audio"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.aiff\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Audio"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".aiff"; ValueData: "GuiaPlay.Audio"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\.alac\OpenWithProgids"; ValueType: none; ValueName: "GuiaPlay.Audio"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations"; ValueType: string; ValueName: ".alac"; ValueData: "GuiaPlay.Audio"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "GuiaPlay"; ValueData: "Software\Clients\Media\GuiaPlay\Capabilities"; Flags: uninsdeletevalue; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\GuiaPlay.exe"; ValueType: string; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\GuiaPlay.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Tasks: windowsintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.avi\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.avi\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.avi\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.wmv\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.wmv\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.wmv\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.webm\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.webm\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.webm\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.m4v\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.m4v\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.m4v\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mpg\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mpg\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mpg\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mpeg\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mpeg\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mpeg\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.ts\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.ts\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.ts\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.m2ts\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.m2ts\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.m2ts\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.3gp\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.3gp\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.3gp\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.ogv\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.ogv\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.ogv\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp3\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp3\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp3\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.wav\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.wav\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.wav\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.ogg\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.ogg\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.ogg\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.flac\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.flac\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.flac\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.aac\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.aac\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.aac\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.m4a\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.m4a\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.m4a\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.wma\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.wma\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.wma\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.opus\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.opus\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.opus\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.aiff\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.aiff\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.aiff\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.alac\shell\GuiaPlay.Open"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Abrir com GuiaPlay"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.alac\shell\GuiaPlay.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.alac\shell\GuiaPlay.Open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--remove-windows-integration"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "GuiaPlayWindowsIntegrationCleanup"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Executar GuiaPlay"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function DetectInstalledRid(const MarkerPath: String): String;
var
  Contents: AnsiString;
  Normalized: String;
begin
  Result := '';
  if not LoadStringFromFile(MarkerPath, Contents) then
  begin
    Result := 'unreadable';
    exit;
  end;

  Normalized := Lowercase(String(Contents));
  if Pos('win-x64', Normalized) > 0 then
    Result := 'win-x64'
  else if Pos('win-x86', Normalized) > 0 then
    Result := 'win-x86'
  else if Pos('guiasys.guiaplay', Normalized) > 0 then
    { Markers anteriores ao M10.3 eram exclusivamente x64 e não possuíam rid. }
    Result := 'win-x64'
  else
    Result := 'unknown';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  MarkerPath: String;
  InstalledRid: String;
begin
  Result := '';
  MarkerPath := AddBackslash(WizardDirValue) + 'install.json';
  if not FileExists(MarkerPath) then
    exit;

  InstalledRid := DetectInstalledRid(MarkerPath);
  if InstalledRid <> '{#TargetRid}' then
  begin
    Result := 'A pasta selecionada já contém uma instalação de arquitetura diferente (' +
      InstalledRid + '). Desinstale essa edição antes de instalar {#TargetRid}. ' +
      'Os dados do usuário em %LocalAppData%\GuiaSys\GuiaPlay serão preservados.';
  end;
end;
