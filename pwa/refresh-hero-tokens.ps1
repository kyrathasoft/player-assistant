param(
    [string]$SiteUrl = 'https://publish.obsidian.md/scarlethorizons',
    [string]$ListingPath = 'PCs/Player Characters Listing',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'data\hero-tokens'),
    [string]$ManifestPath = (Join-Path $PSScriptRoot 'data\heroes.json')
)

$ErrorActionPreference = 'Stop'

function ConvertTo-AccessPath {
    param(
        [Parameter(Mandatory = $true)][string]$VaultPath,
        [switch]$Markdown
    )

    $segments = $VaultPath.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { [uri]::EscapeDataString($_) }
    $path = $segments -join '/'
    return $Markdown ? "$path.md" : $path
}

function Split-MarkdownTableRow {
    param([Parameter(Mandatory = $true)][string]$Line)

    return @($Line.Trim().Trim('|') -split '(?<!\\)\|' |
        ForEach-Object { $_.Trim().Replace('\|', '|') })
}

function Get-WikiLinkNames {
    param([Parameter(Mandatory = $true)][string]$Cell)

    $match = [regex]::Match($Cell, '\[\[(?<target>[^\]|]+)(?:\|(?<display>[^\]]+))?\]\]')
    if (!$match.Success) {
        return @($Cell.Trim())
    }

    $target = $match.Groups['target'].Value.Trim()
    $display = $match.Groups['display'].Value.Trim()
    if ($display.Length -eq 0) {
        $display = $target
    }

    $targetName = ($target -split ',', 2)[0].Trim()
    return @($display, $targetName, ($display -split '\s+', 2)[0]) |
        Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique
}

function Get-WikiLinkTarget {
    param([Parameter(Mandatory = $true)][string]$Cell)

    $match = [regex]::Match($Cell, '\[\[(?<target>[^\]|]+)(?:\|[^\]]+)?\]\]')
    if (!$match.Success) {
        throw "The hero name is not linked to a wiki page: $Cell"
    }
    return $match.Groups['target'].Value.Trim()
}

function Get-PublishPageUrl {
    param(
        [Parameter(Mandatory = $true)][string]$SiteUrl,
        [Parameter(Mandatory = $true)][string]$ListingPath,
        [Parameter(Mandatory = $true)][string]$WikiTarget
    )

    $targetWithoutHeading = ($WikiTarget -split '#', 2)[0].Trim().Trim('/')
    if ([string]::IsNullOrWhiteSpace($targetWithoutHeading)) {
        throw "The hero wiki target is empty: $WikiTarget"
    }
    $listingDirectory = ($ListingPath -replace '\\', '/') -replace '/[^/]+$', ''
    $vaultPath = if ($targetWithoutHeading.Contains('/')) {
        $targetWithoutHeading
    }
    else {
        "$listingDirectory/$targetWithoutHeading".Trim('/')
    }
    $publishPath = $vaultPath.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { [uri]::EscapeDataString($_).Replace('%20', '+') }
    return "$($SiteUrl.TrimEnd('/'))/$($publishPath -join '/')"
}

function Get-JsonPayload {
    param([Parameter(Mandatory = $true)][string]$Markdown)

    $trimmed = $Markdown.Trim()
    if ($trimmed.StartsWith('```')) {
        $trimmed = $trimmed -replace '^\s*```(?:json)?\s*', ''
        $trimmed = $trimmed -replace '\s*```\s*$', ''
    }
    return $trimmed.Trim()
}

