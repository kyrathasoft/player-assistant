# Graph Report - .  (2026-07-06)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 1758 nodes · 3696 edges · 89 communities (82 shown, 7 thin omitted)
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `08ea4036`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- [[_COMMUNITY_Community 0|Community 0]]
- [[_COMMUNITY_Community 1|Community 1]]
- [[_COMMUNITY_Community 2|Community 2]]
- [[_COMMUNITY_Community 3|Community 3]]
- [[_COMMUNITY_Community 4|Community 4]]
- [[_COMMUNITY_Community 5|Community 5]]
- [[_COMMUNITY_Community 6|Community 6]]
- [[_COMMUNITY_Community 7|Community 7]]
- [[_COMMUNITY_Community 8|Community 8]]
- [[_COMMUNITY_Community 9|Community 9]]
- [[_COMMUNITY_Community 10|Community 10]]
- [[_COMMUNITY_Community 11|Community 11]]
- [[_COMMUNITY_Community 12|Community 12]]
- [[_COMMUNITY_Community 13|Community 13]]
- [[_COMMUNITY_Community 14|Community 14]]
- [[_COMMUNITY_Community 15|Community 15]]
- [[_COMMUNITY_Community 16|Community 16]]
- [[_COMMUNITY_Community 17|Community 17]]
- [[_COMMUNITY_Community 18|Community 18]]
- [[_COMMUNITY_Community 19|Community 19]]
- [[_COMMUNITY_Community 20|Community 20]]
- [[_COMMUNITY_Community 21|Community 21]]
- [[_COMMUNITY_Community 22|Community 22]]
- [[_COMMUNITY_Community 23|Community 23]]
- [[_COMMUNITY_Community 24|Community 24]]
- [[_COMMUNITY_Community 25|Community 25]]
- [[_COMMUNITY_Community 26|Community 26]]
- [[_COMMUNITY_Community 27|Community 27]]
- [[_COMMUNITY_Community 28|Community 28]]
- [[_COMMUNITY_Community 29|Community 29]]
- [[_COMMUNITY_Community 30|Community 30]]
- [[_COMMUNITY_Community 31|Community 31]]
- [[_COMMUNITY_Community 32|Community 32]]
- [[_COMMUNITY_Community 33|Community 33]]
- [[_COMMUNITY_Community 34|Community 34]]
- [[_COMMUNITY_Community 35|Community 35]]
- [[_COMMUNITY_Community 36|Community 36]]
- [[_COMMUNITY_Community 37|Community 37]]
- [[_COMMUNITY_Community 38|Community 38]]
- [[_COMMUNITY_Community 39|Community 39]]
- [[_COMMUNITY_Community 40|Community 40]]
- [[_COMMUNITY_Community 41|Community 41]]
- [[_COMMUNITY_Community 42|Community 42]]
- [[_COMMUNITY_Community 43|Community 43]]
- [[_COMMUNITY_Community 44|Community 44]]
- [[_COMMUNITY_Community 45|Community 45]]
- [[_COMMUNITY_Community 46|Community 46]]
- [[_COMMUNITY_Community 47|Community 47]]
- [[_COMMUNITY_Community 48|Community 48]]
- [[_COMMUNITY_Community 49|Community 49]]
- [[_COMMUNITY_Community 50|Community 50]]
- [[_COMMUNITY_Community 51|Community 51]]
- [[_COMMUNITY_Community 52|Community 52]]
- [[_COMMUNITY_Community 53|Community 53]]
- [[_COMMUNITY_Community 54|Community 54]]
- [[_COMMUNITY_Community 55|Community 55]]
- [[_COMMUNITY_Community 56|Community 56]]
- [[_COMMUNITY_Community 57|Community 57]]
- [[_COMMUNITY_Community 58|Community 58]]
- [[_COMMUNITY_Community 59|Community 59]]
- [[_COMMUNITY_Community 60|Community 60]]
- [[_COMMUNITY_Community 61|Community 61]]
- [[_COMMUNITY_Community 62|Community 62]]
- [[_COMMUNITY_Community 63|Community 63]]
- [[_COMMUNITY_Community 64|Community 64]]
- [[_COMMUNITY_Community 65|Community 65]]
- [[_COMMUNITY_Community 66|Community 66]]
- [[_COMMUNITY_Community 67|Community 67]]
- [[_COMMUNITY_Community 68|Community 68]]
- [[_COMMUNITY_Community 69|Community 69]]
- [[_COMMUNITY_Community 70|Community 70]]
- [[_COMMUNITY_Community 71|Community 71]]
- [[_COMMUNITY_Community 72|Community 72]]
- [[_COMMUNITY_Community 73|Community 73]]
- [[_COMMUNITY_Community 74|Community 74]]
- [[_COMMUNITY_Community 75|Community 75]]
- [[_COMMUNITY_Community 76|Community 76]]
- [[_COMMUNITY_Community 77|Community 77]]
- [[_COMMUNITY_Community 78|Community 78]]
- [[_COMMUNITY_Community 79|Community 79]]
- [[_COMMUNITY_Community 80|Community 80]]
- [[_COMMUNITY_Community 81|Community 81]]
- [[_COMMUNITY_Community 82|Community 82]]
- [[_COMMUNITY_Community 83|Community 83]]
- [[_COMMUNITY_Community 84|Community 84]]

