; GP-I10: isolated local TEST installer, not a public release.
; Intentionally uses a distinct AppId AND install path for x64/x86.
; Never registers file associations or changes the existing GuiaPlay installation.
#ifndef MyAppVersion
  #error MyAppVersion must be supplied for a local test build.
#endif
#ifndef NumericVersion
  #error NumericVersion must be supplied for a local test build.
#endif
#ifndef SourceDir
  #error SourceDir must be supplied for a local test build.
#endif
#ifndef ReleaseDir
  #error ReleaseDir must be supplied for a local test build.
#endif
#ifndef TargetRid
  #error TargetRid must be supplied for a local test build.
#endif
#ifndef TestAppId
  #error TestAppId must be supplied for a local test build.
#endif
#ifndef TestInstallDir
  #error TestInstallDir must be supplied for a local test build.
#endif
#ifndef AllowedArchitectures
  #error AllowedArchitectures must be supplied for a local test build.
#endif
#ifndef SetupBaseName
  #error SetupBaseName must be supplied for a local test build.
#endif

#define TestProductName "GuiaPlay Interface 3.0 (TESTE)"

[Setup]
AppId={#TestAppId}
AppName={#TestProductName}
AppVersion={#MyAppVersion}
AppVerName={#TestProductName} {#MyAppVersion}
AppPublisher=GuiaSys Studio
AppComments=PREVIEW LOCAL. Not a production release. Uses shared GuiaPlay settings; back up user data.
DefaultDirName={localappdata}\Programs\{#TestInstallDir}
DefaultGroupName={#TestProductName} {#TargetRid}
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
SetupLogging=yes
UninstallDisplayIcon={app}\GuiaPlay.exe
UninstallDisplayName={#TestProductName} {#TargetRid}
VersionInfoVersion={#NumericVersion}
VersionInfoDescription=GuiaPlay Interface 3.0 Local TEST Setup
VersionInfoCompany=GuiaSys Studio
VersionInfoProductName={#TestProductName}
VersionInfoProductVersion={#NumericVersion}
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GuiaPlay Interface 3.0 (TESTE)"; Filename: "{app}\GuiaPlay.exe"; WorkingDir: "{app}"
; No desktop shortcut by default; never reuse the stable Guiaplay shortcut.
