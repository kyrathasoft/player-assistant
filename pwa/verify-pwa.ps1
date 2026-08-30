param(
    [string]$PwaRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PwaRoot
. (Join-Path $repoRoot 'version-metadata.ps1')
$versionMetadata = Get-PlayerAssistantVersionMetadata -RepoRoot $repoRoot

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (!$Condition) {
        throw $Message
    }
}

$requiredFiles = @(
    'index.html',
    'styles.css',
    'version.js',
    'app.js',
    'translator-worker.js',
    'service-worker.js',
    'optional-pack-loader.js',
    'optional-packs.json',
    'campaign-search-worker.js',
    'manifest.webmanifest',
    'offline.html',
    'icons\icon-192.png',
    'icons\icon-512.png',
    'data\orcish.json',
    'data\elvish.json',
    'data\ghukliak.json',
    'data\heroes.json',
    'level-progression.json',
    'magic-items.json',
    'party-funds.json',
    'quests.json',
    'campaign-search.json'
)
foreach ($relativePath in $requiredFiles) {
    Assert-Condition -Condition (Test-Path -LiteralPath (Join-Path $PwaRoot $relativePath) -PathType Leaf) -Message "Missing PWA file: $relativePath"
}

$manifest = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'manifest.webmanifest') | ConvertFrom-Json
Assert-Condition -Condition (![string]::IsNullOrWhiteSpace($manifest.name)) -Message 'Manifest name is required.'
Assert-Condition -Condition (![string]::IsNullOrWhiteSpace($manifest.short_name)) -Message 'Manifest short_name is required.'
Assert-Condition -Condition (![string]::IsNullOrWhiteSpace($manifest.start_url)) -Message 'Manifest start_url is required.'
Assert-Condition -Condition (@('standalone', 'fullscreen', 'minimal-ui') -contains $manifest.display) -Message 'Manifest display must be install-capable.'

Add-Type -AssemblyName System.Drawing
foreach ($size in @(192, 512)) {
    $iconPath = Join-Path $PwaRoot "icons\icon-$size.png"
    $icon = [System.Drawing.Image]::FromFile($iconPath)
    try {
        Assert-Condition -Condition ($icon.Width -eq $size -and $icon.Height -eq $size) -Message "Install icon must be ${size}x${size}: $iconPath"
    }
    finally {
        $icon.Dispose()
    }
    Assert-Condition -Condition (@($manifest.icons | Where-Object { $_.sizes -eq "${size}x${size}" }).Count -gt 0) -Message "Manifest is missing the ${size}x${size} icon."
}

$lexiconCounts = [ordered]@{}
foreach ($language in @('orcish', 'elvish', 'ghukliak')) {
    $payload = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot "data\$language.json") | ConvertFrom-Json
    $termProperties = @($payload.terms.PSObject.Properties)
    $actualCount = $termProperties.Count
    Assert-Condition -Condition ($actualCount -gt 0) -Message "$language lexicon is empty."
    Assert-Condition -Condition ([int]$payload.entryCount -eq $actualCount) -Message "$language lexicon entryCount does not match its terms."
    $actualMaxPhraseWords = 1
    $normalizedTerms = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($property in $termProperties) {
        $phraseWords = @(($property.Name -split '\s+') | Where-Object { $_.Length -gt 0 }).Count
        $actualMaxPhraseWords = [Math]::Max($actualMaxPhraseWords, $phraseWords)
        $normalizedTerm = (($property.Name.Normalize([System.Text.NormalizationForm]::FormKC).Trim() -split '\s+') -join ' ').ToLowerInvariant()
        Assert-Condition -Condition ($normalizedTerms.Add($normalizedTerm)) -Message "$language contains duplicate terms under translator-worker normalization: $normalizedTerm"
    }
    Assert-Condition -Condition ([int]$payload.maxPhraseWords -eq $actualMaxPhraseWords) -Message "$language maxPhraseWords does not match its terms."
    $lexiconCounts[$language] = $actualCount
}
Assert-Condition -Condition ([int]$lexiconCounts.ghukliak -eq 81204) -Message 'The Ghukliak lexicon must cover every Orcish English term plus its source-only terms.'

$campaignSearch = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'campaign-search.json') | ConvertFrom-Json
Assert-Condition -Condition ([int]$campaignSearch.schemaVersion -eq 2) -Message 'Campaign search data must use the full-text schema.'
Assert-Condition -Condition (@($campaignSearch.pages).Count -gt 0) -Message 'Campaign search data contains no pages.'
Assert-Condition -Condition ([int]$campaignSearch.pageCount -eq @($campaignSearch.pages).Count) -Message 'Campaign search pageCount does not match its pages.'
Assert-Condition -Condition (@($campaignSearch.pages | Where-Object { ![string]::IsNullOrWhiteSpace($_.content) }).Count -gt 0) -Message 'Campaign search data contains no Markdown content.'
Assert-Condition -Condition (@($campaignSearch.pages | Where-Object { [string]::IsNullOrWhiteSpace($_.title) -or $_.url -notmatch '^https://' }).Count -eq 0) -Message 'Campaign search data contains an invalid page title or URL.'
Assert-Condition -Condition (@($campaignSearch.pages | Where-Object { $_.title -eq 'XP Tracking' }).Count -eq 0) -Message 'The protected XP Tracking page must not be included in public PWA search data.'

