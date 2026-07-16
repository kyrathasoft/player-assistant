# Graph Report - .  (2026-07-16)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 2182 nodes · 4605 edges · 103 communities (93 shown, 10 thin omitted)
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS · INFERRED: 5 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `602aba06`
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
- [[_COMMUNITY_Community 85|Community 85]]
- [[_COMMUNITY_Community 86|Community 86]]
- [[_COMMUNITY_Community 87|Community 87]]
- [[_COMMUNITY_Community 88|Community 88]]
- [[_COMMUNITY_Community 89|Community 89]]
- [[_COMMUNITY_Community 90|Community 90]]
- [[_COMMUNITY_Community 91|Community 91]]
- [[_COMMUNITY_Community 92|Community 92]]
- [[_COMMUNITY_Community 93|Community 93]]
- [[_COMMUNITY_Community 94|Community 94]]
- [[_COMMUNITY_Community 95|Community 95]]
- [[_COMMUNITY_Community 96|Community 96]]
- [[_COMMUNITY_Community 97|Community 97]]
- [[_COMMUNITY_Community 98|Community 98]]

## God Nodes (most connected - your core abstractions)
1. `Form1` - 278 edges
2. `RpolAuthUtility` - 63 edges
3. `TaggedNoteCipherUtility` - 49 edges
4. `OrcishTranslatorUtility` - 39 edges
5. `GameForumUtility` - 36 edges
6. `LocalSettingsUtility` - 35 edges
7. `PlayerAssistantUpdateUtility` - 33 edges
8. `PlayerCharacterAssetUtility` - 32 edges
9. `AdventureOutlineUtility` - 31 edges
10. `Task` - 31 edges

## Surprising Connections (you probably didn't know these)
- `AppConfigurationValidationException` --inherits--> `InvalidOperationException`  [EXTRACTED]
  AppConfigurationValidationUtility.cs → SourceIntegrityUtility.cs
- `NetworkRequestException` --inherits--> `InvalidOperationException`  [EXTRACTED]
  NetworkRequestUtility.cs → SourceIntegrityUtility.cs
- `ChunkedHttpContent` --inherits--> `HttpContent`  [EXTRACTED]
  PlayerAssistant.Tests/Program.cs → NetworkRequestUtility.cs
- `ScriptedHttpMessageHandler` --inherits--> `HttpMessageHandler`  [EXTRACTED]
  PlayerAssistant.Tests/Program.cs → NetworkRequestUtility.cs
- `RpolAuthException` --inherits--> `InvalidOperationException`  [EXTRACTED]
  RpolAuthUtility.cs → SourceIntegrityUtility.cs

## Import Cycles
- None detected.

## Communities (103 total, 10 thin omitted)

### Community 0 - "Community 0"
Cohesion: 0.04
Nodes (28): DateTime, DialogResult, float, Action, BackgroundTaskSupervisor, bool, CancellationTokenSource, DateTimeOffset (+20 more)

### Community 1 - "Community 1"
Cohesion: 0.07
Nodes (33): BrowserNewContextOptions, BrowserTypeLaunchOptions, IAsyncDisposable, IBrowser, IBrowserContext, IPage, IPlaywright, IResponse (+25 more)

### Community 2 - "Community 2"
Cohesion: 0.05
Nodes (31): ClosingTags, Content, EncryptedBlockReportStatus, EndIndex, HeroAccessContext, OpeningTags, Tag, TagExpression (+23 more)

### Community 3 - "Community 3"
Cohesion: 0.09
Nodes (60): Assert-AuthenticodeSignatureMatchesPolicy(), Assert-EncryptedLocalSettings(), Assert-NoForbiddenPublishArtifacts(), Assert-NoPlaintextCredentialMarkers(), Assert-NoSensitiveFiles(), Assert-PathInsideRepo(), Assert-PublishedExecutableVersion(), Assert-PublishedKeywordIndex() (+52 more)

### Community 4 - "Community 4"
Cohesion: 0.08
Nodes (29): ConcurrentBag, CrawledPageContent, IProgress, CancellationToken, Dictionary, HashSet, int, IReadOnlyList (+21 more)

