; Build with scripts/build_installer.py. StageDir and BuildVersion are required.
; ChineseSimplified.isl is from jrsoftware/issrc commit 2527da77ff7983514f535d19391a748c5d898336.
; Its redistribution terms are retained in InnoSetup-LICENSE.txt.
#ifndef StageDir
  #error StageDir must point to a freshly built payload
#endif
#ifndef BuildVersion
  #error BuildVersion must identify this installer build
#endif

[Setup]
AppId={{18F9206A-0782-4F0F-9EE8-3F3ED3C18561}
AppName=PartyPilot
AppVersion={#BuildVersion}
DefaultDirName={localappdata}\Programs\PartyPilot
DefaultGroupName=PartyPilot
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
UninstallDisplayIcon={app}\desktop\PartyPilot.Desktop.exe
OutputBaseFilename=PartyPilot-Setup-{#BuildVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "{#AddBackslash(SourcePath)}ChineseSimplified.isl"

[Files]
Source: "{#StageDir}\desktop\PartyPilot.Desktop.exe"; DestDir: "{app}\desktop"; Flags: ignoreversion
Source: "{#StageDir}\bridge\PartyPilot.Bridge.exe"; DestDir: "{app}\bridge"; Flags: ignoreversion
Source: "{#StageDir}\probe\PartyPilot.Probe.exe"; DestDir: "{app}\probe"; Flags: ignoreversion
Source: "{#StageDir}\PartyPilot-LiveFollow.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\PartyPilot-ClientLaunch.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\native\PartyPilot.PrivateSlots.dll"; DestDir: "{app}\native"; Flags: ignoreversion
Source: "{#StageDir}\native\PartyPilot.MultiInstance.dll"; DestDir: "{app}\native"; Flags: ignoreversion

[Icons]
Name: "{group}\PartyPilot"; Filename: "{app}\desktop\PartyPilot.Desktop.exe"; WorkingDir: "{app}\desktop"
Name: "{userdesktop}\PartyPilot"; Filename: "{app}\desktop\PartyPilot.Desktop.exe"; WorkingDir: "{app}\desktop"

[Run]
Filename: "{app}\desktop\PartyPilot.Desktop.exe"; Description: "启动 PartyPilot"; Flags: nowait postinstall skipifsilent
