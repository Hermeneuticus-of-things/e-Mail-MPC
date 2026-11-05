param(
    [Parameter(Mandatory = $true)]
    [string] $InstallerPath,

    [Parameter(Mandatory = $true)]
    [string] $ExpectedVersion,

    [string] $PriorInstallerPath,

    [Parameter(Mandatory = $true)]
    [string] $PortableArchivePath
)

$ErrorActionPreference = 'Stop'

if ($env:GITHUB_ACTIONS -ne 'true') {
    throw 'This destructive installation test must run only on an ephemeral GitHub Actions runner.'
}

$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$priorInstaller = if ([string]::IsNullOrWhiteSpace($PriorInstallerPath)) {
    $null
}
else {
    (Resolve-Path -LiteralPath $PriorInstallerPath).Path
}
$portableArchive = (Resolve-Path -LiteralPath $PortableArchivePath).Path
$testId = [guid]::NewGuid().ToString('N')
$testRoot = Join-Path $env:RUNNER_TEMP "email-mpc-install-test-$testId"
$dataDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'eMail MPC\data'
$dataMarker = Join-Path $dataDirectory "install-validation-$testId.keep"
$portableDirectory = Join-Path $testRoot 'portable'
$installDirectories = @(
    (Join-Path $testRoot 'per-user'),
    (Join-Path $testRoot 'all-users')
)

function Invoke-Setup {
    param(
        [string] $SetupPath,
        [string] $InstallDirectory,
        [string] $ExpectedVersion,
        [switch] $AllUsers
    )

    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$InstallDirectory`"")
    if ($AllUsers) {
        $arguments += '/ALLUSERS'
    }
    else {
        $arguments += '/CURRENTUSER'
    }

    $setupProcess = Start-Process -FilePath $SetupPath -ArgumentList $arguments -Wait -PassThru
    if ($setupProcess.ExitCode -ne 0) {
        throw "Setup failed for '$InstallDirectory' with exit code $($setupProcess.ExitCode)."
    }

    $applicationPath = Join-Path $InstallDirectory 'EMailMPC.Desktop.exe'
    if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
        throw "Setup did not install the application at '$applicationPath'."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $InstallDirectory 'unins000.exe') -PathType Leaf)) {
        throw "Setup did not create an uninstaller at '$InstallDirectory'."
    }

    $registryHive = if ($AllUsers) { 'HKLM:' } else { 'HKCU:' }
    $uninstallKey = Join-Path $registryHive 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{76C2AF69-7A43-4D0E-9B72-3116B8763601}_is1'
    if (-not (Test-Path -LiteralPath $uninstallKey)) {
        throw "Setup did not register the installation at '$uninstallKey'."
    }

    $installedVersion = (Get-ItemProperty -LiteralPath $uninstallKey).DisplayVersion
    if ($installedVersion -ne $ExpectedVersion) {
        throw "Expected installed version '$ExpectedVersion' at '$uninstallKey', found '$installedVersion'."
    }
}

function Invoke-Uninstall {
    param([string] $InstallDirectory)

    $uninstaller = Join-Path $InstallDirectory 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        return
    }

    $uninstallProcess = Start-Process `
        -FilePath $uninstaller `
        -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
        -Wait `
        -PassThru
    if ($uninstallProcess.ExitCode -ne 0) {
        throw "Uninstall failed for '$InstallDirectory' with exit code $($uninstallProcess.ExitCode)."
    }
    if (Test-Path -LiteralPath (Join-Path $InstallDirectory 'EMailMPC.Desktop.exe')) {
        throw "Uninstall left application files in '$InstallDirectory'."
    }
}

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
    Set-Content -LiteralPath $dataMarker -Value 'preserve across program upgrade and uninstall'

    $perUserInstall = $installDirectories[0]
    if ($priorInstaller) {
        Invoke-Setup -SetupPath $priorInstaller -InstallDirectory $perUserInstall -ExpectedVersion '0.0.1'
    }
    Invoke-Setup -SetupPath $installer -InstallDirectory $perUserInstall -ExpectedVersion $ExpectedVersion
    Invoke-Setup -SetupPath $installer -InstallDirectory $perUserInstall -ExpectedVersion $ExpectedVersion
    if (-not (Test-Path -LiteralPath $dataMarker -PathType Leaf)) {
        throw 'A per-user install or same-version upgrade changed data stored outside the program directory.'
    }
    Invoke-Uninstall -InstallDirectory $perUserInstall
    if (-not (Test-Path -LiteralPath $dataMarker -PathType Leaf)) {
        throw 'Per-user uninstall removed data stored outside the program directory.'
    }

    $allUsersInstall = $installDirectories[1]
    if ($priorInstaller) {
        Invoke-Setup -SetupPath $priorInstaller -InstallDirectory $allUsersInstall -ExpectedVersion '0.0.1' -AllUsers
    }
    Invoke-Setup -SetupPath $installer -InstallDirectory $allUsersInstall -ExpectedVersion $ExpectedVersion -AllUsers
    Invoke-Setup -SetupPath $installer -InstallDirectory $allUsersInstall -ExpectedVersion $ExpectedVersion -AllUsers
    if (-not (Test-Path -LiteralPath $dataMarker -PathType Leaf)) {
        throw 'An all-users install or same-version upgrade changed per-user mail data.'
    }
    Invoke-Uninstall -InstallDirectory $allUsersInstall
    if (-not (Test-Path -LiteralPath $dataMarker -PathType Leaf)) {
        throw 'All-users uninstall removed per-user mail data.'
    }

    Expand-Archive -LiteralPath $portableArchive -DestinationPath $portableDirectory
    $portableData = Join-Path $portableDirectory 'data'
    New-Item -ItemType Directory -Path $portableData -Force | Out-Null
    $portableMarker = Join-Path $portableData 'install-validation.keep'
    Set-Content -LiteralPath $portableMarker -Value 'preserve during portable replacement'
    Expand-Archive -LiteralPath $portableArchive -DestinationPath $portableDirectory -Force
    if (-not (Test-Path -LiteralPath $portableMarker -PathType Leaf)) {
        throw 'Replacing portable program files did not preserve the portable data directory.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $portableDirectory 'portable.mode') -PathType Leaf)) {
        throw 'The portable package is missing its portable mode marker.'
    }

    $upgradeScenario = if ($priorInstaller) { 'previous-version upgrade' } else { 'same-version reinstall' }
    Write-Output "Windows install validation passed: per-user install/$upgradeScenario/uninstall, all-users install/$upgradeScenario/uninstall, and portable replacement preserve data."
}
finally {
    foreach ($installDirectory in $installDirectories) {
        try {
            Invoke-Uninstall -InstallDirectory $installDirectory
        }
        catch {
            Write-Warning "Could not clean up test installation '$installDirectory': $($_.Exception.Message)"
        }
    }
    if (Test-Path -LiteralPath $dataMarker) {
        Remove-Item -LiteralPath $dataMarker -Force
    }
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