### Community 5 - "Community 5"
Cohesion: 0.10
Nodes (20): CancellationToken, DateTimeOffset, GeneratedRegex, HttpClient, IEnumerable, int, IReadOnlyList, PlayerAssistantUpdateInfo (+12 more)

### Community 6 - "Community 6"
Cohesion: 0.10
Nodes (14): OrcishAffixEntry, OrcishLanguage, OrcishLexiconEntry, OrcishSequenceTranslation, OrcishTranslationCandidate, OrcishTranslationRequest, Func, IDictionary (+6 more)

### Community 7 - "Community 7"
Cohesion: 0.10
Nodes (46): Add-RcDryRunStep(), Assert-AuthenticodeSignatureMatchesPolicy(), Assert-CodeSigningPolicyConfigured(), Assert-DependencyFreshness(), Assert-ExecutableVersion(), Assert-ExpectedChangedPaths(), Assert-NoVulnerablePackages(), Assert-PathInsideRepo() (+38 more)

### Community 8 - "Community 8"
Cohesion: 0.08
Nodes (26): AuthenticodeSignatureUtility, AuthenticodeSignatureInfo, AuthenticodeSignaturePolicy, Func, PlayerAssistant, InstallerLaunchElevationContext, AuthenticodeSignatureInfo, AuthenticodeSignaturePolicy (+18 more)

### Community 9 - "Community 9"
Cohesion: 0.12
Nodes (16): CancellationToken, DateTimeOffset, DieRollEntry, GameForumChapterDownload, GameForumPostDownload, Hyperlink, IEnumerable, List (+8 more)

### Community 10 - "Community 10"
Cohesion: 0.11
Nodes (23): Encoding, HttpCompletionOption, CancellationToken, DateTimeOffset, Dictionary, Func, HttpClient, HttpContent (+15 more)

### Community 11 - "Community 11"
Cohesion: 0.11
Nodes (14): EncryptedSettingsEnvelope, KeyScope, KeySet, byte, Dictionary, Exception, int, IReadOnlyDictionary (+6 more)

### Community 12 - "Community 12"
Cohesion: 0.07
Nodes (14): AdventureOutlineLineStyle, IReadOnlyList, MyHeroBriefing, MyHeroBriefingActivityItem, MyHeroBriefingHeroCard, MyHeroBriefingQuickLink, MyHeroBriefingResponseItem, MyHeroBriefingUnlockedNoteItem (+6 more)

### Community 13 - "Community 13"
Cohesion: 0.09
Nodes (19): AppSettingsUtility, Action, bool, Dictionary, Exception, Func, HttpClient, IDictionary (+11 more)

### Community 14 - "Community 14"
Cohesion: 0.11
Nodes (14): RpolThreadPost, CancellationToken, Dictionary, Func, IDictionary, IEnumerable, IReadOnlyDictionary, JsonSerializerOptions (+6 more)

### Community 15 - "Community 15"
Cohesion: 0.12
Nodes (7): CancellationToken, Exception, GameForumChapterDownload, GameForumPostDownload, Hyperlink, RpolWebViewVerificationRequest, Task

### Community 16 - "Community 16"
Cohesion: 0.12
Nodes (18): MyHeroBriefingHeroIdentitySource, MyHeroBriefingHeroSummary, MyHeroBriefingRequest, MyHeroBriefingResolvedHero, EncryptedTextIndexEntry, IEnumerable, IReadOnlyList, MyHeroBriefing (+10 more)

### Community 17 - "Community 17"
Cohesion: 0.10
Nodes (10): CancellationToken, Dictionary, HttpClient, PlayerCharacterHeroRow, Regex, string, Task, TimeSpan (+2 more)

### Community 18 - "Community 18"
Cohesion: 0.12
Nodes (10): AdventureOutlineUtility, CancellationToken, Func, int, IReadOnlyList, Regex, string, Task (+2 more)

### Community 19 - "Community 19"
Cohesion: 0.10
Nodes (15): CREDENTIAL, DllImport, IntPtr, Win32FileTime, BackendScope, bool, DateTimeOffset, IDisposable (+7 more)

### Community 20 - "Community 20"
Cohesion: 0.08
Nodes (20): CoreWebView2Cookie, CoreWebView2CookieSameSiteKind, Form, Keys, PlaywrightCookie, TextBox, PlayerAssistant, RpolCredentialsDialog (+12 more)

