$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\backup-encryption.ps1')

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('pa-backup-encryption-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $source = Join-Path $root 'broker.sqlite'
    $encrypted = Join-Path $root 'broker.sqlite.enc'
    $restored = Join-Path $root 'restored.sqlite'
    $key = 'test-backup-encryption-key-with-sufficient-entropy'
    [IO.File]::WriteAllBytes($source, [Text.Encoding]::UTF8.GetBytes("SQLite format 3`0fixture data"))

    Protect-BrokerBackup -SourcePath $source -DestinationPath $encrypted -Secret $key
    Assert-Condition (Test-Path -LiteralPath $encrypted -PathType Leaf) 'The encrypted backup was not created.'
    Assert-Condition (-not ([Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($encrypted)).Contains('SQLite format 3'))) 'The encrypted backup exposes plaintext SQLite content.'

    Unprotect-BrokerBackup -SourcePath $encrypted -DestinationPath $restored -Secret $key
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $restoredHash = (Get-FileHash -LiteralPath $restored -Algorithm SHA256).Hash
    Assert-Condition ($sourceHash -eq $restoredHash) 'The encrypted backup did not restore byte-for-byte.'

    $php = Get-Command php -ErrorAction Stop
    $phpArguments = @()
    $localOpenSsl = Join-Path (Split-Path -Parent $php.Source) 'ext\php_openssl.dll'
    if (Test-Path -LiteralPath $localOpenSsl -PathType Leaf) {
        $phpArguments += @('-d', "extension=$localOpenSsl")
    }
    $phpRestored = Join-Path $root 'php-restored.sqlite'
    $phpEncrypted = Join-Path $root 'php-encrypted.sqlite.enc'
    $previousInteropValues = @{}
    foreach ($name in @('PA_BACKUP_CLASS', 'PA_BACKUP_SECRET', 'PA_BACKUP_SOURCE', 'PA_BACKUP_DESTINATION')) {
        $previousInteropValues[$name] = [Environment]::GetEnvironmentVariable($name)
    }
    try {
        $env:PA_BACKUP_CLASS = (Resolve-Path (Join-Path $PSScriptRoot '..\player-assistant-broker\BrokerOperations.php')).Path
        $env:PA_BACKUP_SECRET = $key
        $env:PA_BACKUP_SOURCE = $encrypted
        $env:PA_BACKUP_DESTINATION = $phpRestored
        $phpDecrypt = "require getenv('PA_BACKUP_CLASS'); BrokerBackupCipher::decryptFile(getenv('PA_BACKUP_SOURCE'), getenv('PA_BACKUP_DESTINATION'), getenv('PA_BACKUP_SECRET'));"
        & $php.Source @phpArguments -r $phpDecrypt
        Assert-Condition ($LASTEXITCODE -eq 0) 'PHP could not restore the PowerShell-encrypted backup.'
        Assert-Condition ($sourceHash -eq (Get-FileHash -LiteralPath $phpRestored -Algorithm SHA256).Hash) 'PHP restored different plaintext from the PowerShell-encrypted backup.'

        $env:PA_BACKUP_SOURCE = $source
        $env:PA_BACKUP_DESTINATION = $phpEncrypted
        $phpEncrypt = "require getenv('PA_BACKUP_CLASS'); BrokerBackupCipher::encryptFile(getenv('PA_BACKUP_SOURCE'), getenv('PA_BACKUP_DESTINATION'), getenv('PA_BACKUP_SECRET'));"
        & $php.Source @phpArguments -r $phpEncrypt
        Assert-Condition ($LASTEXITCODE -eq 0) 'PHP could not encrypt the broker backup.'
        Unprotect-BrokerBackup -SourcePath $phpEncrypted -DestinationPath $restored -Secret $key
        Assert-Condition ($sourceHash -eq (Get-FileHash -LiteralPath $restored -Algorithm SHA256).Hash) 'PowerShell could not restore the PHP-encrypted backup.'
    }
    finally {
        foreach ($name in $previousInteropValues.Keys) {
            [Environment]::SetEnvironmentVariable($name, $previousInteropValues[$name])
        }
    }

    $tampered = [IO.File]::ReadAllBytes($encrypted)
    $tampered[[Math]::Floor($tampered.Length / 2)] = $tampered[[Math]::Floor($tampered.Length / 2)] -bxor 1
    [IO.File]::WriteAllBytes($encrypted, $tampered)
    $rejected = $false
    try { Unprotect-BrokerBackup -SourcePath $encrypted -DestinationPath $restored -Secret $key }
    catch { $rejected = $_.Exception.Message -like '*authentication*' }
    Assert-Condition $rejected 'A tampered encrypted backup was not rejected.'

    Write-Output 'Broker backup encryption tests passed.'
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