## God Nodes (most connected - your core abstractions)
1. `Form1` - 211 edges
2. `PlayerAssistant` - 52 edges
3. `RpolAuthUtility` - 37 edges
4. `GameForumUtility` - 36 edges
5. `LocalSettingsUtility` - 34 edges
6. `PlayerAssistantUpdateUtility` - 33 edges
7. `RpolThreadPostUtility` - 30 edges
8. `PlayerCharacterAssetUtility` - 29 edges
9. `OrcishTranslatorUtility` - 28 edges
10. `AppSettingsUtility` - 26 edges

## Surprising Connections (you probably didn't know these)
- `AppSettingsUtility` --references--> `NetworkRequestPolicy`  [EXTRACTED]
  AppSettingsUtility.cs → NetworkRequestUtility.cs
- `Form1` --references--> `BackgroundTaskSupervisor`  [EXTRACTED]
  Form1.cs → BackgroundTaskSupervisor.cs
- `Form1` --references--> `SearchTextBox`  [EXTRACTED]
  Form1.Designer.cs → SearchTextBox.cs
- `Form1` --references--> `TheCastLoginInfo`  [EXTRACTED]
  Form1.cs → GameForumUtility.cs
- `Form1` --references--> `KeywordIndexProgress`  [EXTRACTED]
  Form1.cs → KeywordIndexCrawler.cs

## Import Cycles
- None detected.

## Communities (89 total, 7 thin omitted)

### Community 0 - "Community 0"
Cohesion: 0.05
Nodes (38): Func, AuthenticodeSignatureInfo, AuthenticodeSignaturePolicy, AuthenticodeSignatureUtility, CancellationToken, DateTimeOffset, GeneratedRegex, HttpClient (+30 more)

### Community 1 - "Community 1"
Cohesion: 0.07
Nodes (31): ConcurrentBag, CrawledPageContent, IProgress, CancellationToken, Dictionary, HashSet, int, IReadOnlyList (+23 more)

### Community 2 - "Community 2"
Cohesion: 0.08
Nodes (21): CancellationToken, DateTimeOffset, IEnumerable, List, Regex, SemaphoreSlim, Task, TimeSpan (+13 more)

### Community 3 - "Community 3"
Cohesion: 0.09
Nodes (60): Assert-AuthenticodeSignatureMatchesPolicy(), Assert-EncryptedLocalSettings(), Assert-NoForbiddenPublishArtifacts(), Assert-NoPlaintextCredentialMarkers(), Assert-NoSensitiveFiles(), Assert-PathInsideRepo(), Assert-PublishedExecutableVersion(), Assert-PublishedKeywordIndex() (+52 more)

### Community 4 - "Community 4"
Cohesion: 0.06
Nodes (22): CREDENTIAL, DllImport, IntPtr, DateTimeOffset, IDictionary, IDisposable, string, RuntimeSecretStoreUtility (+14 more)

### Community 5 - "Community 5"
Cohesion: 0.05
Nodes (21): DateTime, DialogResult, float, Form, Action, bool, DateTimeOffset, int (+13 more)

### Community 6 - "Community 6"
Cohesion: 0.09
Nodes (25): IAsyncDisposable, IBrowser, IBrowserContext, IPage, IPlaywright, IResponse, Password, bool (+17 more)

### Community 7 - "Community 7"
Cohesion: 0.10
Nodes (46): Add-RcDryRunStep(), Assert-AuthenticodeSignatureMatchesPolicy(), Assert-CodeSigningPolicyConfigured(), Assert-DependencyFreshness(), Assert-ExecutableVersion(), Assert-ExpectedChangedPaths(), Assert-NoVulnerablePackages(), Assert-PathInsideRepo() (+38 more)

