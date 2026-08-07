Set-StrictMode -Version Latest

function Set-BrokerBackupPrivateAcl {
    param([Parameter(Mandatory = $true)][string]$Path)

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = [Security.AccessControl.FileSecurity]::new()
    $acl.SetOwner($identity)
    $acl.SetAccessRuleProtection($true, $false)
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            $identity,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.AccessControlType]::Allow))
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Get-BrokerBackupCipherKeys {
    param([Parameter(Mandatory = $true)][string]$Secret)

    if ($Secret.Length -lt 32) {
        throw 'BACKUP_ENCRYPTION_KEY must contain at least 32 characters.'
    }
    $sha512 = [Security.Cryptography.SHA512]::Create()
    try {
        $material = $sha512.ComputeHash([Text.Encoding]::UTF8.GetBytes($Secret))
        $encryptionKey = [byte[]]::new(32)
        $authenticationKey = [byte[]]::new(32)
        [Array]::Copy($material, 0, $encryptionKey, 0, 32)
        [Array]::Copy($material, 32, $authenticationKey, 0, 32)
        [Array]::Clear($material, 0, $material.Length)
        return [pscustomobject]@{
            Encryption = $encryptionKey
            Authentication = $authenticationKey
        }
    }
    finally {
        $sha512.Dispose()
    }
}

function Protect-BrokerBackup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$Secret
    )

    $keys = Get-BrokerBackupCipherKeys -Secret $Secret
    $plaintext = [IO.File]::ReadAllBytes($SourcePath)
    $aes = [Security.Cryptography.Aes]::Create()
    try {
        $aes.KeySize = 256
        $aes.Mode = [Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = $keys.Encryption
        $aes.GenerateIV()
        $encryptor = $aes.CreateEncryptor()
        try { $ciphertext = $encryptor.TransformFinalBlock($plaintext, 0, $plaintext.Length) }
        finally { $encryptor.Dispose() }

        $magic = [Text.Encoding]::ASCII.GetBytes('PABACKUPENCV1')
        $payload = [byte[]]::new($magic.Length + $aes.IV.Length + $ciphertext.Length)
        [Array]::Copy($magic, 0, $payload, 0, $magic.Length)
        [Array]::Copy($aes.IV, 0, $payload, $magic.Length, $aes.IV.Length)
        [Array]::Copy($ciphertext, 0, $payload, $magic.Length + $aes.IV.Length, $ciphertext.Length)
        $hmac = [Security.Cryptography.HMACSHA256]::new($keys.Authentication)
        try { $mac = $hmac.ComputeHash($payload) }
        finally { $hmac.Dispose() }

        $document = [byte[]]::new($payload.Length + $mac.Length)
        [Array]::Copy($payload, 0, $document, 0, $payload.Length)
        [Array]::Copy($mac, 0, $document, $payload.Length, $mac.Length)
        [IO.File]::WriteAllBytes($DestinationPath, $document)
        Set-BrokerBackupPrivateAcl -Path $DestinationPath
    }
    finally {
        $aes.Dispose()
        [Array]::Clear($plaintext, 0, $plaintext.Length)
        [Array]::Clear($keys.Encryption, 0, $keys.Encryption.Length)
        [Array]::Clear($keys.Authentication, 0, $keys.Authentication.Length)
    }
}

function Unprotect-BrokerBackup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$Secret
    )

    $keys = Get-BrokerBackupCipherKeys -Secret $Secret
    $document = [IO.File]::ReadAllBytes($SourcePath)
    $magic = [Text.Encoding]::ASCII.GetBytes('PABACKUPENCV1')
    if ($document.Length -le ($magic.Length + 16 + 32)) { throw 'The encrypted broker backup format is invalid.' }
    for ($index = 0; $index -lt $magic.Length; $index++) {
        if ($document[$index] -ne $magic[$index]) { throw 'The encrypted broker backup format is invalid.' }
    }

    $payloadLength = $document.Length - 32
    $payload = [byte[]]::new($payloadLength)
    $actualMac = [byte[]]::new(32)
    [Array]::Copy($document, 0, $payload, 0, $payloadLength)
    [Array]::Copy($document, $payloadLength, $actualMac, 0, 32)
    $hmac = [Security.Cryptography.HMACSHA256]::new($keys.Authentication)
    try { $expectedMac = $hmac.ComputeHash($payload) }
    finally { $hmac.Dispose() }
    $difference = 0
    for ($index = 0; $index -lt 32; $index++) { $difference = $difference -bor ($actualMac[$index] -bxor $expectedMac[$index]) }
    if ($difference -ne 0) { throw 'The encrypted broker backup authentication failed.' }

    $iv = [byte[]]::new(16)
    [Array]::Copy($payload, $magic.Length, $iv, 0, 16)
    $ciphertextLength = $payload.Length - $magic.Length - 16
    $ciphertext = [byte[]]::new($ciphertextLength)
    [Array]::Copy($payload, $magic.Length + 16, $ciphertext, 0, $ciphertextLength)
    $aes = [Security.Cryptography.Aes]::Create()
    try {
        $aes.KeySize = 256
        $aes.Mode = [Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = $keys.Encryption
        $aes.IV = $iv
        $decryptor = $aes.CreateDecryptor()
        try { $plaintext = $decryptor.TransformFinalBlock($ciphertext, 0, $ciphertext.Length) }
        finally { $decryptor.Dispose() }
        [IO.File]::WriteAllBytes($DestinationPath, $plaintext)
        Set-BrokerBackupPrivateAcl -Path $DestinationPath
        [Array]::Clear($plaintext, 0, $plaintext.Length)
    }
    finally {
        $aes.Dispose()
        [Array]::Clear($keys.Encryption, 0, $keys.Encryption.Length)
        [Array]::Clear($keys.Authentication, 0, $keys.Authentication.Length)
    }
}
