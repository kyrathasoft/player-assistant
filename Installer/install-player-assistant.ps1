param(
    [string]$PayloadDir = (Join-Path $PSScriptRoot 'payload'),
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'kyrathasoft\player-assistant'),
    [switch]$NoDesktopShortcut,
    [switch]$StartAfterInstall,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

$AppName = 'Player Assistant'
$Publisher = 'KyrathaSoft'
$ExecutableName = 'player-assistant.exe'
$payloadExecutablePath = Join-Path $PayloadDir $ExecutableName
if (!(Test-Path -LiteralPath $payloadExecutablePath -PathType Leaf)) {
    throw "Required installer payload executable is missing: $payloadExecutablePath"
}
$Version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($payloadExecutablePath).ProductVersion
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Installer payload executable does not declare a product version: $payloadExecutablePath"
}
$UninstallKeyPath = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\KyrathaSoft Player Assistant'
$EncryptedSidecarFileNames = @(
    'settings.local.json',
    'xp-passwords.json'
)

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function ConvertTo-ProcessArgument {
    param([Parameter(Mandatory = $true)][string]$Value)
    return '"' + ($Value -replace '"', '\"') + '"'
}

function Restart-Elevated {
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        (ConvertTo-ProcessArgument $PSCommandPath),
        '-PayloadDir',
        (ConvertTo-ProcessArgument $PayloadDir),
        '-InstallDir',
        (ConvertTo-ProcessArgument $InstallDir)
    )

    if ($NoDesktopShortcut) {
        $arguments += '-NoDesktopShortcut'
    }

    if ($StartAfterInstall) {
        $arguments += '-StartAfterInstall'
    }

    if ($Quiet) {
        $arguments += '-Quiet'
    }

    Start-Process -FilePath 'powershell.exe' -ArgumentList ($arguments -join ' ') -Verb RunAs -Wait
}

function Show-InstallMessage {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [string]$Title = $AppName
    )

    if ($Quiet) {
        Write-Host $Message
        return
    }

    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        $Message,
        $Title,
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}

function Write-Step {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host "[$AppName Installer] $Message"
}

function Assert-RequiredFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required $Description is missing: $Path"
    }

    if ((Get-Item -LiteralPath $Path).Length -le 0) {
        throw "Required $Description is empty: $Path"
    }
}

function Assert-RequiredDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (!(Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Required $Description is missing: $Path"
    }
}

function Assert-Payload {
    Assert-RequiredDirectory -Path $PayloadDir -Description 'installer payload directory'

    $requiredFiles = @(
        $ExecutableName,
        'settings.json',
        'magic-items.json',
        'settings.local.json',
        'xp-passwords.json',
        'keyword-index.json',
        'game-posts-key-terms.md',
        'sitemap.xml',
        'sitemap-keyword-urls.json',
        'release-manifest.json',
        'release-runtime-inventory.json',
        'release-provenance.json'
    )

    foreach ($fileName in $requiredFiles) {
        Assert-RequiredFile -Path (Join-Path $PayloadDir $fileName) -Description "payload $fileName"
    }

    Assert-RequiredFile -Path (Join-Path $PayloadDir '.playwright\node\win32_x64\node.exe') -Description 'payload Playwright node.exe'
    Assert-RequiredFile -Path (Join-Path $PayloadDir '.playwright\package\package.json') -Description 'payload Playwright package.json'
    Assert-RequiredFile -Path (Join-Path $PayloadDir '.playwright\package\browsers.json') -Description 'payload Playwright browsers.json'

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PayloadDir $ExecutableName))
    if ($versionInfo.ProductVersion -ne $Version) {
        throw "Payload executable product version '$($versionInfo.ProductVersion)' did not match expected version $Version."
    }
}

function Copy-PayloadToStaging {
    param([Parameter(Mandatory = $true)][string]$StagingDir)

    New-Item -ItemType Directory -Force -Path $StagingDir | Out-Null
    Get-ChildItem -LiteralPath $PayloadDir -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $StagingDir -Recurse -Force
    }
}

function Grant-AppDirectoryAccess {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $usersSid = '*S-1-5-32-545'
    & icacls.exe $Directory /grant "${usersSid}:(OI)(CI)M" /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to grant standard Users modify access to $Directory."
    }
}

function Protect-EncryptedSidecars {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $systemSid = '*S-1-5-18'
    $administratorsSid = '*S-1-5-32-544'
    $usersSid = '*S-1-5-32-545'

    foreach ($fileName in $EncryptedSidecarFileNames) {
        $path = Join-Path $Directory $fileName
        Assert-RequiredFile -Path $path -Description "installed encrypted sidecar $fileName"
        Set-ItemProperty -LiteralPath $path -Name IsReadOnly -Value $true

        & icacls.exe $path /inheritance:r /grant:r "${systemSid}:F" "${administratorsSid}:F" "${usersSid}:RX" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to restrict standard Users write access to $path."
        }
    }
}

function Assert-ProtectedEncryptedSidecars {
    param([Parameter(Mandatory = $true)][string]$Directory)

    foreach ($fileName in $EncryptedSidecarFileNames) {
        $path = Join-Path $Directory $fileName
        Assert-RequiredFile -Path $path -Description "installed encrypted sidecar $fileName"
        if (!(Get-Item -LiteralPath $path).IsReadOnly) {
            throw "Installed encrypted sidecar $fileName was not marked read-only."
        }
    }
}

function New-Shortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $shortcutDirectory = Split-Path -Parent $ShortcutPath
    New-Item -ItemType Directory -Force -Path $shortcutDirectory | Out-Null

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = "$TargetPath,0"
    $shortcut.Description = "$AppName $Version"
    $shortcut.Save()
}

