#if Ver < EncodeVer(6,3,0)
 #error Inno Setup 6.3 or newer is required (x64compatible).
#endif
#ifndef AppVersion
 #define AppVersion "0.1.9"
#endif
#ifndef SourceDir
 #define SourceDir "..\artifacts\publish"
#endif
#ifndef OutputDir
 #define OutputDir "..\artifacts"
#endif
[Setup]
AppId={{F7B6403A-93D9-4BE0-BA0F-EED3ECDB5C11}
AppName=X-Shard
AppVersion={#AppVersion}
AppVerName=X-Shard {#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoProductName=X-Shard
UninstallDisplayName=X-Shard
AppPublisher=sksdesign
AppCopyright=Copyright (C) 2026 Sven Klocke (sksdesign)
VersionInfoCompany=sksdesign
AppPublisherURL=https://sksdesign.de/
AppSupportURL=https://github.com/complicatiion/X-Shard
DefaultDirName={autopf}\X-Shard
UsePreviousAppDir=no
DefaultGroupName=X-Shard
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=X-Shard_v{#AppVersion}_win-x64_setup
SetupIconFile=..\src\XShard\Assets\app-icon.ico
; Wizard artwork (logo + green gradient), one file per DPI step (100/125/150/175/200 %); Setup picks the best match.
WizardImageFile=wizard\wizard-large-100.bmp,wizard\wizard-large-125.bmp,wizard\wizard-large-150.bmp,wizard\wizard-large-175.bmp,wizard\wizard-large-200.bmp
WizardSmallImageFile=wizard\wizard-small-100.bmp,wizard\wizard-small-125.bmp,wizard\wizard-small-150.bmp,wizard\wizard-small-175.bmp,wizard\wizard-small-200.bmp
UninstallDisplayIcon={app}\X-Shard.exe
; One legal page: all seven documents are merged at runtime into the standard license memo (see [Code]).
; LICENSE_DECLARATION.rtf is only the fallback content if merging fails.
LicenseFile=legal\LICENSE_DECLARATION.rtf
ChangesAssociations=yes
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
; English is the default installer language; German stays selectable in the language dialog.
LanguageDetectionMethod=none
ShowLanguageDialog=yes
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
[Messages]
english.WizardLicense=Legal Information
english.LicenseLabel=Please read the following documents before continuing.
english.LicenseLabel3=The X-Shard license declaration, EULA, privacy policy, legal notice, copyright notice, third-party components and the full GNU GPL v3 are shown below in sequence. Scroll to read all documents.
english.LicenseAccepted=I have read all documents and &accept the license terms
english.LicenseNotAccepted=I do &not accept
german.WizardLicense=Rechtliche Informationen
german.LicenseLabel=Bitte lesen Sie die folgenden Dokumente, bevor Sie fortfahren.
german.LicenseLabel3=Lizenzerklärung, EULA, Datenschutzerklärung, Impressum, Urheberrecht, Drittkomponenten und die vollständige GNU GPL v3 werden nacheinander angezeigt. Scrollen Sie, um alle Dokumente zu lesen. Die Dokumente liegen in englischer Sprache vor.
german.LicenseAccepted=Ich habe alle Dokumente gelesen und &akzeptiere die Lizenzbedingungen
german.LicenseNotAccepted=Ich &lehne ab
[CustomMessages]
english.ContextMenu=Explorer context menu for ISO files and folders (all users)
german.ContextMenu=Explorer-Kontextmenü für ISO-Dateien und Ordner (alle Benutzer)
english.ExtractVerb=Extract with X-Shard
german.ExtractVerb=Mit X-Shard extrahieren
english.ConvertVerb=Convert with X-Shard
german.ConvertVerb=Mit X-Shard konvertieren
english.CreateVerb=Create XISO with X-Shard
german.CreateVerb=XISO mit X-Shard erstellen
english.Integration=Windows integration:
german.Integration=Windows-Integration:
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "contextmenu"; Description: "{cm:ContextMenu}"; GroupDescription: "{cm:Integration}"
[Registry]
; Classic Explorer verbs (Windows 11: "Show more options"). SystemFileAssociations keeps the default ISO handler ("Mount") untouched.
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.iso\shell\XShard.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:ExtractVerb}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.iso\shell\XShard.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\X-Shard.exe"",0"; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.iso\shell\XShard.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\X-Shard.exe"" --extract ""%1"""; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.iso\shell\XShard.Convert"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:ConvertVerb}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.iso\shell\XShard.Convert"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\X-Shard.exe"",0"; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.iso\shell\XShard.Convert\command"; ValueType: string; ValueName: ""; ValueData: """{app}\X-Shard.exe"" --convert ""%1"""; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.xiso\shell\XShard.Extract"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:ExtractVerb}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.xiso\shell\XShard.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\X-Shard.exe"",0"; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.xiso\shell\XShard.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\X-Shard.exe"" --extract ""%1"""; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.xiso\shell\XShard.Convert"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:ConvertVerb}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.xiso\shell\XShard.Convert"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\X-Shard.exe"",0"; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.xiso\shell\XShard.Convert\command"; ValueType: string; ValueName: ""; ValueData: """{app}\X-Shard.exe"" --convert ""%1"""; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\Directory\shell\XShard.Create"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:CreateVerb}"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\Directory\shell\XShard.Create"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\X-Shard.exe"",0"; Tasks: contextmenu
Root: HKLM; Subkey: "Software\Classes\Directory\shell\XShard.Create\command"; ValueType: string; ValueName: ""; ValueData: """{app}\X-Shard.exe"" --create ""%1"""; Tasks: contextmenu
[Files]
Source: "legal\*.rtf"; Flags: dontcopy
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "portable.flag,data\*"
[Icons]
Name: "{group}\X-Shard"; Filename: "{app}\X-Shard.exe"; WorkingDir: "{app}"; IconFilename: "{app}\X-Shard.exe"; IconIndex: 0; AppUserModelID: "sksdesign.XShard"
Name: "{autodesktop}\X-Shard"; Filename: "{app}\X-Shard.exe"; WorkingDir: "{app}"; IconFilename: "{app}\X-Shard.exe"; IconIndex: 0; AppUserModelID: "sksdesign.XShard"; Tasks: desktopicon
[Run]
Filename: "{app}\X-Shard.exe"; Description: "{cm:LaunchProgram,X-Shard}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DocumentCount = 7;
  { Header written by installer\LegalDocuments.Build for every RTF; the body starts after it. }
  BodyMarker = '\f0\fs20 ';

function InitializeSetup(): Boolean;
begin
  Result := not WizardSilent;
  if not Result then MsgBox('Interactive installation is required to review the legal documents.', mbError, MB_OK);
end;

function DocumentName(Index: Integer): String;
begin
  case Index of
    0: Result := 'LICENSE_DECLARATION';
    1: Result := 'EULA';
    2: Result := 'PRIVACY_POLICY';
    3: Result := 'LEGAL_NOTICE';
    4: Result := 'COPYRIGHT';
    5: Result := 'THIRD_PARTY_LICENSES';
  else
    Result := 'GPL-3.0';
  end;
end;

{ Returns the RTF without its closing brace; KeepHeader keeps the font/color table of the first document. }
function RtfPart(const Rtf: AnsiString; KeepHeader: Boolean): AnsiString;
var
  Last, Start: Integer;
begin
  Last := Length(Rtf);
  while (Last > 0) and (Rtf[Last] <> '}') do Last := Last - 1;
  if KeepHeader then
    Result := Copy(Rtf, 1, Last - 1)
  else
  begin
    Start := Pos(BodyMarker, Rtf);
    if Start = 0 then
      Result := ''
    else
    begin
      Start := Start + Length(BodyMarker);
      Result := Copy(Rtf, Start, Last - Start);
    end;
  end;
end;

{ Merges all legal documents into one RTF shown in the standard license page (one confirmation). }
function MergedLegalRtf(): AnsiString;
var
  Index: Integer;
  Name: String;
  Rtf, Separator: AnsiString;
begin
  Result := '';
  Separator := '\pard\sa120\par\pard\qc\cf1 ________________________________________\cf0\par\pard\sa240\par ';
  for Index := 0 to DocumentCount - 1 do
  begin
    Name := DocumentName(Index) + '.rtf';
    ExtractTemporaryFile(Name);
    if not LoadStringFromFile(ExpandConstant('{tmp}\') + Name, Rtf) then
      RaiseException('Legal document could not be loaded: ' + Name);
    if Index = 0 then
      Result := RtfPart(Rtf, True)
    else
      Result := Result + Separator + RtfPart(Rtf, False);
  end;
  Result := Result + '}';
end;

procedure InitializeWizard();
begin
  WizardForm.LicenseMemo.RTFText := MergedLegalRtf();
end;
