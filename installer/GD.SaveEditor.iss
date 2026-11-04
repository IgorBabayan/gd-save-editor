; Inno Setup script for GD Save Editor.
; Built by .github/workflows/release-installer.yml, which passes:
;   /DAppVersion=x.y.z  /DSourceDir=<publish folder>  /DOutputDir=<output folder>
; Local build example (after `dotnet publish GD.SaveEditor\GD.SaveEditor\GD.SaveEditor.csproj -c Release -r win-x64 --self-contained -o publish`):
;   iscc /DAppVersion=0.1.0 /DSourceDir=..\publish /DOutputDir=..\dist installer\GD.SaveEditor.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "GD Save Editor"
#define AppExeName "GD.SaveEditor.exe"
#define AppPublisher "IgorBabayan"
#define AppUrl "https://github.com/IgorBabayan/gd-save-editor"

[Setup]
AppId={{42275AB9-AC65-4528-A7CB-25AEA463B054}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
; No SetupIconFile yet: the project has no App.ico (add one here once it does).
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir={#OutputDir}
OutputBaseFilename=GD.SaveEditor-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Install per-user by default (no admin), user can choose all-users in the dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Auto-update runs this installer silently while the app may still be closing:
; close it via Restart Manager, reuse the previous install folder and per-user/all-users mode.
CloseApplications=force
UsePreviousAppDir=yes
UsePreviousPrivileges=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
; Auto-update (silent install started by the app with /RELAUNCH=1): start the new version again.
; runasoriginaluser keeps the app un-elevated even if the update needed admin rights.
Filename: "{app}\{#AppExeName}"; Flags: nowait runasoriginaluser; Check: ShouldRelaunch

[Code]
function ShouldRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;