$heroData = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'data\heroes.json') | ConvertFrom-Json
Assert-Condition -Condition ([int]$heroData.schemaVersion -eq 1) -Message 'Hero-token data must use schema version 1.'
Assert-Condition -Condition ($heroData.source -eq 'https://publish.obsidian.md/scarlethorizons/PCs/Player+Characters+Listing') -Message 'Hero-token data has an unexpected source.'
Assert-Condition -Condition (@($heroData.heroes).Count -gt 0) -Message 'Hero-token data contains no active heroes.'
Assert-Condition -Condition ($heroData.dungeonMaster.name -eq 'Dungeon Master') -Message 'Dungeon Master token data is missing.'
Assert-Condition -Condition ($heroData.dungeonMaster.preferLocal -eq $true) -Message 'The approved local Dungeon Master token must override the incorrect wiki image.'
foreach ($hero in @($heroData.heroes)) {
    Assert-Condition -Condition ([string]$hero.wikiPage -match '^https://publish\.obsidian\.md/scarlethorizons/PCs/[^?#]+$') -Message "Hero-token entry has an unsafe wiki page URL: $($hero.name)"
}
foreach ($hero in @($heroData.heroes) + @($heroData.dungeonMaster)) {
    Assert-Condition -Condition (![string]::IsNullOrWhiteSpace($hero.name)) -Message 'A hero-token entry has no character name.'
    Assert-Condition -Condition (@($hero.aliases).Count -gt 0) -Message "Hero-token entry has no aliases: $($hero.name)"
    Assert-Condition -Condition ([string]$hero.token -match '^data/hero-tokens/[a-zA-Z0-9][a-zA-Z0-9._-]*\.(?:avif|gif|jpe?g|png|webp)$') -Message "Hero-token entry has an unsafe path: $($hero.name)"
    Assert-Condition -Condition ([string]$hero.wikiToken -match '^https://publish-\d+\.obsidian\.md/access/[a-zA-Z0-9]+/[^?#]+$') -Message "Hero-token entry has an unsafe wiki URL: $($hero.name)"
    Assert-Condition -Condition ([string]$hero.sha256 -match '^[a-f0-9]{64}$') -Message "Hero-token entry has an invalid SHA-256 hash: $($hero.name)"
    $tokenPath = Join-Path $PwaRoot ([string]$hero.token).Replace('/', '\')
    Assert-Condition -Condition (Test-Path -LiteralPath $tokenPath -PathType Leaf) -Message "Hero token is missing: $($hero.token)"
    Assert-Condition -Condition ((Get-Item -LiteralPath $tokenPath).Length -ge 16) -Message "Hero token is empty: $($hero.token)"
    $actualHash = (Get-FileHash -LiteralPath $tokenPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Condition -Condition ($actualHash -eq [string]$hero.sha256) -Message "Website fallback does not match the current wiki token: $($hero.name)"
}

$magicItems = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'magic-items.json') | ConvertFrom-Json
$magicItemRequiredFields = @('name', 'description', 'date-acquired', 'meta-date-acquired', 'longevity', 'provenance', 'whereabouts', 'viewable-by')
$magicItemLongevityValues = @('one-shot', 'limited-use', 'permanent')
Assert-Condition -Condition ([int]$magicItems.schema_version -eq 1) -Message 'Magic-item fallback data must use schema version 1.'
Assert-Condition -Condition ($magicItems.source -eq 'https://publish.obsidian.md/scarlethorizons/Magic+Items/Kirkilston+Crew+Magic+Items') -Message 'Magic-item fallback data has an unexpected source.'
Assert-Condition -Condition (@($magicItems.items).Count -gt 0) -Message 'Magic-item fallback data contains no items.'
foreach ($magicItem in @($magicItems.items)) {
    foreach ($fieldName in $magicItemRequiredFields) {
        Assert-Condition -Condition ($magicItem.PSObject.Properties.Name -contains $fieldName) -Message "Magic-item fallback entry is missing '$fieldName'."
        Assert-Condition -Condition (![string]::IsNullOrWhiteSpace([string]$magicItem.$fieldName)) -Message "Magic-item fallback entry has an empty '$fieldName'."
    }
    Assert-Condition -Condition ($magicItemLongevityValues -contains [string]$magicItem.longevity) -Message "Magic-item fallback entry has invalid longevity '$($magicItem.longevity)'."
    $magicItemViewers = @([string]$magicItem.'viewable-by' -split ',' | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ -ne '' })
    Assert-Condition -Condition ($magicItemViewers.Count -eq 1 -and $magicItemViewers[0] -eq 'all') -Message "Public magic-item fallback entries must be viewable by all only."
}
Assert-Condition -Condition (@($magicItems.items | Where-Object { $_.name -eq "Armstrong's Chamois" -and $_.'viewable-by' -eq 'all' }).Count -eq 1) -Message "Armstrong's Chamois must be viewable by all."

