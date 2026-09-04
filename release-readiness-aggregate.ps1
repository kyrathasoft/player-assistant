[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EvidencePath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$ExpectedRevision,
    [string]$ExpectedBranch,
    [datetimeoffset]$Now = [datetimeoffset]::UtcNow,
    [int]$MaxAgeHours = 48
)

$ErrorActionPreference = 'Stop'
$Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$RequiredGates = @(
    'source-revision','ci-required-checks','focused-tests','full-tests','build','format','static-analysis',
    'secret-scan','hygiene','release-manifest','package-parity','provenance','installer-update-portable',
    'deployment','live-http','backup','rollback'
)
$SecretKeyPattern = '(?i)(password|passwd|secret|token|cookie|private.?key|credential|authorization|bearer|storage.?state)'

function Canonical-Json([object]$Value) {
    (($Value | ConvertTo-Json -Depth 30 -Compress) -replace "`r`n", "`n")
}
function Get-Hash([string]$Text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToUpperInvariant() }
    finally { $sha.Dispose() }
}
function Redact([object]$Value) {
    if ($null -eq $Value) { return $null }
    if ($Value -is [string]) {
        if ($Value -match '(?i)(bearer\s+|-----BEGIN .*PRIVATE KEY-----|sk-[A-Za-z0-9_-]{8,})') { return '[REDACTED]' }
        return $Value
    }
    if ($Value -is [System.Collections.IDictionary]) {
        $o = [ordered]@{}
        foreach ($k in $Value.Keys) { $o[[string]$k] = if ([string]$k -match $SecretKeyPattern) { '[REDACTED]' } else { Redact $Value[$k] } }
        return $o
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) { return @($Value | ForEach-Object { Redact $_ }) }
    $o = [ordered]@{}
    foreach ($p in $Value.PSObject.Properties) { $o[$p.Name] = if ($p.Name -match $SecretKeyPattern) { '[REDACTED]' } else { Redact $p.Value } }
    return $o
}
function Fail([string]$Message) { throw "Release readiness evidence rejected: $Message" }
function Require([bool]$Condition, [string]$Message) { if (!$Condition) { Fail $Message } }
function Read-Json([string]$Path) {
    Require (Test-Path -LiteralPath $Path -PathType Leaf) "evidence file is missing: $Path"
    try { return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json } catch { Fail "invalid JSON in $Path" }
}
function Get-Property([object]$Object, [string]$Name) { $Object.PSObject.Properties[$Name].Value }
function Assert-Revision([string]$Revision, [string]$Expected, [string]$Where) {
    Require ($Revision -match '^[0-9a-fA-F]{40}$') "$Where has no exact 40-character source revision"
    if (![string]::IsNullOrWhiteSpace($Expected)) { Require ($Revision.ToLowerInvariant() -eq $Expected.ToLowerInvariant()) "$Where source revision does not match expected revision" }
}
function Assert-Record([object]$Record, [string]$ExpectedRev, [string]$ExpectedBr) {
    foreach ($p in @('name','status','source_revision','branch','observed_at','integrity_sha256')) { Require ($null -ne $Record.PSObject.Properties[$p]) "record is missing $p" }
    Require ($Record.name -in $RequiredGates) "unknown gate '$($Record.name)'"
    Assert-Revision ([string]$Record.source_revision) $ExpectedRev "gate '$($Record.name)'"
    Require ([string]$Record.branch -eq $ExpectedBr) "gate '$($Record.name)' branch mismatch"
    try { $observed = [datetimeoffset]::Parse([string]$Record.observed_at) } catch { Fail "gate '$($Record.name)' has an invalid timestamp" }
    Require ($observed.Offset -eq [timespan]::Zero) "gate '$($Record.name)' timestamp is not UTC"
    Require (($Now - $observed).TotalHours -ge 0) "gate '$($Record.name)' timestamp is in the future"
    Require (($Now - $observed).TotalHours -le $MaxAgeHours) "gate '$($Record.name)' evidence is stale"
    $copy = [ordered]@{}
    foreach ($p in $Record.PSObject.Properties) { if ($p.Name -ne 'integrity_sha256') { $copy[$p.Name] = Redact $p.Value } }
    Require ([string]$Record.integrity_sha256 -eq (Get-Hash (Canonical-Json $copy))) "gate '$($Record.name)' integrity hash mismatch"
    Require ([string]$Record.status -eq 'passed') "gate '$($Record.name)' status is '$($Record.status)'; only passed is acceptable"
}
function Assert-Artifact([object]$Artifact, [string]$ExpectedRev, [string]$ExpectedBr) {
    foreach ($p in @('name','status','source_revision','branch','bytes','sha256','complete','verified')) { Require ($null -ne $Artifact.PSObject.Properties[$p]) "artifact is missing $p" }
    Assert-Revision ([string]$Artifact.source_revision) $ExpectedRev "artifact '$($Artifact.name)'"
    Require ([string]$Artifact.branch -eq $ExpectedBr) "artifact '$($Artifact.name)' branch mismatch"
    Require ([string]$Artifact.status -eq 'passed' -and [bool]$Artifact.complete -and [bool]$Artifact.verified) "artifact '$($Artifact.name)' is incomplete or unverified"
    Require ([int64]$Artifact.bytes -gt 0) "artifact '$($Artifact.name)' is empty"
    Require ([string]$Artifact.sha256 -match '^[0-9A-Fa-f]{64}$') "artifact '$($Artifact.name)' has no valid SHA256"
}