### Community 22 - "Community 22"
Cohesion: 0.11
Nodes (5): Image, Stream, FormClosedEventArgs, Icon, ImageLayout

### Community 23 - "Community 23"
Cohesion: 0.13
Nodes (15): Allowed(), Action, Func, GeneratedRegex, IDisposable, NetworkUrlAllowlistValidation, NetworkUrlPurpose, Regex (+7 more)

### Community 24 - "Community 24"
Cohesion: 0.15
Nodes (9): CharacterClass, Level, IReadOnlyList, PartyHeroSheet, PcXpTotal, PlayerCharacterHeroRow, Regex, PartyHeroUtility (+1 more)

### Community 25 - "Community 25"
Cohesion: 0.12
Nodes (10): DestinationPath, Func, int, IReadOnlyList, STAThread, string, PlayerAssistant, Program (+2 more)

### Community 26 - "Community 26"
Cohesion: 0.13
Nodes (11): Brush, Color, Font, FontFamily, Graphics, GraphicsPath, PaintEventArgs, Pen (+3 more)

### Community 27 - "Community 27"
Cohesion: 0.15
Nodes (11): CancellationToken, Dictionary, HashSet, HttpClient, HttpResponseMessage, NetworkResponseContentLimit, Regex, string (+3 more)

### Community 28 - "Community 28"
Cohesion: 0.16
Nodes (12): NetworkResponseTooLargeException, SourceIntegrityRecord, SourceIntegrityShape, CancellationToken, Dictionary, int, InvalidOperationException, JsonSerializerOptions (+4 more)

### Community 29 - "Community 29"
Cohesion: 0.19
Nodes (12): RuntimeHousekeepingOptions, RuntimeHousekeepingReport, DateTimeOffset, Exception, IEnumerable, int, string, TimeSpan (+4 more)

### Community 30 - "Community 30"
Cohesion: 0.14
Nodes (16): EndpointDiagnosticEntry, EndpointSummary, NetworkFailureKind, DateTimeOffset, Dictionary, HttpRequestMessage, HttpStatusCode, int (+8 more)

### Community 31 - "Community 31"
Cohesion: 0.16
Nodes (9): XpTrackingSnapshot, CancellationToken, Exception, IReadOnlyList, PcXpTotal, string, Task, PlayerAssistant (+1 more)

### Community 32 - "Community 32"
Cohesion: 0.21
Nodes (10): AppConfigurationIssue, AppConfigurationValidationReport, AppConfigurationValidationException, AppConfigurationValidationUtility, IReadOnlyDictionary, List, NetworkUrlPurpose, string (+2 more)

### Community 33 - "Community 33"
Cohesion: 0.19
Nodes (9): CancellationToken, HashSet, HttpClient, Hyperlink, Regex, Task, Uri, HtmlUtility (+1 more)

### Community 34 - "Community 34"
Cohesion: 0.23
Nodes (20): Assert-CommandFailsWith(), Assert-CommandPasses(), Assert-FileContains(), Assert-PathInsideRepo(), Assert-RcDryRunSummary(), ConvertTo-ProcessArguments(), Get-PowerShellExecutable(), Invoke-DependencyFreshnessSelfTest() (+12 more)

### Community 35 - "Community 35"
Cohesion: 0.18
Nodes (12): CertificatePinningPolicy, CertificatePinningUtility, DateTimeOffset, HttpRequestMessage, IReadOnlyCollection, Uri, PlayerAssistant, CertificatePinTrustEntry (+4 more)

### Community 36 - "Community 36"
Cohesion: 0.24
Nodes (9): AtomicFileUtility, CancellationToken, FileStream, Func, IEnumerable, int, Task, TimeSpan (+1 more)

### Community 37 - "Community 37"
Cohesion: 0.21
Nodes (18): Assert-DiagnosticStagingIsRedacted(), Assert-DiagnosticZipIsRedacted(), Assert-PathInsideRepo(), ConvertTo-PlainObject(), Get-ExecutableVersionSummary(), Get-FileSummary(), Get-PowerShellExecutable(), Get-Sha256HashText() (+10 more)

### Community 38 - "Community 38"
Cohesion: 0.18
Nodes (9): KeywordUrls, NodeCount, SitemapIndexResult, CancellationToken, Dictionary, HttpClient, Task, SitemapUtility (+1 more)