$partyFunds = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'party-funds.json') | ConvertFrom-Json
$partyFundsGemstoneValuePattern = '^\s*(\d+(?:\.\d+)?)\s+gp$'
$partyFundsExpectedFields = @('coins', 'fiction-date', 'gemstones', 'meta-date', 'schema_version', 'text')
$partyFundsNormalizedText = ([string]$partyFunds.text).Replace("`r`n", "`n").Replace("`r", "`n")
$partyFundsRecords = @($partyFundsNormalizedText -split "`n`n---`n`n")
$partyFundsRecordDates = @()
foreach ($record in $partyFundsRecords) {
    $dateText = @($record -split "`n", 2)[0].Trim()
    $parsedDate = [DateTime]::MinValue
    Assert-Condition -Condition ([DateTime]::TryParseExact($dateText, 'M/d/yyyy', [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::None, [ref]$parsedDate)) -Message "Party-funds record has an invalid meta date: $dateText"
    $partyFundsRecordDates += $parsedDate
}
for ($index = 1; $index -lt $partyFundsRecordDates.Count; $index++) {
    Assert-Condition -Condition ($partyFundsRecordDates[$index - 1] -ge $partyFundsRecordDates[$index]) -Message 'Party-funds records must be ordered newest-first.'
}
$validCoins = ($partyFunds.coins -and
    $partyFunds.coins.PSObject.Properties.Name -contains 'copper' -and
    $partyFunds.coins.PSObject.Properties.Name -contains 'silver' -and
    $partyFunds.coins.PSObject.Properties.Name -contains 'gold' -and
    [int]$partyFunds.coins.copper -ge 0 -and
    [int]$partyFunds.coins.silver -ge 0 -and
    [int]$partyFunds.coins.gold -ge 0)
Assert-Condition -Condition ([int]$partyFunds.schema_version -eq 2) -Message 'Party-funds fallback data must use schema version 2.'
Assert-Condition -Condition ((@($partyFunds.PSObject.Properties.Name | Sort-Object) -join ',') -eq ($partyFundsExpectedFields -join ',')) -Message 'Party-funds fallback data has unexpected root fields.'
Assert-Condition -Condition (![string]::IsNullOrWhiteSpace([string]$partyFunds.'meta-date') -and ![string]::IsNullOrWhiteSpace([string]$partyFunds.'fiction-date') -and ![string]::IsNullOrWhiteSpace([string]$partyFunds.text)) -Message 'Party-funds fallback metadata is incomplete.'
Assert-Condition -Condition ($partyFundsRecords.Count -gt 0 -and @($partyFundsRecords[0] -split "`n", 2)[0].Trim() -eq [string]$partyFunds.'meta-date') -Message 'The newest party-funds record must appear first and match meta-date.'
Assert-Condition -Condition ($partyFunds.coins -ne $null) -Message 'Party-funds fallback data is missing coins.'
Assert-Condition -Condition ($validCoins) -Message 'Party-funds fallback coin totals are invalid.'
Assert-Condition -Condition ($partyFunds.PSObject.Properties.Name -contains 'gemstones') -Message 'Party-funds fallback gemstone entries are missing.'
foreach ($gemstone in @($partyFunds.gemstones)) {
    foreach ($fieldName in @('type', 'size', 'quality', 'value')) {
        Assert-Condition -Condition ($gemstone.PSObject.Properties.Name -contains $fieldName) -Message "Party-funds fallback entry is missing '$fieldName'."
        Assert-Condition -Condition (![string]::IsNullOrWhiteSpace([string]$gemstone.$fieldName) ) -Message "Party-funds fallback entry has an empty '$fieldName'."
    }
    Assert-Condition -Condition ([string]$gemstone.value -match $partyFundsGemstoneValuePattern) -Message "Party-funds fallback gemstone value '$($gemstone.value)' is invalid."
}

$levelProgression = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'level-progression.json') | ConvertFrom-Json
$expectedClassProgression = [ordered]@{
    'feycaster' = @(0, 1500, 3000, 6000, 12000, 24000, 48000, 120000, 240000, 360000, 480000, 600000, 720000, 840000, 960000, 1080000, 1200000, 1320000, 1440000, 1560000, 1680000, 1800000, 1920000, 2040000, 2160000, 2280000, 2400000, 2520000, 2640000, 2760000, 2880000, 3000000, 3120000, 3240000, 3360000, 3480000)
    'fighter' = @(0, 2000, 4000, 8000, 16000, 32000, 64000, 120000, 240000, 360000, 480000, 600000, 720000, 840000, 960000, 1080000, 1200000, 1320000, 1440000, 1560000, 1680000, 1800000, 1920000, 2040000, 2160000, 2280000, 2400000, 2520000, 2640000, 2760000, 2880000, 3000000, 3120000, 3240000, 3360000, 3480000)
    'illusionist' = @(0, 2500, 5000, 10000, 20000, 40000, 80000, 150000, 300000, 450000, 600000, 750000, 900000, 1050000, 1200000, 1350000, 1500000, 1650000, 1800000, 1950000, 2100000, 2250000, 2400000, 2550000, 2700000, 2850000, 3000000, 3150000, 3300000, 3450000, 3600000, 3750000, 3900000, 4050000, 4200000, 4350000)
    'mystic-theurge' = @(0, 2750, 5500, 11000, 22000, 44000, 88000, 165000, 330000, 495000, 660000, 825000, 990000, 1155000, 1320000, 1485000, 1650000, 1815000, 1980000, 2145000, 2145000, 2475000, 2640000, 2805000, 2970000, 3135000, 3300000, 3465000, 3630000, 3795000, 3960000, 4125000, 4290000, 4455000, 4620000, 4785000)
    'paladin' = @(0, 2750, 5500, 12000, 24000, 45000, 95000, 175000, 350000, 500000, 650000, 800000, 950000, 1100000, 1250000, 1400000, 1550000, 1700000, 1850000, 2000000, 2150000, 2300000, 2450000, 2600000, 2750000, 2900000, 3050000, 3200000, 3350000, 3500000, 3650000, 3800000, 3950000, 4100000, 4250000, 4400000)
    'ranger' = @(0, 2250, 4500, 10000, 20000, 40000, 90000, 150000, 300000, 425000, 550000, 675000, 800000, 925000, 1050000, 1175000, 1300000, 1425000, 1550000, 1675000, 1800000, 1925000, 2050000, 2175000, 2300000, 2425000, 2550000, 2675000, 2800000, 2925000, 3050000, 3175000, 3300000, 3425000, 3550000, 3675000)
}
$expectedClassNames = [ordered]@{
    'feycaster' = 'Feycaster'
    'fighter' = 'Fighter'
    'illusionist' = 'Illusionist'
    'mystic-theurge' = 'Mystic Theurge'
    'paladin' = 'Paladin'
    'ranger' = 'Ranger'
}
Assert-Condition -Condition ([int]$levelProgression.schema_version -eq 1) -Message 'Level-progression data must use schema version 1.'
Assert-Condition -Condition ($levelProgression.source -eq 'https://publish.obsidian.md/scarlethorizons/Classes/Class+Level+Progression') -Message 'Level-progression data has an unexpected index source.'
Assert-Condition -Condition ((@($levelProgression.PSObject.Properties.Name | Sort-Object) -join ',') -eq 'classes,progression_semantics,schema_version,source') -Message 'Level-progression data has unexpected root fields.'
$classProperties = @($levelProgression.classes.PSObject.Properties)
Assert-Condition -Condition ($classProperties.Count -eq 6) -Message 'Level-progression data must contain exactly six classes.'
Assert-Condition -Condition ((@($classProperties.Name | Sort-Object) -join ',') -eq (@($expectedClassProgression.Keys | Sort-Object) -join ',')) -Message 'Level-progression data contains an unexpected class set.'
foreach ($classProperty in $classProperties) {
    $classId = $classProperty.Name
    $classData = $classProperty.Value
    $expectedFields = @('level_progression', 'name', 'notes', 'published_maximum_level', 'source')
    Assert-Condition -Condition ((@($classData.PSObject.Properties.Name | Sort-Object) -join ',') -eq ($expectedFields -join ',')) -Message "Level-progression class '$classId' does not match the required schema."
    Assert-Condition -Condition ($classData.name -eq $expectedClassNames[$classId]) -Message "Level-progression class '$classId' has the wrong name."
    Assert-Condition -Condition ($classData.source -eq "https://publish.obsidian.md/scarlethorizons/Classes/$([uri]::EscapeDataString($expectedClassNames[$classId]).Replace('%20', '+'))") -Message "Level-progression class '$classId' has the wrong source."
    Assert-Condition -Condition ([int]$classData.published_maximum_level -eq $(if ($classId -eq 'feycaster') { 12 } else { 36 })) -Message "Level-progression class '$classId' has the wrong published maximum level."
    $entries = @($classData.level_progression)
    Assert-Condition -Condition ($entries.Count -eq 36) -Message "Level-progression class '$classId' must contain levels 1 through 36."
    Assert-Condition -Condition ((@($entries.level) -join ',') -eq ((1..36) -join ',')) -Message "Level-progression class '$classId' has missing, duplicate, or unordered levels."
    Assert-Condition -Condition (@($entries | Where-Object { $_.minimum_xp -isnot [int] -and $_.minimum_xp -isnot [long] }).Count -eq 0) -Message "Level-progression class '$classId' has a non-integer XP threshold."
    Assert-Condition -Condition ((@($entries.minimum_xp) -join ',') -eq ($expectedClassProgression[$classId] -join ',')) -Message "Level-progression class '$classId' does not match the published XP thresholds."
}

