#define AppName "eMail MPC"
#define AppVersion GetEnv("APP_VERSION")
#define PublishDir GetEnv("APP_PUBLISH_DIR")
#define OutputDir GetEnv("APP_OUTPUT_DIR")

[Setup]
AppId={{76C2AF69-7A43-4D0E-9B72-3116B8763601}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=eMail MPC
AppPublisherURL=https://github.com/Hermeneuticus-of-things/e-Mail-MPC
DefaultDirName={autopf}\eMail MPC
DefaultGroupName=eMail MPC
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=eMail-MPC-{#AppVersion}-windows-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\EMailMPC.Desktop.exe
Uninstallable=yes

[Messages]
WelcomeLabel2=This wizard installs eMail MPC for your Windows account or for all users. Existing mail data stays in the user's local data folder and is not removed when the app is upgraded or uninstalled.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\eMail MPC"; Filename: "{app}\EMailMPC.Desktop.exe"
Name: "{autodesktop}\eMail MPC"; Filename: "{app}\EMailMPC.Desktop.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\EMailMPC.Desktop.exe"; Description: "Launch eMail MPC"; Flags: postinstall nowait skipifsilent
