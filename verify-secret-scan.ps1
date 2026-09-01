param(
    [string]$RepoRoot = $PSScriptRoot,
    [switch]$IncludeHistory
)

$ErrorActionPreference = 'Stop'

$SecretPatterns = @(
    [pscustomobject]@{
        Name = 'OpenAI API key'
        Pattern = 'sk-(proj|live|test|svcacct)-[A-Za-z0-9_-]{20,}'
    },
    [pscustomobject]@{
        Name = 'OpenAI API key environment variable'
        Pattern = 'OPENAI_API_KEY\s*='
    },
    [pscustomobject]@{
        Name = 'private key block'
        Pattern = '-----BEGIN (RSA |DSA |EC |OPENSSH |PGP )?PRIVATE KEY-----'
    },
    [pscustomobject]@{
        Name = 'bearer token literal'
        Pattern = 'Authorization:\s*Bearer\s+[A-Za-z0-9._~+/=-]{12,}'
    },
    [pscustomobject]@{
        Name = 'generic API key assignment'
        Pattern = '(?i)(api[_-]?key|access[_-]?token|client[_-]?secret)\s*[:=]\s*["'']?[A-Za-z0-9._~+/=-]{16,}'
    }
)

$ForbiddenTrackedPathPatterns = @(
    '(^|/)\.env$',
    '(^|/)launch-codex\.ps1$'
)

$ExcludedContentPathPatterns = @(
    '(^|/)bin/',
    '(^|/)obj/',
    '(^|/)Release/',
    '(^|/)Release-verify/',
    '(^|/)publish/',
    '(^|/)publish-msbuild/',
    '(^|/)\.playwright/',
    '(^|/)codex-scratch/',
    '(^|/)graphify-out/'
)

$AllowedFixtureMatches = @(
    [pscustomobject]@{
        PathPattern = '^verify-rc-self-tests\.ps1$'
        LinePattern = 'OPENAI_API_KEY=sk-test-abcdefghijklmnopqrstuvwxyz123456'
    },
    [pscustomobject]@{
        PathPattern = '^verify-secret-scan\.ps1$'
        LinePattern = 'OPENAI_API_KEY=sk-test-abcdefghijklmnopqrstuvwxyz123456'
    },
    [pscustomobject]@{
        PathPattern = '^(?:PlayerAssistant\.Tests/ProtectedDataNegativeSpaceTests\.cs|web-deploy/tests/protected-data-negative-space-tests\.php)$'
        LinePattern = 'Authorization:'
    }
)

$GitGrepContentPathspec = @(
    '.',
    ':(exclude)bin/**',
    ':(exclude)obj/**',
    ':(exclude)Release/**',
    ':(exclude)Release-verify/**',
    ':(exclude)publish/**',
    ':(exclude)publish-msbuild/**',
    ':(exclude)**/.playwright/**',
    ':(exclude)codex-scratch/**',
    ':(exclude)graphify-out/**'
)

function Resolve-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-PathInsideRepo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $repoRoot = Resolve-FullPath $PSScriptRoot
    $fullPath = Resolve-FullPath $Path
    $repoRootWithSeparator = $repoRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

    if (!$fullPath.StartsWith($repoRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase) -and
        !$fullPath.Equals($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use $Description outside repo root: $fullPath"
    }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [string]$WorkingDirectory = $resolvedRepoRoot,

        [switch]$AllowFailure
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.Arguments = ConvertTo-ProcessArguments -Arguments $Arguments
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $startInfo.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
    $startInfo.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Unable to start git command."
    }

    $standardOutput = $process.StandardOutput.ReadToEnd()
    $standardError = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne 0 -and !$AllowFailure) {
        throw "git $($Arguments -join ' ') failed with exit code $($process.ExitCode): $standardError"
    }

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = $standardOutput
        Error = $standardError
    }
}

function ConvertTo-ProcessArguments {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $escapedArguments = $Arguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + ($_ -replace '"', '\"') + '"'
        }
        else {
            $_
        }
    }

    return ($escapedArguments -join ' ')
}

function Add-Finding {
    param(
        [System.Collections.Generic.List[string]]$Findings,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    [void]$Findings.Add($Message)
}

function Test-ForbiddenTrackedPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Paths,

        [System.Collections.Generic.List[string]]$Findings
    )

    foreach ($path in $Paths) {
        $normalizedPath = $path.Replace('\', '/')
        foreach ($pattern in $ForbiddenTrackedPathPatterns) {
            if ($normalizedPath -match $pattern) {
                Add-Finding -Findings $Findings -Message "Forbidden tracked path: $path"
            }
        }
    }
}

function Test-IsContentPathIncluded {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $normalizedPath = $Path.Replace('\', '/')
    foreach ($pattern in $ExcludedContentPathPatterns) {
        if ($normalizedPath -match $pattern) {
            return $false
        }
    }

    return $true
}

function Test-IsAllowedFixtureMatch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Line
    )

    $normalizedPath = $Path.Replace('\', '/')
    foreach ($fixture in $AllowedFixtureMatches) {
        if ($normalizedPath -match $fixture.PathPattern -and $Line -match $fixture.LinePattern) {
            return $true
        }
    }

    return $false
}

