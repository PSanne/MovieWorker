; Windows-Installer für MovieWorker (Inno Setup 6).
; Aufruf: ISCC.exe /DAppVersion=1.0.0 build\installer.iss   (erwartet die fertige App im Ordner „publish“)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6E4C2B7A-3F1D-4B8E-9A52-7D0C1E5F9B31}
AppName=MovieWorker
AppVersion={#AppVersion}
AppVerName=MovieWorker {#AppVersion}
AppPublisher=MovieWorker
AppPublisherURL=https://github.com/PSanne/MovieWorker
AppSupportURL=https://github.com/PSanne/MovieWorker/issues
DefaultDirName={autopf}\MovieWorker
DefaultGroupName=MovieWorker
DisableProgramGroupPage=yes
OutputDir=..\installer-out
OutputBaseFilename=MovieWorker-Setup-v{#AppVersion}
SetupIconFile=..\src\MovieWorker.App\Assets\MovieWorker.ico
UninstallDisplayIcon={app}\MovieWorker.exe
UninstallDisplayName=MovieWorker {#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "assoc"; Description: "MovieWorker-Projekte (.mvproj) per Doppelklick mit MovieWorker öffnen"; GroupDescription: "Dateizuordnung:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\MovieWorker"; Filename: "{app}\MovieWorker.exe"
Name: "{group}\{cm:UninstallProgram,MovieWorker}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\MovieWorker"; Filename: "{app}\MovieWorker.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.mvproj"; ValueType: string; ValueName: ""; ValueData: "MovieWorker.Projekt"; Flags: uninsdeletevalue; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\MovieWorker.Projekt"; ValueType: string; ValueName: ""; ValueData: "MovieWorker-Projekt"; Flags: uninsdeletekey; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\MovieWorker.Projekt\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\MovieWorker.exe,0"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\MovieWorker.Projekt\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\MovieWorker.exe"" ""%1"""; Tasks: assoc

[Run]
Filename: "{app}\MovieWorker.exe"; Description: "{cm:LaunchProgram,MovieWorker}"; Flags: nowait postinstall skipifsilent
