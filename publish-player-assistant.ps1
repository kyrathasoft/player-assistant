param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'Release\publish')
)

$ErrorActionPreference = 'Stop'

if (Test-Path -LiteralPath $OutputDir) {
    $resolvedOutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
    if ($resolvedOutputDir -notlike "$PSScriptRoot*") {
        throw "Refusing to clean output directory outside repo root: $resolvedOutputDir"
    }

    Get-ChildItem -LiteralPath $resolvedOutputDir -Force | Remove-Item -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

dotnet publish "$PSScriptRoot\player-assistant.csproj" `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $OutputDir

Get-ChildItem -Path $OutputDir -Filter *.pdb -File | Remove-Item -Force

function Write-AppEncryptedLocalSettings {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    $format = 'app-protected-v1'
    $seed = 'PlayerAssistant.LocalSettings.v1'
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $key = $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($seed))
    }
    finally {
        $sha256.Dispose()
    }
    $plaintextBytes = [System.Text.Encoding]::UTF8.GetBytes(
        (Get-Content -Raw -LiteralPath $SourcePath))
    $aes = [System.Security.Cryptography.Aes]::Create()
    $aes.Key = $key
    $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
    $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
    $aes.GenerateIV()
    $iv = [byte[]]$aes.IV.Clone()
    try {
        $encryptor = $aes.CreateEncryptor()
        try {
            $ciphertext = $encryptor.TransformFinalBlock($plaintextBytes, 0, $plaintextBytes.Length)
        }
        finally {
            $encryptor.Dispose()
        }
    }
    finally {
        $aes.Dispose()
    }

    $payloadBytes = [byte[]]::new($iv.Length + $ciphertext.Length)
    [System.Buffer]::BlockCopy($iv, 0, $payloadBytes, 0, $iv.Length)
    [System.Buffer]::BlockCopy($ciphertext, 0, $payloadBytes, $iv.Length, $ciphertext.Length)

    $envelope = [pscustomobject]@{
        format = $format
        payload = [Convert]::ToBase64String($payloadBytes)
    }

    $envelope | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $DestinationPath -Encoding UTF8
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Release\keyword-index.json') -Destination (Join-Path $OutputDir 'keyword-index.json') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Release\keyword-index.json') -Destination (Join-Path $OutputDir 'keyword-index.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Release\game-posts-key-terms.md') -Destination (Join-Path $OutputDir 'game-posts-key-terms.md') -Force
Write-AppEncryptedLocalSettings -SourcePath (Join-Path $PSScriptRoot 'settings.local.json') -DestinationPath (Join-Path $OutputDir 'settings.local.json')