### Community 8 - "Community 8"
Cohesion: 0.10
Nodes (24): Encoding, HttpCompletionOption, CancellationToken, DateTimeOffset, Dictionary, Func, HttpClient, HttpContent (+16 more)

### Community 9 - "Community 9"
Cohesion: 0.10
Nodes (15): EncryptedSettingsEnvelope, KeyScope, KeySet, byte, Dictionary, Exception, int, IReadOnlyDictionary (+7 more)

### Community 10 - "Community 10"
Cohesion: 0.11
Nodes (14): CancellationToken, Dictionary, Func, IDictionary, IEnumerable, IReadOnlyDictionary, JsonSerializerOptions, Regex (+6 more)

### Community 11 - "Community 11"
Cohesion: 0.14
Nodes (13): Func, IDictionary, IEnumerable, IReadOnlyDictionary, IReadOnlyList, Regex, OrcishAffixEntry, OrcishLanguage (+5 more)

### Community 12 - "Community 12"
Cohesion: 0.13
Nodes (3): CancellationToken, Exception, Task

### Community 13 - "Community 13"
Cohesion: 0.10
Nodes (19): BinaryWriter, Action, DateTimeOffset, Dictionary, IDisposable, int, IReadOnlyDictionary, IReadOnlyList (+11 more)

### Community 14 - "Community 14"
Cohesion: 0.11
Nodes (9): CancellationToken, Dictionary, HttpClient, Regex, string, Task, TimeSpan, PlayerCharacterAssetUtility (+1 more)

### Community 15 - "Community 15"
Cohesion: 0.19
Nodes (3): EventArgs, TheCastLoginInfo, MouseEventArgs

### Community 16 - "Community 16"
Cohesion: 0.12
Nodes (9): DestinationPath, Func, int, IReadOnlyList, STAThread, string, Program, SourcePath (+1 more)

### Community 17 - "Community 17"
Cohesion: 0.16
Nodes (10): Func, GeneratedRegex, Regex, string, Uri, NetworkUrlAllowlistUtility, NetworkUrlAllowlistValidation, NetworkUrlPolicyRule (+2 more)

### Community 18 - "Community 18"
Cohesion: 0.13
Nodes (11): Brush, Color, Font, FontFamily, Graphics, GraphicsPath, PaintEventArgs, Pen (+3 more)

### Community 19 - "Community 19"
Cohesion: 0.15
Nodes (10): CancellationToken, Dictionary, HashSet, HttpClient, HttpResponseMessage, Regex, string, Task (+2 more)

### Community 20 - "Community 20"
Cohesion: 0.18
Nodes (11): CancellationToken, Dictionary, int, InvalidOperationException, JsonSerializerOptions, Regex, string, Task (+3 more)

### Community 21 - "Community 21"
Cohesion: 0.15
Nodes (9): bool, Dictionary, Exception, int, IReadOnlyDictionary, JsonElement, string, AppSettingsUtility (+1 more)

### Community 22 - "Community 22"
Cohesion: 0.25
Nodes (9): DateTimeOffset, Exception, IEnumerable, int, string, TimeSpan, RuntimeHousekeepingOptions, RuntimeHousekeepingReport (+1 more)

### Community 23 - "Community 23"
Cohesion: 0.17
Nodes (8): CancellationToken, Exception, IReadOnlyList, string, Task, PcXpTotal, XpTrackingSnapshot, XpTrackingUtility

### Community 24 - "Community 24"
Cohesion: 0.22
Nodes (9): IReadOnlyDictionary, List, string, AppConfigurationIssue, AppConfigurationIssueSeverity, AppConfigurationValidationException, AppConfigurationValidationReport, AppConfigurationValidationUtility (+1 more)

### Community 25 - "Community 25"
Cohesion: 0.16
Nodes (14): EndpointSummary, DateTimeOffset, Dictionary, HttpRequestMessage, HttpStatusCode, int, JsonSerializerOptions, object (+6 more)

### Community 26 - "Community 26"
Cohesion: 0.23
Nodes (20): Assert-CommandFailsWith(), Assert-CommandPasses(), Assert-FileContains(), Assert-PathInsideRepo(), Assert-RcDryRunSummary(), ConvertTo-ProcessArguments(), Get-PowerShellExecutable(), Invoke-DependencyFreshnessSelfTest() (+12 more)