$questData = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'quests.json') | ConvertFrom-Json
$questRequiredFields = @('title', 'summary', 'giver', 'visibility', 'state', 'objectives', 'reward', 'dates', 'gated-by', 'unlocked-by', 'wiki-url')
$questVisibilityValues = @('individual-only', 'party-only', 'individual-or-party')
$questStateValues = @('available', 'active', 'available (abandoned)', 'completed', 'withdrawn')
Assert-Condition -Condition ([int]$questData.schema_version -eq 1) -Message 'Quest data must use schema version 1.'
Assert-Condition -Condition ((@($questData.PSObject.Properties.Name | Sort-Object) -join ',') -eq 'quests,schema_version') -Message 'Quest data has unexpected root fields.'
$questProperties = @($questData.quests.PSObject.Properties)
Assert-Condition -Condition ($questProperties.Count -gt 0 -and $questProperties.Count -le 100) -Message 'Quest data must contain between 1 and 100 quests.'
$questIds = @($questProperties.Name)
foreach ($questProperty in $questProperties) {
    Assert-Condition -Condition ($questProperty.Name -match '^[a-z0-9]+(?:-[a-z0-9]+)*$') -Message "Quest data has an invalid identifier '$($questProperty.Name)'."
    $quest = $questProperty.Value
    $actualFields = @($quest.PSObject.Properties.Name)
    $unexpectedFields = @($actualFields | Where-Object { $questRequiredFields -notcontains $_ -and $_ -ne 'meta-date' })
    $missingFields = @($questRequiredFields | Where-Object { $actualFields -notcontains $_ })
    Assert-Condition -Condition ($unexpectedFields.Count -eq 0 -and $missingFields.Count -eq 0) -Message "Quest '$($questProperty.Name)' does not match the required schema."
    foreach ($fieldName in @('title', 'summary', 'giver', 'visibility', 'state', 'wiki-url')) {
        Assert-Condition -Condition (![string]::IsNullOrWhiteSpace([string]$quest.$fieldName)) -Message "Quest '$($questProperty.Name)' has an empty '$fieldName'."
    }
    Assert-Condition -Condition ($questVisibilityValues -contains [string]$quest.visibility) -Message "Quest '$($questProperty.Name)' has invalid visibility."
    Assert-Condition -Condition ($questStateValues -contains [string]$quest.state) -Message "Quest '$($questProperty.Name)' has invalid state."
    Assert-Condition -Condition (@($quest.objectives).Count -gt 0 -and @($quest.objectives).Count -le 20) -Message "Quest '$($questProperty.Name)' has invalid objectives."
    Assert-Condition -Condition (@($quest.objectives | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) }).Count -eq 0) -Message "Quest '$($questProperty.Name)' has an empty objective."
    $dateFields = @($quest.dates.PSObject.Properties.Name)
    $unexpectedDateFields = @($dateFields | Where-Object { @('accepted', 'expires', 'completed') -notcontains $_ })
    $missingDateFields = @(@('accepted', 'expires') | Where-Object { $dateFields -notcontains $_ })
    Assert-Condition -Condition ($unexpectedDateFields.Count -eq 0 -and $missingDateFields.Count -eq 0) -Message "Quest '$($questProperty.Name)' has invalid dates."

    Assert-Condition -Condition (@($quest.'gated-by' | Where-Object { [string]$_ -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' }).Count -eq 0) -Message "Quest '$($questProperty.Name)' has an invalid gate."
    Assert-Condition -Condition (@($quest.'unlocked-by' | Where-Object { [string]$_ -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' }).Count -eq 0) -Message "Quest '$($questProperty.Name)' has an invalid prerequisite."
    Assert-Condition -Condition (@($quest.'unlocked-by' | Where-Object { [string]$_ -eq $questProperty.Name -or $questIds -notcontains ([string]$_) }).Count -eq 0) -Message "Quest '$($questProperty.Name)' references an unknown or self prerequisite."
    $questWikiUrlAllowed = ([string]$quest.'wiki-url' -match '^https://publish[.]obsidian[.]md/scarlethorizons/(?:Quests|NPCs|Meta/IC|Meta|Writings|Locations)/[^?#]+$') -or (($questProperty.Name -eq 'trace-murfex-last-journey') -and [string]$quest.'wiki-url' -eq "https://publish.obsidian.md/scarlethorizons/Player-Contributed/Jelb's+Family+Genealogy") -or (($questProperty.Name -eq 'investigate-impurax-resurgence') -and [string]$quest.'wiki-url' -eq 'https://publish.obsidian.md/scarlethorizons/Powers/Gods/Impurax') -or (($questProperty.Name -eq 'recover-calico-five-remains') -and [string]$quest.'wiki-url' -eq 'https://publish.obsidian.md/scarlethorizons/Powers/Factions/Calico+Five') -or (($questProperty.Name -eq 'harvest-xanderpetals') -and [string]$quest.'wiki-url' -eq 'https://publish.obsidian.md/scarlethorizons/Meta/Xanderpetals') -or (($questProperty.Name -eq 'find-darkforest-giants-grave') -and [string]$quest.'wiki-url' -eq 'https://publish.obsidian.md/scarlethorizons/Locations/Darkforest')
    Assert-Condition -Condition $questWikiUrlAllowed -Message "Quest '$($questProperty.Name)' has an invalid wiki URL."
}

$featureModulePaths = @(
    'modules/translator.js',
    'modules/search.js',
    'modules/dice.js',
    'modules/account-session.js',
    'modules/messages-activity.js',
    'modules/presence.js',
    'modules/update-lifecycle.js',
    'service-worker-controller.js'
)
$versionedFeatureModulePaths = @($featureModulePaths | ForEach-Object {
    './{0}?v=${{VERSION_METADATA.appRevision}}' -f $_
})
foreach ($script in @('version.js', 'app.js', 'translator-worker.js', 'service-worker.js', 'service-worker-tests.mjs') + $featureModulePaths) {
    & node --check (Join-Path $PwaRoot $script)
    Assert-Condition -Condition ($LASTEXITCODE -eq 0) -Message "JavaScript syntax check failed: $script"
}

$html = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'index.html')
$versionScript = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'version.js')
$appScriptEntry = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'app.js')
$featureModuleScripts = @($featureModulePaths | ForEach-Object {
    Get-Content -Raw -LiteralPath (Join-Path $PwaRoot $_)
})
$appScript = @($appScriptEntry) + $featureModuleScripts -join [Environment]::NewLine
$translatorWorker = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'translator-worker.js')
$requestTranslationFunction = [regex]::Match(
    $appScript,
    'const requestTranslation = \(event\) => \{.*?worker\?\.addEventListener',
    [System.Text.RegularExpressions.RegexOptions]::Singleline).Value
$styles = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'styles.css')
$serviceWorker = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'service-worker.js')
$optionalLoader = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'optional-pack-loader.js')
$serviceWorkerTests = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'service-worker-tests.mjs')
$browserSmoke = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'browser-smoke.mjs')
$deploymentTest = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'test-deployment.ps1')
$productionResponseContracts = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot 'production-response-contracts.ps1')
$monitorScript = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot '..\web-deploy\monitor-pwa.ps1')
$monitorWorkflow = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot '..\.github\workflows\pwa-synthetic-monitor.yml')
$prSmokeWorkflow = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot '..\.github\workflows\pr-smoke.yml')
$fullRegressionWorkflow = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot '..\.github\workflows\hardening.yml')
$referencedIds = [regex]::Matches($appScript, "byId\('([^']+)'\)") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
foreach ($id in $referencedIds) {
    Assert-Condition -Condition ($html.Contains("id=`"$id`"")) -Message "app.js references a missing HTML element: $id"
}
Assert-Condition -Condition ($appScript.Contains("credentials: 'same-origin'")) -Message 'Character authentication must use same-origin cookies.'
Assert-Condition -Condition ($appScript.Contains("cache: 'no-store'")) -Message 'Character authentication requests must bypass browser caching.'
Assert-Condition -Condition ($appScript.Contains("'/login'") -and $appScript.Contains("'/session'") -and $appScript.Contains("'/xp'") -and $appScript.Contains("'/xp-awards'") -and $appScript.Contains("'/word-counts'") -and $appScript.Contains("'/presence'") -and $appScript.Contains("'/quests'") -and $appScript.Contains("'/quest-requests'") -and $appScript.Contains("'/messages'") -and $appScript.Contains("'/logout'")) -Message 'Character authentication and protected player-data routes are incomplete.'
Assert-Condition -Condition ($html.Contains('id="message-dm-nav" hidden') -and $html.Contains('id="message-player-nav" hidden') -and $styles.Contains('.nav-item[hidden]') -and $appScript.Contains('messageDmNavButton.hidden = !authenticated || isDungeonMaster;') -and $appScript.Contains('messagePlayerNavButton.hidden = !canMessagePlayer;') -and $appScript.Contains("requestedView === 'message-player' && !canMessagePlayer") -and $appScript.Contains('authenticatedMessageSnapshot?.player_recipients.length')) -Message 'Messaging navigation and view access must remain hidden until the matching authenticated role and recipient availability are active.'
Assert-Condition -Condition ($html.Contains('id="message-notification-button"') -and $html.Contains('id="message-notification-count"') -and $html.Contains('id="message-notification-dialog"') -and $html.Contains('id="message-notification-list"')) -Message 'Unread-message notification controls are incomplete.'
Assert-Condition -Condition ($appScript.Contains("everyPlayerOption.value = 'all-players';") -and $appScript.Contains("{ recipient_role: 'all_players' }")) -Message 'Dungeon Master messaging must support selecting every player.'
Assert-Condition -Condition ($appScript.Contains('validateMessageSnapshot') -and $appScript.Contains('loadMessages') -and $appScript.Contains('renderMessageNotifications') -and $appScript.Contains('markMessageRead') -and $appScript.Contains('`/messages/${messageId}/read`') -and $appScript.Contains('loadXpSummary(), loadWordCountSummary(), loadQuests(), loadMessages()')) -Message 'Login-time unread-message retrieval or acknowledgement is incomplete.'
Assert-Condition -Condition ($styles.Contains('.message-notification-button') -and $styles.Contains('.message-notification-button[hidden]') -and $styles.Contains('.message-notification-count') -and $styles.Contains('.message-notification-list')) -Message 'Unread-message notification styling is incomplete.'
Assert-Condition -Condition ($html.Contains('autocomplete="current-password"')) -Message 'The character password field is not configured safely.'
Assert-Condition -Condition ($html.Contains('id="xp-card"') -and $html.Contains('id="xp-total"') -and $html.Contains('id="xp-class-level"') -and $html.Contains('id="xp-hit-points"') -and $html.Contains('id="xp-tnl"') -and $html.Contains('id="xp-party-rows"')) -Message 'The protected XP dashboard card is incomplete.'
Assert-Condition -Condition ($appScript.Contains('xpTotal.textContent = `${Number(character.xp_total).toLocaleString(''en-US'')} (TNL: ${tnlLabel})`;') -and $appScript.Contains('if (tnl) tnl.textContent = tnlLabel;')) -Message 'A player''s current XP total must show TNL on the same line.'
Assert-Condition -Condition ($appScript.Contains('totalCell.textContent = `${Number(character.xp_total).toLocaleString(''en-US'')} (TNL: ${tnlLabel})`;') -and $appScript.Contains("const tnlLabel = character.xp_to_next_level === null")) -Message 'The Dungeon Master party XP rows must show each character''s TNL on the same line as current XP.'
Assert-Condition -Condition ($html.Contains('id="online-users-summary"') -and $html.Contains('id="online-users-status"') -and $html.Contains('id="online-users-list"')) -Message 'The Dungeon Master online-user display is incomplete.'
Assert-Condition -Condition ($appScript.Contains('validatePresenceSnapshot') -and $appScript.Contains('updatePresencePolling') -and $appScript.Contains('document.hidden') -and $appScript.Contains('navigator.onLine')) -Message 'Authenticated presence polling is incomplete.'
Assert-Condition -Condition ($appScript.Contains('payload.schema_version !== 2') -and $appScript.Contains('Last login ${new Intl.DateTimeFormat') -and $appScript.Contains('Never logged in')) -Message 'Inactive-user last-login rendering is incomplete.'
Assert-Condition -Condition ($html.Contains('id="word-count-card"') -and $html.Contains('id="word-count-wiki"') -and $html.Contains('id="word-count-ic"') -and $html.Contains('id="word-count-ooc"') -and $html.Contains('id="word-count-date"')) -Message 'The protected word-count dashboard card is incomplete.'
Assert-Condition -Condition ($html.Contains('data-view="quests"') -and $html.Contains('data-view-panel="quests"') -and $html.Contains('id="quests-status"') -and $html.Contains('id="quest-list"') -and $html.Contains('id="quest-state-cycle"') -and $html.Contains('id="quest-state-cycle-label"')) -Message 'The protected Quests dashboard is incomplete.'
Assert-Condition -Condition ($html.Contains('data-view="xp-awards"') -and $html.Contains('data-view-panel="xp-awards"') -and $html.Contains('id="xp-awards-status"') -and $html.Contains('id="xp-awards-list"')) -Message 'The protected XP Awards dashboard is incomplete.'
Assert-Condition -Condition ($appScript.Contains("await requestAuthenticationApi('/xp-awards')") -and $appScript.Contains('canViewXpAwards') -and $appScript.Contains('renderXpAwardsUi') -and $appScript.Contains('validateXpAwardsSnapshot')) -Message 'XP Awards must load through the authenticated broker session.'
Assert-Condition -Condition ($appScript.Contains('displayProgression') -and $appScript.Contains('xp_to_next_level') -and $appScript.Contains('xp-award-progress-summary')) -Message 'XP Awards character headings must include the current TNL when available.'
Assert-Condition -Condition (!$appScript.Contains('XP_AWARDS_PLAYER_GROUPS') -and !$appScript.Contains("fetch('XP/") -and !$appScript.Contains("fetch(`XP/")) -Message 'The PWA must not contain direct legacy XP data paths or client-side XP authorization maps.'
Assert-Condition -Condition ($appScript.Contains('const QUEST_STATUS_VALUES') -and $appScript -match "(?s)const QUEST_STATE_DISPLAY_ORDER = Object\.freeze\(\[\s*'active',\s*'available',\s*'available \(abandoned\)',\s*'gated',\s*'completed',\s*'withdrawn'\s*\]\)" -and $appScript.Contains("'individual-only'") -and $appScript.Contains("'party-only'") -and $appScript.Contains("'individual-or-party'") -and $appScript.Contains("if (viewName === 'quests') questStateFilter = '';") -and $appScript.Contains('QUEST_STATE_DISPLAY_ORDER.indexOf(left.quest.state)') -and $appScript.Contains("const cycleValues = ['', ...availableStates]") -and $appScript.Contains("orderedQuests.filter((quest) => quest.state === questStateFilter)") -and $appScript.Contains('renderQuestUi')) -Message 'The Quests dashboard does not support ordered and filterable lifecycle states.'
Assert-Condition -Condition ($appScript.Contains("await requestAuthenticationApi('/quests')") -and $appScript.Contains('authenticatedAccount === null')) -Message 'Quest records must load only through the authenticated broker session.'
Assert-Condition -Condition ($html.Contains('id="quest-alert-dialog"') -and $html.Contains('id="quest-alert-list"') -and $html.Contains('id="quest-alert-summary"')) -Message 'The quest-request alert dialog is incomplete.'
Assert-Condition -Condition ($appScript.Contains('submitQuestInterest') -and $appScript.Contains('decideQuestRequest') -and $appScript.Contains('acknowledgeQuestNotification') -and $appScript.Contains('request_status_values') -and $appScript.Contains('pending_requests') -and $appScript.Contains('notifications')) -Message 'The quest-request lifecycle UI is incomplete.'
Assert-Condition -Condition ($appScript.Contains("authenticatedAccount?.role === 'player'") -and $appScript.Contains("authenticatedAccount?.role !== 'dm'") -and $appScript.Contains("body: { decision }")) -Message 'Quest requests must be player-only and decisions must be Dungeon-Master-only.'
Assert-Condition -Condition ($html.Contains('data-view="magic-items"') -and $html.Contains('data-view-panel="magic-items"') -and $html.Contains('id="magic-items-status"') -and $html.Contains('id="magic-item-list"')) -Message 'The Magic Items page is incomplete.'
Assert-Condition -Condition ($appScript.Contains('fetchBrokerMagicItems') -and $appScript.Contains('fetchFallbackMagicItems') -and $appScript.Contains("requestAuthenticationApi('/magic-items')") -and $appScript.Contains("fetch('magic-items.json'") -and $appScript.Contains('data_source: ''fallback''')) -Message 'Magic-item broker authorization and public fallback loading are incomplete.'
Assert-Condition -Condition ($appScript.Contains('MAGIC_ITEM_LONGEVITY_VALUES') -and $appScript.Contains("'one-shot'") -and $appScript.Contains("'limited-use'") -and $appScript.Contains("'permanent'")) -Message 'Magic-item longevity validation is incomplete.'
Assert-Condition -Condition ($appScript.Contains('getMagicItemViewers') -and $appScript.Contains('isMagicItemVisible') -and $appScript.Contains("getMagicItemViewers(item?.['viewable-by']).includes('all')") -and !$appScript.Contains('viewableBy.includes(name)') -and $appScript.Contains('renderMagicItems();')) -Message "Magic-item records must be filtered by the broker, not character-name matching in the client."
Assert-Condition -Condition ($html.Contains('id="magic-item-counts"') -and $html.Contains('id="magic-item-count-one-shot"') -and $html.Contains('id="magic-item-count-limited-use"') -and $html.Contains('id="magic-item-count-permanent"')) -Message 'The Magic Items page is missing its longevity breakdown.'
Assert-Condition -Condition ($appScript.Contains('const visibleItems = magicItemSnapshot.items.filter(isMagicItemVisible);') -and $appScript.Contains('MAGIC_ITEM_LONGEVITY_VALUES.map') -and $appScript.Contains('magic-item-count-${longevity}')) -Message 'Magic-item longevity totals must be calculated from the items visible to the logged-in character.'
Assert-Condition -Condition ($styles.Contains('.magic-item-counts') -and $styles.Contains('.magic-item-counts[hidden]')) -Message 'The magic-item longevity breakdown styles are incomplete.'
Assert-Condition -Condition ($html.Contains('id="auth-dashboard-token"') -and $html.Contains('id="auth-account-token"')) -Message 'Authenticated hero-token image elements are missing.'
Assert-Condition -Condition ($appScript.Contains("fetch('data/heroes.json?v=2'") -and $appScript.Contains('findAuthenticatedHero') -and $appScript.Contains("account.role === 'dm'")) -Message 'Authenticated hero-token selection is incomplete or lacks cache-safe manifest versioning.'
Assert-Condition -Condition ($appScript.Contains('const validHeroToken = (hero) =>') -and $appScript.Contains('const validPlayerHero = (hero) =>') -and $appScript.Contains('!validHeroToken(payload.dungeonMaster)') -and $appScript.Contains('payload.heroes.filter(validPlayerHero)')) -Message 'Player wiki-page validation must not invalidate the Dungeon Master entry or suppress all player tokens.'
Assert-Condition -Condition ($appScript.Contains('image.src = hero.token') -and $appScript.Contains('wikiImage.src = hero.wikiToken')) -Message 'Hero tokens must display the website copy immediately and prefer the wiki once it loads.'
Assert-Condition -Condition ($appScript.Contains("window.open(wikiPage, '_blank', 'noopener,noreferrer')") -and $appScript.Contains('image.onclick = openWikiPage') -and $appScript.Contains("event.key === 'Enter'") -and $appScript.Contains("event.key === ' '")) -Message 'Player hero tokens must open their wiki pages in a new tab by mouse or keyboard.'
Assert-Condition -Condition ($appScript.Contains("image.title = ``click here to go to `${hero.name}'s wiki page...``;") -and $appScript.Contains("image.setAttribute('aria-label', image.title)")) -Message 'Linked hero tokens must provide the requested hover tooltip and accessible label.'
Assert-Condition -Condition ($appScript.Contains('if (hero.preferLocal === true) return;')) -Message 'The Dungeon Master token must be able to retain its approved local image.'
Assert-Condition -Condition ($appScript.Contains('const DUNGEON_MASTER_HERO') -and $appScript.Contains("if (accountAtStart.role === 'dm')") -and $appScript.Contains("setHeroTokenImage(byId('auth-dashboard-token'), DUNGEON_MASTER_HERO)")) -Message 'The Dungeon Master dashboard token must render synchronously without depending on the hero manifest.'
Assert-Condition -Condition ($appScript.Contains("authDialog?.addEventListener('close'") -and $appScript.Contains('void renderAuthenticatedHeroToken()')) -Message 'Closing the login dialog must restore the authenticated dashboard token.'
Assert-Condition -Condition ($browserSmoke.Contains('Dialog focus containment failed') -and $browserSmoke.Contains('Dialog focus restoration failed') -and $browserSmoke.Contains('Visible keyboard focus contract failed') -and $browserSmoke.Contains('Protected XP Awards table semantics failed') -and $browserSmoke.Contains('Protected narrow mobile layout overflows horizontally')) -Message 'Browser smoke must cover dialog focus containment/restoration, visible keyboard focus, protected table semantics, and authenticated narrow mobile layout.'
Assert-Condition -Condition ($browserSmoke.Contains("transitionDuration") -and $browserSmoke.Contains("animationDuration") -and $browserSmoke.Contains("reduced-motion visual contract failed")) -Message 'Browser smoke must verify computed reduced-motion styling, not only media-query matching.'
Assert-Condition -Condition ($appScript.Contains("navigator.serviceWorker.addEventListener('controllerchange'") -and $appScript.Contains('window.location.reload()')) -Message 'Existing PWA clients must reload after a service-worker update.'
Assert-Condition -Condition ($styles.Contains('width: 256px;') -and $styles.Contains('height: 256px;') -and $styles.Contains('.authenticated-hero-token')) -Message 'Authenticated hero tokens must be displayed at twice their original width and height.'
Assert-Condition -Condition ($styles.Contains('transform: translateY(10px);')) -Message 'Authenticated hero tokens must retain their horizontal position and sit 10 pixels lower.'
$dungeonMasterDashboardStyle = [regex]::Match($styles, '(?s)#auth-dashboard-token\.is-dungeon-master-token\s*\{(?<body>.*?)\}')
Assert-Condition -Condition ($dungeonMasterDashboardStyle.Success -and $dungeonMasterDashboardStyle.Groups['body'].Value.Contains('width: 384px;') -and $dungeonMasterDashboardStyle.Groups['body'].Value.Contains('height: 384px;')) -Message 'The Dungeon Master token in the XP card must be displayed at twice its original width and height.'
Assert-Condition -Condition ($appScript.Contains("'is-dungeon-master-token'") -and $appScript.Contains('hero?.name === DUNGEON_MASTER_HERO.name')) -Message 'The Dungeon Master dashboard-size class is not assigned dynamically.'
Assert-Condition -Condition ($styles.Contains('#auth-logout') -and $styles.Contains('margin-top: 5px;')) -Message 'The Log Out button must be positioned 5 pixels lower.'
$mobileHeroStyle = [regex]::Match($styles, '(?s)@media\s+\(max-width:\s*48rem\)\s*\{.*?\.authenticated-hero-token\s*\{(?<body>.*?)\}')
Assert-Condition -Condition ($mobileHeroStyle.Success -and $mobileHeroStyle.Groups['body'].Value.Contains('max-width: 100%;') -and $mobileHeroStyle.Groups['body'].Value.Contains('height: auto;')) -Message 'Authenticated hero tokens must remain bounded and square on mobile screens.'
$xpCharacterNameStyle = [regex]::Match($styles, '(?s)\.xp-character-name\s*\{(?<body>.*?)\}')
Assert-Condition -Condition ($xpCharacterNameStyle.Success -and $xpCharacterNameStyle.Groups['body'].Value.Contains('font-size: 2rem;')) -Message 'The PC name below the XP date must be displayed at twice its original font size.'
$heroTokenStyle = [regex]::Match($styles, '(?s)\.authenticated-hero-token\s*\{(?<body>.*?)\}')
Assert-Condition -Condition ($heroTokenStyle.Success -and $heroTokenStyle.Groups['body'].Value -notmatch '(?m)^\s*border(?:-(?:top|right|bottom|left|width|style|color))?\s*:') -Message 'Authenticated hero tokens must not have a CSS border.'
Assert-Condition -Condition (!$appScript.Contains('XP+Tracking') -and !$html.Contains('XP+Tracking')) -Message 'The XP source URL must remain outside the browser application.'
Assert-Condition -Condition ($serviceWorker.Contains("url.pathname.startsWith('/scarlethorizons/api/')")) -Message 'The service worker must exclude protected API responses.'
Assert-Condition -Condition ($serviceWorker.Contains("new Request(asset, { cache: 'reload' })")) -Message 'Service-worker upgrades must bypass stale browser shell caches.'
Assert-Condition -Condition ($serviceWorker.Contains("'./optional-pack-loader.js'") -and $serviceWorker.Contains("'./optional-packs.json'") -and !$serviceWorker.Contains('OFFLINE_DATA_ASSETS') -and !$serviceWorker.Contains("cacheAssets(DATA_CACHE")) -Message 'Optional packs must be absent from install-time general precache.'
Assert-Condition -Condition ($serviceWorker.Contains('networkFirstData') -and $serviceWorker.Contains("url.pathname.endsWith('/data/heroes.json')") -and $serviceWorker.Contains("url.pathname.includes('/data/hero-tokens/')")) -Message 'Hero-token manifests and images must refresh from the network before using cached copies.'
Assert-Condition -Condition ($optionalLoader.Contains('pack-hash=') -and $optionalLoader.Contains('cache.put(cacheKey') -and $optionalLoader.Contains('removePack') -and $optionalLoader.Contains('manifestPromise.delete(manifestUrl)')) -Message 'Optional packs must use content-addressed caches, retryable manifests, and explicit removal.'
Assert-Condition -Condition ($appScript.Contains("updateViaCache: 'none'") -and $appScript.Contains('await registration.update()')) -Message 'The PWA must explicitly check for uncached service-worker updates.'
Assert-Condition -Condition ($manifest.start_url -eq './#dashboard' -and $manifest.scope -eq './') -Message 'The manifest must keep navigation inside the deployed PWA directory.'
Assert-Condition -Condition ($html.Contains('href="manifest.webmanifest"') -and $appScript.Contains('service-worker.js')) -Message 'The install manifest or service-worker registration is missing.'
Assert-Condition -Condition ($html.Contains("<script src=`"version.js?v=$($versionMetadata.PwaMetadataRevision)`"></script>") -and $html.Contains("<script type=`"module`" src=`"app.js?v=$($versionMetadata.PwaAppRevision)`"></script>") -and $appScriptEntry.Contains("from './modules/translator.js?v=$($versionMetadata.PwaAppRevision)'") -and $appScriptEntry.Contains("from './modules/search.js?v=$($versionMetadata.PwaAppRevision)'") -and $appScriptEntry.Contains("from './modules/dice.js?v=$($versionMetadata.PwaAppRevision)'")) -Message 'The PWA entry point must load version metadata and cache-busted translator, search, and dice feature modules.'
Assert-Condition -Condition ($featureModulePaths.Count -eq @($featureModulePaths | Where-Object { $deploymentTest.Contains("'$_' = @('application/javascript', 'text/javascript')") }).Count) -Message 'Public deployment verification must allow every PWA feature module.'
Assert-Condition -Condition ($monitorScript.Contains('RequireProtectedApi') -and $monitorScript.Contains('PWA_MONITOR_CHARACTER_NAME') -and $monitorScript.Contains('PWA_MONITOR_PASSWORD') -and $monitorScript.Contains('MaximumXpAgeSeconds') -and $monitorScript.Contains('MaximumWordCountAgeSeconds')) -Message 'The production monitor must require credentials and explicit XP/word-count freshness limits.'
Assert-Condition -Condition ($productionResponseContracts.Contains('[bool]$Payload.stale -eq $false') -and $productionResponseContracts.Contains('XP source snapshot is stale') -and $productionResponseContracts.Contains('Word-count source snapshot is stale') -and $productionResponseContracts.Contains('Word-count broker snapshot is stale') -and $productionResponseContracts.Contains('Test-ProductionInteger $Payload.schema_version') -and $deploymentTest.Contains('Assert-ProductionXpResponse') -and $deploymentTest.Contains('Assert-ProductionWordCountResponse')) -Message 'Deployment verification must reject stale or malformed authorized protected responses.'
Assert-Condition -Condition ($productionResponseContracts.Contains('Invoke-ProductionSessionCleanup') -and $productionResponseContracts.Contains('Assert-ProductionAnonymousSessionResponse') -and $productionResponseContracts.Contains('Assert-ProductionLoginResponse') -and $productionResponseContracts.Contains('Assert-ProductionIdentityResponse') -and $deploymentTest.Contains('Assert-ProductionAnonymousSessionResponse') -and $deploymentTest.Contains('Assert-ProductionLoginResponse') -and $deploymentTest.Contains('Assert-ProductionIdentityResponse') -and $deploymentTest.Contains('Invoke-ProductionSessionCleanup') -and $deploymentTest.Contains('Invoke-ProductionMonitorLogout') -and $deploymentTest.Contains('$postLogoutSessionResponse') -and $deploymentTest.Contains("'X-CSRF-Token'")) -Message 'Anonymous and authorized identity response shapes must use reusable fail-closed contracts, and monitor cleanup must verify logout.'
Assert-Condition -Condition ($monitorWorkflow.Contains('secrets.PWA_MONITOR_CHARACTER_NAME') -and $monitorWorkflow.Contains('secrets.PWA_MONITOR_PASSWORD') -and $monitorWorkflow.Contains('RequireProtectedApi')) -Message 'The scheduled production monitor must exercise authorized protected-response and freshness checks.'
Assert-Condition -Condition ($prSmokeWorkflow.Contains('.\web-deploy\tests\pwa-monitor-contract-tests.ps1') -and $fullRegressionWorkflow.Contains('./web-deploy/tests/pwa-monitor-contract-tests.ps1')) -Message 'PR smoke and full-regression CI must execute the production-response contract tests.'
Assert-Condition -Condition ($html.Contains("styles.css?v=$($versionMetadata.PwaStylesRevision)") -and $html.Contains("$($versionMetadata.PwaVersion) PWA") -and $versionScript.Contains("pwaVersion: '$($versionMetadata.PwaVersion)'") -and $versionScript.Contains("metadataRevision: $($versionMetadata.PwaMetadataRevision)") -and $versionScript.Contains("stylesRevision: $($versionMetadata.PwaStylesRevision)") -and $versionScript.Contains("appRevision: $($versionMetadata.PwaAppRevision)") -and $versionScript.Contains("cacheRevision: $($versionMetadata.PwaCacheRevision)") -and $serviceWorker.Contains("importScripts('./version.js?v=$($versionMetadata.PwaMetadataRevision)')") -and $serviceWorker.Contains('VERSION_METADATA.cacheRevision') -and $serviceWorker.Contains('VERSION_METADATA.stylesRevision') -and $appScriptEntry.Contains("from './service-worker-controller.js?v=$($versionMetadata.PwaAppRevision)'") -and $appScriptEntry.Contains('PLAYER_ASSISTANT_VERSION_METADATA?.pwaVersion') -and $deploymentTest.Contains("'version.js' = @('application/javascript', 'text/javascript')") -and $versionedFeatureModulePaths.Count -eq @($versionedFeatureModulePaths | Where-Object { $serviceWorker.Contains($_) }).Count -and $serviceWorker.Contains("'./level-progression.json'") -and $serviceWorker.Contains("'./magic-items.json'")) -Message 'The PWA shell must use centralized cache-busting metadata, preload every cache-busted feature module, and preload the progression and magic-item data.'
Assert-Condition -Condition ($html.Contains('value="ghukliak"') -and $html.Contains('Goblin') -and $appScript.Contains("languageSelect?.value === 'ghukliak'") -and $translatorWorker.Contains("message.language === 'ghukliak'")) -Message 'The PWA translator must expose the Goblin/Ghukliak language in its UI and worker.'
Assert-Condition -Condition ([System.Text.RegularExpressions.Regex]::IsMatch($styles, '\.translation-loading\[hidden\]\s*\{\s*display:\s*none\s*!important;\s*\}', [System.Text.RegularExpressions.RegexOptions]::Singleline)) -Message 'The translator loading indicator must remain hidden whenever its hidden attribute is set.'
Assert-Condition -Condition (!$translatorWorker.Contains(".replaceAll('’', `"'`")")) -Message 'Translator normalization must preserve distinct straight- and curly-apostrophe lexicon terms.'
Assert-Condition -Condition ($requestTranslationFunction.IndexOf('const id = ++translatorRequestId;', [System.StringComparison]::Ordinal) -ge 0 -and $requestTranslationFunction.IndexOf('const id = ++translatorRequestId;', [System.StringComparison]::Ordinal) -lt $requestTranslationFunction.IndexOf('if (source.trim().length === 0)', [System.StringComparison]::Ordinal) -and !$appScript.Contains('if (message.loading)')) -Message 'Every translator input state must invalidate prior worker responses before early-return validation.'
Assert-Condition -Condition (!$serviceWorker.Contains("url.pathname.includes('/XP/')")) -Message 'The service worker must never fetch or cache legacy public XP data.'
Assert-Condition -Condition ($html.Contains('class="magic-items-dashboard message-player-form"') -and $styles.Contains('.message-player-form > #message-player-recipient') -and $styles.Contains('margin-block: 5px;') -and $styles.Contains('.message-player-form > #message-player-text') -and $styles.Contains('margin-top: 5px;') -and $styles.Contains('.message-player-form > .magic-items-source-row') -and $styles.Contains('margin-top: 10px;')) -Message 'The Message a Player form must preserve the requested spacing between its labels, fields, and submit row.'
Assert-Condition -Condition ($serviceWorker.Contains("'./party-funds.json'")) -Message 'The PWA shell must preload party-funds data.'
$apacheConfig = Get-Content -Raw -LiteralPath (Join-Path $PwaRoot '.htaccess')
Assert-Condition -Condition ($apacheConfig.Contains('RewriteRule ^XP(?:/|$) - [R=404,L,NC]')) -Message 'Apache must deny legacy public XP paths.'
Assert-Condition -Condition (!(Test-Path -LiteralPath (Join-Path $PwaRoot 'XP'))) -Message 'Legacy XP histories must not remain in the public PWA tree.'
Assert-Condition -Condition ($apacheConfig.Contains('AddType image/webp .webp')) -Message 'Apache must serve WebP hero tokens with the correct MIME type.'
Assert-Condition -Condition ($apacheConfig.Contains('level-progression\.json')) -Message 'Apache must require fresh level-progression data.'
Assert-Condition -Condition ($apacheConfig.Contains('img-src ''self'' data: https://*.obsidian.md')) -Message 'The content security policy must allow preferred wiki hero images.'
Assert-Condition -Condition ($apacheConfig.Contains('connect-src ''self'' https://publish-01.obsidian.md')) -Message 'The content security policy must allow the preferred magic-item wiki source.'
Assert-Condition -Condition ($apacheConfig.Contains("object-src 'none'") -and $apacheConfig.Contains("frame-src 'none'") -and $apacheConfig.Contains('upgrade-insecure-requests')) -Message 'The content security policy must deny plugin/frame execution and upgrade insecure requests.'
Assert-Condition -Condition ($apacheConfig.Contains('Strict-Transport-Security "max-age=31536000"')) -Message 'HSTS must be enabled for the PWA host.'
Assert-Condition -Condition ($apacheConfig.Contains('magic-items\.json|party-funds\.json|quests\.json')) -Message 'Apache must require revalidation for public quest, party funds, and magic-item data.'
Assert-Condition -Condition ($apacheConfig.Contains('campaign-search\.json')) -Message 'Apache must require revalidation for the scheduled campaign-search word-count data.'
Assert-Condition -Condition ($apacheConfig.Contains('data/heroes\.json|data/hero-tokens/[^/]+')) -Message 'Apache must require revalidation for hero-token metadata and images.'
Assert-Condition -Condition ($html.Contains('id="update-banner"') -and $html.Contains('id="update-apply"') -and $appScript.Contains('SKIP_WAITING') -and $serviceWorker.Contains("event.data?.type === 'SKIP_WAITING'")) -Message 'The PWA must expose an explicit service-worker update prompt.'
Assert-Condition -Condition ($serviceWorker.Contains('cacheAssets') -and $serviceWorker.Contains('deleteCurrentCaches') -and $serviceWorker.Contains('safeCachePut') -and $serviceWorker.Contains('isValidJsonPayload') -and $serviceWorker.Contains('isValidCachedResponse') -and $serviceWorker.Contains('fetchValidated') -and $serviceWorker.Contains('NAVIGATION_TIMEOUT_MS') -and $serviceWorker.Contains('rejectObsoleteWorker') -and !$serviceWorker.Contains('.then(() => self.skipWaiting())') -and $serviceWorkerTests.Contains('testHttpErrorPrefersValidCachedShell') -and $serviceWorkerTests.Contains('testMalformedJsonNetworkPrefersValidCachedData') -and $serviceWorkerTests.Contains('testMandatoryPrecacheRejectsInvalidJsonAndDeletesShell') -and $serviceWorkerTests.Contains('testNavigationFallsBackAfterBoundedNetworkTimeout')) -Message 'Service-worker installation, response validation, navigation timeout, and cache reads must fail closed on interrupted, corrupt, quota-limited, or obsolete-worker paths.'
Assert-Condition -Condition ($serviceWorkerTests.Contains('testPartialInstallDeletesVersionedCaches') -and $serviceWorkerTests.Contains('testQuotaFailureReturnsNetworkResponse') -and $serviceWorkerTests.Contains('testOptionalPackRequestsBypassServiceWorker') -and $serviceWorkerTests.Contains('testCorruptNavigationFallbackUsesValidOfflineShell') -and $serviceWorkerTests.Contains('testObsoleteWorkerCannotDeleteNewerCaches')) -Message 'Service-worker failure-injection coverage is incomplete.'
Assert-Condition -Condition ($html.Contains('id="xp-retry"') -and $html.Contains('id="quests-retry"') -and $html.Contains('id="xp-awards-retry"') -and $html.Contains('id="messages-retry"') -and $html.Contains('id="magic-items-freshness"') -and $html.Contains('id="party-funds-freshness"') -and $html.Contains('id="messages-freshness"') -and $appScript.Contains("void loadXpAwards(true)")) -Message 'Protected PWA views must expose freshness indicators and explicit retry controls.'

Write-Output "PWA verified: $($lexiconCounts.orcish) Orcish terms, $($lexiconCounts.elvish) Elvish terms, $($lexiconCounts.ghukliak) Ghukliak terms, $(@($heroData.heroes).Count) player tokens and the Dungeon Master token, $($campaignSearch.pageCount) full-text campaign pages, install manifest and offline shell valid."
