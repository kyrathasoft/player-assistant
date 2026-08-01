[CmdletBinding()]
param(
    [string]$DreamHostHost = 'pdx1-shared-a1-13.dreamhost.com',
    [string]$DreamHostUser = 'dh_4gg2za',
    [string]$HostAlias = 'player-assistant-dreamhost',
    [string]$KeyPath = (Join-Path $env:USERPROFILE '.ssh\dreamhost_player_assistant'),
    [switch]$DoNotOpenDreamHostPanel
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Command {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (!(Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required Windows OpenSSH command not found: $Name. Install the OpenSSH Client optional feature and rerun this script."
    }
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Enable-SshAgent {
    $service = Get-Service -Name 'ssh-agent' -ErrorAction Stop
    if ($service.StartType -ne 'Disabled' -and $service.Status -eq 'Running') {
        return
    }

    if (Test-Administrator) {
        Set-Service -Name 'ssh-agent' -StartupType Automatic
        Start-Service -Name 'ssh-agent'
        return
    }

    $elevatedCommand = @"
Set-Service -Name 'ssh-agent' -StartupType Automatic -ErrorAction Stop
Start-Service -Name 'ssh-agent' -ErrorAction Stop
"@
    $process = Start-Process -FilePath 'powershell.exe' -Verb RunAs -Wait -PassThru `
        -ArgumentList @('-NoProfile', '-Command', $elevatedCommand)
    if ($process.ExitCode -ne 0) {
        throw 'Administrator approval was not granted for the Windows OpenSSH Authentication Agent.'
    }

    $service = Get-Service -Name 'ssh-agent'
    if ($service.Status -ne 'Running') {
        throw 'The Windows OpenSSH Authentication Agent did not start.'
    }
}

function Set-ManagedSshConfig {
    param(
        [Parameter(Mandatory = $true)][string]$ConfigPath,
        [Parameter(Mandatory = $true)][string]$Alias,
        [Parameter(Mandatory = $true)][string]$RemoteHost,
        [Parameter(Mandatory = $true)][string]$RemoteUser,
        [Parameter(Mandatory = $true)][string]$IdentityPath
    )

    $startMarker = '# BEGIN player-assistant DreamHost access'
    $endMarker = '# END player-assistant DreamHost access'
    $normalizedIdentity = $IdentityPath.Replace('\', '/')
    $managedBlock = @"
$startMarker
Host $Alias
    HostName $RemoteHost
    User $RemoteUser
    Port 22
    IdentityFile "$normalizedIdentity"
    IdentitiesOnly yes
$endMarker
"@
    $existing = if (Test-Path -LiteralPath $ConfigPath) {
        [IO.File]::ReadAllText($ConfigPath)
    } else {
        ''
    }
    $pattern = '(?ms)^' + [regex]::Escape($startMarker) + '\r?\n.*?^' `
        + [regex]::Escape($endMarker) + '\r?\n?'
    $withoutManagedBlock = [regex]::Replace($existing, $pattern, '').TrimEnd()
    $separator = if ($withoutManagedBlock.Length -gt 0) { "`r`n`r`n" } else { '' }
    [IO.File]::WriteAllText(
        $ConfigPath,
        $withoutManagedBlock + $separator + $managedBlock + "`r`n",
        [Text.UTF8Encoding]::new($false))
}

function Test-DreamHostAccess {
    param(
        [Parameter(Mandatory = $true)][string]$Alias,
        [Parameter(Mandatory = $true)][string]$RemoteUser
    )

    $remoteCheck = @"
test -d /home/$RemoteUser/bryanmiller.us/scarlethorizons/pwa &&
test -d /home/$RemoteUser/bryanmiller.us/scarlethorizons/api &&
test -d /home/$RemoteUser/player-assistant-broker &&
printf READY
"@
    $result = & ssh -o BatchMode=yes -o ConnectTimeout=15 `
        -o StrictHostKeyChecking=accept-new $Alias $remoteCheck 2>&1
    return $LASTEXITCODE -eq 0 -and (($result -join "`n").Trim() -eq 'READY')
}

if ($env:OS -ne 'Windows_NT') {
    throw 'This setup script supports Windows only.'
}

Assert-Command -Name 'ssh'
Assert-Command -Name 'ssh-add'
Assert-Command -Name 'ssh-keygen'
Enable-SshAgent

$sshDirectory = Split-Path -Parent $KeyPath
if (!(Test-Path -LiteralPath $sshDirectory)) {
    New-Item -ItemType Directory -Path $sshDirectory | Out-Null
}

if (!(Test-Path -LiteralPath $KeyPath)) {
    Write-Host 'Creating a dedicated DreamHost SSH key. Choose a strong passphrase when prompted.'
    & ssh-keygen -t ed25519 -a 100 -f $KeyPath -C 'player-assistant DreamHost'
    if ($LASTEXITCODE -ne 0) {
        throw 'SSH key generation failed.'
    }
}

$publicKeyPath = "$KeyPath.pub"
if (!(Test-Path -LiteralPath $publicKeyPath)) {
    & ssh-keygen -y -f $KeyPath | Set-Content -LiteralPath $publicKeyPath -Encoding ascii
    if ($LASTEXITCODE -ne 0) {
        throw 'The public SSH key could not be recovered from the private key.'
    }
}

& ssh-add $KeyPath
if ($LASTEXITCODE -ne 0) {
    throw 'The DreamHost SSH key could not be loaded into the Windows SSH agent.'
}

$sshConfigPath = Join-Path $sshDirectory 'config'
Set-ManagedSshConfig `
    -ConfigPath $sshConfigPath `
    -Alias $HostAlias `
    -RemoteHost $DreamHostHost `
    -RemoteUser $DreamHostUser `
    -IdentityPath $KeyPath

if (!(Test-DreamHostAccess -Alias $HostAlias -RemoteUser $DreamHostUser)) {
    Write-Host ''
    Write-Host 'Add this public key to the DreamHost user before continuing:' -ForegroundColor Yellow
    Write-Host (Get-Content -Raw -LiteralPath $publicKeyPath).Trim()
    Write-Host ''
    if (!$DoNotOpenDreamHostPanel) {
        Start-Process 'https://panel.dreamhost.com/index.cgi?tree=users.users'
    }
    Read-Host 'After the public key is installed for the DreamHost user, press Enter to verify access'
}

if (!(Test-DreamHostAccess -Alias $HostAlias -RemoteUser $DreamHostUser)) {
    throw "SSH access is not ready. Confirm that the displayed public key is installed for DreamHost user '$DreamHostUser', then rerun this script."
}

Write-Host ''
Write-Host 'DreamHost access is ready for Codex.' -ForegroundColor Green
Write-Host "SSH alias: $HostAlias"
Write-Host "Private key: $KeyPath"
Write-Host 'Verified deploy targets:'
Write-Host "  /home/$DreamHostUser/bryanmiller.us/scarlethorizons/pwa"
Write-Host "  /home/$DreamHostUser/bryanmiller.us/scarlethorizons/api"
Write-Host "  /home/$DreamHostUser/player-assistant-broker"
Write-Host 'No website password, API token, or private key was stored in the repository.'