### Community 27 - "Community 27"
Cohesion: 0.21
Nodes (18): Assert-DiagnosticStagingIsRedacted(), Assert-DiagnosticZipIsRedacted(), Assert-PathInsideRepo(), ConvertTo-PlainObject(), Get-ExecutableVersionSummary(), Get-FileSummary(), Get-PowerShellExecutable(), Get-Sha256HashText() (+10 more)

### Community 28 - "Community 28"
Cohesion: 0.17
Nodes (9): KeywordUrls, NodeCount, CancellationToken, Dictionary, HttpClient, Task, SitemapIndexResult, SitemapUtility (+1 more)

### Community 29 - "Community 29"
Cohesion: 0.20
Nodes (11): DateTimeOffset, HttpRequestMessage, IReadOnlyCollection, Uri, CertificatePinningPolicy, CertificatePinningUtility, CertificatePinTrustEntry, ISet (+3 more)

### Community 30 - "Community 30"
Cohesion: 0.22
Nodes (8): ObsidianPublishSiteInfo, CancellationToken, Dictionary, HttpClient, Regex, Task, ObsidianPublishSiteInfo, ObsidianPublishUtility

### Community 31 - "Community 31"
Cohesion: 0.29
Nodes (8): CancellationToken, FileStream, Func, IEnumerable, int, Task, TimeSpan, AtomicFileUtility

### Community 32 - "Community 32"
Cohesion: 0.12
Nodes (5): PlayerAssistant, FileDownloadCounters, LocalIndexSearchOutcome, LoginInfoDisplayMode, RandomNumberUtility

### Community 34 - "Community 34"
Cohesion: 0.23
Nodes (15): Assert-Payload(), Assert-ProtectedEncryptedSidecars(), Assert-RequiredDirectory(), Assert-RequiredFile(), ConvertTo-ProcessArgument(), Copy-PayloadToStaging(), Grant-AppDirectoryAccess(), Install-PlayerAssistant() (+7 more)

### Community 35 - "Community 35"
Cohesion: 0.15
Nodes (12): DateTimeOffset, Exception, int, JsonSerializerOptions, List, object, string, TimeSpan (+4 more)

### Community 36 - "Community 36"
Cohesion: 0.19
Nodes (10): BackgroundTaskHandle, bool, CancellationToken, CancellationTokenSource, Dictionary, Func, object, Task (+2 more)

### Community 37 - "Community 37"
Cohesion: 0.21
Nodes (10): Match, HashSet, IEnumerable, IReadOnlyDictionary, Regex, string, PostingCategory, PostTotalsRow (+2 more)

### Community 38 - "Community 38"
Cohesion: 0.28
Nodes (7): CancellationToken, Exception, FileStream, JsonSerializerOptions, string, Task, RuntimeArtifactUtility

### Community 39 - "Community 39"
Cohesion: 0.23
Nodes (11): Assert-AuthenticodeSignatureMatchesPolicy(), Assert-RequiredFile(), ConvertFrom-EncryptedSettingsFile(), ConvertFrom-SettingsFile(), ConvertTo-PlainSettingsObject(), Get-SettingsDerivationScope(), Get-Sha256Bytes(), Protect-RuntimeSidecarFiles() (+3 more)

### Community 40 - "Community 40"
Cohesion: 0.18
Nodes (7): PlayerAssistant.Launcher, IEnumerable, STAThread, string, Program, RegistryHive, RegistryView

### Community 41 - "Community 41"
Cohesion: 0.15
Nodes (3): IEnumerable, JsonElement, LocalIndexSearchOutcome

### Community 42 - "Community 42"
Cohesion: 0.30
Nodes (6): CancellationToken, HttpClient, Image, Task, Uri, ImageDownloadUtility

### Community 44 - "Community 44"
Cohesion: 0.19
Nodes (4): Image, Stream, Icon, ImageLayout

### Community 45 - "Community 45"
Cohesion: 0.18
Nodes (10): Exception, int, JsonSerializerOptions, string, LastCrashDiagnostic, LastCrashDiagnosticUtility, LastCrashEnvironment, LastCrashException (+2 more)

### Community 46 - "Community 46"
Cohesion: 0.22
Nodes (6): Exception, Func, IEnumerable, int, string, RuntimeBackupUtility

