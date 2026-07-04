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
    foreach ($commit in $commits) {
        $treeOutput = Invoke-Git -Arguments @('ls-tree', '-r', '--name-only', $commit)
        $paths = @($treeOutput.Output -split "`r?`n" | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
        Test-ForbiddenTrackedPaths -Paths $paths -Findings $Findings

        foreach ($secretPattern in $SecretPatterns) {
            $grepOutput = Invoke-Git -Arguments ([string[]](@('grep', '-n', '-E', $secretPattern.Pattern, $commit, '--') + $GitGrepContentPathspec)) -AllowFailure
            if ($grepOutput.ExitCode -eq 0 -and ![string]::IsNullOrWhiteSpace($grepOutput.Output)) {
                $grepOutput.Output.TrimEnd() -split "`r?`n" |
                    ForEach-Object {
                        $allowedFixture = $false
                        $parts = $_ -split ':', 4
                        if ($parts.Length -ge 4) {
                            $path = $parts[1]
                            $line = $parts[3]
                            if (Test-IsAllowedFixtureMatch -Path $path -Line $line) {
                                $allowedFixture = $true
                            }
                        }

                        if (!$allowedFixture) {
                            Add-Finding -Findings $Findings -Message "$($secretPattern.Name): $_"
                        }
                    }
            }
        }
    }
}

$resolvedRepoRoot = Resolve-FullPath $RepoRoot
Assert-PathInsideRepo -Path $resolvedRepoRoot -Description 'repository root'

$findings = [System.Collections.Generic.List[string]]::new()
$trackedOutput = Invoke-Git -Arguments @('ls-files') -WorkingDirectory $resolvedRepoRoot
$trackedPaths = @($trackedOutput.Output -split "`r?`n" | Where-Object { ![string]::IsNullOrWhiteSpace($_) })

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