function Write-Uninstaller {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $uninstallPath = Join-Path $Directory 'uninstall-player-assistant.ps1'
    $script = @"
param([switch]`$Quiet)
`$ErrorActionPreference = 'Stop'
`$installDir = Split-Path -Parent `$PSCommandPath
`$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'KyrathaSoft\Player Assistant.lnk'
`$desktopShortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Player Assistant.lnk'
Remove-Item -LiteralPath 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\KyrathaSoft Player Assistant' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath `$startMenuShortcut -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath `$desktopShortcut -Force -ErrorAction SilentlyContinue
`$startMenuFolder = Split-Path -Parent `$startMenuShortcut
if ((Test-Path -LiteralPath `$startMenuFolder) -and -not (Get-ChildItem -LiteralPath `$startMenuFolder -Force | Select-Object -First 1)) {
    Remove-Item -LiteralPath `$startMenuFolder -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 300
Remove-Item -LiteralPath `$installDir -Recurse -Force
if (-not `$Quiet) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show('Player Assistant has been uninstalled.', 'Player Assistant', 'OK', 'Information') | Out-Null
}
"@

    Set-Content -LiteralPath $uninstallPath -Value $script -Encoding UTF8
}

function Register-UninstallEntry {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $executablePath = Join-Path $Directory $ExecutableName
    $uninstallPath = Join-Path $Directory 'uninstall-player-assistant.ps1'
    $estimatedSizeKb = [int]((Get-ChildItem -LiteralPath $Directory -Recurse -Force -File | Measure-Object -Property Length -Sum).Sum / 1KB)

    New-Item -Path $UninstallKeyPath -Force | Out-Null
    New-ItemProperty -Path $UninstallKeyPath -Name 'DisplayName' -Value $AppName -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $UninstallKeyPath -Name 'DisplayVersion' -Value $Version -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $UninstallKeyPath -Name 'Publisher' -Value $Publisher -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $UninstallKeyPath -Name 'InstallLocation' -Value $Directory -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $UninstallKeyPath -Name 'DisplayIcon' -Value $executablePath -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $UninstallKeyPath -Name 'EstimatedSize' -Value $estimatedSizeKb -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $UninstallKeyPath -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $UninstallKeyPath -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $UninstallKeyPath -Name 'UninstallString' -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallPath`"" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $UninstallKeyPath -Name 'QuietUninstallString' -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallPath`" -Quiet" -PropertyType String -Force | Out-Null
}

function Install-PlayerAssistant {
    Assert-Payload

    $resolvedInstallDir = [System.IO.Path]::GetFullPath($InstallDir)
    $installParent = Split-Path -Parent $resolvedInstallDir
    New-Item -ItemType Directory -Force -Path $installParent | Out-Null

    $stagingDir = Join-Path $installParent ("player-assistant.installing.{0}" -f ([Guid]::NewGuid().ToString('N')))
    $backupDir = Join-Path $installParent ("player-assistant.backup.{0}" -f ([Guid]::NewGuid().ToString('N')))
    $movedExisting = $false

    try {
        Write-Step "Copying application files..."
        Copy-PayloadToStaging -StagingDir $stagingDir
        Write-Uninstaller -Directory $stagingDir

        Write-Step "Installing to $resolvedInstallDir..."
        if (Test-Path -LiteralPath $resolvedInstallDir) {
            Move-Item -LiteralPath $resolvedInstallDir -Destination $backupDir -Force
            $movedExisting = $true
        }

        Move-Item -LiteralPath $stagingDir -Destination $resolvedInstallDir -Force

        Write-Step 'Applying application directory permissions...'
        Grant-AppDirectoryAccess -Directory $resolvedInstallDir
        Protect-EncryptedSidecars -Directory $resolvedInstallDir
        Assert-ProtectedEncryptedSidecars -Directory $resolvedInstallDir

        $executablePath = Join-Path $resolvedInstallDir $ExecutableName
        Write-Step 'Creating shortcuts...'
        New-Shortcut `
            -ShortcutPath (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'KyrathaSoft\Player Assistant.lnk') `
            -TargetPath $executablePath `
            -WorkingDirectory $resolvedInstallDir

        if (!$NoDesktopShortcut) {
            New-Shortcut `
                -ShortcutPath (Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Player Assistant.lnk') `
                -TargetPath $executablePath `
                -WorkingDirectory $resolvedInstallDir
        }

        Write-Step 'Registering uninstall entry...'
        Register-UninstallEntry -Directory $resolvedInstallDir

        if ($movedExisting -and (Test-Path -LiteralPath $backupDir)) {
            Remove-Item -LiteralPath $backupDir -Recurse -Force
        }

        if ($StartAfterInstall) {
            Start-Process -FilePath $executablePath -WorkingDirectory $resolvedInstallDir
        }

        Show-InstallMessage "$AppName $Version was installed to:`r`n$resolvedInstallDir"
    }
    catch {
        if (Test-Path -LiteralPath $stagingDir) {
            Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
        }

        if ($movedExisting -and !(Test-Path -LiteralPath $resolvedInstallDir) -and (Test-Path -LiteralPath $backupDir)) {
            Move-Item -LiteralPath $backupDir -Destination $resolvedInstallDir -Force
        }

        throw
    }
}

try {
    if (!(Test-IsAdministrator)) {
        Restart-Elevated
        return
    }

    Install-PlayerAssistant
}
catch {
    if ($Quiet) {
        Write-Error $_
    }
    else {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message,
            "$AppName Installer Error",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    }

    exit 1
}
