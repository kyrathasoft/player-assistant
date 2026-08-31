$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'release-manifest.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('release-manifest-tests-' + [Guid]::NewGuid().ToString('N'))
$manifest = Join-Path $root 'Release\release-manifest.json'
$inventory = Join-Path $root 'inventory.json'
try {
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'Release\publish'), (Join-Path $root 'Release\installer\player-assistant-0.9.5\payload'), (Join-Path $root 'pwa\online-installer-for-pwa\dist') | Out-Null
    'runtime' | Set-Content -LiteralPath (Join-Path $root 'Release\publish\player-assistant.exe') -NoNewline
    'encrypted installer sidecar' | Set-Content -LiteralPath (Join-Path $root 'Release\installer\player-assistant-0.9.5\payload\settings.local.json') -NoNewline
    'shell' | Set-Content -LiteralPath (Join-Path $root 'pwa\index.html') -NoNewline
    'same bytes' | Set-Content -LiteralPath (Join-Path $root 'pwa\online-installer-for-pwa\install-player-assistant-web.php') -NoNewline
    'same bytes' | Set-Content -LiteralPath (Join-Path $root 'pwa\online-installer-for-pwa\dist\install-player-assistant-web.php') -NoNewline
    @{
        schema_version = 1; hash_algorithm = 'SHA256'; roots = @('Release/publish','Release/installer','pwa')
        exclude = @('Release/release-manifest.json'); mutable = @(); forbidden = @('**/settings.local.json','**/*secret*')
        allowed = @('Release/installer/*/payload/settings.local.json')
        source_package_pairs = @(@{ source = 'pwa/online-installer-for-pwa/install-player-assistant-web.php'; distribution = 'pwa/online-installer-for-pwa/dist/install-player-assistant-web.php' })
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $inventory
    function Invoke-Manifest([string]$mode) {
        $previousPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $hostCommand = Get-Command pwsh.exe -ErrorAction SilentlyContinue
            if ($null -eq $hostCommand) { $hostCommand = Get-Command powershell.exe -ErrorAction Stop }
            & $hostCommand.Source -NoProfile -ExecutionPolicy Bypass -File $script -Mode $mode -Root $root -ManifestPath $manifest -InventoryPath $inventory -SourceRevision 'test-revision' 2>&1 | Out-Null
            return $LASTEXITCODE
        } finally { $ErrorActionPreference = $previousPreference }
    }
    function Assert-Fails([scriptblock]$action, [string]$name) {
        $code = & $action
        if ($code -eq 0) { throw "Expected failure: $name" }
    }
    if ((Invoke-Manifest 'Generate') -ne 0) { throw 'initial generation failed' }
    if ((Invoke-Manifest 'Verify') -ne 0) { throw 'reproducibility verification failed' }

    Copy-Item $manifest "$manifest.repro"; if ((Get-FileHash $manifest).Hash -ne (Get-FileHash "$manifest.repro").Hash) { throw 'generation was not byte-reproducible' }
    Remove-Item "$manifest.repro"
    Remove-Item (Join-Path $root 'pwa\index.html'); Assert-Fails { Invoke-Manifest 'Verify' } 'missing artifact'
    'shell' | Set-Content -LiteralPath (Join-Path $root 'pwa\index.html') -NoNewline
    'extra' | Set-Content -LiteralPath (Join-Path $root 'pwa\extra.dll') -NoNewline; Assert-Fails { Invoke-Manifest 'Verify' } 'extra artifact'; Remove-Item (Join-Path $root 'pwa\extra.dll')
    Move-Item (Join-Path $root 'pwa\index.html') (Join-Path $root 'pwa\renamed.html'); Assert-Fails { Invoke-Manifest 'Verify' } 'renamed artifact'; Move-Item (Join-Path $root 'pwa\renamed.html') (Join-Path $root 'pwa\index.html')
    Add-Content -LiteralPath (Join-Path $root 'pwa\index.html') -Value 'changed'; Assert-Fails { Invoke-Manifest 'Verify' } 'modified artifact'
    'shell' | Set-Content -LiteralPath (Join-Path $root 'pwa\index.html') -NoNewline
    [IO.File]::WriteAllText((Join-Path $root 'pwa\index.html'), "shell`r`n", [Text.UTF8Encoding]::new($false)); Assert-Fails { Invoke-Manifest 'Verify' } 'line-ending change'
    'shell' | Set-Content -LiteralPath (Join-Path $root 'pwa\index.html') -NoNewline
    $doc = Get-Content -Raw $manifest | ConvertFrom-Json; [array]::Reverse($doc.files); $doc | ConvertTo-Json -Depth 12 | Set-Content $manifest; Assert-Fails { Invoke-Manifest 'Verify' } 'reordered entries'
    Invoke-Manifest 'Generate' | Out-Null
    'different bytes' | Set-Content -LiteralPath (Join-Path $root 'pwa\online-installer-for-pwa\dist\install-player-assistant-web.php') -NoNewline; Assert-Fails { Invoke-Manifest 'Verify' } 'source/package drift'
    'same bytes' | Set-Content -LiteralPath (Join-Path $root 'pwa\online-installer-for-pwa\dist\install-player-assistant-web.php') -NoNewline
    'secret' | Set-Content -LiteralPath (Join-Path $root 'pwa\client-secret.txt') -NoNewline; Assert-Fails { Invoke-Manifest 'Verify' } 'forbidden private file'
    Remove-Item (Join-Path $root 'pwa\client-secret.txt')
    'plaintext settings' | Set-Content -LiteralPath (Join-Path $root 'pwa\settings.local.json') -NoNewline; Assert-Fails { Invoke-Manifest 'Verify' } 'forbidden settings file'
    Remove-Item (Join-Path $root 'pwa\settings.local.json')
    if ((Invoke-Manifest 'Verify') -ne 0) { throw 'final verification failed' }
    Write-Output 'Release manifest deterministic tests passed.'
}
finally { if (Test-Path $root) { Remove-Item $root -Recurse -Force } }