### Community 48 - "Community 48"
Cohesion: 0.27
Nodes (6): Action, Exception, Func, string, Task, StartupLoggingUtility

### Community 49 - "Community 49"
Cohesion: 0.15
Nodes (11): Button, ListBox, Panel, Form1, IContainer, Label, MenuStrip, RadioButton (+3 more)

### Community 50 - "Community 50"
Cohesion: 0.18
Nodes (9): HttpContent, ChunkedHttpContent, byte, CancellationToken, HttpRequestMessage, HttpResponseMessage, Stream, Task (+1 more)

### Community 51 - "Community 51"
Cohesion: 0.19
Nodes (5): Add-CredentialReaderType(), ConvertTo-PortableEncryptedSettingsJson(), New-SignedHostedSettingsJson(), Read-CredentialSecret(), Write-FramedString()

### Community 52 - "Community 52"
Cohesion: 0.17
Nodes (3): Downloaded, ErrorMessage, IReadOnlyCollection

### Community 53 - "Community 53"
Cohesion: 0.21
Nodes (8): ReleaseIntegrityManifest, ReleaseIntegrityManifestFile, IReadOnlyList, List, string, ReleaseIntegrityManifest, ReleaseIntegrityManifestFile, ReleaseIntegrityManifestUtility

### Community 54 - "Community 54"
Cohesion: 0.21
Nodes (6): Assert-PathInsideRepo(), Get-FileManifestEntry(), Get-ReleaseManifest(), Resolve-FullPath(), Test-StartupHealthHasRequiredPhases(), Wait-ForStartupHealth()

### Community 56 - "Community 56"
Cohesion: 0.20
Nodes (7): HttpMessageHandler, Dictionary, Func, InMemoryWindowsCredentialStoreBackend, ObservedWindowsCredentialStoreBackend, RequestObservation, ScriptedHttpMessageHandler

### Community 57 - "Community 57"
Cohesion: 0.47
Nodes (4): GeneratedRegex, Regex, string, SensitiveTextRedactionUtility

### Community 58 - "Community 58"
Cohesion: 0.25
Nodes (8): Assert-PathInsideRepo(), Assert-RegeneratedArtifacts(), Assert-RequiredFile(), Backup-AndRemove(), Copy-IfExists(), Resolve-FullPath(), Test-StartupHealthHasRequiredPhases(), Wait-ForStartupHealth()

### Community 59 - "Community 59"
Cohesion: 0.36
Nodes (10): Add-Finding(), Assert-PathInsideRepo(), ConvertTo-ProcessArguments(), Invoke-Git(), Resolve-FullPath(), Test-ForbiddenTrackedPaths(), Test-HistoryContent(), Test-IsAllowedFixtureMatch() (+2 more)

### Community 60 - "Community 60"
Cohesion: 0.22
Nodes (5): Action, Func, HttpClient, IDisposable, DelegateDisposable

### Community 61 - "Community 61"
Cohesion: 0.22
Nodes (9): net10.0, PlayerAssistant.Launcher, net10.0-windows, Microsoft.NET.Sdk, PlayerAssistant.Tests, net10.0-windows, Microsoft.NET.Sdk, to-orcish (+1 more)

### Community 62 - "Community 62"
Cohesion: 0.31
Nodes (4): HashSet, IEnumerable, string, KeywordTermsFileUtility

### Community 63 - "Community 63"
Cohesion: 0.20
Nodes (6): int, object, string, LoopbackHttpServer, RequestObservation, TcpListener

### Community 65 - "Community 65"
Cohesion: 0.28
Nodes (3): Assert-RequiredFile(), Get-SigningKey(), New-SigningKeyPair()

### Community 66 - "Community 66"
Cohesion: 0.22
Nodes (8): Additional hardening backlog, Follow-up hardening backlog, Future hardening implementation tasks, Hardening completed in this session, New backlog, Next hardening tasks, Post-backlog hardening candidates, Remaining hardening backlog

### Community 67 - "Community 67"
Cohesion: 0.36
Nodes (5): Action, Exception, Task, UiOperationFailure, UiOperationFailureReporter

### Community 68 - "Community 68"
Cohesion: 0.31
Nodes (4): JsonSerializerOptions, string, UserPreferences, UserPreferencesUtility

### Community 69 - "Community 69"
Cohesion: 0.36
Nodes (7): Assert-ExecutableVersionsMatch(), Assert-ParityFilesMatch(), Assert-PathInsideRepo(), Assert-RequiredFile(), Get-ExecutableVersionSummary(), Get-FileHashText(), Resolve-FullPath()

