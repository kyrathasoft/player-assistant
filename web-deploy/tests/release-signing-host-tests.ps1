$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ('release-policy-test-' + [guid]::NewGuid().ToString('N'))
$output = Join-Path $root 'output'
$expectedKey = Join-Path $root 'expected-public.xml'
New-Item -ItemType Directory -Path $output | Out-Null
try {
    $rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new(2048)
    $rsa.PersistKeyInCsp = $false
    [IO.File]::WriteAllText($expectedKey, $rsa.ToXmlString($false), [Text.UTF8Encoding]::new($false))
    $rsa.Clear(); $rsa.Dispose()

    $buildOutput = & pwsh -NoProfile -File (Join-Path $repo 'build-release-update-artifacts.ps1') `
        -OutputDir $output `
        -PublishDir (Join-Path $repo 'Release') `
        -InstallerPath (Join-Path $repo 'Release\player-assistant.exe') `
        -Version '0.9.1-hardening.1' `
        -GenerateEphemeralSigningKey `
        -ExpectedPublicKeyXmlPath $expectedKey 2>&1
    if ($LASTEXITCODE -eq 0) { throw 'Ephemeral signing was accepted with an expected trusted key.' }
    if (($buildOutput -join "`n") -notmatch 'cannot be combined with ephemeral signing') { throw 'The ephemeral-signing rejection message was not emitted.' }

    $missingHosts = Join-Path $root 'missing-known-hosts'
    $deployOutput = & pwsh -NoProfile -File (Join-Path $repo 'web-deploy\deploy-pwa-files.ps1') `
        -Files @('campaign-search.json') `
        -SshKeyPath (Join-Path $root 'missing-key') `
        -KnownHostsPath $missingHosts 2>&1
    if ($LASTEXITCODE -eq 0) { throw 'Deployment proceeded without pinned host identity or key.' }
    if (($deployOutput -join "`n") -notmatch 'Pinned DreamHost known-hosts file is required') { throw 'The pinned-host rejection message was not emitted.' }

    $deployScript = Get-Content -Raw (Join-Path $repo 'web-deploy\deploy-pwa-files.ps1')
    foreach ($token in @('StrictHostKeyChecking=yes', 'UserKnownHostsFile=', 'dreamhost_known_hosts')) {
        if ($deployScript -notmatch [regex]::Escape($token)) { throw "Missing strict host identity contract: $token" }
    }
    $hardening = Get-Content -Raw (Join-Path $repo '.github\workflows\hardening.yml')
    foreach ($token in @('PLAYER_ASSISTANT_UPDATE_MANIFEST_PRIVATE_KEY_XML', 'PLAYER_ASSISTANT_UPDATE_MANIFEST_PUBLIC_KEY_XML', 'required for trusted release artifacts')) {
        if ($hardening -notmatch [regex]::Escape($token)) { throw "Missing trusted release workflow contract: $token" }
    }
    $mirrorWorkflow = Get-Content -Raw (Join-Path $repo '.github\workflows\mirror-to-gitea.yml')
    if ($mirrorWorkflow -notmatch 'SOURCE_REPO:\s+https://x-access-token:\$\{\{ github\.token \}\}@github\.com/\$\{\{ github\.repository \}\}\.git') {
        throw 'Gitea mirror workflow does not use the correctly formed GitHub token expression.'
    }
    if ($mirrorWorkflow -match 'x-access-token:\*\*\*\s+github\.token') {
        throw 'Gitea mirror workflow contains the malformed credential expression.'
    }
    Write-Output 'Release-signing and SSH-host identity policy tests passed.'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