### Community 39 - "Community 39"
Cohesion: 0.22
Nodes (8): ObsidianPublishSiteInfo, CancellationToken, Dictionary, HttpClient, Regex, Task, ObsidianPublishUtility, PlayerAssistant

### Community 40 - "Community 40"
Cohesion: 0.23
Nodes (9): CancellationToken, Exception, FileStream, JsonSerializerOptions, string, T, Task, PlayerAssistant (+1 more)

### Community 41 - "Community 41"
Cohesion: 0.13
Nodes (6): DateTimeOffset, IDictionary, IDisposable, IWindowsCredentialStoreBackend, string, RuntimeSecretStoreUtility

### Community 42 - "Community 42"
Cohesion: 0.24
Nodes (10): HostedSettingsSigningKeyTrustEntry, DateTimeOffset, Dictionary, IDisposable, int, IReadOnlyList, JsonSerializerOptions, string (+2 more)

### Community 43 - "Community 43"
Cohesion: 0.25
Nodes (7): CancellationToken, HttpClient, Image, Task, Uri, ImageDownloadUtility, PlayerAssistant

### Community 44 - "Community 44"
Cohesion: 0.23
Nodes (15): Assert-Payload(), Assert-ProtectedEncryptedSidecars(), Assert-RequiredDirectory(), Assert-RequiredFile(), ConvertTo-ProcessArgument(), Copy-PayloadToStaging(), Grant-AppDirectoryAccess(), Install-PlayerAssistant() (+7 more)

### Community 45 - "Community 45"
Cohesion: 0.18
Nodes (11): Match, PostingCategory, HashSet, IEnumerable, IReadOnlyDictionary, PostTotalsSummary, Regex, string (+3 more)

### Community 46 - "Community 46"
Cohesion: 0.12
Nodes (13): Button, Label, ListBox, Panel, SearchTextBox, Form1, PlayerAssistant, IContainer (+5 more)

### Community 47 - "Community 47"
Cohesion: 0.17
Nodes (8): IWindowsCredentialStoreBackend, Dictionary, Func, StoredSecret, InMemoryWindowsCredentialStoreBackend, ObservedWindowsCredentialStoreBackend, ScriptedHttpMessageHandler, ThrowingWindowsCredentialStoreBackend

### Community 48 - "Community 48"
Cohesion: 0.16
Nodes (12): StartupHealthException, DateTimeOffset, Exception, int, JsonSerializerOptions, List, object, string (+4 more)

### Community 49 - "Community 49"
Cohesion: 0.23
Nodes (11): Assert-AuthenticodeSignatureMatchesPolicy(), Assert-RequiredFile(), ConvertFrom-EncryptedSettingsFile(), ConvertFrom-SettingsFile(), ConvertTo-PlainSettingsObject(), Get-SettingsDerivationScope(), Get-Sha256Bytes(), Protect-RuntimeSidecarFiles() (+3 more)

### Community 50 - "Community 50"
Cohesion: 0.16
Nodes (5): IDisposable, PlayerAssistant, StatusActivityScope, ToString(), JsonObject

### Community 51 - "Community 51"
Cohesion: 0.18
Nodes (7): IEnumerable, STAThread, string, PlayerAssistant.Launcher, Program, RegistryHive, RegistryView

### Community 52 - "Community 52"
Cohesion: 0.20
Nodes (7): Exception, Func, IEnumerable, int, string, PlayerAssistant, RuntimeBackupUtility

### Community 53 - "Community 53"
Cohesion: 0.22
Nodes (3): string, PlayerAssistant, RuntimePathUtility

### Community 54 - "Community 54"
Cohesion: 0.25
Nodes (7): Action, Exception, Func, string, Task, PlayerAssistant, StartupLoggingUtility

### Community 55 - "Community 55"
Cohesion: 0.14
Nodes (7): PlayerAssistant, PlayerAssistant, PlayerAssistant, PlayerAssistant, PlayerAssistant, PlayerAssistant, Text

### Community 56 - "Community 56"
Cohesion: 0.36
Nodes (5): GeneratedRegex, Regex, string, PlayerAssistant, SensitiveTextRedactionUtility