function Save-WikiImageSnapshot {
    param(
        [Parameter(Mandatory = $true)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)][string]$PublishContentHost,
        [Parameter(Mandatory = $true)][string]$SiteUid,
        [Parameter(Mandatory = $true)][string]$VaultAssetPath,
        [Parameter(Mandatory = $true)][string]$TokenFileName,
        [Parameter(Mandatory = $true)][string]$OutputDirectory
    )

    $assetAccessPath = ConvertTo-AccessPath -VaultPath $VaultAssetPath
    $assetUrl = "https://$PublishContentHost/access/$SiteUid/$assetAccessPath"
    $response = $Client.GetAsync($assetUrl).GetAwaiter().GetResult()
    try {
        if (!$response.IsSuccessStatusCode) {
            throw "Hero token download failed with HTTP $([int]$response.StatusCode): $TokenFileName"
        }
        $mediaType = [string]$response.Content.Headers.ContentType.MediaType
        if (!$mediaType.StartsWith('image/', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Hero token did not return an image content type: $TokenFileName"
        }
        $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        if ($bytes.Length -lt 16 -or $bytes.Length -gt 5MB) {
            throw "Hero token size is outside the allowed range: $TokenFileName"
        }

        $remoteHash = [Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        $destinationPath = Join-Path $OutputDirectory $TokenFileName
        $localHash = if (Test-Path -LiteralPath $destinationPath -PathType Leaf) {
            [Convert]::ToHexString(
                [System.Security.Cryptography.SHA256]::HashData(
                    [System.IO.File]::ReadAllBytes($destinationPath))).ToLowerInvariant()
        }
        else {
            ''
        }
        $changed = $localHash -ne $remoteHash
        if ($changed) {
            $temporaryPath = "$destinationPath.tmp"
            [System.IO.File]::WriteAllBytes($temporaryPath, $bytes)
            Move-Item -LiteralPath $temporaryPath -Destination $destinationPath -Force
        }

        return [pscustomobject]@{
            WikiUrl = $assetUrl
            Sha256 = $remoteHash
            Changed = $changed
        }
    }
    finally {
        $response.Dispose()
    }
}

$siteUri = [uri]$SiteUrl
$siteSlug = $siteUri.AbsolutePath.Trim('/')
if ([string]::IsNullOrWhiteSpace($siteSlug)) {
    throw 'The Obsidian Publish site URL must include its site slug.'
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AutomaticDecompression = [System.Net.DecompressionMethods]::All
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(45)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('PlayerAssistant-PWA-HeroTokens/1.0')

try {
    $siteHtml = $client.GetStringAsync($siteUri).GetAwaiter().GetResult()
    $uidMatch = [regex]::Match($siteHtml, '"uid":"(?<uid>[^"]+)"')
    $hostMatch = [regex]::Match($siteHtml, '"host":"(?<host>[^"]+)"')
    if (!$uidMatch.Success -or !$hostMatch.Success) {
        throw 'Obsidian Publish site metadata could not be found.'
    }

    $siteUid = $uidMatch.Groups['uid'].Value
    $publishContentHost = $hostMatch.Groups['host'].Value
    if ($publishContentHost -notmatch '^publish-\d+\.obsidian\.md$') {
        throw "Unexpected Obsidian Publish content host: $publishContentHost"
    }

    $listingAccessPath = ConvertTo-AccessPath -VaultPath $ListingPath -Markdown
    $listingMarkdownUrl = "https://$publishContentHost/access/$siteUid/$listingAccessPath"
    $listingMarkdown = $client.GetStringAsync($listingMarkdownUrl).GetAwaiter().GetResult()

    $manifestAccessPath = ConvertTo-AccessPath -VaultPath 'asset-manifest' -Markdown
    $assetManifestUrl = "https://$publishContentHost/access/$siteUid/$manifestAccessPath"
    $assetManifestMarkdown = $client.GetStringAsync($assetManifestUrl).GetAwaiter().GetResult()
    $assetManifest = Get-JsonPayload -Markdown $assetManifestMarkdown | ConvertFrom-Json -AsHashtable

    $tableRows = @($listingMarkdown -split '\r?\n' |
        Where-Object { $_.Trim().StartsWith('|') -and $_.Trim().EndsWith('|') } |
        ForEach-Object { ,(Split-MarkdownTableRow -Line $_) })
    if ($tableRows.Count -lt 3) {
        throw 'The player-character listing did not contain a usable Markdown table.'
    }

    $headers = @($tableRows[0] | ForEach-Object { ($_ -replace '[^a-zA-Z]', '').ToLowerInvariant() })
    $nameIndex = [array]::IndexOf($headers, 'name')
    $tokenIndex = [array]::IndexOf($headers, 'token')
    if ($nameIndex -lt 0 -or $tokenIndex -lt 0) {
        throw 'The player-character listing must contain Name and Token columns.'
    }

    [System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    $heroes = [System.Collections.Generic.List[object]]::new()
    $changedTokenCount = 0
    foreach ($cells in $tableRows | Select-Object -Skip 2) {
        if ($cells.Count -le [Math]::Max($nameIndex, $tokenIndex)) {
            continue
        }

        $tokenMatch = [regex]::Match($cells[$tokenIndex], '!\[\[(?<file>[^|#\]]+)')
        if (!$tokenMatch.Success) {
            continue
        }

        $tokenFileName = [System.IO.Path]::GetFileName($tokenMatch.Groups['file'].Value.Trim())
        if ($tokenFileName -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]*\.(?:avif|gif|jpe?g|png|webp)$') {
            throw "The listing contains an unsafe hero token filename: $tokenFileName"
        }
        if (!$assetManifest.ContainsKey($tokenFileName)) {
            throw "The asset manifest does not contain the listed hero token: $tokenFileName"
        }

        $aliases = @(Get-WikiLinkNames -Cell $cells[$nameIndex])
        if ($aliases.Count -eq 0) {
            throw "The listing contains no usable name for hero token: $tokenFileName"
        }
        $wikiTarget = Get-WikiLinkTarget -Cell $cells[$nameIndex]
        $wikiPage = Get-PublishPageUrl `
            -SiteUrl $SiteUrl `
            -ListingPath $ListingPath `
            -WikiTarget $wikiTarget

        $vaultAssetPath = [string]$assetManifest[$tokenFileName]
        $snapshot = Save-WikiImageSnapshot `
            -Client $client `
            -PublishContentHost $publishContentHost `
            -SiteUid $siteUid `
            -VaultAssetPath $vaultAssetPath `
            -TokenFileName $tokenFileName `
            -OutputDirectory $OutputDirectory
        if ($snapshot.Changed) {
            $changedTokenCount++
        }

        $heroes.Add([ordered]@{
            name = $aliases[0]
            aliases = $aliases
            token = "data/hero-tokens/$tokenFileName"
            wikiToken = $snapshot.WikiUrl
            wikiPage = $wikiPage
            sha256 = $snapshot.Sha256
        })
    }

    if ($heroes.Count -eq 0) {
        throw 'No hero tokens were resolved from the player-character listing.'
    }

    $dungeonMasterTokenFileName = 'dungeon-master.webp'
    if (!$assetManifest.ContainsKey($dungeonMasterTokenFileName)) {
        throw "The asset manifest does not contain the Dungeon Master token: $dungeonMasterTokenFileName"
    }
    $dungeonMasterAssetPath = [string]$assetManifest[$dungeonMasterTokenFileName]
    $dungeonMasterWikiPath = ConvertTo-AccessPath -VaultPath $dungeonMasterAssetPath
    $dungeonMasterWikiUrl = "https://$publishContentHost/access/$siteUid/$dungeonMasterWikiPath"
    $dungeonMasterLocalPath = Join-Path $OutputDirectory $dungeonMasterTokenFileName
    if (!(Test-Path -LiteralPath $dungeonMasterLocalPath -PathType Leaf)) {
        throw "The locally approved Dungeon Master token is missing: $dungeonMasterLocalPath"
    }
    $dungeonMasterHash = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.IO.File]::ReadAllBytes($dungeonMasterLocalPath))).ToLowerInvariant()
    $dungeonMasterVersionedFileName = "dungeon-master-$($dungeonMasterHash.Substring(0, 12)).webp"
    $dungeonMasterVersionedPath = Join-Path $OutputDirectory $dungeonMasterVersionedFileName
    Copy-Item -LiteralPath $dungeonMasterLocalPath -Destination $dungeonMasterVersionedPath -Force

    $payload = [ordered]@{
        schemaVersion = 1
        source = "$($SiteUrl.TrimEnd('/'))/$($ListingPath.Replace(' ', '+'))"
        dungeonMaster = [ordered]@{
            name = 'Dungeon Master'
            aliases = @('Dungeon Master')
            token = "data/hero-tokens/$dungeonMasterVersionedFileName"
            wikiToken = $dungeonMasterWikiUrl
            preferLocal = $true
            sha256 = $dungeonMasterHash
        }
        heroes = $heroes
    }
    $json = $payload | ConvertTo-Json -Depth 6 -Compress
    [System.IO.File]::WriteAllText($ManifestPath, $json, [System.Text.UTF8Encoding]::new($false))

    Write-Output "Hero tokens refreshed: $($heroes.Count) active characters and the Dungeon Master; $changedTokenCount website fallback image(s) updated."
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
