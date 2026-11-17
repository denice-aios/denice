; Copyright (c) Denice.ai. All rights reserved.
;
; Inno Setup 6 script for the per-user Windows installer (owner decision pkg-494745156d56: "A per-user Inno Setup
; installer (`denice-setup-<version>-<rid>.exe`, no admin prompt): installs into `%LOCALAPPDATA%\Programs\Denice`,
; adds the Start Menu shortcut and an Installed apps entry with uninstall, then leaves updating to Denice.").
; It also puts the install folder on the user's PATH (owner decision pkg-d4e90c3ce176 install-path) so a new terminal
; runs `denice`; uninstall takes exactly that entry out again.
;
; It packs one platform's extracted release zip (denice.exe + versions\<version>\**) into
; denice-setup-v<version>-<rid>.exe. Build it with eng\windows\build-installer.ps1, which passes these defines:
;   AppVersion  the full version, e.g. 0.1.108520000-pre-release+abc1234
;   Rid         win-x64 or win-arm64
;   Layout      the folder holding the extracted release zip
;   OutDir      where the setup file is written

#ifndef AppVersion
  #error AppVersion must be defined (/DAppVersion=<version>)
#endif
#ifndef Rid
  #error Rid must be defined (/DRid=win-x64 or /DRid=win-arm64)
#endif
#ifndef Layout
  #error Layout must be defined (/DLayout=<extracted release zip folder>)
#endif
#ifndef OutDir
  #define OutDir "."
#endif

; x64 builds run on x64 Windows and, through emulation, on Arm64 Windows 11 ("x64compatible", Inno Setup's
; recommended value for x64 binaries); Arm64 builds run on Arm64 Windows only.
#if Rid == "win-x64"
  #define Arch "x64compatible"
#elif Rid == "win-arm64"
  #define Arch "arm64"
#else
  #error Rid must be win-x64 or win-arm64
#endif

[Setup]
; Fixed for every Denice installer: Windows finds an existing install (and its uninstaller) by this id.
AppId={{7CE66CF4-3F91-454E-8A78-85E7192786D9}
AppName=Denice
AppVersion={#AppVersion}
AppVerName=Denice {#AppVersion}
AppPublisher=Denice.ai
; Per user, never an administrator prompt.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Denice
DisableDirPage=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed={#Arch}
ArchitecturesInstallIn64BitMode={#Arch}
UninstallDisplayName=Denice
UninstallDisplayIcon={app}\denice.exe
OutputDir={#OutDir}
OutputBaseFilename=denice-setup-v{#AppVersion}-{#Rid}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; A running Denice holds denice.exe open; Setup asks to close it (Restart Manager) instead of failing.
CloseApplications=yes
RestartApplications=no
; Tells running programs (Explorer, so new terminals) that the user's PATH changed, on install and on uninstall.
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; The whole release layout. Setup only adds and overwrites: it never deletes files it did not list, so versions
; Denice downloaded itself, and its denice.exe.ver / denice.exe.old handling, are left alone on an upgrade.
Source: "{#Layout}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Denice"; Filename: "{app}\denice.exe"; WorkingDir: "{app}"

[Registry]
; The user's PATH (HKCU, no administrator rights), only when the folder is not on it yet. Uninstall removes just this
; entry in CurUninstallStepChanged below; the value itself is never deleted with the entry.
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Check: NeedsAddPath(ExpandConstant('{app}'))

[UninstallDelete]
; Uninstall removes the whole install folder, including versions Denice downloaded after Setup ran. Denice's
; settings live in %LOCALAPPDATA%\Denice, outside this folder, and stay. The folder is fixed (DisableDirPage=yes),
; so this never points at a folder the user chose.
Type: filesandordirs; Name: "{app}"

[Code]
const
  EnvironmentKey = 'Environment';

// True when Dir is not yet an entry of the user's PATH (compared without regard to case).
function NeedsAddPath(Dir: string): Boolean;
var
  Paths: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Paths) then
    Result := True
  else
    Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Paths) + ';') = 0;
end;

// Removes the ';' + Dir that [Registry] appended and leaves every other character of the user's PATH as it was. When
// nothing else is left (Setup created the value), the value goes too.
procedure RemovePath(Dir: string);
var
  Paths: string;
  At: Integer;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Paths) then
    exit;
  Paths := ';' + Paths + ';';
  At := Pos(';' + Uppercase(Dir) + ';', Uppercase(Paths));
  if At = 0 then
    exit;
  Delete(Paths, At, Length(Dir) + 1);
  Paths := Copy(Paths, 2, Length(Paths) - 2);
  if Paths = '' then
    RegDeleteValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path')
  else
    RegWriteExpandStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Paths);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemovePath(ExpandConstant('{app}'));
end;