### Community 57 - "Community 57"
Cohesion: 0.19
Nodes (5): Add-CredentialReaderType(), ConvertTo-PortableEncryptedSettingsJson(), New-SignedHostedSettingsJson(), Read-CredentialSecret(), Write-FramedString()

### Community 59 - "Community 59"
Cohesion: 0.21
Nodes (10): Create(), Exception, int, JsonSerializerOptions, string, From(), LastCrashDiagnosticUtility, PlayerAssistant (+2 more)

### Community 60 - "Community 60"
Cohesion: 0.20
Nodes (8): ChunkedHttpContent, byte, CancellationToken, HttpRequestMessage, HttpResponseMessage, Stream, Task, TransportContext

### Community 61 - "Community 61"
Cohesion: 0.21
Nodes (6): Assert-PathInsideRepo(), Get-FileManifestEntry(), Get-ReleaseManifest(), Resolve-FullPath(), Test-StartupHealthHasRequiredPhases(), Wait-ForStartupHealth()

### Community 62 - "Community 62"
Cohesion: 0.20
Nodes (7): BackgroundTaskHandle, BackgroundTaskSupervisor, bool, CancellationTokenSource, Dictionary, object, PlayerAssistant

### Community 63 - "Community 63"
Cohesion: 0.24
Nodes (5): Control, IEnumerable, List, MyHeroBriefingThreadPosts, RpolThreadSplitResult

### Community 64 - "Community 64"
Cohesion: 0.20
Nodes (5): Downloaded, ErrorMessage, DieRollEntry, IReadOnlyCollection, TDownload

### Community 66 - "Community 66"
Cohesion: 0.25
Nodes (6): UiOperationFailure, Action, Exception, Task, PlayerAssistant, UiOperationFailureReporter

### Community 67 - "Community 67"
Cohesion: 0.27
Nodes (5): HashSet, IEnumerable, string, KeywordTermsFileUtility, PlayerAssistant

### Community 68 - "Community 68"
Cohesion: 0.24
Nodes (7): ReleaseIntegrityManifest, ReleaseIntegrityManifestFile, IReadOnlyList, List, string, PlayerAssistant, ReleaseIntegrityManifestUtility

### Community 69 - "Community 69"
Cohesion: 0.18
Nodes (10): Additional hardening backlog, Follow-up hardening backlog, Fresh backlog, Future hardening implementation tasks, Hardening completed in this session, New backlog, Next hardening tasks, Player-facing features completed in this session (+2 more)

### Community 70 - "Community 70"
Cohesion: 0.25
Nodes (8): Assert-PathInsideRepo(), Assert-RegeneratedArtifacts(), Assert-RequiredFile(), Backup-AndRemove(), Copy-IfExists(), Resolve-FullPath(), Test-StartupHealthHasRequiredPhases(), Wait-ForStartupHealth()

### Community 71 - "Community 71"
Cohesion: 0.36
Nodes (10): Add-Finding(), Assert-PathInsideRepo(), ConvertTo-ProcessArguments(), Invoke-Git(), Resolve-FullPath(), Test-ForbiddenTrackedPaths(), Test-HistoryContent(), Test-IsAllowedFixtureMatch() (+2 more)

### Community 72 - "Community 72"
Cohesion: 0.25
Nodes (5): IEnumerable, IReadOnlyDictionary, string, PlayerAssistant, XpPasswordStoreUtility

### Community 73 - "Community 73"
Cohesion: 0.22
Nodes (9): net10.0, PlayerAssistant.Launcher, net10.0-windows, Microsoft.NET.Sdk, PlayerAssistant.Tests, net10.0-windows, Microsoft.NET.Sdk, to-orcish (+1 more)

### Community 74 - "Community 74"
Cohesion: 0.20
Nodes (6): int, object, string, LoopbackHttpServer, RequestObservation, TcpListener

### Community 76 - "Community 76"
Cohesion: 0.28
Nodes (3): Assert-RequiredFile(), Get-SigningKey(), New-SigningKeyPair()

### Community 77 - "Community 77"
Cohesion: 0.36
Nodes (6): Allowed(), ProcessStartInfo, ExternalUrlLaunchUtility, PlayerAssistant, Rejected(), ExternalUrlLaunchValidation