### Community 70 - "Community 70"
Cohesion: 0.31
Nodes (4): Assert-AuthenticodeSignatureMatchesPolicy(), Assert-ManifestSignature(), Assert-RequiredFile(), Test-CodeSigningPolicyConfigured()

### Community 71 - "Community 71"
Cohesion: 0.28
Nodes (3): Assert-EncryptedSidecar(), Assert-InstallerProtectsSidecars(), Assert-RequiredFile()

### Community 72 - "Community 72"
Cohesion: 0.39
Nodes (3): ProcessStartInfo, ExternalUrlLaunchUtility, ExternalUrlLaunchValidation

### Community 73 - "Community 73"
Cohesion: 0.29
Nodes (4): Keys, KeyEventArgs, SearchTextBox, TextBox

### Community 74 - "Community 74"
Cohesion: 0.62
Nodes (6): Assert-AuthenticodeSignatureMatchesPolicy(), Assert-EncryptedEnvelope(), Assert-RequiredDirectory(), Assert-RequiredFile(), Test-CodeSigningPolicyConfigured(), Test-InstallerDirectory()

### Community 75 - "Community 75"
Cohesion: 0.38
Nodes (3): IReadOnlyDictionary, string, XpPasswordStoreUtility

### Community 76 - "Community 76"
Cohesion: 0.53
Nodes (5): Assert-PathInsideRepo(), Get-ItemSizeBytes(), Remove-RetentionItem(), Remove-StaleItems(), Resolve-FullPath()

### Community 78 - "Community 78"
Cohesion: 0.40
Nodes (5): Microsoft.Playwright (1.60.0), net10.0-windows, SkiaSharp (3.119.4), player-assistant, Microsoft.NET.Sdk

### Community 79 - "Community 79"
Cohesion: 0.40
Nodes (4): Meta Commands, RTK - Rust Token Killer (Codex CLI), Rule, Verification

### Community 80 - "Community 80"
Cohesion: 0.50
Nodes (3): graphify, Project Constraints, Repository instructions

### Community 81 - "Community 81"
Cohesion: 0.67
Nodes (3): ApplicationSettingsBase, PlayerAssistant.Properties, Settings

### Community 82 - "Community 82"
Cohesion: 0.50
Nodes (4): DungeonMaster, net10.0-windows, SkiaSharp (3.119.4), Microsoft.NET.Sdk

### Community 83 - "Community 83"
Cohesion: 0.50
Nodes (3): Action, IDisposable, DelegateDisposable

## Knowledge Gaps
- **62 isolated node(s):** `AppConfigurationIssueSeverity`, `net10.0-windows`, `SkiaSharp (3.119.4)`, `Microsoft.NET.Sdk`, `LoginInfoDisplayMode` (+57 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **7 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `PlayerAssistant` connect `Community 32` to `Community 0`, `Community 1`, `Community 2`, `Community 4`, `Community 6`, `Community 8`, `Community 9`, `Community 10`, `Community 11`, `Community 13`, `Community 14`, `Community 16`, `Community 17`, `Community 19`, `Community 20`, `Community 22`, `Community 23`, `Community 24`, `Community 25`, `Community 28`, `Community 29`, `Community 30`, `Community 35`, `Community 36`, `Community 37`, `Community 45`, `Community 46`, `Community 47`, `Community 48`, `Community 53`, `Community 56`, `Community 60`, `Community 62`, `Community 67`, `Community 68`, `Community 72`, `Community 73`, `Community 75`?**
  _High betweenness centrality (0.566) - this node is a cross-community bridge._
- **Why does `Form1` connect `Community 5` to `Community 32`, `Community 33`, `Community 1`, `Community 36`, `Community 37`, `Community 41`, `Community 43`, `Community 44`, `Community 12`, `Community 15`, `Community 18`, `Community 52`, `Community 55`?**
  _High betweenness centrality (0.207) - this node is a cross-community bridge._
- **What connects `AppConfigurationIssueSeverity`, `net10.0-windows`, `SkiaSharp (3.119.4)` to the rest of the system?**
  _62 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.05209397344228805 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.06946386946386947 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.08365384615384615 - nodes in this community are weakly interconnected._
- **Should `Community 3` be split into smaller, more focused modules?**
  _Cohesion score 0.08581349206349206 - nodes in this community are weakly interconnected._