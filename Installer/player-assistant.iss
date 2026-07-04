#ifndef PayloadDir
#define PayloadDir "..\Release\publish"
#endif

#ifndef OutputDir
#define OutputDir "..\Release\installer"
#endif

#ifndef Version
#define Version "0.9.0-hardening.5"
#endif

#ifndef InstallerVersion
#define InstallerVersion Version
#endif

[Setup]
AppId={{E8A3143B-6B8D-44EA-93D2-3AC69061D311}
AppName=Player Assistant
AppVersion={#Version}
AppVerName=Player Assistant {#Version}
AppPublisher=KyrathaSoft
DefaultDirName={autopf}\kyrathasoft\player-assistant
DefaultGroupName=KyrathaSoft
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=p-assist-{#InstallerVersion}
SetupIconFile=..\Assets\dragon-icon.ico
UninstallDisplayIcon={app}\player-assistant.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110
CloseApplications=yes
RestartApplications=no
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Player Assistant"; Filename: "{app}\player-assistant.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\Player Assistant"; Filename: "{app}\player-assistant.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\player-assistant.exe"; Description: "Launch Player Assistant"; Flags: nowait postinstall skipifsilent

[Code]
const
  RequiredRuntimeName = '.NET Desktop Runtime 10 x64';
  RuntimeDownloadUrl = 'https://dotnet.microsoft.com/en-us/download/dotnet/10.0';

function StartsWithText(const Value: string; const Prefix: string): Boolean;
begin
  Result := CompareText(Copy(Value, 1, Length(Prefix)), Prefix) = 0;
end;

function HasRuntimeDirectory(const DotNetRoot: string): Boolean;
var
  FindRec: TFindRec;
  SearchPath: string;
begin
  Result := False;
  if DotNetRoot = '' then
  begin
    Exit;
  end;

  SearchPath := AddBackslash(DotNetRoot) + 'shared\Microsoft.WindowsDesktop.App\10.*';
  if FindFirst(SearchPath, FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function HasRuntimeRegistryValue(): Boolean;
var
  RuntimeVersion: string;
begin
  Result :=
    RegQueryStringValue(
      HKLM64,
      'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
      'Version',
      RuntimeVersion)
    and StartsWithText(RuntimeVersion, '10.');
end;

function IsRequiredRuntimeInstalled(): Boolean;
begin
  Result :=
    HasRuntimeRegistryValue()
    or HasRuntimeDirectory(ExpandConstant('{commonpf64}\dotnet'))
    or HasRuntimeDirectory(ExpandConstant('{commonpf}\dotnet'));
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
  PromptResult: Integer;
begin
  Result := True;
  if IsRequiredRuntimeInstalled() then
  begin
    Exit;
  end;

  if WizardSilent() then
  begin
    Log(RequiredRuntimeName + ' is required but was not detected.');
    Result := False;
    Exit;
  end;

  PromptResult := MsgBox(
    'Player Assistant requires the ' + RequiredRuntimeName + '.' + #13#10 + #13#10 +
    'The required runtime was not detected on this computer. Setup can open the official Microsoft .NET download page for you now. Install the x64 .NET Desktop Runtime, then run Player Assistant setup again.' + #13#10 + #13#10 +
    'Open the Microsoft download page?',
    mbConfirmation,
    MB_YESNO);

  if PromptResult = IDYES then
  begin
    if not ShellExec('open', RuntimeDownloadUrl, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode) then
    begin
      MsgBox(
        'Setup could not open the Microsoft .NET download page automatically. Please visit:' + #13#10 + RuntimeDownloadUrl,
        mbError,
        MB_OK);
    end;
  end;

  Result := False;
end;