### Community 78 - "Community 78"
Cohesion: 0.31
Nodes (4): JsonSerializerOptions, string, PlayerAssistant, UserPreferencesUtility

### Community 79 - "Community 79"
Cohesion: 0.36
Nodes (7): Assert-ExecutableVersionsMatch(), Assert-ParityFilesMatch(), Assert-PathInsideRepo(), Assert-RequiredFile(), Get-ExecutableVersionSummary(), Get-FileHashText(), Resolve-FullPath()

### Community 80 - "Community 80"
Cohesion: 0.31
Nodes (4): Assert-AuthenticodeSignatureMatchesPolicy(), Assert-ManifestSignature(), Assert-RequiredFile(), Test-CodeSigningPolicyConfigured()

### Community 81 - "Community 81"
Cohesion: 0.28
Nodes (3): Assert-EncryptedSidecar(), Assert-InstallerProtectsSidecars(), Assert-RequiredFile()

### Community 82 - "Community 82"
Cohesion: 0.25
Nodes (7): Active Leads and Open Threads, Adventure Outline, Ch 1 - Kirkilston, Ch 2 - Supper With Nuanda, Ch 3 - Joining the Caravan to Raven's Pass, Ch 4 - Battle at Blightstone Pit, Ch 5 - A Betentacled Escape

### Community 83 - "Community 83"
Cohesion: 0.62
Nodes (6): Assert-AuthenticodeSignatureMatchesPolicy(), Assert-EncryptedEnvelope(), Assert-RequiredDirectory(), Assert-RequiredFile(), Test-CodeSigningPolicyConfigured(), Test-InstallerDirectory()

### Community 84 - "Community 84"
Cohesion: 0.53
Nodes (4): BackgroundTaskHandle, CancellationToken, Func, Task

### Community 85 - "Community 85"
Cohesion: 0.33
Nodes (3): BinaryWriter, IReadOnlyDictionary, RSA

### Community 86 - "Community 86"
Cohesion: 0.53
Nodes (5): Assert-PathInsideRepo(), Get-ItemSizeBytes(), Remove-RetentionItem(), Remove-StaleItems(), Resolve-FullPath()

### Community 88 - "Community 88"
Cohesion: 0.33
Nodes (6): Microsoft.Playwright (1.60.0), Microsoft.Web.WebView2 (1.0.4022.49), SkiaSharp (3.119.4), net10.0-windows, player-assistant, Microsoft.NET.Sdk

### Community 89 - "Community 89"
Cohesion: 0.40
Nodes (4): Generated artifact update policy, graphify, Project Constraints, Repository instructions

### Community 91 - "Community 91"
Cohesion: 0.40
Nodes (3): Action, DelegateDisposable, PlayerAssistant

### Community 93 - "Community 93"
Cohesion: 0.40
Nodes (4): Meta Commands, RTK - Rust Token Killer (Codex CLI), Rule, Verification

### Community 94 - "Community 94"
Cohesion: 0.67
Nodes (3): ApplicationSettingsBase, PlayerAssistant.Properties, Settings

### Community 95 - "Community 95"
Cohesion: 0.50
Nodes (3): Conclusion, Findings, Source Review Findings

## Knowledge Gaps
- **400 isolated node(s):** `PlayerAssistant`, `string`, `Regex`, `int`, `ExistingChapterSection` (+395 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **10 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Text` connect `Community 55` to `Community 1`, `Community 2`, `Community 5`, `Community 72`, `Community 10`, `Community 12`, `Community 50`, `Community 19`, `Community 91`?**
  _High betweenness centrality (0.144) - this node is a cross-community bridge._
- **Why does `Form1` connect `Community 0` to `Community 64`, `Community 65`, `Community 66`, `Community 26`, `Community 12`, `Community 15`, `Community 50`, `Community 20`, `Community 21`, `Community 22`, `Community 58`, `Community 63`?**
  _High betweenness centrality (0.112) - this node is a cross-community bridge._
- **What connects `PlayerAssistant`, `string`, `Regex` to the rest of the system?**
  _400 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.036954087346024636 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.06541822721598002 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.05364314400458979 - nodes in this community are weakly interconnected._
- **Should `Community 3` be split into smaller, more focused modules?**
  _Cohesion score 0.08581349206349206 - nodes in this community are weakly interconnected._