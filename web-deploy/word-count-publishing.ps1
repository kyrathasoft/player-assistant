Set-StrictMode -Version Latest

if ($null -eq ('PlayerAssistantWordCountCredentialStore' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class PlayerAssistantWordCountCredentialStore
{
    private const int Generic = 1;
    private const int PersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredReadW", SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredWriteW", SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);

    public static string Read(string target)
    {
        IntPtr pointer;
        if (!CredRead(target, Generic, 0, out pointer))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Credential not found: " + target);
        try
        {
            Credential credential = Marshal.PtrToStructure<Credential>(pointer);
            byte[] bytes = new byte[credential.CredentialBlobSize];
            if (bytes.Length > 0)
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        finally { CredFree(pointer); }
    }

    public static void Write(string target, string secret)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(secret);
        IntPtr blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            Credential credential = new Credential {
                Type = Generic,
                TargetName = target,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to write credential: " + target);
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
            for (int index = 0; index < bytes.Length; index++)
                Marshal.WriteByte(blob, index, 0);
            Marshal.FreeCoTaskMem(blob);
        }
    }
}
'@
}

function Get-WordCountCredentialSecret {
    param([Parameter(Mandatory = $true)][string]$TargetName)
    return [PlayerAssistantWordCountCredentialStore]::Read($TargetName)
}

function Set-WordCountCredentialSecret {
    param(
        [Parameter(Mandatory = $true)][string]$TargetName,
        [Parameter(Mandatory = $true)][string]$Secret
    )
    [PlayerAssistantWordCountCredentialStore]::Write($TargetName, $Secret)
}

function New-WordCountSignedEnvelope {
    param(
        [Parameter(Mandatory = $true)][string]$SnapshotJson,
        [Parameter(Mandatory = $true)][string]$PrivateKeyBase64,
        [Parameter(Mandatory = $true)][string]$PublicKeyBase64,
        [Parameter(Mandatory = $true)][string]$KeyId,
        [Parameter(Mandatory = $true)][string]$PhpPath
    )

    $signingCode = @'
$payload = json_decode(stream_get_contents(STDIN), true, 16, JSON_THROW_ON_ERROR);
$secret = base64_decode((string)getenv('PA_WORD_COUNT_PRIVATE_KEY'), true);
$expectedPublic = base64_decode((string)getenv('PA_WORD_COUNT_PUBLIC_KEY'), true);
if (!is_array($payload)
    || !is_string($secret)
    || strlen($secret) !== SODIUM_CRYPTO_SIGN_SECRETKEYBYTES
    || !is_string($expectedPublic)
    || strlen($expectedPublic) !== SODIUM_CRYPTO_SIGN_PUBLICKEYBYTES
    || !hash_equals($expectedPublic, sodium_crypto_sign_publickey_from_secretkey($secret))) {
    fwrite(STDERR, "Invalid word-count signing key material.\n");
    exit(2);
}
$canonical = json_encode($payload, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
$envelope = [
    'payload' => $payload,
    'signature' => [
        'algorithm' => 'Ed25519',
        'key_id' => (string)getenv('PA_WORD_COUNT_KEY_ID'),
        'value' => base64_encode(sodium_crypto_sign_detached($canonical, $secret)),
    ],
];
echo json_encode($envelope, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
'@
    $env:PA_WORD_COUNT_PRIVATE_KEY = $PrivateKeyBase64
    $env:PA_WORD_COUNT_PUBLIC_KEY = $PublicKeyBase64
    $env:PA_WORD_COUNT_KEY_ID = $KeyId
    try {
        $result = $SnapshotJson | & $PhpPath -r $signingCode
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($result)) {
            throw 'Unable to sign the canonical word-count source.'
        }
        return [string]$result
    }
    finally {
        Remove-Item Env:PA_WORD_COUNT_PRIVATE_KEY -ErrorAction SilentlyContinue
        Remove-Item Env:PA_WORD_COUNT_PUBLIC_KEY -ErrorAction SilentlyContinue
        Remove-Item Env:PA_WORD_COUNT_KEY_ID -ErrorAction SilentlyContinue
        $PrivateKeyBase64 = $null
    }
}

function Test-WordCountSignedEnvelope {
    param(
        [Parameter(Mandatory = $true)][string]$EnvelopeJson,
        [Parameter(Mandatory = $true)][string]$PublicKeyBase64,
        [Parameter(Mandatory = $true)][string]$KeyId,
        [Parameter(Mandatory = $true)][string]$PhpPath
    )

    $verificationCode = @'
$envelope = json_decode(stream_get_contents(STDIN), true, 16, JSON_THROW_ON_ERROR);
$payload = $envelope['payload'] ?? null;
$signature = $envelope['signature'] ?? null;
$publicKey = base64_decode((string)getenv('PA_WORD_COUNT_PUBLIC_KEY'), true);
$valid = is_array($payload)
    && is_array($signature)
    && ($signature['algorithm'] ?? null) === 'Ed25519'
    && ($signature['key_id'] ?? null) === (string)getenv('PA_WORD_COUNT_KEY_ID')
    && is_string($publicKey)
    && strlen($publicKey) === SODIUM_CRYPTO_SIGN_PUBLICKEYBYTES;
if ($valid) {
    $signatureBytes = base64_decode((string)($signature['value'] ?? ''), true);
    $canonical = json_encode($payload, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
    $valid = is_string($signatureBytes)
        && sodium_crypto_sign_verify_detached($signatureBytes, $canonical, $publicKey);
}
if (!$valid) { exit(2); }
echo json_encode($payload, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
'@
    $env:PA_WORD_COUNT_PUBLIC_KEY = $PublicKeyBase64
    $env:PA_WORD_COUNT_KEY_ID = $KeyId
    try {
        $payloadJson = $EnvelopeJson | & $PhpPath -r $verificationCode
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($payloadJson)) {
            throw 'The canonical word-count source signature is invalid.'
        }
        return ([string]$payloadJson | ConvertFrom-Json)
    }
    finally {
        Remove-Item Env:PA_WORD_COUNT_PUBLIC_KEY -ErrorAction SilentlyContinue
        Remove-Item Env:PA_WORD_COUNT_KEY_ID -ErrorAction SilentlyContinue
    }
}

function Invoke-WordCountPublishTransaction {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$StageSource,
        [Parameter(Mandatory = $true)][scriptblock]$PublishSource,
        [Parameter(Mandatory = $true)][scriptblock]$VerifySource,
        [Parameter(Mandatory = $true)][scriptblock]$PublishBroker,
        [Parameter(Mandatory = $true)][scriptblock]$RollbackSource,
        [Parameter(Mandatory = $true)][scriptblock]$CleanupSource
    )

    $staged = $false
    $sourcePublished = $false
    try {
        & $StageSource
        $staged = $true
        & $PublishSource
        $sourcePublished = $true
        & $VerifySource
        $response = & $PublishBroker
        try {
            & $CleanupSource
        }
        catch {
            # Source and broker are already consistent. Cleanup is best-effort.
        }
        return $response
    }
    catch {
        if ($sourcePublished) {
            try {
                & $RollbackSource
            }
            catch {
                # Preserve the original transaction failure.
            }
        } else {
            try {
                & $CleanupSource
            }
            catch {
                # Preserve the original transaction failure.
            }
        }
        throw
    }
}
