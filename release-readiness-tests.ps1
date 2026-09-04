$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$Aggregator = Join-Path $Root 'release-readiness-aggregate.ps1'
$Scratch = Join-Path ([IO.Path]::GetTempPath()) ('release-readiness-fixtures-' + [Guid]::NewGuid().ToString('N'))
$Now = [datetimeoffset]'2026-08-31T12:00:00+00:00'
$Revision = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$Branch = 'fix/pwa-pageshow-auth'
$Utf8 = [Text.UTF8Encoding]::new($false)
$Gates = @('source-revision','ci-required-checks','focused-tests','full-tests','build','format','static-analysis','secret-scan','hygiene','release-manifest','package-parity','provenance','installer-update-portable','deployment','live-http','backup','rollback')
function Canonical($x) { (($x | ConvertTo-Json -Depth 20 -Compress) -replace "`r`n", "`n") }
function Hash($s) { $sha=[Security.Cryptography.SHA256]::Create(); try { ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($s)))).Replace('-','').ToUpperInvariant() } finally { $sha.Dispose() } }
function New-Document([switch]$AcceptedException) {
  $records = foreach ($name in $Gates) {
    $r = [ordered]@{ name=$name; status='passed'; source_revision=$Revision; branch=$Branch; observed_at=$Now.ToString('O'); details=[ordered]@{ result='verified'; output='sanitized' } }
    $r.integrity_sha256 = Hash (Canonical $r); [pscustomobject]$r
  }
  $doc = [ordered]@{ schema_version=1; source_revision=$Revision; branch=$Branch; generated_at=$Now.ToString('O'); records=@($records); artifacts=@([ordered]@{ name='release-package'; status='passed'; source_revision=$Revision; branch=$Branch; bytes=42; sha256=('B'*64); complete=$true; verified=$true }); exceptions=@() }
  if ($AcceptedException) { $doc.exceptions=@([ordered]@{ id='RPOL-ADMIN'; accepted=$true; scope='external'; reason='Accepted external RPOL limitation; broker remains fail-closed.' }) }
  [pscustomobject]$doc
}
function Write-Doc($path,$doc) { [IO.File]::WriteAllText($path,(Canonical $doc)+"`n",$Utf8) }
function Invoke-Case($name,$doc,$expected) {
  $input=Join-Path $Scratch "$name.json"; $output=Join-Path $Scratch "$name.report.json"; Write-Doc $input $doc
  & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $Aggregator -EvidencePath $input -OutputPath $output -ExpectedRevision $Revision -ExpectedBranch $Branch -Now $Now
  if ($expected -eq 'pass') { if ($LASTEXITCODE -ne 0 -or !(Test-Path $output)) { throw "$name should pass." }; return }
  if ($LASTEXITCODE -eq 0) { throw "$name should fail closed." }
}
try {
  if (Test-Path $Scratch) { Remove-Item $Scratch -Recurse -Force }; New-Item $Scratch -ItemType Directory | Out-Null
Invoke-Case 'positive' (New-Document) 'pass'
$d=New-Document; $d.records=@($d.records | Where-Object name -ne 'backup'); Invoke-Case 'missing-evidence' $d 'fail'
$d=New-Document; $d.generated_at='2026-08-28T12:00:00+00:00'; Invoke-Case 'stale-evidence' $d 'fail'
$d=New-Document; $d.branch='main'; Invoke-Case 'branch-mismatch' $d 'fail'
$d=New-Document; $r=$d.records | Where-Object name -eq 'full-tests'; $r.status='failed'; $r.integrity_sha256=Hash (Canonical ([ordered]@{name=$r.name;status=$r.status;source_revision=$r.source_revision;branch=$r.branch;observed_at=$r.observed_at;details=$r.details})); Invoke-Case 'failed-check' $d 'fail'
$d=New-Document; $d.artifacts[0].complete=$false; Invoke-Case 'partial-artifact' $d 'fail'
$d=New-Document; $d.records=@($d.records)+$d.records[0]; Invoke-Case 'conflicting-statuses' $d 'fail'
Invoke-Case 'accepted-external-blocker' (New-Document -AcceptedException) 'pass'
$report=Get-Content -Raw (Join-Path $Scratch 'accepted-external-blocker.report.json') | ConvertFrom-Json
if ($report.status -ne 'blocked' -or $report.readiness -ne $false) { throw 'Accepted external exception must remain an explicit blocker.' }
$all = Get-ChildItem $Scratch -Filter '*.json' -File | Where-Object Name -notlike '*.report.json'
if ($all.Count -ne 8) { throw "Expected 8 deterministic fixtures, found $($all.Count)." }
Write-Output 'Release readiness evidence fixtures passed.'
} finally {
  if (Test-Path $Scratch) { Remove-Item $Scratch -Recurse -Force }
}