$root = [IO.Path]::GetFullPath($EvidencePath)
Require (Test-Path -LiteralPath $root -PathType Leaf) "evidence document is missing: $root"
if ([string]::IsNullOrWhiteSpace($ExpectedRevision)) {
    try { $ExpectedRevision = (& git -C (Split-Path $root -Parent) rev-parse HEAD 2>$null).Trim() } catch { $ExpectedRevision = $null }
    Require ($ExpectedRevision -match '^[0-9a-fA-F]{40}$') 'the current source revision could not be resolved from Git'
}
if ([string]::IsNullOrWhiteSpace($ExpectedBranch)) {
    try { $ExpectedBranch = (& git -C (Split-Path $root -Parent) branch --show-current 2>$null).Trim() } catch { $ExpectedBranch = $null }
}
$input = Read-Json $root
foreach ($p in @('schema_version','source_revision','branch','generated_at','records','artifacts','exceptions')) { Require ($null -ne $input.PSObject.Properties[$p]) "document is missing $p" }
Require ([int]$input.schema_version -eq 1) 'unsupported evidence schema'
Assert-Revision ([string]$input.source_revision) $ExpectedRevision 'document'
if ([string]::IsNullOrWhiteSpace($ExpectedBranch)) { $ExpectedBranch = [string]$input.branch }
Require (![string]::IsNullOrWhiteSpace($ExpectedBranch)) 'expected branch is missing'
Require ([string]$input.branch -eq $ExpectedBranch) 'document branch mismatch'
try { $generated = [datetimeoffset]::Parse([string]$input.generated_at) } catch { Fail 'document has an invalid generated_at timestamp' }
Require ($generated.Offset -eq [timespan]::Zero) 'document generated_at is not UTC'
Require (($Now - $generated).TotalHours -ge 0 -and ($Now - $generated).TotalHours -le $MaxAgeHours) 'document evidence is stale or from the future'

$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($record in @($input.records)) { Require $seen.Add([string]$record.name) "duplicate gate '$($record.name)'"; Assert-Record $record $input.source_revision $ExpectedBranch }
foreach ($gate in $RequiredGates) { Require $seen.Contains($gate) "required gate '$gate' is missing" }
foreach ($artifact in @($input.artifacts)) { Assert-Artifact $artifact $input.source_revision $ExpectedBranch }
$acceptedExceptions = @()
foreach ($exception in @($input.exceptions)) {
    foreach ($p in @('id','accepted','scope','reason')) { Require ($null -ne $exception.PSObject.Properties[$p]) "exception is missing $p" }
    Require ([bool]$exception.accepted) "exception '$($exception.id)' is not explicitly accepted"
    Require ([string]$exception.scope -eq 'external') "exception '$($exception.id)' is not external"
    Require (![string]::IsNullOrWhiteSpace([string]$exception.reason)) "exception '$($exception.id)' has no reason"
    $acceptedExceptions += [ordered]@{ id=[string]$exception.id; scope='external'; reason=[string]$exception.reason; status='accepted-blocker' }
}

$sanitized = Redact $input
$report = [ordered]@{
    schema_version = 1
    report_type = 'release-readiness'
    source_revision = [string]$input.source_revision
    branch = [string]$input.branch
    generated_at = $Now.ToUniversalTime().ToString('O')
    required_gates = @($RequiredGates)
    gate_count = $RequiredGates.Count
    artifact_count = @($input.artifacts).Count
    accepted_external_exceptions = $acceptedExceptions
    status = if ($acceptedExceptions.Count -gt 0) { 'blocked' } else { 'ready' }
    readiness = ($acceptedExceptions.Count -eq 0)
    evidence = $sanitized
}
$reportJson = Canonical-Json $report
$report.report_sha256 = Get-Hash $reportJson
$out = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($out)) | Out-Null
[IO.File]::WriteAllText($out, (Canonical-Json $report) + "`n", $Utf8NoBom)
Write-Output "Release readiness report written: $out ($($report.status))."
