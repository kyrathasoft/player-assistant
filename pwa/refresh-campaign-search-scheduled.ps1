[CmdletBinding()]
param(
    [switch]$Force,
    [string]$OutputPath = (Join-Path $PSScriptRoot 'campaign-search.json')
)

$ErrorActionPreference = 'Stop'

$centralTimeZone = [TimeZoneInfo]::FindSystemTimeZoneById('Central Standard Time')
$centralNow = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $centralTimeZone)
$isScheduledWindow = $centralNow.DayOfWeek -eq [DayOfWeek]::Friday -and $centralNow.Hour -eq 7

if (!$Force -and !$isScheduledWindow) {
    Write-Output "Skipping campaign word-count refresh; Central time is $($centralNow.ToString('yyyy-MM-dd HH:mm:ss zzz')) and the scheduled window is Friday at 07:00."
    exit 0
}

& (Join-Path $PSScriptRoot 'refresh-campaign-search.ps1') -OutputPath $OutputPath

$payload = Get-Content -Raw -LiteralPath $OutputPath | ConvertFrom-Json
if ([int]$payload.schemaVersion -ne 2 -or
    [int]$payload.pageCount -le 0 -or
    [int]$payload.contentPageCount -le 0 -or
    [int]$payload.failedPageCount -ne 0 -or
    [long]$payload.wordCount -le 0) {
    throw 'The refreshed campaign search payload failed its word-count integrity checks.'
}

Write-Output "Campaign word count refreshed at Central time $($centralNow.ToString('yyyy-MM-dd HH:mm:ss zzz')): $($payload.wordCount) words across $($payload.contentPageCount) content pages."