function Test-TrackedContent {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Paths,

        [System.Collections.Generic.List[string]]$Findings
    )

    foreach ($path in $Paths) {
        if (!(Test-IsContentPathIncluded -Path $path)) {
            continue
        }

        $fullPath = Join-Path $resolvedRepoRoot $path
        if (!(Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            continue
        }

        $lineNumber = 0
        foreach ($line in [System.IO.File]::ReadLines($fullPath)) {
            $lineNumber++
            foreach ($secretPattern in $SecretPatterns) {
                if ($line -match $secretPattern.Pattern) {
                    if (Test-IsAllowedFixtureMatch -Path $path -Line $line) {
                        continue
                    }

                    Add-Finding -Findings $Findings -Message "$($secretPattern.Name): ${path}:$lineNumber"
                }
            }
        }
    }
}

function Test-HistoryContent {
    param(
        [System.Collections.Generic.List[string]]$Findings
    )

    $commitsOutput = Invoke-Git -Arguments @('rev-list', '--all')
    $commits = @($commitsOutput.Output -split "`r?`n" | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
    if ($commits.Count -eq 0) {
        return
    }

    $historyPathsOutput = Invoke-Git -Arguments @('log', '--all', '--root', '--format=', '--name-only', '-z', '--no-renames', '-m')
    $historyPaths = @(
        $historyPathsOutput.Output -split "`0" |
            Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
    Test-ForbiddenTrackedPaths -Paths $historyPaths -Findings $Findings

    # Every line that exists in reachable history appears as an addition in the
    # root commit, the commit that introduced it, or a merge-parent diff. Scan
    # that patch stream once instead of starting Git for every pattern/commit.
    $historyDiffArguments = [string[]](@(
        'log',
        '--all',
        '--format=commit:%H',
        '--no-renames',
        '--root',
        '--text',
        '-m',
        '-p',
        '--'
    ) + $GitGrepContentPathspec)
    $historyDiffOutput = Invoke-Git -Arguments $historyDiffArguments
    $currentCommit = ''
    $currentPath = ''

    foreach ($diffLine in ($historyDiffOutput.Output -split "`r?`n")) {
        if ($diffLine.StartsWith('commit:', [System.StringComparison]::Ordinal)) {
            $currentCommit = $diffLine.Substring('commit:'.Length)
            continue
        }

        if ($diffLine.StartsWith('diff --git ', [System.StringComparison]::Ordinal)) {
            $destinationMarker = $diffLine.LastIndexOf(' b/', [System.StringComparison]::Ordinal)
            $currentPath = if ($destinationMarker -ge 0) {
                $diffLine.Substring($destinationMarker + 3).Trim('"')
            }
            else {
                ''
            }
            continue
        }

        if ($diffLine.Length -lt 2 -or
            ($diffLine[0] -ne '+' -and $diffLine[0] -ne '-') -or
            $diffLine.StartsWith('+++', [System.StringComparison]::Ordinal) -or
            $diffLine.StartsWith('---', [System.StringComparison]::Ordinal)) {
            continue
        }

        $contentLine = $diffLine.Substring(1)
        $findingPath = if ([string]::IsNullOrWhiteSpace($currentPath)) { '<unparsed-git-path>' } else { $currentPath }
        foreach ($secretPattern in $SecretPatterns) {
            if ($contentLine -match $secretPattern.Pattern -and
                ([string]::IsNullOrWhiteSpace($currentPath) -or
                    !(Test-IsAllowedFixtureMatch -Path $currentPath -Line $contentLine))) {
                Add-Finding -Findings $Findings -Message "$($secretPattern.Name): ${currentCommit}:${findingPath}:$contentLine"
            }
        }
    }
}

$resolvedRepoRoot = Resolve-FullPath $RepoRoot
Assert-PathInsideRepo -Path $resolvedRepoRoot -Description 'repository root'

$findings = [System.Collections.Generic.List[string]]::new()
$trackedOutput = Invoke-Git -Arguments @('ls-files', '-z') -WorkingDirectory $resolvedRepoRoot
$trackedPaths = @($trackedOutput.Output -split "`0" | Where-Object { ![string]::IsNullOrWhiteSpace($_) })

Test-ForbiddenTrackedPaths -Paths $trackedPaths -Findings $findings
Test-TrackedContent -Paths $trackedPaths -Findings $findings

if ($IncludeHistory) {
    Test-HistoryContent -Findings $findings
}

if ($findings.Count -gt 0) {
    Write-Output "Secret scan findings:"
    $findings | Sort-Object -Unique | ForEach-Object { Write-Output "  $_" }
    throw "Secret scan failed."
}

Write-Output "Secret scan passed."
Write-Output "  Tracked files scanned: $($trackedPaths.Count)"
Write-Output "  History scanned: $IncludeHistory"
