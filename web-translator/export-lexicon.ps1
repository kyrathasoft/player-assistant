param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$webRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $webRoot
$assemblyPath = Join-Path $repoRoot 'Release\player-assistant.dll'
$outputPath = Join-Path $webRoot 'orcish-lexicon.json'

if (-not $SkipBuild)
{
    & dotnet build (Join-Path $repoRoot 'player-assistant.csproj') -c Release -o (Join-Path $repoRoot 'Release')
    if ($LASTEXITCODE -ne 0)
    {
        throw 'The Release build failed; the web lexicon was not exported.'
    }
}

if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf))
{
    throw "The compiled translator assembly was not found at '$assemblyPath'."
}

$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$translatorType = $assembly.GetType('PlayerAssistant.OrcishTranslatorUtility', $true)
$bindingFlags = [Reflection.BindingFlags]'Public,NonPublic,Static'
$entries = @($translatorType.GetMethod('BuildLexiconEntriesFromSource', $bindingFlags).Invoke($null, $null))
$uniqueEnglishTerms = @(
    $entries |
        ForEach-Object { ([string]$_.English).Trim() } |
        Sort-Object -Unique
).Count

$groups = [Collections.Generic.Dictionary[string, Collections.Generic.List[object]]]::new([StringComparer]::OrdinalIgnoreCase)
$canonicalTerms = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$maxEnglishPhraseWords = 1

foreach ($entry in $entries)
{
    $english = ([string]$entry.English).Trim()
    $key = $english.ToLowerInvariant()
    [Collections.Generic.List[object]]$candidateList = $null

    if (-not $groups.TryGetValue($key, [ref]$candidateList))
    {
        $candidateList = [Collections.Generic.List[object]]::new()
        $groups.Add($key, $candidateList)
        $canonicalTerms.Add($key, $english)
    }

    $tags = @($entry.Tags | ForEach-Object { [string]$_ })
    $candidate = [object[]]@(
        [string]$entry.Orcish,
        $(if ($null -eq $entry.PartOfSpeech) { $null } else { [string]$entry.PartOfSpeech }),
        $(if ($null -eq $entry.GrammarClass) { $null } else { [string]$entry.GrammarClass }),
        $tags
    )
    $candidateList.Add($candidate)

    $phraseWords = @($english -split '\s+' | Where-Object { $_ }).Count
    if ($phraseWords -gt $maxEnglishPhraseWords)
    {
        $maxEnglishPhraseWords = $phraseWords
    }
}

$terms = [ordered]@{}
$sortedKeys = [string[]]::new($groups.get_Count())
$groups.get_Keys().CopyTo($sortedKeys, 0)
[Array]::Sort($sortedKeys, [StringComparer]::OrdinalIgnoreCase)
foreach ($key in $sortedKeys)
{
    [Collections.Generic.List[object]]$candidateList = $null
    if (-not $groups.TryGetValue($key, [ref]$candidateList))
    {
        throw "The grouped lexicon entry '$key' could not be retrieved."
    }

    $terms[$key] = [object[]]@(
        $canonicalTerms[$key],
        [object[]]($candidateList.ToArray())
    )
}

$sourceCommit = (& git -C $repoRoot rev-parse --short HEAD 2>$null)
if ($LASTEXITCODE -ne 0)
{
    $sourceCommit = $null
}

$document = [ordered]@{
    schemaVersion = 1
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    sourceCommit = $sourceCommit
    uniqueEnglishTerms = $uniqueEnglishTerms
    entryCount = $entries.Count
    maxEnglishPhraseWords = $maxEnglishPhraseWords
    candidateFields = @('orcish', 'partOfSpeech', 'grammarClass', 'tags')
    terms = $terms
}

$json = $document | ConvertTo-Json -Depth 8 -Compress
[IO.File]::WriteAllText($outputPath, $json, [Text.UTF8Encoding]::new($false))

Write-Output "Exported $($entries.Count) assembled entries across $uniqueEnglishTerms English terms to $outputPath"
