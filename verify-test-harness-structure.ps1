param(
    [string]$RepoRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (!$Condition) {
        throw $Message
    }
}

$testsRoot = Join-Path $RepoRoot 'PlayerAssistant.Tests'
$programPath = Join-Path $testsRoot 'Program.cs'
$catalogPath = Join-Path $testsRoot 'TestCatalog.cs'
$domainFiles = @(
    'TestCases.Application.cs',
    'TestCases.Campaign.cs',
    'TestCases.Release.cs',
    'TestCases.Shared.cs',
    'TestCases.Translator.cs',
    'TestInfrastructure.cs'
)

Assert-Condition -Condition (Test-Path -LiteralPath $programPath -PathType Leaf) -Message 'The test harness runner is missing.'
Assert-Condition -Condition (Test-Path -LiteralPath $catalogPath -PathType Leaf) -Message 'The test catalog is missing.'
foreach ($domainFile in $domainFiles) {
    $domainPath = Join-Path $testsRoot $domainFile
    Assert-Condition -Condition (Test-Path -LiteralPath $domainPath -PathType Leaf) -Message "The domain-focused test file '$domainFile' is missing."
}

$program = Get-Content -Raw -LiteralPath $programPath
$catalog = Get-Content -Raw -LiteralPath $catalogPath
Assert-Condition -Condition ($program.Contains('TestCatalog.Create()')) -Message 'The runner must discover tests through TestCatalog.Create().'
Assert-Condition -Condition (!$program.Contains('var tests = new (string Name, Action Test)[]')) -Message 'Test registrations must not remain embedded in Program.cs.'

$registrationMatches = [regex]::Matches(
    $catalog,
    '(?m)^\s*\("(?<name>[^"]+)",\s*(?<target>[A-Za-z0-9_.]+)\),?\s*$')
Assert-Condition -Condition ($registrationMatches.Count -eq 435) -Message 'The test catalog must retain all 435 desktop regression tests.'

$names = @($registrationMatches | ForEach-Object { $_.Groups['name'].Value })
$duplicateNames = @($names | Group-Object | Where-Object Count -gt 1 | Select-Object -ExpandProperty Name)
Assert-Condition -Condition ($duplicateNames.Count -eq 0) -Message "The test catalog contains duplicate names: $($duplicateNames -join ', ')."

Write-Output "Test harness structure verified: $($registrationMatches.Count) discoverable tests across $($domainFiles.Count) domain/support files."
