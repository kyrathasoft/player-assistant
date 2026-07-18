using PlayerAssistant;
using Microsoft.Playwright;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Windows.Forms;
using System.Xml.Linq;

var requestedTestFilter = args.Length > 0 ? string.Join(" ", args).Trim() : string.Empty;

var tests = new (string Name, Action Test)[]
{
    ("orcish translator returns one-to-one english mapping", OrcishTranslatorReturnsOneToOneEnglishMapping),
    ("orcish translator returns several english matches for one orcish word", OrcishTranslatorReturnsSeveralEnglishMatchesForOneOrcishWord),
    ("orcish translator uses part of speech to disambiguate", OrcishTranslatorUsesPartOfSpeechToDisambiguate),
    ("orcish translator filters human terms by register", OrcishTranslatorFiltersHumanTermsByRegister),
    ("orcish translator supports reverse lookup for respectful human term", OrcishTranslatorSupportsReverseLookupForRespectfulHumanTerm),
    ("orcish translator generates plural possessives systematically", OrcishTranslatorGeneratesPluralPossessivesSystematically),
    ("orcish translator supports pale adjective registers", OrcishTranslatorSupportsPaleAdjectiveRegisters),
    ("orcish translator prefers vrak for default skin terms", OrcishTranslatorPrefersVrakForDefaultSkinTerms),
    ("orcish translator reserves drukh for reverent monster hide", OrcishTranslatorReservesDrukhForReverentMonsterHide),
    ("orcish translator supports oglur verb family", OrcishTranslatorSupportsOglurVerbFamily),
    ("orcish translator exposes both i pronoun variants", OrcishTranslatorExposesBothIPronounVariants),
    ("orcish translator random i picker returns valid variant", OrcishTranslatorRandomIPickerReturnsValidVariant),
    ("orcish translator replaces emphasized i in english text", OrcishTranslatorReplacesEmphasizedIInEnglishText),
    ("orcish translator exposes both really adverb variants", OrcishTranslatorExposesBothReallyAdverbVariants),
    ("orcish translator random really picker returns valid variant", OrcishTranslatorRandomReallyPickerReturnsValidVariant),
    ("orcish translator exposes both if variants", OrcishTranslatorExposesBothIfVariants),
    ("orcish translator alternates repeated if terms in sequence", OrcishTranslatorAlternatesRepeatedIfTermsInSequence),
    ("orcish translator exposes both but variants", OrcishTranslatorExposesBothButVariants),
    ("orcish translator supports sarcastic but variants", OrcishTranslatorSupportsSarcasticButVariants),
    ("orcish translator random but picker returns valid variant", OrcishTranslatorRandomButPickerReturnsValidVariant),
    ("orcish translator supports Kirkilston refuge phrase vocabulary", OrcishTranslatorSupportsKirkilstonRefugePhraseVocabulary),
    ("orcish translator supports regional map history vocabulary", OrcishTranslatorSupportsRegionalMapHistoryVocabulary),
    ("orcish translator supports Xavamros rulership vocabulary", OrcishTranslatorSupportsXavamrosRulershipVocabulary),
    ("orcish translator supports Prince Xavin youthful adventure vocabulary", OrcishTranslatorSupportsPrinceXavinYouthfulAdventureVocabulary),
    ("orcish translator supports Kirkilston church and Red Laws vocabulary", OrcishTranslatorSupportsKirkilstonChurchAndRedLawsVocabulary),
    ("orcish translator supports Kirkliston economy vocabulary", OrcishTranslatorSupportsKirklistonEconomyVocabulary),
    ("orcish translator supports Kirkliston watch vocabulary", OrcishTranslatorSupportsKirklistonWatchVocabulary),
    ("orcish translator supports Kirkliston daily life vocabulary", OrcishTranslatorSupportsKirklistonDailyLifeVocabulary),
    ("orcish translator supports Kirkliston wilderness opportunity vocabulary", OrcishTranslatorSupportsKirklistonWildernessOpportunityVocabulary),
    ("orcish translator supports Morgan tavern observation vocabulary", OrcishTranslatorSupportsMorganTavernObservationVocabulary),
    ("orcish translator treats dwarf only as Dwarven race", OrcishTranslatorTreatsDwarfOnlyAsDwarvenRace),
    ("orcish translator supports Morgan dining acknowledgement vocabulary", OrcishTranslatorSupportsMorganDiningAcknowledgementVocabulary),
    ("orcish translator supports Kelpie road and inn vocabulary", OrcishTranslatorSupportsKelpieRoadAndInnVocabulary),
    ("orcish translator supports Kelpie fellowship prayer vocabulary", OrcishTranslatorSupportsKelpieFellowshipPrayerVocabulary),
    ("orcish translator supports heraldic stranger equipment vocabulary", OrcishTranslatorSupportsHeraldicStrangerEquipmentVocabulary),
    ("orcish translator supports historical wiki fodder vocabulary", OrcishTranslatorSupportsHistoricalWikiFodderVocabulary),
    ("orcish translator supports twelve page wiki scrape vocabulary", OrcishTranslatorSupportsTwelvePageWikiScrapeVocabulary),
    ("orcish translator supports recovered scroll vocabulary", OrcishTranslatorSupportsRecoveredScrollVocabulary),
    ("orcish translator propagates repaired roots through derived families", OrcishTranslatorPropagatesRepairedRootsThroughDerivedFamilies),
    ("orcish translator audits review-promoted derived families", OrcishTranslatorAuditsReviewPromotedDerivedFamilies),
    ("orcish translator shortens mechanically lengthened forms", OrcishTranslatorShortensMechanicallyLengthenedForms),
    ("orcish translator derives predictable morphology by rule", OrcishTranslatorDerivesPredictableMorphologyByRule),
    ("orcish translator culls low value exonym pass throughs", OrcishTranslatorCullsLowValueExonymPassThroughs),
    ("orcish translator enforces lexicon quality invariants", OrcishTranslatorEnforcesLexiconQualityInvariants),
    ("orcish translator reviews proposed lexicon additions", OrcishTranslatorReviewsProposedLexiconAdditions),
    ("orcish translator supports ten page wiki sample vocabulary", OrcishTranslatorSupportsTenPageWikiSampleVocabulary),
    ("orcish translator supports near kin morphology families", OrcishTranslatorSupportsNearKinMorphologyFamilies),
    ("orcish translator supports fifteen page sample vocabulary", OrcishTranslatorSupportsFifteenPageSampleVocabulary),
    ("orcish translator supports twenty page sample vocabulary", OrcishTranslatorSupportsTwentyPageSampleVocabulary),
    ("orcish translator supports thirty page sample vocabulary", OrcishTranslatorSupportsThirtyPageSampleVocabulary),
    ("orcish translator supports thirty page followup vocabulary", OrcishTranslatorSupportsThirtyPageFollowupVocabulary),
    ("orcish translator supports sixty seven page sample vocabulary", OrcishTranslatorSupportsSixtySevenPageSampleVocabulary),
    ("orcish translator exposes unique english term count", OrcishTranslatorExposesUniqueEnglishTermCount),
    ("to-orcish translates terms before trailing punctuation", ToOrcishTranslatesTermsBeforeTrailingPunctuation),
    ("to-orcish translates dotted abbreviation terms", ToOrcishTranslatesDottedAbbreviationTerms),
    ("to-orcish translates terms inside parentheses", ToOrcishTranslatesTermsInsideParentheses),
    ("to-orcish translates terms inside quotes", ToOrcishTranslatesTermsInsideQuotes),
    ("to-orcish translates words after newlines", ToOrcishTranslatesWordsAfterNewlines),
    ("app configuration validation accepts complete runtime", AppConfigurationValidationAcceptsCompleteRuntime),
    ("settings json accepts current schema version", SettingsJsonAcceptsCurrentSchemaVersion),
    ("settings json rejects future schema version", SettingsJsonRejectsFutureSchemaVersion),
    ("app settings loads hosted encrypted xp tracking url", AppSettingsLoadsHostedEncryptedXpTrackingUrl),
    ("app settings loads hosted encrypted xp tracking url from fixture server", AppSettingsLoadsHostedEncryptedXpTrackingUrlFromFixtureServer),
    ("hosted settings trusted version is encrypted at rest", HostedSettingsTrustedVersionIsEncryptedAtRest),
    ("hosted settings trusted version rejects tampered payload", HostedSettingsTrustedVersionRejectsTamperedPayload),
    ("hosted settings rejects rollback below trusted version floor", HostedSettingsRejectsRollbackBelowTrustedVersionFloor),
    ("hosted settings rejects unexpected signed content identity", HostedSettingsRejectsUnexpectedSignedContentIdentity),
    ("app settings hosted settings failure logs tampered envelope", AppSettingsHostedSettingsFailureLogsTamperedEnvelope),
    ("app settings hosted settings failure logs plaintext payload", AppSettingsHostedSettingsFailureLogsPlaintextPayload),
    ("app settings hosted settings failure logs oversized payload", AppSettingsHostedSettingsFailureLogsOversizedPayload),
    ("app settings hosted settings failure logs unreachable fixture server", AppSettingsHostedSettingsFailureLogsUnreachableFixtureServer),
    ("xp password store loads salted hash sidecar", XpPasswordStoreLoadsSaltedHashSidecar),
    ("xp password store uses unique salts and omits plaintext", XpPasswordStoreUsesUniqueSaltsAndOmitsPlaintext),
    ("xp password store accepts first and full character names", XpPasswordStoreAcceptsFirstAndFullCharacterNames),
    ("xp password store accepts hash sidecar with utf8 bom", XpPasswordStoreAcceptsHashSidecarWithUtf8Bom),
    ("xp password store rejects legacy encrypted sidecar", XpPasswordStoreRejectsLegacyEncryptedSidecar),
    ("xp password store migrates encrypted sidecar", XpPasswordStoreMigratesEncryptedSidecar),
    ("xp password store reports missing sidecar by name", XpPasswordStoreReportsMissingSidecarByName),
    ("app configuration validation reports missing url", AppConfigurationValidationReportsMissingUrl),
    ("app configuration validation rejects disallowed network host", AppConfigurationValidationRejectsDisallowedNetworkHost),
    ("app configuration validation writes repair guidance", AppConfigurationValidationWritesRepairGuidance),
    ("app configuration validation suppresses missing rpol credentials before hosted settings failure", AppConfigurationValidationSuppressesMissingRpolCredentialsBeforeHostedSettingsFailure),
    ("app configuration validation warns about missing rpol credentials after hosted settings failure", AppConfigurationValidationWarnsAboutMissingRpolCredentialsAfterHostedSettingsFailure),
    ("app configuration validation warns about missing sidecars", AppConfigurationValidationWarnsAboutMissingSidecars),
    ("app settings loads rpol credentials from local settings sidecar", AppSettingsLoadsRpolCredentialsFromLocalSettingsSidecar),
    ("app settings uses local rpol credentials when credential store is unavailable", AppSettingsUsesLocalRpolCredentialsWhenCredentialStoreIsUnavailable),
    ("app settings migrate hosted rpol credentials into credential manager", AppSettingsMigrateHostedRpolCredentialsIntoCredentialManager),
    ("app configuration validation accepts valid release manifest", AppConfigurationValidationAcceptsValidReleaseManifest),
    ("app configuration validation rejects missing manifest file", AppConfigurationValidationRejectsMissingManifestFile),
    ("app configuration validation rejects manifest hash mismatch", AppConfigurationValidationRejectsManifestHashMismatch),
    ("health argument surfaces release manifest issue", HealthArgumentSurfacesReleaseManifestIssue),
    ("startup dependency matrix reports bad config and sidecars", StartupDependencyMatrixReportsBadConfigAndSidecars),
    ("startup dependency matrix ignores corrupt optional local settings", StartupDependencyMatrixIgnoresCorruptOptionalLocalSettings),
    ("application version metadata matches hardening release", ApplicationVersionMetadataMatchesHardeningRelease),
    ("application version argument returns version text", ApplicationVersionArgumentReturnsVersionText),
    ("startup manifest status distinguishes skipped and failed", StartupManifestStatusDistinguishesSkippedAndFailed),
    ("startup error log entry includes phase and exception", StartupErrorLogEntryIncludesPhaseAndException),
    ("last crash diagnostic writes redacted exception details", LastCrashDiagnosticWritesRedactedExceptionDetails),
    ("startup health records required phase success", StartupHealthRecordsRequiredPhaseSuccess),
    ("startup health writes schema version", StartupHealthWritesSchemaVersion),
    ("startup health records required phase failure", StartupHealthRecordsRequiredPhaseFailure),
    ("startup health records optional phase failure without throwing", StartupHealthRecordsOptionalPhaseFailureWithoutThrowing),
    ("runtime housekeeping removes stale temp and atomic files", RuntimeHousekeepingRemovesStaleTempAndAtomicFiles),
    ("runtime housekeeping preserves fresh and unrelated tmp files", RuntimeHousekeepingPreservesFreshAndUnrelatedTmpFiles),
    ("runtime housekeeping removes old quarantined json only", RuntimeHousekeepingRemovesOldQuarantinedJsonOnly),
    ("runtime housekeeping removes old backup files only", RuntimeHousekeepingRemovesOldBackupFilesOnly),
    ("runtime housekeeping rotates oversized startup log", RuntimeHousekeepingRotatesOversizedStartupLog),
    ("runtime housekeeping skips locked files", RuntimeHousekeepingSkipsLockedFiles),
    ("ui operation failure reporter logs status and dialog", UiOperationFailureReporterLogsStatusAndDialog),
    ("status bar activity indicator tracks async operations", StatusBarActivityIndicatorTracksAsyncOperations),
    ("background task supervisor suppresses duplicate phases", BackgroundTaskSupervisorSuppressesDuplicatePhases),
    ("background task supervisor logs failures", BackgroundTaskSupervisorLogsFailures),
    ("background task supervisor cancels running tasks on dispose", BackgroundTaskSupervisorCancelsRunningTasksOnDispose),
    ("atomic file promotion preserves existing destination on locked replacement", AtomicFilePromotionPreservesExistingDestinationOnLockedReplacement),
    ("atomic file promotion creates bounded runtime backups", AtomicFilePromotionCreatesBoundedRuntimeBackups),
    ("network request retries transient failures", NetworkRequestRetriesTransientFailures),
    ("outbound network diagnostics records sanitized success endpoint", OutboundNetworkDiagnosticsRecordsSanitizedSuccessEndpoint),
    ("outbound network diagnostics records failure counts", OutboundNetworkDiagnosticsRecordsFailureCounts),
    ("network request rejects disallowed host before send", NetworkRequestRejectsDisallowedHostBeforeSend),
    ("network request does not retry unauthorized", NetworkRequestDoesNotRetryUnauthorized),
    ("network circuit breaker opens after repeated terminal failures", NetworkCircuitBreakerOpensAfterRepeatedTerminalFailures),
    ("network circuit breaker clears after success", NetworkCircuitBreakerClearsAfterSuccess),
    ("startup dependency matrix classifies terminal network failure", StartupDependencyMatrixClassifiesTerminalNetworkFailure),
    ("network request wraps timeout", NetworkRequestWrapsTimeout),
    ("network request preserves caller cancellation", NetworkRequestPreservesCallerCancellation),
    ("network allowlist rejects credentialed and escaped hosts", NetworkAllowlistRejectsCredentialedAndEscapedHosts),
    ("network allowlist accepts obsidian publish content hosts", NetworkAllowlistAcceptsObsidianPublishContentHosts),
    ("network allowlist rejects unexpected hosted settings path", NetworkAllowlistRejectsUnexpectedHostedSettingsPath),
    ("network allowlist rejects unexpected update path", NetworkAllowlistRejectsUnexpectedUpdatePath),
    ("network allowlist generic policy rejects unrelated update host paths", NetworkAllowlistGenericPolicyRejectsUnrelatedUpdateHostPaths),
    ("network response limits define defaults", NetworkResponseLimitsDefineDefaults),
    ("network response limit rejects oversized html header", NetworkResponseLimitRejectsOversizedHtmlHeader),
    ("network response limit rejects oversized markdown stream", NetworkResponseLimitRejectsOversizedMarkdownStream),
    ("network response limit rejects oversized json cache stream", NetworkResponseLimitRejectsOversizedJsonCacheStream),
    ("network response limit rejects oversized image header", NetworkResponseLimitRejectsOversizedImageHeader),
    ("markdown async fetch preserves caller cancellation", MarkdownAsyncFetchPreservesCallerCancellation),
    ("runtime artifact loader quarantines malformed json", RuntimeArtifactLoaderQuarantinesMalformedJson),
    ("runtime artifact loader restores newest valid backup", RuntimeArtifactLoaderRestoresNewestValidBackup),
    ("startup dependency matrix logs locked runtime artifact failures", StartupDependencyMatrixLogsLockedRuntimeArtifactFailures),
    ("login info cache load returns empty for malformed json", LoginInfoCacheLoadReturnsEmptyForMalformedJson),
    ("asset manifest load returns empty for malformed json", AssetManifestLoadReturnsEmptyForMalformedJson),
    ("published asset fallback resolves transclusion without attachment index", PublishedAssetFallbackResolvesTransclusionWithoutAttachmentIndex),
    ("hero token filename resolves listing display name", HeroTokenFileNameResolvesListingDisplayName),
    ("former pc listing parses three-column hero rows", FormerPcListingParsesThreeColumnHeroRows),
    ("active hero markdown cancellation writes no files", ActiveHeroMarkdownCancellationWritesNoFiles),
    ("former hero markdown cancellation writes no inactive files", FormerHeroMarkdownCancellationWritesNoInactiveFiles),
    ("player character refresh cancellation clears in progress flag", PlayerCharacterRefreshCancellationClearsInProgressFlag),
    ("player character refresh is not delayed when hero images are suppressed", PlayerCharacterRefreshIsNotDelayedWhenHeroImagesAreSuppressed),
    ("game forum startup cancellation writes no manifests", GameForumStartupCancellationWritesNoManifests),
    ("keyword index loader quarantines malformed json", KeywordIndexLoaderQuarantinesMalformedJson),
    ("keyword index loader salvages legacy disallowed urls", KeywordIndexLoaderSalvagesLegacyDisallowedUrls),
    ("sitemap validation rejects poisoned url", SitemapValidationRejectsPoisonedUrl),
    ("sitemap keyword dictionary preserves existing output on rejected url", SitemapKeywordDictionaryPreservesExistingOutputOnRejectedUrl),
    ("source integrity records first accepted sitemap", SourceIntegrityRecordsFirstAcceptedSitemap),
    ("source integrity rejects collapsed sitemap and preserves output", SourceIntegrityRejectsCollapsedSitemapAndPreservesOutput),
    ("source integrity rejects collapsed markdown and preserves output", SourceIntegrityRejectsCollapsedMarkdownAndPreservesOutput),
    ("source integrity rejects collapsed keyword index and preserves output", SourceIntegrityRejectsCollapsedKeywordIndexAndPreservesOutput),
    ("keyword index validation rejects poisoned url entries", KeywordIndexValidationRejectsPoisonedUrlEntries),
    ("keyword index validation rejects poisoned match urls", KeywordIndexValidationRejectsPoisonedMatchUrls),
    ("keyword terms release copy generates from keyword index", KeywordTermsReleaseCopyGeneratesFromKeywordIndex),
    ("keyword terms publish copy preserves parent release terms", KeywordTermsPublishCopyPreservesParentReleaseTerms),
    ("rpol auth detects login page fallback", RpolAuthDetectsLoginPageFallback),
    ("rpol auth distinguishes blocked and remote failures", RpolAuthDistinguishesBlockedAndRemoteFailures),
    ("rpol auth prefers installed browsers before playwright chromium", RpolAuthPrefersInstalledBrowsersBeforePlaywrightChromium),
    ("rpol auth enforces browser tls validation", RpolAuthEnforcesBrowserTlsValidation),
    ("rpol auth classifies transport security failures", RpolAuthClassifiesTransportSecurityFailures),
    ("rpol auth cached failure short circuits html fetch", RpolAuthCachedFailureShortCircuitsHtmlFetch),
    ("rpol auth cached failure logs once", RpolAuthCachedFailureLogsOnce),
    ("rpol snapshot signs and verifies canonical payload", RpolSnapshotSignsAndVerifiesCanonicalPayload),
    ("rpol snapshot rejects another game", RpolSnapshotRejectsAnotherGame),
    ("rpol snapshot sanitizes credentials and login form", RpolSnapshotSanitizesCredentialsAndLoginForm),
    ("rpol snapshot accepts sanitized campaign content", RpolSnapshotAcceptsSanitizedCampaignContent),
    ("rpol snapshot rejects login-only content", RpolSnapshotRejectsLoginOnlyContent),
    ("rpol challenge detection ignores passive cloudflare references", RpolChallengeDetectionIgnoresPassiveCloudflareReferences),
    ("rpol verification recognizes authenticated browser title", RpolVerificationRecognizesAuthenticatedBrowserTitle),
    ("snapshot publisher state advances one target and wraps", SnapshotPublisherStateAdvancesOneTargetAndWraps),
    ("snapshot discovery approves game links", SnapshotDiscoveryApprovesGameLinks),
    ("snapshot publisher state persists its cursor", SnapshotPublisherStatePersistsItsCursor),
    ("snapshot publisher state rejects an invalid cursor", SnapshotPublisherStateRejectsInvalidCursor),
    ("network allowlist accepts only broker api path", NetworkAllowlistAcceptsOnlyBrokerApiPath),
    ("snapshot publisher argument is recognized", SnapshotPublisherArgumentIsRecognized),
    ("rpol auth caches blocked and expired session failures", RpolAuthCachesBlockedAndExpiredSessionFailures),
    ("rpol storage state validation accepts current rpol cookies", RpolStorageStateValidationAcceptsCurrentRpolCookies),
    ("rpol storage state validation deletes malformed state", RpolStorageStateValidationDeletesMalformedState),
    ("rpol storage state validation deletes stale state", RpolStorageStateValidationDeletesStaleState),
    ("rpol storage state validation deletes non-rpol state", RpolStorageStateValidationDeletesNonRpolState),
    ("show-all thread url preserves base query and adds show all", ShowAllThreadUrlPreservesBaseQueryAndAddsShowAll),
    ("rpol thread export preserves existing output on cancellation", RpolThreadExportPreservesExistingOutputOnCancellation),
    ("rpol thread export commits staged output", RpolThreadExportCommitsStagedOutput),
    ("rpol thread export rejects collapsed source and preserves existing output", RpolThreadExportRejectsCollapsedSourceAndPreservesExistingOutput),
    ("die roll extraction keeps only saved-log lines", DieRollExtractionKeepsOnlySavedLogLines),
    ("die roll extraction handles live rpol paragraph markup", DieRollExtractionHandlesLiveRpolParagraphMarkup),
    ("die roll sync appends only unsaved rolls", DieRollSyncAppendsOnlyUnsavedRolls),
    ("regional map downloads when missing", RegionalMapDownloadsWhenMissing),
    ("regional map downloads when older than one hour", RegionalMapDownloadsWhenOlderThanOneHour),
    ("regional map skips when newer than one hour", RegionalMapSkipsWhenNewerThanOneHour),
    ("regional map downloads when newer but transparent", RegionalMapDownloadsWhenNewerButTransparent),
    ("startup status includes download count and size", StartupStatusIncludesDownloadCountAndSize),
    ("adventure outline builds from saved IC html", AdventureOutlineBuildsFromSavedIcHtml),
    ("adventure outline fills every chapter through latest IC chapter", AdventureOutlineFillsEveryChapterThroughLatestIcChapter),
    ("adventure outline parses rpol linked author exports", AdventureOutlineParsesRpolLinkedAuthorExports),
    ("adventure outline summarizes table roles concisely", AdventureOutlineSummarizesTableRolesConcisely),
    ("adventure outline skips empty bullet marker posts", AdventureOutlineSkipsEmptyBulletMarkerPosts),
    ("adventure outline rejects weak generated summaries", AdventureOutlineRejectsWeakGeneratedSummaries),
    ("adventure outline fallback summaries preserve scene specifics", AdventureOutlineFallbackSummariesPreserveSceneSpecifics),
    ("adventure outline merges new saved IC bullets", AdventureOutlineMergesNewSavedIcBullets),
    ("adventure outline falls back to Obsidian markdown", AdventureOutlineFallsBackToObsidianMarkdown),
    ("adventure outline prefers saved IC html over fallback", AdventureOutlinePrefersSavedIcHtmlOverFallback),
    ("adventure outline ignores failed fallback markdown fetch", AdventureOutlineIgnoresFailedFallbackMarkdownFetch),
    ("adjusted post tallies aggregate saved IC html", AdjustedPostTalliesAggregateSavedIcHtml),
    ("keyword search falls back to The-prefixed term", KeywordSearchFallsBackToThePrefixedTerm),
    ("keyword search keeps quoted phrases together", KeywordSearchKeepsQuotedPhrasesTogether),
    ("keyword search accepts url source metadata", KeywordSearchAcceptsUrlSourceMetadata),
    ("keyword search filters rpol hero metadata-only hits", KeywordSearchFiltersRpolHeroMetadataOnlyHits),
    ("show menu contains xp item", ShowMenuContainsXpItem),
    ("show menu contains my hero briefing item", ShowMenuContainsMyHeroBriefingItem),
    ("show menu contains adventure outline item", ShowMenuContainsAdventureOutlineItem),
    ("adventure outline view displays generated markdown", AdventureOutlineViewDisplaysGeneratedMarkdown),
    ("about menu contains author and update items", AboutMenuContainsAuthorAndUpdateItems),
    ("about author text lists developer info", AboutAuthorTextListsDeveloperInfo),
    ("about version text shows app version", AboutVersionTextShowsAppVersion),
    ("update check verifies signed p-assist manifest", UpdateCheckVerifiesSignedPAssistManifest),
    ("update check accepts manifest signature made before trailing newline", UpdateCheckAcceptsManifestSignatureMadeBeforeTrailingNewline),
    ("update check chooses newest signed manifest entry", UpdateCheckChoosesNewestSignedManifestEntry),
    ("update check rejects tampered signed manifest", UpdateCheckRejectsTamperedSignedManifest),
    ("update check rejects retired manifest signing key", UpdateCheckRejectsRetiredManifestSigningKey),
    ("update check compares against current app version", UpdateCheckComparesAgainstCurrentAppVersion),
    ("update check reports latest version message", UpdateCheckReportsLatestVersionMessage),
    ("update check fetches signed manifest from allowed update host", UpdateCheckFetchesSignedManifestFromAllowedUpdateHost),
    ("update check remembers highest trusted version observed", UpdateCheckRemembersHighestTrustedVersionObserved),
    ("legacy trusted update state migrates to protected format", LegacyTrustedUpdateStateMigratesToProtectedFormat),
    ("trusted update state is encrypted at rest", TrustedUpdateStateIsEncryptedAtRest),
    ("trusted update state rejects tampered payload", TrustedUpdateStateRejectsTamperedPayload),
    ("update check rejects signed manifest rollback below trusted version floor", UpdateCheckRejectsSignedManifestRollbackBelowTrustedVersionFloor),
    ("update host certificate pinning accepts trusted leaf pin", UpdateHostCertificatePinningAcceptsTrustedLeafPin),
    ("update host certificate pinning accepts trusted intermediate pin", UpdateHostCertificatePinningAcceptsTrustedIntermediatePin),
    ("update host certificate pinning supports rotation window", UpdateHostCertificatePinningSupportsRotationWindow),
    ("update host certificate pinning rejects retired pin", UpdateHostCertificatePinningRejectsRetiredPin),
    ("update host certificate pinning rejects mismatched pin", UpdateHostCertificatePinningRejectsMismatchedPin),
    ("update host certificate validation allows trusted tls with pin mismatch", UpdateHostCertificateValidationAllowsTrustedTlsWithPinMismatch),
    ("certificate validation skips pin extraction for non update hosts", CertificateValidationSkipsPinExtractionForNonUpdateHosts),
    ("verified updater downloads installer to controlled path", VerifiedUpdaterDownloadsInstallerToControlledPath),
    ("verified updater rejects installer sha256 mismatch", VerifiedUpdaterRejectsInstallerSha256Mismatch),
    ("verified updater rejects installer signer mismatch", VerifiedUpdaterRejectsInstallerSignerMismatch),
    ("verified installer launch re-verifies before execution", VerifiedInstallerLaunchReverifiesBeforeExecution),
    ("verified installer launch rejects signer changes after verification", VerifiedInstallerLaunchRejectsSignerChangesAfterVerification),
    ("verified installer launch rejects elevation changes after verification", VerifiedInstallerLaunchRejectsElevationChangesAfterVerification),
    ("search enter triggers click when enabled", SearchEnterTriggersClickWhenEnabled),
    ("keyword search uppercases encrypted index results without changing launch URL", KeywordSearchUppercasesEncryptedIndexResultsWithoutChangingLaunchUrl),
    ("keyword search uppercases online Obsidian fallback results", KeywordSearchUppercasesOnlineObsidianFallbackResults),
    ("keyword search backfills online hits into keyword index", KeywordSearchBackfillsOnlineHitsIntoKeywordIndex),
    ("keyword search offers online fallback on local miss", KeywordSearchOffersOnlineFallbackOnLocalMiss),
    ("keyword search cancels previous online fallback", KeywordSearchCancelsPreviousOnlineFallback),
    ("keyword search rpol scope excludes obsidian-only whiteheart", KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheart),
    ("keyword search rpol scope excludes obsidian-only whiteheart stiffwhiskers", KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheartStiffwhiskers),
    ("keyword search expands hero first and full names", KeywordSearchExpandsHeroFirstAndFullNames),
    ("my hero briefing builds selected hero summary boundary", MyHeroBriefingBuildsSelectedHeroSummaryBoundary),
    ("my hero briefing prefers authenticated hero identity", MyHeroBriefingPrefersAuthenticatedHeroIdentity),
    ("my hero briefing requires explicit dungeon master hero selection", MyHeroBriefingRequiresExplicitDungeonMasterHeroSelection),
    ("my hero briefing leaves ambiguous first name unresolved", MyHeroBriefingLeavesAmbiguousFirstNameUnresolved),
    ("my hero briefing hides xp for unauthenticated selected hero card", MyHeroBriefingHidesXpForUnauthenticatedSelectedHeroCard),
    ("my hero briefing builds recent hero activity", MyHeroBriefingBuildsRecentHeroActivity),
    ("my hero briefing builds likely open response items", MyHeroBriefingBuildsLikelyOpenResponseItems),
    ("my hero briefing surfaces relevant unlocked notes", MyHeroBriefingSurfacesRelevantUnlockedNotes),
    ("my hero briefing requests hero selection when no hero selected", MyHeroBriefingRequestsHeroSelectionWhenNoHeroSelected),
    ("my hero briefing display text includes focused sections", MyHeroBriefingDisplayTextIncludesFocusedSections),
    ("my hero briefing styles likely response key", MyHeroBriefingStylesLikelyResponseKey),
    ("my hero briefing loads cached thread posts from runtime artifacts", MyHeroBriefingLoadsCachedThreadPostsFromRuntimeArtifacts),
    ("my hero briefing loads flat cached thread files from runtime artifacts", MyHeroBriefingLoadsFlatCachedThreadFilesFromRuntimeArtifacts),
    ("my hero briefing encrypted index loader tolerates malformed json", MyHeroBriefingEncryptedIndexLoaderToleratesMalformedJson),
    ("party hero sheet parser reads summary and hides xp lines", PartyHeroSheetParserReadsSummaryAndHidesXpLines),
    ("party hero listing summary overrides stale cached sheet", PartyHeroListingSummaryOverridesStaleCachedSheet),
    ("party hero xp visibility follows authenticated character", PartyHeroXpVisibilityFollowsAuthenticatedCharacter),
    ("tagged note cipher decrypts for matching level tag", TaggedNoteCipherDecryptsForMatchingLevelTag),
    ("tagged note cipher decrypts for matching character tag", TaggedNoteCipherDecryptsForMatchingCharacterTag),
    ("tagged note cipher rejects unmet class tag", TaggedNoteCipherRejectsUnmetClassTag),
    ("tagged note cipher accepts either-or ability tag", TaggedNoteCipherAcceptsEitherOrAbilityTag),
    ("tagged note cipher accepts bare class alternative", TaggedNoteCipherAcceptsBareClassAlternative),
    ("tagged note cipher accepts class level shorthand and faction tag", TaggedNoteCipherAcceptsClassLevelShorthandAndFactionTag),
    ("tagged note cipher accepts grouped and expression tag", TaggedNoteCipherAcceptsGroupedAndExpressionTag),
    ("tagged note cipher reports mismatched decrypt tags", TaggedNoteCipherReportsMismatchedDecryptTags),
    ("tagged note cipher reports encrypted markdown block counts", TaggedNoteCipherReportsEncryptedMarkdownBlockCounts),
    ("tagged note cipher indexes encrypted markdown frontmatter tags", TaggedNoteCipherIndexesEncryptedMarkdownFrontmatterTags),
    ("tagged note cipher authenticates visible tags", TaggedNoteCipherAuthenticatesVisibleTags),
    ("xp display recognizes dungeon master access", XpDisplayRecognizesDungeonMasterAccess),
    ("xp display finds totals by first and full character names", XpDisplayFindsTotalsByFirstAndFullCharacterNames),
    ("xp display stores multiple totals for dungeon master", XpDisplayStoresMultipleTotalsForDungeonMaster),
    ("xp tracking parser reads latest table totals", XpTrackingParserReadsLatestTableTotals),
    ("xp tracking parser rejects missing latest table", XpTrackingParserRejectsMissingLatestTable),
    ("xp tracking failure message hides url and directs players to dm", XpTrackingFailureMessageHidesUrlAndDirectsPlayersToDm),
    ("xp tracking missing pc message directs players to dm", XpTrackingMissingPcMessageDirectsPlayersToDm),
    ("show menu contains party item", ShowMenuContainsPartyItem),
    ("show menu contains former pcs item", ShowMenuContainsFormerPcsItem),
    ("former pcs view displays token name and class", FormerPcsViewDisplaysTokenNameAndClass),
    ("external url launch policy accepts http and https", ExternalUrlLaunchPolicyAcceptsHttpAndHttps),
    ("external url launch policy rejects unsafe inputs", ExternalUrlLaunchPolicyRejectsUnsafeInputs),
    ("hero image paths follow listing markdown table", HeroImagePathsFollowListingMarkdownTable),
    ("hero asset paths reject escaped targets", HeroAssetPathsRejectEscapedTargets),
    ("legacy local settings migrate to scoped encryption", LegacyLocalSettingsMigrateToPortableEncryption),
    ("v1 local settings migrate to authenticated encryption", V1LocalSettingsMigrateToAuthenticatedEncryption),
    ("v2 local settings migrate to scoped encryption", V2LocalSettingsMigrateToScopedEncryption),
    ("local settings are encrypted on load", LocalSettingsAreEncryptedOnLoad),
    ("portable encrypted settings byte loader clears source buffer", PortableEncryptedSettingsByteLoaderClearsSourceBuffer),
    ("credential manager utf8 helpers clear transient buffers", CredentialManagerUtf8HelpersClearTransientBuffers),
    ("local settings encrypt command writes portable envelope", LocalSettingsEncryptCommandWritesPortableEnvelope),
    ("local settings decrypt command writes plaintext json", LocalSettingsDecryptCommandWritesPlaintextJson),
    ("local settings rejects future schema version", LocalSettingsRejectsFutureSchemaVersion),
    ("scoped local settings reject copied install path", ScopedLocalSettingsRejectCopiedInstallPath),
    ("authenticated local settings reject tampered payload", AuthenticatedLocalSettingsRejectTamperedPayload),
    ("local settings restores newest valid backup", LocalSettingsRestoresNewestValidBackup),
    ("runtime path utility rejects escaped paths", RuntimePathUtilityRejectsEscapedPaths),
    ("health argument returns startup health summary", HealthArgumentReturnsStartupHealthSummary),
    ("publish verification accepts current output", PublishVerificationAcceptsCurrentOutput),
    ("publish verification rejects stale startup log", PublishVerificationRejectsStaleStartupLog),
    ("publish verification rejects startup health artifact", PublishVerificationRejectsStartupHealthArtifact),
    ("publish verification rejects outbound network diagnostics artifact", PublishVerificationRejectsOutboundNetworkDiagnosticsArtifact),
    ("publish verification rejects last crash artifact", PublishVerificationRejectsLastCrashArtifact),
    ("publish verification rejects malformed settings json", PublishVerificationRejectsMalformedSettingsJson),
    ("publish verification rejects future settings schema", PublishVerificationRejectsFutureSettingsSchema),
    ("publish verification accepts encrypted rpol local settings sidecar", PublishVerificationAcceptsEncryptedRpolLocalSettingsSidecar),
    ("publish verification rejects plaintext rpol local settings sidecar", PublishVerificationRejectsPlaintextRpolLocalSettingsSidecar),
    ("publish verification accepts missing hosted local settings url", PublishVerificationAcceptsMissingHostedLocalSettingsUrl),
    ("publish verification rejects missing xp password sidecar", PublishVerificationRejectsMissingXpPasswordSidecar),
    ("publish verification rejects plaintext xp password sidecar", PublishVerificationRejectsPlaintextXpPasswordSidecar),
    ("publish verification rejects malformed keyword index", PublishVerificationRejectsMalformedKeywordIndex),
    ("publish verification rejects malformed sitemap", PublishVerificationRejectsMalformedSitemap),
    ("publish verification rejects incomplete playwright runtime", PublishVerificationRejectsIncompletePlaywrightRuntime),
    ("publish verification rejects mismatched executable version", PublishVerificationRejectsMismatchedExecutableVersion),
    ("publish verification rejects stale release manifest", PublishVerificationRejectsStaleReleaseManifest),
    ("publish verification rejects malformed runtime inventory", PublishVerificationRejectsMalformedRuntimeInventory),
    ("publish verification rejects malformed release provenance", PublishVerificationRejectsMalformedReleaseProvenance),
    ("publish verification rejects unsigned executable when signing required", PublishVerificationRejectsUnsignedExecutableWhenSigningRequired),
    ("installer scripts target program files install path", InstallerScriptsTargetProgramFilesInstallPath),
    ("installer package verification accepts current package", InstallerPackageVerificationAcceptsCurrentPackage),
    ("installer package verification rejects unsigned payload when signing required", InstallerPackageVerificationRejectsUnsignedPayloadWhenSigningRequired),
    ("release update artifact verification accepts generated signed manifest", ReleaseUpdateArtifactVerificationAcceptsGeneratedSignedManifest),
    ("release update artifact verification rejects manifest hash mismatch", ReleaseUpdateArtifactVerificationRejectsManifestHashMismatch),
    ("hardening workflow builds and uploads signed release update artifacts", HardeningWorkflowBuildsAndUploadsSignedReleaseUpdateArtifacts),
    ("published health verification accepts current output", PublishedHealthVerificationAcceptsCurrentOutput),
    ("secret scan accepts current repository", SecretScanAcceptsCurrentRepository),
    ("secret scan rejects tracked env secret", SecretScanRejectsTrackedEnvSecret),
    ("release publish parity accepts current output", ReleasePublishParityAcceptsCurrentOutput),
    ("release publish parity rejects mismatched sidecar", ReleasePublishParityRejectsMismatchedSidecar),
    ("diagnostic bundle redacts sensitive values", DiagnosticBundleRedactsSensitiveValues),
    ("diagnostic bundle verify only rejects forbidden auth state", DiagnosticBundleVerifyOnlyRejectsForbiddenAuthState),
    ("diagnostic retention cleanup removes old diagnostics and preserves unrelated scratch files", DiagnosticRetentionCleanupRemovesOldDiagnosticsAndPreservesUnrelatedScratchFiles)
};

if (!string.IsNullOrWhiteSpace(requestedTestFilter))
{
    tests = tests
        .Where(test => test.Name.Contains(requestedTestFilter, StringComparison.OrdinalIgnoreCase))
        .ToArray();
}

var failures = new List<string>();

foreach (var (name, test) in tests)
{
    try
    {
        using var credentialStoreScope = RuntimeSecretStoreUtility.UseBackendForTests(new InMemoryWindowsCredentialStoreBackend());
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        var rootException = ex is TargetInvocationException tie && tie.InnerException is not null
            ? tie.InnerException
            : ex;

        failures.Add($"{name}: {rootException}");
        Console.WriteLine($"FAIL {name}: {rootException}");
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Failures:");
    foreach (var failure in failures)
    {
        Console.WriteLine(failure);
    }

    return 1;
}

return 0;

static void OrcishTranslatorReturnsOneToOneEnglishMapping()
{
    var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("hello");

    AssertEqual(1, results.Count, "expected one translation for hello");
    AssertEqual("zug", results[0].Translation, "unexpected Orcish translation for hello");
}

static void OrcishTranslatorReturnsSeveralEnglishMatchesForOneOrcishWord()
{
    var results = OrcishTranslatorUtility.TranslateOrcishToEnglish("mokra");

    AssertEqual(2, results.Count, "expected two English translations for mokra");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "friend", StringComparison.OrdinalIgnoreCase)), "expected friend translation");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "ally", StringComparison.OrdinalIgnoreCase)), "expected ally translation");
}

static void OrcishTranslatorUsesPartOfSpeechToDisambiguate()
{
    var nounResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("watch", partOfSpeech: "noun");
    var verbResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("watch", partOfSpeech: "verb");
    var unfilteredResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("watch");

    AssertEqual(1, nounResults.Count, "expected one noun translation for watch");
    AssertEqual("thrak", nounResults[0].Translation, "unexpected noun translation for watch");
    AssertEqual(1, verbResults.Count, "expected one verb translation for watch");
    AssertEqual("gor", verbResults[0].Translation, "unexpected verb translation for watch");
    AssertEqual(2, unfilteredResults.Count, "expected both translations when no part of speech is supplied");
}

static void OrcishTranslatorFiltersHumanTermsByRegister()
{
    var neutralResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("human", partOfSpeech: "noun", requiredTags: ["neutral"]);
    var insultingResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("weak human", partOfSpeech: "noun", requiredTags: ["insulting"]);
    var respectfulResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("free human", partOfSpeech: "noun", requiredTags: ["respectful"]);
    var pluralResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("humans", partOfSpeech: "noun", requiredTags: ["neutral", "plural"]);

    AssertEqual(1, neutralResults.Count, "expected one neutral human translation");
    AssertEqual("margi", neutralResults[0].Translation, "unexpected neutral human translation");
    AssertEqual(1, insultingResults.Count, "expected one insulting human translation");
    AssertEqual("thrum-skin", insultingResults[0].Translation, "unexpected insulting human translation");
    AssertEqual(1, respectfulResults.Count, "expected one respectful human translation");
    AssertEqual("surgar", respectfulResults[0].Translation, "unexpected respectful human translation");
    AssertEqual(1, pluralResults.Count, "expected one plural neutral human translation");
    AssertEqual("margith", pluralResults[0].Translation, "unexpected plural human translation");
}

static void OrcishTranslatorSupportsReverseLookupForRespectfulHumanTerm()
{
    var results = OrcishTranslatorUtility.TranslateOrcishToEnglish("surgar", partOfSpeech: "noun", requiredTags: ["respectful"]);

    AssertEqual(2, results.Count, "expected respectful reverse lookup to surface both English glosses");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "sun-born", StringComparison.OrdinalIgnoreCase)), "expected sun-born reverse translation");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "free human", StringComparison.OrdinalIgnoreCase)), "expected free human reverse translation");
}

static void OrcishTranslatorGeneratesPluralPossessivesSystematically()
{
    var neutralResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("humans'", partOfSpeech: "noun", requiredTags: ["neutral", "plural", "possessive"]);
    var insultingResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("softskins'", partOfSpeech: "noun", requiredTags: ["insulting", "plural", "possessive"]);
    var respectfulReverseResults = OrcishTranslatorUtility.TranslateOrcishToEnglish("surgariuk", partOfSpeech: "noun", requiredTags: ["respectful", "plural", "possessive"]);

    AssertEqual(1, neutralResults.Count, "expected neutral plural possessive");
    AssertEqual("margithuk", neutralResults[0].Translation, "unexpected neutral plural possessive");
    AssertEqual(1, insultingResults.Count, "expected insulting plural possessive");
    AssertEqual("thrum-skinaruk", insultingResults[0].Translation, "unexpected insulting plural possessive");
    AssertTrue(respectfulReverseResults.Any(result => string.Equals(result.Translation, "sun-born ones'", StringComparison.OrdinalIgnoreCase)), "expected respectful plural possessive reverse translation");
    AssertTrue(respectfulReverseResults.Any(result => string.Equals(result.Translation, "free humans'", StringComparison.OrdinalIgnoreCase)), "expected respectful plural possessive reverse translation for gloss");
}

static void OrcishTranslatorSupportsPaleAdjectiveRegisters()
{
    var neutralResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("pale", partOfSpeech: "adjective", requiredTags: ["neutral"]);
    var fearfulResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("pale with fear", partOfSpeech: "adjective", requiredTags: ["fear", "pejorative"]);
    var reverseResults = OrcishTranslatorUtility.TranslateOrcishToEnglish("kelnagak", partOfSpeech: "adjective", requiredTags: ["fear", "pejorative"]);

    AssertEqual(1, neutralResults.Count, "expected neutral pale adjective");
    AssertEqual("kelnib", neutralResults[0].Translation, "unexpected neutral pale adjective");
    AssertEqual(1, fearfulResults.Count, "expected fear-pale adjective");
    AssertEqual("kelnagak", fearfulResults[0].Translation, "unexpected fear-pale adjective");
    AssertTrue(reverseResults.Any(result => string.Equals(result.Translation, "pale with fear", StringComparison.OrdinalIgnoreCase)), "expected fear-pale reverse translation");
}

static void OrcishTranslatorPrefersVrakForDefaultSkinTerms()
{
    var skinResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("skin", partOfSpeech: "noun", requiredTags: ["default"]);
    var hidesResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("hides", partOfSpeech: "noun", requiredTags: ["default", "plural"]);
    var possessiveResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("skins'", partOfSpeech: "noun", requiredTags: ["default", "plural", "possessive"]);

    AssertEqual(1, skinResults.Count, "expected one default skin translation");
    AssertEqual("vrak", skinResults[0].Translation, "unexpected default skin translation");
    AssertEqual(1, hidesResults.Count, "expected one default hides translation");
    AssertEqual("vraki", hidesResults[0].Translation, "unexpected default hides translation");
    AssertEqual(1, possessiveResults.Count, "expected one default plural possessive skin translation");
    AssertEqual("vrakiuk", possessiveResults[0].Translation, "unexpected default plural possessive skin translation");
}

static void OrcishTranslatorReservesDrukhForReverentMonsterHide()
{
    var reverentResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("hide", partOfSpeech: "noun", requiredTags: ["reverent", "monster", "thick-hide"]);
    var reverseResults = OrcishTranslatorUtility.TranslateOrcishToEnglish("drukh", partOfSpeech: "noun", requiredTags: ["reverent", "monster", "thick-hide"]);
    var pluralPossessiveResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("hides'", partOfSpeech: "noun", requiredTags: ["reverent", "monster", "thick-hide", "plural", "possessive"]);

    AssertEqual(1, reverentResults.Count, "expected one reverent monster hide translation");
    AssertEqual("drukh", reverentResults[0].Translation, "unexpected reverent monster hide translation");
    AssertTrue(reverseResults.Any(result => string.Equals(result.Translation, "hide", StringComparison.OrdinalIgnoreCase)), "expected reverse hide translation for drukh");
    AssertEqual(1, pluralPossessiveResults.Count, "expected one reverent monster plural possessive hide translation");
    AssertEqual("drukhiuk", pluralPossessiveResults[0].Translation, "unexpected reverent monster plural possessive hide translation");
}

static void OrcishTranslatorSupportsOglurVerbFamily()
{
    AssertEqual("oglar", OrcishTranslatorUtility.TranslateEnglishToOrcish("to see", partOfSpeech: "verb", requiredTags: ["infinitive"])[0].Translation, "unexpected infinitive see translation");
    AssertEqual("oglur", OrcishTranslatorUtility.TranslateEnglishToOrcish("sees", partOfSpeech: "verb", requiredTags: ["present"])[0].Translation, "unexpected present see translation");
    AssertEqual("oglash", OrcishTranslatorUtility.TranslateEnglishToOrcish("saw", partOfSpeech: "verb", requiredTags: ["past"])[0].Translation, "unexpected past see translation");
    AssertEqual("ogluk", OrcishTranslatorUtility.TranslateEnglishToOrcish("have seen", partOfSpeech: "verb", requiredTags: ["perfect"])[0].Translation, "unexpected perfect see translation");
    AssertEqual("oglurin", OrcishTranslatorUtility.TranslateEnglishToOrcish("is seeing", partOfSpeech: "verb", requiredTags: ["progressive"])[0].Translation, "unexpected progressive see translation");
    AssertEqual("oglaruk", OrcishTranslatorUtility.TranslateEnglishToOrcish("will see", partOfSpeech: "verb", requiredTags: ["future"])[0].Translation, "unexpected future see translation");
    AssertEqual("noglur", OrcishTranslatorUtility.TranslateEnglishToOrcish("does not see", partOfSpeech: "verb", requiredTags: ["present", "negative"])[0].Translation, "unexpected negative present see translation");
    AssertEqual("noglash", OrcishTranslatorUtility.TranslateEnglishToOrcish("did not see", partOfSpeech: "verb", requiredTags: ["past", "negative"])[0].Translation, "unexpected negative past see translation");
}

static void OrcishTranslatorExposesBothIPronounVariants()
{
    var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("I", partOfSpeech: "pronoun");

    AssertEqual(2, results.Count, "expected two plain I variants");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "Ugh", StringComparison.OrdinalIgnoreCase)), "expected Ugh variant");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "Grrt", StringComparison.OrdinalIgnoreCase)), "expected Grrt variant");
}

static void OrcishTranslatorRandomIPickerReturnsValidVariant()
{
    var result = OrcishTranslatorUtility.TranslateEnglishToOrcishRandom("I", partOfSpeech: "pronoun");

    AssertTrue(result is not null, "expected random I picker to return a variant");
    AssertTrue(
        string.Equals(result!.Translation, "Ugh", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(result.Translation, "Grrt", StringComparison.OrdinalIgnoreCase),
        "expected random I picker to return one of the plain variants");
}

static void OrcishTranslatorReplacesEmphasizedIInEnglishText()
{
    var translated = OrcishTranslatorUtility.TranslateEnglishTextToOrcishPronouns("if I {emphasis} see");

    AssertEqual("if Grrt-Ugh see", translated, "expected emphasized I to become Grrt-Ugh");
}

static void OrcishTranslatorExposesBothReallyAdverbVariants()
{
    var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("really", partOfSpeech: "adverb");

    AssertEqual(2, results.Count, "expected two really adverb variants");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "grak", StringComparison.OrdinalIgnoreCase)), "expected grak variant");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "urkh", StringComparison.OrdinalIgnoreCase)), "expected urkh variant");
}

static void OrcishTranslatorRandomReallyPickerReturnsValidVariant()
{
    var result = OrcishTranslatorUtility.TranslateEnglishToOrcishRandom("really", partOfSpeech: "adverb");

    AssertTrue(result is not null, "expected random really picker to return a variant");
    AssertTrue(
        string.Equals(result!.Translation, "grak", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(result.Translation, "urkh", StringComparison.OrdinalIgnoreCase),
        "expected random really picker to return one of the adverb variants");
}

static void OrcishTranslatorExposesBothIfVariants()
{
    var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("if", partOfSpeech: "conjunction");

    AssertEqual(2, results.Count, "expected two plain if variants");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "ut", StringComparison.OrdinalIgnoreCase)), "expected ut variant");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "ka", StringComparison.OrdinalIgnoreCase)), "expected ka variant");
}

static void OrcishTranslatorAlternatesRepeatedIfTermsInSequence()
{
    var results = OrcishTranslatorUtility.TranslateEnglishSequenceToOrcish(["if", "if", "if", "if"]);

    AssertEqual(4, results.Count, "expected four translated terms");
    AssertFalse(string.IsNullOrWhiteSpace(results[0].Translation), "expected first if translation");
    AssertFalse(string.Equals(results[0].Translation, results[1].Translation, StringComparison.OrdinalIgnoreCase), "expected second if to alternate");
    AssertEqual(results[0].Translation, results[2].Translation, "expected third if to alternate back to the first choice");
    AssertEqual(results[1].Translation, results[3].Translation, "expected fourth if to alternate back to the second choice");
}

static void OrcishTranslatorExposesBothButVariants()
{
    var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("but", partOfSpeech: "conjunction");

    AssertEqual(2, results.Count, "expected two plain but variants");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "rokh", StringComparison.OrdinalIgnoreCase)), "expected rokh variant");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "nar", StringComparison.OrdinalIgnoreCase)), "expected nar variant");
}

static void OrcishTranslatorSupportsSarcasticButVariants()
{
    var results = OrcishTranslatorUtility.TranslateEnglishToOrcish("sarcastic but", partOfSpeech: "conjunction");

    AssertEqual(2, results.Count, "expected two sarcastic but variants");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "rokhki", StringComparison.OrdinalIgnoreCase)), "expected sarcastic rokh variant");
    AssertTrue(results.Any(result => string.Equals(result.Translation, "narki", StringComparison.OrdinalIgnoreCase)), "expected sarcastic nar variant");
}

static void OrcishTranslatorRandomButPickerReturnsValidVariant()
{
    var result = OrcishTranslatorUtility.TranslateEnglishToOrcishRandom("but", partOfSpeech: "conjunction");

    AssertTrue(result is not null, "expected random but picker to return a variant");
    AssertTrue(
        string.Equals(result!.Translation, "rokh", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(result.Translation, "nar", StringComparison.OrdinalIgnoreCase),
        "expected random but picker to return one of the plain variants");
}

static void OrcishTranslatorSupportsKirkilstonRefugePhraseVocabulary()
{
    AssertEqual("Kirkilston", OrcishTranslatorUtility.TranslateEnglishToOrcish("Kirkilston", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "unexpected Kirkilston proper noun translation");
    AssertEqual("mauk vargu dakkin", OrcishTranslatorUtility.TranslateEnglishToOrcish("may opt to be staying", partOfSpeech: "verb", requiredTags: ["fixed-phrase"])[0].Translation, "unexpected staying choice phrase translation");
    AssertEqual("nak-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("vicinity", partOfSpeech: "noun", requiredTags: ["nearby"])[0].Translation, "unexpected vicinity translation");
    AssertEqual("nul-dakkin", OrcishTranslatorUtility.TranslateEnglishToOrcish("abandoned", partOfSpeech: "adjective", requiredTags: ["deserted"])[0].Translation, "unexpected abandoned translation");
    AssertEqual("ti-hek", OrcishTranslatorUtility.TranslateEnglishToOrcish("tower", partOfSpeech: "noun", requiredTags: ["built"])[0].Translation, "unexpected tower translation");
    AssertEqual("noglar", OrcishTranslatorUtility.TranslateEnglishToOrcish("hidden", partOfSpeech: "adjective", requiredTags: ["concealed"])[0].Translation, "unexpected hidden translation");
    AssertEqual("burz-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("cave", partOfSpeech: "noun", requiredTags: ["underground"])[0].Translation, "unexpected cave translation");
    AssertEqual("mokh-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("base", partOfSpeech: "noun", requiredTags: ["operations"])[0].Translation, "unexpected base translation");
    AssertEqual("mauk dok", OrcishTranslatorUtility.TranslateEnglishToOrcish("may be from", partOfSpeech: "verb", requiredTags: ["origin"])[0].Translation, "unexpected origin modal translation");
    AssertEqual("dakku-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("residence", partOfSpeech: "noun", requiredTags: ["dwelling"])[0].Translation, "unexpected residence translation");
}

static void OrcishTranslatorSupportsRegionalMapHistoryVocabulary()
{
    AssertEqual("dak-mokhuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("regional", partOfSpeech: "adjective", requiredTags: ["area"])[0].Translation, "unexpected regional translation");
    AssertEqual("thog-var", OrcishTranslatorUtility.TranslateEnglishToOrcish("idea", partOfSpeech: "noun", requiredTags: ["abstract"])[0].Translation, "unexpected idea translation");
    AssertEqual("mur-dakuri", OrcishTranslatorUtility.TranslateEnglishToOrcish("centuries", partOfSpeech: "noun", requiredTags: ["long-span", "plural"])[0].Translation, "unexpected centuries translation");
    AssertEqual("mur-oglar", OrcishTranslatorUtility.TranslateEnglishToOrcish("famous", partOfSpeech: "adjective", requiredTags: ["known"])[0].Translation, "unexpected famous translation");
    AssertEqual("ut-dravash", OrcishTranslatorUtility.TranslateEnglishToOrcish("recovered", partOfSpeech: "verb", requiredTags: ["reclaim"])[0].Translation, "unexpected recovered translation");
    AssertEqual("dwarf-mog-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("dwarven settlement", partOfSpeech: "noun", requiredTags: ["dwarven"])[0].Translation, "unexpected dwarven settlement translation");
    AssertEqual("thrum-quum-mog-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("thorpes", partOfSpeech: "noun", requiredTags: ["rural", "plural"])[0].Translation, "unexpected thorpes translation");
    AssertEqual("thrum-mog-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("hamlets", partOfSpeech: "noun", requiredTags: ["small", "plural"])[0].Translation, "unexpected hamlets translation");
    AssertEqual("mog-dak-muri", OrcishTranslatorUtility.TranslateEnglishToOrcish("towns", partOfSpeech: "noun", requiredTags: ["settlement", "plural"])[0].Translation, "unexpected towns translation");
    AssertEqual("mog-dak-tii", OrcishTranslatorUtility.TranslateEnglishToOrcish("cities", partOfSpeech: "noun", requiredTags: ["settlement", "plural"])[0].Translation, "unexpected cities translation");
    AssertEqual("Glittering Caves", OrcishTranslatorUtility.TranslateEnglishToOrcish("Glittering Caves", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "unexpected Glittering Caves proper noun translation");
    AssertEqual("ut-dravku", OrcishTranslatorUtility.TranslateEnglishToOrcish("re-take", partOfSpeech: "verb", requiredTags: ["reclaim"])[0].Translation, "unexpected re-take translation");
    AssertEqual("orukhi", OrcishTranslatorUtility.TranslateEnglishToOrcish("orcs", partOfSpeech: "noun", requiredTags: ["orc", "plural"])[0].Translation, "unexpected orcs translation");
    AssertEqual("koboldi", OrcishTranslatorUtility.TranslateEnglishToOrcish("kobolds", partOfSpeech: "noun", requiredTags: ["plural"])[0].Translation, "unexpected kobolds translation");
}

static void OrcishTranslatorSupportsXavamrosRulershipVocabulary()
{
    AssertEqual("dug-agh-dug", OrcishTranslatorUtility.TranslateEnglishToOrcish("IV", partOfSpeech: "numeral", requiredTags: ["fourth"])[0].Translation, "unexpected IV translation");
    AssertEqual("murdrath-kelnib dakuruk", OrcishTranslatorUtility.TranslateEnglishToOrcish("hoary with age", partOfSpeech: "adjective", requiredTags: ["very-old"])[0].Translation, "unexpected hoary with age translation");
    AssertEqual("dargu-tukur arhk darg-thrak", OrcishTranslatorUtility.TranslateEnglishToOrcish("retains the throne", partOfSpeech: "verb", requiredTags: ["rulership"])[0].Translation, "unexpected retains the throne translation");
    AssertEqual("dargin arhk burz nak-oglar-dak darg-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("ruling the subterranean near-surface haunts", partOfSpeech: "verb", requiredTags: ["territory"])[0].Translation, "unexpected ruling the subterranean near-surface haunts translation");
    AssertEqual("mok-grak", OrcishTranslatorUtility.TranslateEnglishToOrcish("just as", partOfSpeech: "conjunction", requiredTags: ["equivalence"])[0].Translation, "unexpected just as translation");
    AssertEqual("darg-mog", OrcishTranslatorUtility.TranslateEnglishToOrcish("Governor", partOfSpeech: "noun", requiredTags: ["ruler"])[0].Translation, "unexpected Governor translation");
    AssertEqual("dargur-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("holds sway", partOfSpeech: "verb", requiredTags: ["authority"])[0].Translation, "unexpected holds sway translation");
    AssertEqual("oglar-dak mokhi", OrcishTranslatorUtility.TranslateEnglishToOrcish("surface communities", partOfSpeech: "noun", requiredTags: ["surface"])[0].Translation, "unexpected surface communities translation");
}

static void OrcishTranslatorSupportsPrinceXavinYouthfulAdventureVocabulary()
{
    AssertEqual("darg-ti-mog", OrcishTranslatorUtility.TranslateEnglishToOrcish("Prince", partOfSpeech: "noun", requiredTags: ["higher-than-governor"])[0].Translation, "unexpected Prince translation");
    AssertEqual("Xavin", OrcishTranslatorUtility.TranslateEnglishToOrcish("Xavin", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "unexpected Xavin proper noun translation");
    AssertEqual("grak ik mogumuk dug mur-dakur", OrcishTranslatorUtility.TranslateEnglishToOrcish("well into his second century", partOfSpeech: "adjective", requiredTags: ["aged"])[0].Translation, "unexpected age phrase translation");
    AssertEqual("ashdak nurik grod", OrcishTranslatorUtility.TranslateEnglishToOrcish("still youthful enough", partOfSpeech: "adjective", requiredTags: ["sufficient"])[0].Translation, "unexpected youthful enough translation");
    AssertEqual("dargu-thog ak", OrcishTranslatorUtility.TranslateEnglishToOrcish("insist on", partOfSpeech: "verb", requiredTags: ["stubborn"])[0].Translation, "unexpected insist on translation");
    AssertEqual("lag-oglarin", OrcishTranslatorUtility.TranslateEnglishToOrcish("exploring", partOfSpeech: "verb", requiredTags: ["seeking"])[0].Translation, "unexpected exploring translation");
    AssertEqual("vark-yankin", OrcishTranslatorUtility.TranslateEnglishToOrcish("adventuring", partOfSpeech: "verb", requiredTags: ["danger"])[0].Translation, "unexpected adventuring translation");
    AssertEqual("nul-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("nonsense", partOfSpeech: "noun", requiredTags: ["foolish"])[0].Translation, "unexpected nonsense translation");
    AssertEqual("morzur", OrcishTranslatorUtility.TranslateEnglishToOrcish("plagues", partOfSpeech: "verb", requiredTags: ["present"])[0].Translation, "unexpected plagues translation");
}

static void OrcishTranslatorSupportsKirkilstonChurchAndRedLawsVocabulary()
{
    AssertEqual("tur mog-nargash dok", OrcishTranslatorUtility.TranslateEnglishToOrcish("is named after", partOfSpeech: "verb", requiredTags: ["passive"])[0].Translation, "unexpected named after translation");
    AssertEqual("mograth-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("church", partOfSpeech: "noun", requiredTags: ["religious"])[0].Translation, "unexpected church translation");
    AssertEqual("mograth-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("kirk", partOfSpeech: "noun", requiredTags: ["synonym"])[0].Translation, "unexpected kirk translation");
    AssertEqual("murk-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("center", partOfSpeech: "noun", requiredTags: ["central"])[0].Translation, "unexpected center translation");
    AssertEqual("thrak-murk-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("centerpiece", partOfSpeech: "noun", requiredTags: ["important"])[0].Translation, "unexpected centerpiece translation");
    AssertEqual("rug-mograth-dak-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("Red Temple", partOfSpeech: "noun", requiredTags: ["red-law"])[0].Translation, "unexpected Red Temple translation");
    AssertEqual("gor-ti-hek", OrcishTranslatorUtility.TranslateEnglishToOrcish("Watchtower", partOfSpeech: "noun", requiredTags: ["watch"])[0].Translation, "unexpected Watchtower translation");
    AssertEqual("thrak-murk-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("key centers", partOfSpeech: "noun", requiredTags: ["important"])[0].Translation, "unexpected key centers translation");
    AssertEqual("mograth-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("faith", partOfSpeech: "noun", requiredTags: ["belief"])[0].Translation, "unexpected faith translation");
    AssertEqual("mograthuk darg-bib", OrcishTranslatorUtility.TranslateEnglishToOrcish("Ecclesiastical Law", partOfSpeech: "noun", requiredTags: ["church"])[0].Translation, "unexpected Ecclesiastical Law translation");
    AssertEqual("dargash", OrcishTranslatorUtility.TranslateEnglishToOrcish("Governed", partOfSpeech: "verb", requiredTags: ["past-participle"])[0].Translation, "unexpected governed translation");
    AssertEqual("tur dargash fa", OrcishTranslatorUtility.TranslateEnglishToOrcish("is led by", partOfSpeech: "verb", requiredTags: ["passive"])[0].Translation, "unexpected passive led translation");
    AssertEqual("drath-mograth", OrcishTranslatorUtility.TranslateEnglishToOrcish("seasoned Priest", partOfSpeech: "noun", requiredTags: ["experienced"])[0].Translation, "unexpected seasoned Priest translation");
    AssertEqual("mograth-darg", OrcishTranslatorUtility.TranslateEnglishToOrcish("Prelacy", partOfSpeech: "noun", requiredTags: ["religious"])[0].Translation, "unexpected Prelacy translation");
    AssertEqual("rug-darg-bibi", OrcishTranslatorUtility.TranslateEnglishToOrcish("Red Laws", partOfSpeech: "noun", requiredTags: ["red-law"])[0].Translation, "unexpected Red Laws translation");
    AssertEqual("hek-darg", OrcishTranslatorUtility.TranslateEnglishToOrcish("implementation", partOfSpeech: "noun", requiredTags: ["execution"])[0].Translation, "unexpected implementation translation");
    AssertEqual("dug-agh-ash mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("triad", partOfSpeech: "noun", requiredTags: ["three"])[0].Translation, "unexpected triad translation");
    AssertEqual("mograth-darg agh darg-bib darg-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("religious and administrative authority", partOfSpeech: "noun", requiredTags: ["bureaucracy"])[0].Translation, "unexpected religious and administrative authority translation");
    AssertEqual("thrum-darg-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("more relaxed", partOfSpeech: "adjective", requiredTags: ["lenient"])[0].Translation, "unexpected more relaxed translation");
    AssertEqual("oglar-ti thrum-darg-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("notably more lenient", partOfSpeech: "adjective", requiredTags: ["noticeable"])[0].Translation, "unexpected notably more lenient translation");
    AssertEqual("mur-darg-nu-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("less rigid", partOfSpeech: "adjective", requiredTags: ["less"])[0].Translation, "unexpected less rigid translation");
    AssertEqual("mograth-bib", OrcishTranslatorUtility.TranslateEnglishToOrcish("religious doctrine", partOfSpeech: "noun", requiredTags: ["teaching"])[0].Translation, "unexpected religious doctrine translation");
    AssertEqual("grak-nak-dak-mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("immediate surrounding area", partOfSpeech: "noun", requiredTags: ["immediate"])[0].Translation, "unexpected immediate surrounding area translation");
}

static void OrcishTranslatorSupportsKirklistonEconomyVocabulary()
{
    AssertEqual("drav-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("economy", partOfSpeech: "noun", requiredTags: ["commerce"])[0].Translation, "unexpected economy translation");
    AssertEqual("murk-dakur nak", OrcishTranslatorUtility.TranslateEnglishToOrcish("revolves around", partOfSpeech: "verb", requiredTags: ["central"])[0].Translation, "unexpected revolves around translation");
    AssertEqual("quum-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("livestock", partOfSpeech: "noun", requiredTags: ["kept"])[0].Translation, "unexpected livestock translation");
    AssertEqual("thrum-quum-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("sheep", partOfSpeech: "noun", requiredTags: ["kept"])[0].Translation, "unexpected sheep translation");
    AssertEqual("quum-hekin", OrcishTranslatorUtility.TranslateEnglishToOrcish("agriculture", partOfSpeech: "noun", requiredTags: ["farming"])[0].Translation, "unexpected agriculture translation");
    AssertEqual("thrak-quum-hek", OrcishTranslatorUtility.TranslateEnglishToOrcish("chief crop", partOfSpeech: "noun", requiredTags: ["primary"])[0].Translation, "unexpected chief crop translation");
    AssertEqual("dok-dravin", OrcishTranslatorUtility.TranslateEnglishToOrcish("export", partOfSpeech: "noun", requiredTags: ["outbound"])[0].Translation, "unexpected export translation");
    AssertEqual("rukh-mauk", OrcishTranslatorUtility.TranslateEnglishToOrcish("mead", partOfSpeech: "noun", requiredTags: ["fermented"])[0].Translation, "unexpected mead translation");
    AssertEqual("gruul-hek hekin-var", OrcishTranslatorUtility.TranslateEnglishToOrcish("lumber production", partOfSpeech: "noun", requiredTags: ["wood"])[0].Translation, "unexpected lumber production translation");
    AssertEqual("hrogar-lag", OrcishTranslatorUtility.TranslateEnglishToOrcish("caravan route", partOfSpeech: "noun", requiredTags: ["transport"])[0].Translation, "unexpected caravan route translation");
    AssertEqual("hrogar-mokhi", OrcishTranslatorUtility.TranslateEnglishToOrcish("caravans", partOfSpeech: "noun", requiredTags: ["transport", "plural"])[0].Translation, "unexpected caravans translation");
    AssertEqual("mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("company", partOfSpeech: "noun", requiredTags: ["trade"])[0].Translation, "unexpected company translation");
    AssertEqual("dravur dok", OrcishTranslatorUtility.TranslateEnglishToOrcish("benefits from", partOfSpeech: "verb", requiredTags: ["advantage"])[0].Translation, "unexpected benefits from translation");
    AssertEqual("mokru-thogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("interactions", partOfSpeech: "noun", requiredTags: ["contact"])[0].Translation, "unexpected interactions translation");
    AssertEqual("draviki", OrcishTranslatorUtility.TranslateEnglishToOrcish("merchants", partOfSpeech: "noun", requiredTags: ["trade"])[0].Translation, "unexpected merchants translation");
    AssertEqual("hrowk-dravi drav-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("exchange of goods", partOfSpeech: "noun", requiredTags: ["exchange"])[0].Translation, "unexpected exchange of goods translation");
    AssertEqual("hrowku mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("transport persons", partOfSpeech: "verb", requiredTags: ["transport"])[0].Translation, "unexpected transport persons translation");
    AssertEqual("hrowk-drav", OrcishTranslatorUtility.TranslateEnglishToOrcish("cargo", partOfSpeech: "noun", requiredTags: ["carried-goods"])[0].Translation, "unexpected cargo translation");
    AssertEqual("thrak-grak", OrcishTranslatorUtility.TranslateEnglishToOrcish("of import", partOfSpeech: "adjective", requiredTags: ["important"])[0].Translation, "unexpected of import translation");
    AssertEqual("quum-drav zorn-dakur", OrcishTranslatorUtility.TranslateEnglishToOrcish("rate of pay", partOfSpeech: "noun", requiredTags: ["payment", "rate"])[0].Translation, "unexpected rate of pay translation");
    AssertEqual("drav-biti", OrcishTranslatorUtility.TranslateEnglishToOrcish("shares", partOfSpeech: "noun", requiredTags: ["ownership", "plural"])[0].Translation, "unexpected shares translation");
}

static void OrcishTranslatorSupportsKirklistonWatchVocabulary()
{
    AssertEqual("nul-tukrin", OrcishTranslatorUtility.TranslateEnglishToOrcish("Lacking", partOfSpeech: "verb", requiredTags: ["negative"])[0].Translation, "unexpected lacking translation");
    AssertEqual("bib-darguk gor-hek", OrcishTranslatorUtility.TranslateEnglishToOrcish("formal wall", partOfSpeech: "noun", requiredTags: ["formal"])[0].Translation, "unexpected formal wall translation");
    AssertEqual("thrak-gor-hek-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("significant defensive structures", partOfSpeech: "noun", requiredTags: ["defense"])[0].Translation, "unexpected significant defensive structures translation");
    AssertEqual("lag-tukur ak", OrcishTranslatorUtility.TranslateEnglishToOrcish("relies on", partOfSpeech: "verb", requiredTags: ["dependence"])[0].Translation, "unexpected relies on translation");
    AssertEqual("nikmokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("small group", partOfSpeech: "noun", requiredTags: ["small"])[0].Translation, "unexpected small group translation");
    AssertEqual("yanki-grod", OrcishTranslatorUtility.TranslateEnglishToOrcish("brave", partOfSpeech: "adjective", requiredTags: ["courage"])[0].Translation, "unexpected brave translation");
    AssertEqual("nul-hekin", OrcishTranslatorUtility.TranslateEnglishToOrcish("untrained", partOfSpeech: "adjective", requiredTags: ["untrained"])[0].Translation, "unexpected untrained translation");
    AssertEqual("mog-oglar mok", OrcishTranslatorUtility.TranslateEnglishToOrcish("known as", partOfSpeech: "verb", requiredTags: ["known"])[0].Translation, "unexpected known as translation");
    AssertEqual("thrum-mog-dak thrak", OrcishTranslatorUtility.TranslateEnglishToOrcish("Hamlet Watch", partOfSpeech: "noun", requiredTags: ["watch"])[0].Translation, "unexpected Hamlet Watch translation");
    AssertEqual("gor-thog-in", OrcishTranslatorUtility.TranslateEnglishToOrcish("protecting", partOfSpeech: "verb", requiredTags: ["protection", "progressive"])[0].Translation, "unexpected protecting translation");
    AssertEqual("lag-ti-bit gor-thog-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("additional protection", partOfSpeech: "noun", requiredTags: ["additional", "protection"])[0].Translation, "unexpected additional protection translation");
    AssertEqual("tukur-darg gorin", OrcishTranslatorUtility.TranslateEnglishToOrcish("responsible for safeguarding", partOfSpeech: "verb", requiredTags: ["responsibility"])[0].Translation, "unexpected responsible for safeguarding translation");
    AssertEqual("vark-thogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("threats", partOfSpeech: "noun", requiredTags: ["danger"])[0].Translation, "unexpected threats translation");
    AssertEqual("nul-darg-thog darg-gashi", OrcishTranslatorUtility.TranslateEnglishToOrcish("forces of chaos", partOfSpeech: "noun", requiredTags: ["chaos"])[0].Translation, "unexpected forces of chaos translation");
    AssertEqual("nak-vril-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("nearby wilds", partOfSpeech: "noun", requiredTags: ["nearby"])[0].Translation, "unexpected nearby wilds translation");
    AssertEqual("yanki-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("bravery", partOfSpeech: "noun", requiredTags: ["courage"])[0].Translation, "unexpected bravery translation");
    AssertEqual("mur-grod agh grotash-nu grod-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("rugged and resilient spirit", partOfSpeech: "noun", requiredTags: ["resilient"])[0].Translation, "unexpected rugged and resilient spirit translation");
    AssertEqual("Kirklistonuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("Kirkliston's", partOfSpeech: "noun", requiredTags: ["possessive"])[0].Translation, "unexpected Kirkliston's translation");
    AssertEqual("dak-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("inhabitants", partOfSpeech: "noun", requiredTags: ["resident"])[0].Translation, "unexpected inhabitants translation");
}

static void OrcishTranslatorSupportsKirklistonDailyLifeVocabulary()
{
    AssertEqual("dakur-dakur-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("daily life", partOfSpeech: "noun", requiredTags: ["routine"])[0].Translation, "unexpected daily life translation");
    AssertEqual("agh-narg bibnak Kirkilston", OrcishTranslatorUtility.TranslateEnglishToOrcish("alternate spelling 'Kirkilston'", partOfSpeech: "noun", requiredTags: ["alternate"])[0].Translation, "unexpected alternate spelling translation");
    AssertEqual("nargash fa", OrcishTranslatorUtility.TranslateEnglishToOrcish("marked by", partOfSpeech: "verb", requiredTags: ["marked"])[0].Translation, "unexpected marked by translation");
    AssertEqual("mur-hekin mokh-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("hardworking ethos", partOfSpeech: "noun", requiredTags: ["values"])[0].Translation, "unexpected hardworking ethos translation");
    AssertEqual("dak-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("residents", partOfSpeech: "noun", requiredTags: ["resident"])[0].Translation, "unexpected residents translation");
    AssertEqual("hekin ik", OrcishTranslatorUtility.TranslateEnglishToOrcish("engaged in", partOfSpeech: "verb", requiredTags: ["working"])[0].Translation, "unexpected engaged in translation");
    AssertEqual("thrum-quum-mog-hekin", OrcishTranslatorUtility.TranslateEnglishToOrcish("shepherding", partOfSpeech: "noun", requiredTags: ["sheep"])[0].Translation, "unexpected shepherding translation");
    AssertEqual("nak-dakuk dravi", OrcishTranslatorUtility.TranslateEnglishToOrcish("local trades", partOfSpeech: "noun", requiredTags: ["local"])[0].Translation, "unexpected local trades translation");
    AssertEqual("thruk-dakku-heki", OrcishTranslatorUtility.TranslateEnglishToOrcish("essential amenities", partOfSpeech: "noun", requiredTags: ["essential"])[0].Translation, "unexpected essential amenities translation");
    AssertEqual("zol-hekruhur", OrcishTranslatorUtility.TranslateEnglishToOrcish("blacksmith", partOfSpeech: "noun", requiredTags: ["iron"])[0].Translation, "unexpected blacksmith translation");
    AssertEqual("rukh-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("tavern", partOfSpeech: "noun", requiredTags: ["drink"])[0].Translation, "unexpected tavern translation");
    AssertEqual("nik-drav-dak-mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("small market area", partOfSpeech: "noun", requiredTags: ["small"])[0].Translation, "unexpected small market area translation");
    AssertEqual("mokh-dakur-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("social life", partOfSpeech: "noun", requiredTags: ["social"])[0].Translation, "unexpected social life translation");
    AssertEqual("mokh-mokrui", OrcishTranslatorUtility.TranslateEnglishToOrcish("communal gatherings", partOfSpeech: "noun", requiredTags: ["communal"])[0].Translation, "unexpected communal gatherings translation");
    AssertEqual("mauk-mokhi", OrcishTranslatorUtility.TranslateEnglishToOrcish("festivals", partOfSpeech: "noun", requiredTags: ["celebration"])[0].Translation, "unexpected festivals translation");
    AssertEqual("drav-mauki", OrcishTranslatorUtility.TranslateEnglishToOrcish("fairs", partOfSpeech: "noun", requiredTags: ["trade"])[0].Translation, "unexpected fairs translation");
    AssertEqual("murk-thrak-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("focal points", partOfSpeech: "noun", requiredTags: ["central"])[0].Translation, "unexpected focal points translation");
    AssertEqual("mokh-mokru-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("social cohesion", partOfSpeech: "noun", requiredTags: ["unity"])[0].Translation, "unexpected social cohesion translation");
}

static void OrcishTranslatorSupportsKirklistonWildernessOpportunityVocabulary()
{
    AssertEqual("dakkin k'ik arhk burz-nak", OrcishTranslatorUtility.TranslateEnglishToOrcish("living in the shadow", partOfSpeech: "verb", requiredTags: ["shadow"])[0].Translation, "unexpected living in the shadow translation");
    AssertEqual("Burz-ti Ti-Daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("Blackpeak Mountains", partOfSpeech: "noun", requiredTags: ["mountains"])[0].Translation, "unexpected Blackpeak Mountains translation");
    AssertEqual("Burz-gruul", OrcishTranslatorUtility.TranslateEnglishToOrcish("Darkforest", partOfSpeech: "noun", requiredTags: ["forest"])[0].Translation, "unexpected Darkforest translation");
    AssertEqual("mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("people", partOfSpeech: "noun", requiredTags: ["plural"])[0].Translation, "unexpected people translation");
    AssertEqual("noglar-nu ur", OrcishTranslatorUtility.TranslateEnglishToOrcish("no strangers to", partOfSpeech: "verb", requiredTags: ["familiar"])[0].Translation, "unexpected no strangers to translation");
    AssertEqual("grot-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("hardship", partOfSpeech: "noun", requiredTags: ["difficulty"])[0].Translation, "unexpected hardship translation");
    AssertEqual("vark-thogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("dangers", partOfSpeech: "noun", requiredTags: ["danger"])[0].Translation, "unexpected dangers translation");
    AssertEqual("nargash fa", OrcishTranslatorUtility.TranslateEnglishToOrcish("posed by", partOfSpeech: "verb", requiredTags: ["caused-by"])[0].Translation, "unexpected posed by translation");
    AssertEqual("vril-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("wilderness", partOfSpeech: "noun", requiredTags: ["wild"])[0].Translation, "unexpected wilderness translation");
    AssertEqual("grotash-nu-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("resilience", partOfSpeech: "noun", requiredTags: ["resilient"])[0].Translation, "unexpected resilience translation");
    AssertEqual("varg-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("opportunities", partOfSpeech: "noun", requiredTags: ["opportunity"])[0].Translation, "unexpected opportunities translation");
    AssertEqual("vark-yank-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("adventurers", partOfSpeech: "noun", requiredTags: ["danger"])[0].Translation, "unexpected adventurers translation");
    AssertEqual("tar ut", OrcishTranslatorUtility.TranslateEnglishToOrcish("be it", partOfSpeech: "conjunction", requiredTags: ["alternative"])[0].Translation, "unexpected be it translation");
    AssertEqual("dravin", OrcishTranslatorUtility.TranslateEnglishToOrcish("aiding", partOfSpeech: "verb", requiredTags: ["help"])[0].Translation, "unexpected aiding translation");
    AssertEqual("gor-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("defense", partOfSpeech: "noun", requiredTags: ["defense"])[0].Translation, "unexpected defense translation");
    AssertEqual("hekin ik", OrcishTranslatorUtility.TranslateEnglishToOrcish("engaging in", partOfSpeech: "verb", requiredTags: ["working"])[0].Translation, "unexpected engaging in translation");
    AssertEqual("drav hek-vari", OrcishTranslatorUtility.TranslateEnglishToOrcish("trade activities", partOfSpeech: "noun", requiredTags: ["commerce"])[0].Translation, "unexpected trade activities translation");
    AssertEqual("hek-bib", OrcishTranslatorUtility.TranslateEnglishToOrcish("jobs board", partOfSpeech: "noun", requiredTags: ["work"])[0].Translation, "unexpected jobs board translation");
    AssertEqual("ut-narg-bibi", OrcishTranslatorUtility.TranslateEnglishToOrcish("following postings", partOfSpeech: "noun", requiredTags: ["following"])[0].Translation, "unexpected following postings translation");
}

static void OrcishTranslatorSupportsMorganTavernObservationVocabulary()
{
    AssertEqual("Brand", OrcishTranslatorUtility.TranslateEnglishToOrcish("Brand", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "unexpected Brand translation");
    AssertEqual("varkin ik", OrcishTranslatorUtility.TranslateEnglishToOrcish("slides into", partOfSpeech: "verb", requiredTags: ["entering"])[0].Translation, "unexpected slides into translation");
    AssertEqual("Morganuk quum-biti agh rukh-banti", OrcishTranslatorUtility.TranslateEnglishToOrcish("Morgan's Morsels & Tankards", partOfSpeech: "noun", requiredTags: ["tavern"])[0].Translation, "unexpected tavern name translation");
    AssertEqual("quum-biti", OrcishTranslatorUtility.TranslateEnglishToOrcish("Morsels", partOfSpeech: "noun", requiredTags: ["food"])[0].Translation, "unexpected Morsels translation");
    AssertEqual("rukh-banti", OrcishTranslatorUtility.TranslateEnglishToOrcish("Tankards", partOfSpeech: "noun", requiredTags: ["vessel"])[0].Translation, "unexpected Tankards translation");
    AssertEqual("nul-thogin", OrcishTranslatorUtility.TranslateEnglishToOrcish("unconsciously", partOfSpeech: "adverb", requiredTags: ["unconscious"])[0].Translation, "unexpected unconsciously translation");
    AssertEqual("nu grotash ogh", OrcishTranslatorUtility.TranslateEnglishToOrcish("doesn't interfere with", partOfSpeech: "verb", requiredTags: ["negative"])[0].Translation, "unexpected doesn't interfere with translation");
    AssertEqual("lag ur", OrcishTranslatorUtility.TranslateEnglishToOrcish("access to", partOfSpeech: "preposition", requiredTags: ["access"])[0].Translation, "unexpected access to translation");
    AssertEqual("zol-bant", OrcishTranslatorUtility.TranslateEnglishToOrcish("pommel", partOfSpeech: "noun", requiredTags: ["handle"])[0].Translation, "unexpected pommel translation");
    AssertEqual("zol-gash", OrcishTranslatorUtility.TranslateEnglishToOrcish("sword", partOfSpeech: "noun", requiredTags: ["weapon"])[0].Translation, "unexpected sword translation");
    AssertEqual("oglur nak", OrcishTranslatorUtility.TranslateEnglishToOrcish("looks around", partOfSpeech: "verb", requiredTags: ["nearby"])[0].Translation, "unexpected looks around translation");
    AssertEqual("dravik-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("customers", partOfSpeech: "noun", requiredTags: ["buyer"])[0].Translation, "unexpected customers translation");
    AssertEqual("nak-dak-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("locals", partOfSpeech: "noun", requiredTags: ["local"])[0].Translation, "unexpected locals translation");
    AssertEqual("oglar-thogin", OrcishTranslatorUtility.TranslateEnglishToOrcish("sizing up", partOfSpeech: "verb", requiredTags: ["assessing"])[0].Translation, "unexpected sizing up translation");
    AssertEqual("mok ughat tukra", OrcishTranslatorUtility.TranslateEnglishToOrcish("what do we have", partOfSpeech: "verb", requiredTags: ["question"])[0].Translation, "unexpected what do we have translation");
    AssertEqual("darg-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("station", partOfSpeech: "noun", requiredTags: ["rank"])[0].Translation, "unexpected station translation");
    AssertEqual("dwarfuk hekash", OrcishTranslatorUtility.TranslateEnglishToOrcish("dwarven made", partOfSpeech: "verb", requiredTags: ["dwarven"])[0].Translation, "unexpected dwarven made translation");
    AssertEqual("thrak-thog-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("high quality", partOfSpeech: "noun", requiredTags: ["high"])[0].Translation, "unexpected high quality translation");
    AssertEqual("dug-agh-ash bantin", OrcishTranslatorUtility.TranslateEnglishToOrcish("triple braided", partOfSpeech: "adjective", requiredTags: ["braided"])[0].Translation, "unexpected triple braided translation");
    AssertEqual("dug-agh-ash", OrcishTranslatorUtility.TranslateEnglishToOrcish("triple", partOfSpeech: "adjective", requiredTags: ["multiplier"])[0].Translation, "unexpected triple translation");
    AssertEqual("darg-ti-mog", OrcishTranslatorUtility.TranslateEnglishToOrcish("noble", partOfSpeech: "noun", requiredTags: ["noble"])[0].Translation, "unexpected noble translation");
    AssertEqual("dargin mokh-zog", OrcishTranslatorUtility.TranslateEnglishToOrcish("ruling family", partOfSpeech: "noun", requiredTags: ["ruling"])[0].Translation, "unexpected ruling family translation");
    AssertEqual("mokh-zog", OrcishTranslatorUtility.TranslateEnglishToOrcish("family", partOfSpeech: "noun", requiredTags: ["root-repaired"])[0].Translation, "unexpected family translation");
    AssertEqual("dakku-dak mokh-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("home base", partOfSpeech: "noun", requiredTags: ["operations"])[0].Translation, "unexpected home base translation");
}

static void OrcishTranslatorTreatsDwarfOnlyAsDwarvenRace()
{
    var dwarf = OrcishTranslatorUtility.TranslateEnglishToOrcish("dwarf", partOfSpeech: "noun", requiredTags: ["dwarven-race"]);

    AssertEqual(1, dwarf.Count, "dwarf should have a Dwarven race translation");
    AssertEqual("dwarf", dwarf[0].Translation, "unexpected Dwarven race translation");
    AssertEqual("species", dwarf[0].GrammarClass ?? string.Empty, "dwarf should be classified as a species");
    AssertTrue(dwarf[0].Tags?.Contains("dwarven-race", StringComparer.OrdinalIgnoreCase) == true, "dwarf should carry the Dwarven race sense");
    AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("dwarf", partOfSpeech: "noun", requiredTags: ["diminutive"]).Count, "dwarf should not carry a diminutive sense");
    AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("dwarf", partOfSpeech: "noun", requiredTags: ["midget"]).Count, "dwarf should not carry a midget sense");
    AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("midget", partOfSpeech: "noun").Count, "Orcish should not have a midget term");
}

static void OrcishTranslatorSupportsMorganDiningAcknowledgementVocabulary()
{
    AssertEqual("grukhur", OrcishTranslatorUtility.TranslateEnglishToOrcish("grunts", partOfSpeech: "verb", requiredTags: ["rough"])[0].Translation, "unexpected grunts translation");
    AssertEqual("ut-oglarur mogumuk oglar-thog ur", OrcishTranslatorUtility.TranslateEnglishToOrcish("returns his attention to", partOfSpeech: "verb", requiredTags: ["attention"])[0].Translation, "unexpected returns his attention to translation");
    AssertEqual("darg-dravik", OrcishTranslatorUtility.TranslateEnglishToOrcish("proprietor", partOfSpeech: "noun", requiredTags: ["owner"])[0].Translation, "unexpected proprietor translation");
    AssertEqual("mokra-narg", OrcishTranslatorUtility.TranslateEnglishToOrcish("Well met", partOfSpeech: "interjection", requiredTags: ["greeting"])[0].Translation, "unexpected Well met translation");
    AssertEqual("rukh-quum", OrcishTranslatorUtility.TranslateEnglishToOrcish("ale", partOfSpeech: "noun", requiredTags: ["fermented"])[0].Translation, "unexpected ale translation");
    AssertEqual("rukh-quum", OrcishTranslatorUtility.TranslateEnglishToOrcish("soup", partOfSpeech: "noun", requiredTags: ["liquid"])[0].Translation, "unexpected soup translation");
    AssertEqual("hek-quum", OrcishTranslatorUtility.TranslateEnglishToOrcish("bread", partOfSpeech: "noun", requiredTags: ["baked"])[0].Translation, "unexpected bread translation");
    AssertEqual("mauk-drav", OrcishTranslatorUtility.TranslateEnglishToOrcish("please", partOfSpeech: "interjection", requiredTags: ["request"])[0].Translation, "unexpected please translation");
    AssertEqual("tukru-drav", OrcishTranslatorUtility.TranslateEnglishToOrcish("Obliged", partOfSpeech: "interjection", requiredTags: ["thanks"])[0].Translation, "unexpected Obliged translation");
    AssertEqual("nargu-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("acknowledge", partOfSpeech: "verb", requiredTags: ["acknowledgement"])[0].Translation, "unexpected acknowledge translation");
    AssertEqual("nargur-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("acknowledges", partOfSpeech: "verb", requiredTags: ["acknowledgement"])[0].Translation, "unexpected acknowledges translation");
    AssertEqual("nargash-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("acknowledged", partOfSpeech: "verb", requiredTags: ["acknowledgement"])[0].Translation, "unexpected acknowledged translation");
    AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("acknnowledges", partOfSpeech: "verb").Count, "misspelled acknnowledges should not be a lexicon entry");
    AssertEqual("quum-dak-mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("dining area", partOfSpeech: "noun", requiredTags: ["food"])[0].Translation, "unexpected dining area translation");
    AssertEqual("thrum-yank", OrcishTranslatorUtility.TranslateEnglishToOrcish("lanky", partOfSpeech: "adjective", requiredTags: ["thin"])[0].Translation, "unexpected lanky translation");
    AssertEqual("mur-oglur ik", OrcishTranslatorUtility.TranslateEnglishToOrcish("stares into", partOfSpeech: "verb", requiredTags: ["staring"])[0].Translation, "unexpected stares into translation");
    AssertEqual("rukh-turi", OrcishTranslatorUtility.TranslateEnglishToOrcish("flames", partOfSpeech: "noun", requiredTags: ["fire"])[0].Translation, "unexpected flames translation");
}

static void OrcishTranslatorSupportsKelpieRoadAndInnVocabulary()
{
    AssertEqual("Kelpie", OrcishTranslatorUtility.TranslateEnglishToOrcish("Kelpie", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "unexpected Kelpie translation");
    AssertEqual("Burz-gruul", OrcishTranslatorUtility.TranslateEnglishToOrcish("Darkwood Forest", partOfSpeech: "noun", requiredTags: ["forest"])[0].Translation, "unexpected Darkwood Forest translation");
    AssertEqual("Ravenuk Lag", OrcishTranslatorUtility.TranslateEnglishToOrcish("Raven’s Pass", partOfSpeech: "noun", requiredTags: ["pass"])[0].Translation, "unexpected Raven's Pass translation");
    AssertEqual("fletragi", OrcishTranslatorUtility.TranslateEnglishToOrcish("traveller", partOfSpeech: "noun", requiredTags: ["wayfarer"])[0].Translation, "unexpected traveller translation");
    AssertEqual("lagu ughatuk lag", OrcishTranslatorUtility.TranslateEnglishToOrcish("make their way", partOfSpeech: "verb", requiredTags: ["travel"])[0].Translation, "unexpected make their way translation");
    AssertEqual("vrul-lagi", OrcishTranslatorUtility.TranslateEnglishToOrcish("hedgerows", partOfSpeech: "noun", requiredTags: ["hedge"])[0].Translation, "unexpected hedgerows translation");
    AssertEqual("dug-lag-mokrui", OrcishTranslatorUtility.TranslateEnglishToOrcish("crossroads", partOfSpeech: "noun", requiredTags: ["junction"])[0].Translation, "unexpected crossroads translation");
    AssertEqual("mokh-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("common folk", partOfSpeech: "noun", requiredTags: ["folk"])[0].Translation, "unexpected common folk translation");
    AssertEqual("narg-bib-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("notice board", partOfSpeech: "noun", requiredTags: ["notice"])[0].Translation, "unexpected notice board translation");
    AssertEqual("nargash mogumuk oglar-krub", OrcishTranslatorUtility.TranslateEnglishToOrcish("caught his eye", partOfSpeech: "verb", requiredTags: ["attention"])[0].Translation, "unexpected caught his eye translation");
    AssertEqual("dak-hekmogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("settlers", partOfSpeech: "noun", requiredTags: ["settlement"])[0].Translation, "unexpected settlers translation");
    AssertEqual("rukh-dak darg-dravik", OrcishTranslatorUtility.TranslateEnglishToOrcish("innkeeper", partOfSpeech: "noun", requiredTags: ["tavern"])[0].Translation, "unexpected innkeeper translation");
    AssertEqual("ash zol-ti-drav-zol", OrcishTranslatorUtility.TranslateEnglishToOrcish("a single gold coin", partOfSpeech: "noun", requiredTags: ["gold"])[0].Translation, "unexpected single gold coin translation");
    AssertEqual("ashdak ur mogumuk mog-narg", OrcishTranslatorUtility.TranslateEnglishToOrcish("left to his name", partOfSpeech: "verb", requiredTags: ["remaining"])[0].Translation, "unexpected left to his name translation");
    AssertEqual("mogum taruk dakkin-naut", OrcishTranslatorUtility.TranslateEnglishToOrcish("he’d be sleeping", partOfSpeech: "verb", requiredTags: ["sleeping"])[0].Translation, "unexpected he'd be sleeping translation");
    AssertEqual("nul-togruk", OrcishTranslatorUtility.TranslateEnglishToOrcish("toothless", partOfSpeech: "adjective", requiredTags: ["toothless"])[0].Translation, "unexpected toothless translation");
}

static void OrcishTranslatorSupportsKelpieFellowshipPrayerVocabulary()
{
    AssertEqual("dakku-bant-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("bench", partOfSpeech: "noun", requiredTags: ["seat"])[0].Translation, "unexpected bench translation");
    AssertEqual("hrowku", OrcishTranslatorUtility.TranslateEnglishToOrcish("carry", partOfSpeech: "verb", requiredTags: ["carrying"])[0].Translation, "unexpected carry translation");
    AssertEqual("hrowkur", OrcishTranslatorUtility.TranslateEnglishToOrcish("carries", partOfSpeech: "verb", requiredTags: ["carrying"])[0].Translation, "unexpected carries translation");
    AssertEqual("hrowkash", OrcishTranslatorUtility.TranslateEnglishToOrcish("carried", partOfSpeech: "verb", requiredTags: ["carrying"])[0].Translation, "unexpected carried translation");
    AssertEqual("hrowkin", OrcishTranslatorUtility.TranslateEnglishToOrcish("carrying", partOfSpeech: "verb", requiredTags: ["carrying"])[0].Translation, "unexpected carrying translation");
    AssertEqual("gruul-hek-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("lumberjacks", partOfSpeech: "noun", requiredTags: ["wood"])[0].Translation, "unexpected lumberjacks translation");
    AssertEqual("quum-hekmoguk nurik-mog", OrcishTranslatorUtility.TranslateEnglishToOrcish("farmer’s daughter", partOfSpeech: "noun", requiredTags: ["family"])[0].Translation, "unexpected farmer's daughter translation");
    AssertEqual("thrum-narg mograth-narg", OrcishTranslatorUtility.TranslateEnglishToOrcish("quiet prayer", partOfSpeech: "noun", requiredTags: ["prayer"])[0].Translation, "unexpected quiet prayer translation");
    AssertEqual("Demetra", OrcishTranslatorUtility.TranslateEnglishToOrcish("Demetra", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "unexpected Demetra translation");
    AssertEqual("mokru-mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("fellowship", partOfSpeech: "noun", requiredTags: ["companionship"])[0].Translation, "unexpected fellowship translation");
    AssertEqual("varg-thog ur gor-dargu", OrcishTranslatorUtility.TranslateEnglishToOrcish("willing to stand", partOfSpeech: "verb", requiredTags: ["willing"])[0].Translation, "unexpected willing to stand translation");
    AssertEqual("zol-gash-darg-mog", OrcishTranslatorUtility.TranslateEnglishToOrcish("knight", partOfSpeech: "noun", requiredTags: ["noble"])[0].Translation, "unexpected knight translation");
    AssertEqual("gash-hrog", OrcishTranslatorUtility.TranslateEnglishToOrcish("warhorse", partOfSpeech: "noun", requiredTags: ["mount"])[0].Translation, "unexpected warhorse translation");
    AssertEqual("rukh-hekin", OrcishTranslatorUtility.TranslateEnglishToOrcish("kindling", partOfSpeech: "verb", requiredTags: ["ignite"])[0].Translation, "unexpected kindling translation");
}

static void OrcishTranslatorSupportsHeraldicStrangerEquipmentVocabulary()
{
    AssertEqual("rug-mograth-bant", OrcishTranslatorUtility.TranslateEnglishToOrcish("red cross", partOfSpeech: "noun", requiredTags: ["heraldic"])[0].Translation, "unexpected red cross translation");
    AssertEqual("khal-bib", OrcishTranslatorUtility.TranslateEnglishToOrcish("tabard", partOfSpeech: "noun", requiredTags: ["heraldic"])[0].Translation, "unexpected tabard translation");
    AssertEqual("zol-bant-khal", OrcishTranslatorUtility.TranslateEnglishToOrcish("chainmail hauberk", partOfSpeech: "noun", requiredTags: ["chainmail"])[0].Translation, "unexpected chainmail hauberk translation");
    AssertEqual("zol-mog-ti-khal", OrcishTranslatorUtility.TranslateEnglishToOrcish("kettle hat", partOfSpeech: "noun", requiredTags: ["helmet"])[0].Translation, "unexpected kettle hat translation");
    AssertEqual("zornash zol-bant-dok", OrcishTranslatorUtility.TranslateEnglishToOrcish("hung haft-down", partOfSpeech: "verb", requiredTags: ["haft-down"])[0].Translation, "unexpected hung haft-down translation");
    AssertEqual("gor-zol-murk", OrcishTranslatorUtility.TranslateEnglishToOrcish("boss", partOfSpeech: "noun", requiredTags: ["shield"])[0].Translation, "unexpected shield boss translation");
    AssertEqual("dravik-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("patrons", partOfSpeech: "noun", requiredTags: ["tavern"])[0].Translation, "unexpected tavern patrons translation");
    AssertEqual("morz-krub-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("sore thumb", partOfSpeech: "noun", requiredTags: ["thumb"])[0].Translation, "unexpected sore thumb translation");
    AssertEqual("narg-gash", OrcishTranslatorUtility.TranslateEnglishToOrcish("target", partOfSpeech: "noun", requiredTags: ["target"])[0].Translation, "unexpected target translation");
    AssertEqual("rug-mograth-bant narg-var", OrcishTranslatorUtility.TranslateEnglishToOrcish("red cross design", partOfSpeech: "noun", requiredTags: ["design"])[0].Translation, "unexpected red cross design translation");
    AssertEqual("ut-dakur ur", OrcishTranslatorUtility.TranslateEnglishToOrcish("then to", partOfSpeech: "adverb", requiredTags: ["then"])[0].Translation, "unexpected then to translation");
}

static void OrcishTranslatorSupportsHistoricalWikiFodderVocabulary()
{
    AssertEqual("murk-dak-mur-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("intercontinental", partOfSpeech: "adjective")[0].Translation, "unexpected intercontinental translation");
    AssertEqual("morz-vrak-rukh", OrcishTranslatorUtility.TranslateEnglishToOrcish("sickness", partOfSpeech: "noun")[0].Translation, "unexpected sickness translation");
    AssertEqual("brak-grod-nu-hek-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("violence", partOfSpeech: "noun")[0].Translation, "unexpected violence translation");
    AssertEqual("mokh-ash-flit", OrcishTranslatorUtility.TranslateEnglishToOrcish("society", partOfSpeech: "noun")[0].Translation, "unexpected society translation");
    AssertEqual("nul-darg-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("anarchy", partOfSpeech: "noun")[0].Translation, "unexpected anarchy translation");
    AssertEqual("margith", OrcishTranslatorUtility.TranslateEnglishToOrcish("humanity", partOfSpeech: "noun")[0].Translation, "unexpected humanity translation");
    AssertEqual("dak-mur-bant-murkuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("planet", partOfSpeech: "noun")[0].Translation, "unexpected planet translation");
    AssertEqual("grak-laguk", OrcishTranslatorUtility.TranslateEnglishToOrcish("conventional", partOfSpeech: "adjective")[0].Translation, "unexpected conventional translation");
    AssertEqual("gash-dakur-hek-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("warfare", partOfSpeech: "noun")[0].Translation, "unexpected warfare translation");
    AssertEqual("dakur-thogu-mog", OrcishTranslatorUtility.TranslateEnglishToOrcish("survivor", partOfSpeech: "noun")[0].Translation, "unexpected survivor translation");
    AssertEqual("brak-thog-ti-morz-dakur", OrcishTranslatorUtility.TranslateEnglishToOrcish("holocaust", partOfSpeech: "noun")[0].Translation, "unexpected holocaust translation");
    AssertEqual("dak-mogi-zorn", OrcishTranslatorUtility.TranslateEnglishToOrcish("population", partOfSpeech: "noun")[0].Translation, "unexpected population translation");
    AssertEqual("dak-mur-ti-mur-kaag-tuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("global", partOfSpeech: "adjective")[0].Translation, "unexpected global translation");
    AssertEqual("dok-ka-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("relation", partOfSpeech: "noun")[0].Translation, "unexpected relation translation");
    AssertEqual("heku-yankuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("harden", partOfSpeech: "verb")[0].Translation, "unexpected harden translation");
    AssertEqual("dak-burzuk-gor-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("bunker", partOfSpeech: "noun")[0].Translation, "unexpected bunker translation");
    AssertEqual("darg-gash-mogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("leadership", partOfSpeech: "noun")[0].Translation, "unexpected leadership translation");
    AssertEqual("drav-zol-mokhuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("affluent", partOfSpeech: "adjective")[0].Translation, "unexpected affluent translation");
    AssertEqual("darg-thog-laguk", OrcishTranslatorUtility.TranslateEnglishToOrcish("influential", partOfSpeech: "adjective")[0].Translation, "unexpected influential translation");
    AssertEqual("thrum-zorn-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("decline", partOfSpeech: "noun")[0].Translation, "unexpected decline translation");
    AssertEqual("gakh-dakur-tiwi", OrcishTranslatorUtility.TranslateEnglishToOrcish("decade", partOfSpeech: "noun")[0].Translation, "unexpected decade translation");
    AssertEqual("murk-dak-muri", OrcishTranslatorUtility.TranslateEnglishToOrcish("international", partOfSpeech: "adjective")[0].Translation, "unexpected international translation");
    AssertEqual("grot-lag-lagu-zorn", OrcishTranslatorUtility.TranslateEnglishToOrcish("crawl", partOfSpeech: "verb")[0].Translation, "unexpected crawl translation");
    AssertEqual("disasdok-dok-lag-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("mothball", partOfSpeech: "verb")[0].Translation, "unexpected mothball translation");
    AssertEqual("morz-dok-hekinuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("medical", partOfSpeech: "adjective")[0].Translation, "unexpected medical translation");
    AssertEqual("gor-thog-ti-drav-thruk", OrcishTranslatorUtility.TranslateEnglishToOrcish("care", partOfSpeech: "noun")[0].Translation, "unexpected care translation");
    AssertEqual("lagu-zorn-dok", OrcishTranslatorUtility.TranslateEnglishToOrcish("revert", partOfSpeech: "verb")[0].Translation, "unexpected revert translation");
    AssertEqual("nu-zorn", OrcishTranslatorUtility.TranslateEnglishToOrcish("shortage", partOfSpeech: "noun")[0].Translation, "unexpected shortage translation");
    AssertEqual("burz-hek-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("infrastructure", partOfSpeech: "noun")[0].Translation, "unexpected infrastructure translation");
    AssertEqual("rukh-gurmog", OrcishTranslatorUtility.TranslateEnglishToOrcish("drug", partOfSpeech: "noun")[0].Translation, "unexpected drug translation");
    AssertEqual("rukh-gurmog", OrcishTranslatorUtility.TranslateEnglishToOrcish("pharmaceutical", partOfSpeech: "noun")[0].Translation, "unexpected pharmaceutical translation");
    AssertEqual("ash-ash-dakur-ti", OrcishTranslatorUtility.TranslateEnglishToOrcish("annual", partOfSpeech: "adjective")[0].Translation, "unexpected annual translation");
    AssertEqual("mograth-hek", OrcishTranslatorUtility.TranslateEnglishToOrcish("observance", partOfSpeech: "noun")[0].Translation, "unexpected observance translation");
    AssertEqual("ukin", OrcishTranslatorUtility.TranslateEnglishToOrcish("voluntary", partOfSpeech: "adjective")[0].Translation, "unexpected voluntary translation");
    AssertEqual("dok-lag-ti-dakku-dak", OrcishTranslatorUtility.TranslateEnglishToOrcish("exile", partOfSpeech: "noun")[0].Translation, "unexpected exile translation");
    AssertEqual("thrum-zorn-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("decadence", partOfSpeech: "noun")[0].Translation, "unexpected decadence translation");
    AssertEqual("nargu-grod-zog-dorn", OrcishTranslatorUtility.TranslateEnglishToOrcish("praise", partOfSpeech: "verb")[0].Translation, "unexpected praise translation");
    AssertEqual("mauk-hek", OrcishTranslatorUtility.TranslateEnglishToOrcish("skill", partOfSpeech: "noun")[0].Translation, "unexpected skill translation");
    AssertEqual("brak-thog-morz-dok", OrcishTranslatorUtility.TranslateEnglishToOrcish("vengeance", partOfSpeech: "noun")[0].Translation, "unexpected vengeance translation");
    AssertEqual("nargu-morz-thog-nu", OrcishTranslatorUtility.TranslateEnglishToOrcish("insult", partOfSpeech: "verb")[0].Translation, "unexpected insult translation");
    AssertEqual("nargash-morz-thog-nu", OrcishTranslatorUtility.TranslateEnglishToOrcish("insulted", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "past"])[0].Translation, "unexpected rule-derived insulted translation");

    var diseaseMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("morz-vrak-rukh", partOfSpeech: "noun");
    AssertTrue(diseaseMeanings.Any(static candidate => candidate.Translation == "sickness"), "reverse disease form should include sickness");
    var communityMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("mokh-ash-flit", partOfSpeech: "noun");
    AssertTrue(communityMeanings.Any(static candidate => candidate.Translation == "community"), "reverse community form should retain community");
    AssertTrue(communityMeanings.Any(static candidate => candidate.Translation == "society"), "reverse community form should include society");
    var chaosMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("nul-darg-thog", partOfSpeech: "noun");
    AssertTrue(chaosMeanings.Any(static candidate => candidate.Translation == "chaos"), "reverse chaos form should retain chaos");
    AssertTrue(chaosMeanings.Any(static candidate => candidate.Translation == "anarchy"), "reverse chaos form should include anarchy");
    var humanityMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("margith", partOfSpeech: "noun");
    AssertTrue(humanityMeanings.Any(static candidate => candidate.Translation == "humans"), "reverse humanity form should retain humans");
    AssertTrue(humanityMeanings.Any(static candidate => candidate.Translation == "humanity"), "reverse humanity form should include humanity");
    var leadershipMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("darg-gash-mogi", partOfSpeech: "noun");
    AssertTrue(leadershipMeanings.Any(static candidate => candidate.Translation == "leaders"), "reverse leadership form should retain leaders");
    AssertTrue(leadershipMeanings.Any(static candidate => candidate.Translation == "leadership"), "reverse leadership form should include leadership");
    var drugMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("rukh-gurmog", partOfSpeech: "noun");
    AssertTrue(drugMeanings.Any(static candidate => candidate.Translation == "drug"), "reverse potion form should include drug");
    AssertTrue(drugMeanings.Any(static candidate => candidate.Translation == "pharmaceutical"), "reverse potion form should include pharmaceutical");
    var declineMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("thrum-zorn-thog", partOfSpeech: "noun");
    AssertTrue(declineMeanings.Any(static candidate => candidate.Translation == "decline"), "reverse decline form should retain decline");
    AssertTrue(declineMeanings.Any(static candidate => candidate.Translation == "decadence"), "reverse decline form should include decadence");
    var skillMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("mauk-hek", partOfSpeech: "noun");
    AssertTrue(skillMeanings.Any(static candidate => candidate.Translation == "ability"), "reverse skill form should retain ability");
    AssertTrue(skillMeanings.Any(static candidate => candidate.Translation == "skill"), "reverse skill form should include skill");
    AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("radiation").Count, "dropped radiation candidate should remain absent");
    AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("nuclear").Count, "dropped nuclear candidate should remain absent");
    AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("science").Count, "dropped science candidate should remain absent");
    AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish("program").Count, "dropped program candidate should remain absent");
}

static void OrcishTranslatorSupportsTwelvePageWikiScrapeVocabulary()
{
    var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["veteran"] = "drath-gash",
        ["discipline"] = "hekin-gash-darg-lag",
        ["reliability"] = "gor-laguk-thog",
        ["incursion"] = "gash-narg-ik-lagu-dak",
        ["schism"] = "mokh-zorn-dug",
        ["captivity"] = "darg-varkum-thog",
        ["reverence"] = "grak-tur-ti-mograth-thog",
        ["pantheon"] = "mograth-darg-mogi-mokh-zorn",
        ["convert"] = "varu-mograth-thog",
        ["layover"] = "nul-vrak-dakur-lagu-dok",
        ["militia"] = "nak-dakuk-gash-darg-morz",
        ["craftsman"] = "mauk-hek-heku-mog",
        ["restriction"] = "gor-dak-dargu-mokh-mokh",
        ["felony"] = "darg-bib-grum-brak-morz-bibuk",
        ["judge"] = "darg-bib-grum-brak-mog",
        ["censorship"] = "dargu-mokh-mokh-narg-bib",
        ["taxation"] = "darg-thog-quum-drav",
        ["imprisonment"] = "darg-varkum-thog",
        ["privilege"] = "var-tiuk-grak-nak-ti",
        ["decree"] = "darg-narg-darg-bib-grum-brak",
        ["selflessness"] = "drakuin-mur-kaag-tuk",
        ["phenomenon"] = "var-tiuk-dakur-hek-ti",
        ["madness"] = "thog-vrak-nul-darg-thog",
        ["aberrant"] = "morz-bibuk-var-tiuk",
        ["resistant"] = "goru-varkuk",
        ["emanation"] = "gurmog-thog-dok-darg-krag",
        ["industrial"] = "hek-zol-hek-grum-morzuk",
        ["machine"] = "hek-grum-morz-hek-zol",
        ["steam"] = "rukh-ash-dak-rukh-ti-hush",
        ["dissipation"] = "thrum-zorn-thog"
    };

    foreach (var pair in expected)
    {
        var results = OrcishTranslatorUtility.TranslateEnglishToOrcish(pair.Key);
        AssertTrue(results.Any(candidate => candidate.Translation == pair.Value), $"unexpected translation for {pair.Key}");
    }

    var captivityMeanings = OrcishTranslatorUtility.TranslateOrcishToEnglish("darg-varkum-thog", partOfSpeech: "noun");
    AssertTrue(captivityMeanings.Any(static candidate => candidate.Translation == "captivity"), "reverse captivity form should include captivity");
    AssertTrue(captivityMeanings.Any(static candidate => candidate.Translation == "imprisonment"), "reverse captivity form should include imprisonment");
}

static void OrcishTranslatorSupportsRecoveredScrollVocabulary()
{
    var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["dedicate"] = "draku-mur-kaag-tuk",
        ["curb"] = "dargu-mokh-mokh-gor-dak",
        ["spread"] = "lagu-zorn-mur-kaag-tuk",
        ["spore"] = "gruul-rukh-bit",
        ["epicenter"] = "murk-dak-dakur-hek-ti",
        ["pay"] = "quum-dravu",
        ["weak"] = "nu-brak-burz-yankuk",
        ["third"] = "dug-agh-ash-darg-lag",
        ["fourth"] = "dug-dug-darg-lag",
        ["break"] = "braku",
        ["leg"] = "vrak-lag",
        ["possible"] = "mauk-grrt-ashuk",
        ["seize"] = "dravku-krag-flit-darg-gash",
        ["track"] = "lag-narg-bib",
        ["source"] = "dok-darg-krag-dak",
        ["labor"] = "hek-grum-morz",
        ["prefer"] = "vargu-dak-zog-ti",
        ["waste"] = "flitu-dok-lag-ti",
        ["desire"] = "thruk-thog-var",
        ["reader"] = "bib-oglaru-mog",
        ["strange"] = "var-tiuk-gi",
        ["question"] = "narg-bib-thog-var",
        ["lie"] = "nargu-morz-bibuk",
        ["remove"] = "dravku-krag-flit-dok-lag-ti",
        ["deliver"] = "dravku-ik-draku",
        ["instruction"] = "darg-narg-narg-bib",
        ["follow"] = "lagu-dok-dak-tuk",
        ["watcher-mark"] = "thrak-narg-bib",
        ["thorn"] = "grodu-vrak-zorn-bit-dak",
        ["hill"] = "thrum-brak-grrt-ti-dak",
        ["split"] = "heku-dug-dak",
        ["root"] = "grodu-vrak-mokh-dak",
        ["collect"] = "mokhu-mur-kaag-tuk",
        ["hunger"] = "quum-thruk",
        ["crude"] = "nu-brak-burz-mauk-hek",
        ["depict"] = "heku-narg-bib",
        ["jagged"] = "brakuk-zol-nak",
        ["sinkhole"] = "defuh-burz-dak-ti",
        ["maw"] = "narg-ik-ti",
        ["northward"] = "ur-doku-goth-surg-lag",
        ["rough"] = "brakuk-dak-thrum-ti",
        ["sketch"] = "nu-brak-burz-mauk-hek-narg-bib",
        ["route"] = "lag",
        ["label"] = "mog-narg-narg-bib",
        ["precise"] = "grak-nak-ti-zorn",
        ["suggest"] = "nargu-thog-var",
        ["rendezvous"] = "mokru-dak"
    };

    foreach (var pair in expected)
    {
        var results = OrcishTranslatorUtility.TranslateEnglishToOrcish(pair.Key);
        AssertTrue(results.Any(candidate => candidate.Translation == pair.Value), $"unexpected translation for {pair.Key}");
    }

    var sharedForms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["braku"] = "break",
        ["vrak-lag"] = "leg",
        ["hek-grum-morz"] = "labor",
        ["lag"] = "route",
        ["mokru-dak"] = "rendezvous"
    };

    foreach (var pair in sharedForms)
    {
        var meanings = OrcishTranslatorUtility.TranslateOrcishToEnglish(pair.Key);
        AssertTrue(meanings.Any(candidate => candidate.Translation == pair.Value), $"reverse {pair.Key} form should include {pair.Value}");
    }
}

static void OrcishTranslatorPropagatesRepairedRootsThroughDerivedFamilies()
{
    AssertEqual("dakur-hrowkuk gash-lag", OrcishTranslatorUtility.TranslateEnglishToOrcish("day's march", partOfSpeech: "noun", requiredTags: ["root-repaired"])[0].Translation, "unexpected repaired day's march translation");
    AssertEqual("ut-dravku", OrcishTranslatorUtility.TranslateEnglishToOrcish("retake", partOfSpeech: "verb", requiredTags: ["reclaim", "root-repaired"])[0].Translation, "unexpected repaired retake translation");
    AssertEqual("mokrai", OrcishTranslatorUtility.TranslateEnglishToOrcish("allies", partOfSpeech: "noun", requiredTags: ["base-ally", "plural", "root-repaired"])[0].Translation, "unexpected repaired allies translation");
    AssertEqual("mokh-zogi", OrcishTranslatorUtility.TranslateEnglishToOrcish("families", partOfSpeech: "noun", requiredTags: ["base-family", "plural", "root-repaired"])[0].Translation, "unexpected repaired families translation");
    AssertEqual("noglar-grak", OrcishTranslatorUtility.TranslateEnglishToOrcish("secretly", partOfSpeech: "adverb", requiredTags: ["base-secret", "root-repaired"])[0].Translation, "unexpected repaired secretly translation");
    AssertEqual("darg-dakuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("station's", partOfSpeech: "noun", requiredTags: ["base-station", "possessive", "root-repaired"])[0].Translation, "unexpected repaired station possessive translation");
    AssertEqual("darg-daki", OrcishTranslatorUtility.TranslateEnglishToOrcish("stations", partOfSpeech: "noun", requiredTags: ["base-station", "s-form", "root-repaired"])[0].Translation, "unexpected repaired stations translation");
    AssertEqual("kelnib-in", OrcishTranslatorUtility.TranslateEnglishToOrcish("paling", partOfSpeech: "verb", requiredTags: ["base-pale", "progressive", "root-repaired"])[0].Translation, "unexpected repaired paling translation");
    AssertEqual("darg-dakash", OrcishTranslatorUtility.TranslateEnglishToOrcish("stationed", partOfSpeech: "verb", requiredTags: ["base-station", "past", "root-repaired"])[0].Translation, "unexpected repaired stationed translation");
}

static void OrcishTranslatorAuditsReviewPromotedDerivedFamilies()
{
    AssertEqual("vargur", OrcishTranslatorUtility.TranslateEnglishToOrcish("lets", partOfSpeech: "verb", requiredTags: ["base-let", "derived-audited"])[0].Translation, "unexpected audited lets translation");
    AssertEqual("margiuk-grod-krag", OrcishTranslatorUtility.TranslateEnglishToOrcish("man’s", partOfSpeech: "noun", requiredTags: ["base-man", "derived-audited"])[0].Translation, "unexpected audited man's translation");
    AssertEqual("oglurin", OrcishTranslatorUtility.TranslateEnglishToOrcish("seeing", partOfSpeech: "verb", requiredTags: ["base-see", "derived-audited"])[0].Translation, "unexpected audited seeing translation");
    AssertEqual("gorin", OrcishTranslatorUtility.TranslateEnglishToOrcish("watching", partOfSpeech: "verb", requiredTags: ["base-watch", "derived-audited"])[0].Translation, "unexpected audited watching translation");
    AssertEqual("hrowk-khal-thrumuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("bag's", partOfSpeech: "noun", requiredTags: ["base-bag", "derived-audited"])[0].Translation, "unexpected audited bag possessive translation");
    AssertEqual("oglar-krubuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("eye's", partOfSpeech: "noun", requiredTags: ["base-eye", "derived-audited"])[0].Translation, "unexpected audited eye possessive translation");
    AssertEqual("dok-ka-burz-bantuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("tether's", partOfSpeech: "noun", requiredTags: ["base-tether", "derived-audited"])[0].Translation, "unexpected audited tether possessive translation");
    AssertEqual("dug-agh-ash-ash-dokuuk", OrcishTranslatorUtility.TranslateEnglishToOrcish("trio's", partOfSpeech: "noun", requiredTags: ["base-trio", "derived-audited"])[0].Translation, "unexpected audited trio possessive translation");
    AssertEqual("dornukikash", OrcishTranslatorUtility.TranslateEnglishToOrcish("hushed", partOfSpeech: "verb", requiredTags: ["base-hush", "derived-audited"])[0].Translation, "unexpected audited hushed translation");
}

static void OrcishTranslatorShortensMechanicallyLengthenedForms()
{
    AssertEqual("dak-mokh", OrcishTranslatorUtility.TranslateEnglishToOrcish("area", partOfSpeech: "noun", requiredTags: ["area", "shortened"])[0].Translation, "unexpected shortened area translation");
    AssertEqual("dak-mokhi", OrcishTranslatorUtility.TranslateEnglishToOrcish("areas", partOfSpeech: "noun", requiredTags: ["base-area", "shortened"])[0].Translation, "unexpected shortened areas translation");
    AssertEqual("kaag-thogash", OrcishTranslatorUtility.TranslateEnglishToOrcish("smelled", partOfSpeech: "verb", requiredTags: ["base-smell", "shortened"])[0].Translation, "unexpected shortened smelled translation");
    AssertEqual("oglar-gashash", OrcishTranslatorUtility.TranslateEnglishToOrcish("aimed", partOfSpeech: "verb", requiredTags: ["base-aim", "shortened"])[0].Translation, "unexpected shortened aimed translation");
    AssertEqual("oglar-gashin", OrcishTranslatorUtility.TranslateEnglishToOrcish("aiming", partOfSpeech: "verb", requiredTags: ["base-aim", "shortened"])[0].Translation, "unexpected shortened aiming translation");
    AssertEqual("narg-bib-zol", OrcishTranslatorUtility.TranslateEnglishToOrcish("stylus", partOfSpeech: "noun", requiredTags: ["writing", "shortened"])[0].Translation, "unexpected shortened stylus translation");

    var huntResults = OrcishTranslatorUtility.TranslateEnglishToOrcish("hunt", partOfSpeech: "verb", requiredTags: ["hunt"]);
    AssertEqual(1, huntResults.Count, "hunt should not retain a generated duplicate translation");
    AssertEqual("gash-lag-mokh", huntResults[0].Translation, "unexpected hunt translation");
}

static void OrcishTranslatorDerivesPredictableMorphologyByRule()
{
    AssertEqual("dak-mokhi", OrcishTranslatorUtility.TranslateEnglishToOrcish("areas", partOfSpeech: "noun", requiredTags: ["derived-by-rule", "plural"])[0].Translation, "areas should be generated from the area root");
    AssertEqual("hrowkur", OrcishTranslatorUtility.TranslateEnglishToOrcish("carries", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "present"])[0].Translation, "carries should be generated from the carry root");
    AssertEqual("hrowkash", OrcishTranslatorUtility.TranslateEnglishToOrcish("carried", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "past"])[0].Translation, "carried should be generated from the carry root");
    AssertEqual("hrowkin", OrcishTranslatorUtility.TranslateEnglishToOrcish("carrying", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "progressive"])[0].Translation, "carrying should be generated from the carry root");
    AssertEqual("nargur-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("acknowledges", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "present"])[0].Translation, "acknowledges should inflect the first root in its compound");
    AssertEqual("nargash-thog", OrcishTranslatorUtility.TranslateEnglishToOrcish("acknowledged", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "past"])[0].Translation, "acknowledged should inflect the first root in its compound");
    AssertEqual("oglar-gashash", OrcishTranslatorUtility.TranslateEnglishToOrcish("aimed", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "past"])[0].Translation, "aimed should be generated from the aim verb root");
    AssertEqual("oglar-gashin", OrcishTranslatorUtility.TranslateEnglishToOrcish("aiming", partOfSpeech: "verb", requiredTags: ["derived-by-rule", "progressive"])[0].Translation, "aiming should be generated from the aim verb root");
}

static void OrcishTranslatorCullsLowValueExonymPassThroughs()
{
    foreach (var culled in new[] { "abby", "archontos", "lexie", "rosk", "sulla", "vul's" })
    {
        AssertEqual(0, OrcishTranslatorUtility.TranslateEnglishToOrcish(culled).Count, $"low-value exonym '{culled}' should be culled");
    }

    AssertEqual("Kirkilston", OrcishTranslatorUtility.TranslateEnglishToOrcish("Kirkilston", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "intentional Kirkilston exonym should remain");
    AssertEqual("Xavin", OrcishTranslatorUtility.TranslateEnglishToOrcish("Xavin", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "intentional Xavin exonym should remain");
    AssertEqual("Kelpie", OrcishTranslatorUtility.TranslateEnglishToOrcish("Kelpie", partOfSpeech: "noun", requiredTags: ["proper-noun"])[0].Translation, "intentional Kelpie exonym should remain");
}

static void OrcishTranslatorEnforcesLexiconQualityInvariants()
{
    var entries = OrcishTranslatorUtility.GetLexiconEntries();
    var duplicateSignatures = entries
        .GroupBy(
            static entry => $"{entry.English}\u001F{entry.Orcish}\u001F{entry.PartOfSpeech}",
            StringComparer.OrdinalIgnoreCase)
        .Where(static group => group.Count() > 1)
        .Select(static group => FormatLexiconEntry(group.First()))
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    AssertEqual(string.Empty, string.Join("; ", duplicateSignatures), "lexicon should not contain exact duplicate English/Orcish/part-of-speech entries");

    var singleWordEnglishWithOrcishSpaces = entries
        .Where(static entry => IsSingleWord(entry.English))
        .Where(static entry => entry.Orcish.Contains(' '))
        .Where(static entry => !HasAnyTag(entry, "fixed-phrase", "proper-noun", "exonym"))
        .Select(static entry => FormatLexiconEntry(entry))
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    AssertEqual(string.Empty, string.Join("; ", singleWordEnglishWithOrcishSpaces), "single-word English entries should not translate to Orcish phrases without an explicit phrase/name tag");

    var entriesWithDigits = entries
        .Where(static entry => entry.Orcish.Any(char.IsDigit))
        .Select(static entry => FormatLexiconEntry(entry))
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    AssertEqual(string.Empty, string.Join("; ", entriesWithDigits), "Orcish translations should not contain digits");

    var placeholderSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "bbc",
        "dbe",
        "dca",
        "dcbi",
        "fcb",
        "fcd",
        "fce"
    };
    var entriesWithPlaceholderSegments = entries
        .Where(entry => SplitOrcishSegments(entry.Orcish).Any(placeholderSegments.Contains))
        .Select(static entry => FormatLexiconEntry(entry))
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    AssertEqual(string.Empty, string.Join("; ", entriesWithPlaceholderSegments), "Orcish translations should not contain placeholder-looking generated segments");

    var unapprovedPassThroughs = entries
        .Where(static entry => string.Equals(entry.English, entry.Orcish, StringComparison.OrdinalIgnoreCase))
        .Where(static entry => !HasAnyTag(entry, "proper-noun", "exonym", "game-term"))
        .Select(static entry => FormatLexiconEntry(entry))
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    AssertEqual(string.Empty, string.Join("; ", unapprovedPassThroughs), "direct pass-through translations should be approved as proper nouns, exonyms, or game terms");

    var generatedPassThroughs = entries
        .Where(static entry => string.Equals(entry.English, entry.Orcish, StringComparison.OrdinalIgnoreCase))
        .Where(static entry => HasAnyTag(entry, "generated"))
        .Where(static entry => !HasAnyTag(entry, "keep-exonym", "keep-lore-term", "orc-origin"))
        .Select(static entry => FormatLexiconEntry(entry))
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    AssertEqual(string.Empty, string.Join("; ", generatedPassThroughs), "generated direct pass-through translations should be explicitly kept or removed");
}

static void OrcishTranslatorReviewsProposedLexiconAdditions()
{
    var existingEntries = new OrcishLexiconEntry[]
    {
        new("hello", "zug", PartOfSpeech: "interjection"),
        new("carry", "hrowku", PartOfSpeech: "verb", Tags: ["infinitive"]),
        new("stone", "krag", PartOfSpeech: "noun")
    };

    var reverseCollisionIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
        new OrcishLexiconEntry("greeting", "zug", PartOfSpeech: "noun"),
        existingEntries);
    AssertTrue(
        reverseCollisionIssues.Any(static issue => issue.Code == "orcish-form-collision"),
        "a proposed Orcish form already used by another English term should be rejected");

    var closeFormIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
        new OrcishLexiconEntry("shout", "zugg", PartOfSpeech: "verb"),
        existingEntries);
    AssertTrue(
        closeFormIssues.Any(static issue => issue.Code == "close-form-conflict"),
        "an easily confused Orcish form should require explicit review");

    var wrongRootIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
        new OrcishLexiconEntry(
            "carried",
            "murkash",
            PartOfSpeech: "verb",
            Tags: ["root-derived", "base-carry", "past"]),
        existingEntries);
    AssertTrue(
        wrongRootIssues.Any(static issue => issue.Code == "root-morphology-mismatch"),
        "a derived form that abandons its declared root should be rejected");

    var faithfulRootIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
        new OrcishLexiconEntry(
            "carried",
            "hrowkash",
            PartOfSpeech: "verb",
            Tags: ["root-derived", "base-carry", "past"]),
        existingEntries);
    AssertEqual(0, faithfulRootIssues.Count, "a faithful rule-derived root form should pass review");

    var compoundIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
        new OrcishLexiconEntry("stone road", "krag-lag", PartOfSpeech: "noun", Tags: ["compound"]),
        existingEntries);
    AssertTrue(
        compoundIssues.Any(static issue => issue.Code == "compound-root-review-required"),
        "a compound should identify a source root or record explicit manual review");

    var reviewedSharedFormIssues = OrcishLexiconReviewUtility.ReviewProposedEntry(
        new OrcishLexiconEntry("greeting", "zug", PartOfSpeech: "noun", Tags: ["shared-form"]),
        existingEntries);
    AssertFalse(
        reviewedSharedFormIssues.Any(static issue => issue.Code == "orcish-form-collision"),
        "an intentional shared reverse form should be accepted only with an explicit review tag");

    AssertThrows<InvalidOperationException>(() =>
        OrcishLexiconReviewUtility.EnsureCanAdd(
            new OrcishLexiconEntry("greeting", "zug", PartOfSpeech: "noun"),
            existingEntries));
}

static bool IsSingleWord(string value)
{
    return !value.Any(char.IsWhiteSpace);
}

static bool HasAnyTag(OrcishLexiconEntry entry, params string[] tags)
{
    return tags.Any(tag =>
        (entry.Tags ?? Array.Empty<string>())
        .Any(entryTag => string.Equals(entryTag, tag, StringComparison.OrdinalIgnoreCase)));
}

static IEnumerable<string> SplitOrcishSegments(string orcish)
{
    return orcish
        .Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

static string FormatLexiconEntry(OrcishLexiconEntry entry)
{
    return $"{entry.English}->{entry.Orcish} [{entry.PartOfSpeech ?? "?"}]";
}

static void OrcishTranslatorSupportsTenPageWikiSampleVocabulary()
{
    var entries = OrcishTranslatorUtility.GetLexiconEntries()
        .Where(entry => HasAnyTag(entry, "ten-page-sample"))
        .ToArray();

    AssertEqual(230, entries.Length, "expected every candidate from the ten-page wiki sample");
    foreach (var entry in entries)
    {
        var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
        AssertTrue(
            translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
            $"expected '{entry.English}' to translate as '{entry.Orcish}'");
    }
}

static void OrcishTranslatorSupportsNearKinMorphologyFamilies()
{
    var entries = OrcishTranslatorUtility.GetLexiconEntries()
        .Where(entry => HasAnyTag(entry, "near-kin"))
        .Where(entry => !HasAnyTag(entry, "fifteen-page-near-kin", "twenty-page-near-kin", "thirty-page-near-kin", "thirty-page-followup-near-kin", "sixty-seven-page-near-kin"))
        .ToArray();

    AssertEqual(302, entries.Length, "expected every candidate from the 139 near-kin families");
    AssertEqual(
        139,
        entries.SelectMany(entry => entry.Tags ?? Array.Empty<string>())
            .Where(tag => tag.StartsWith("family-", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(),
        "expected all near-kin source families");

    foreach (var entry in entries)
    {
        var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
        AssertTrue(
            translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
            $"expected '{entry.English}' to translate as '{entry.Orcish}'");
    }

    var urgedRoot = OrcishTranslatorUtility.TranslateEnglishToOrcish("urged").Single().Translation;
    AssertTrue(
        OrcishTranslatorUtility.TranslateEnglishToOrcish("urge").Single().Translation.StartsWith(urgedRoot, StringComparison.OrdinalIgnoreCase),
        "urge should retain the urged family root");
    AssertTrue(
        OrcishTranslatorUtility.TranslateEnglishToOrcish("urging").Single().Translation.StartsWith(urgedRoot, StringComparison.OrdinalIgnoreCase),
        "urging should retain the urged family root");

    var sinkingRoot = OrcishTranslatorUtility.TranslateEnglishToOrcish("sinking").Single().Translation;
    foreach (var form in new[] { "sink", "sank", "sunk" })
    {
        AssertTrue(
            OrcishTranslatorUtility.TranslateEnglishToOrcish(form).Single().Translation.StartsWith(sinkingRoot, StringComparison.OrdinalIgnoreCase),
            $"{form} should retain the sinking family root");
    }
}

static void OrcishTranslatorSupportsFifteenPageSampleVocabulary()
{
    var entries = OrcishTranslatorUtility.GetLexiconEntries()
        .Where(entry => HasAnyTag(entry, "fifteen-page-sample", "fifteen-page-near-kin"))
        .ToArray();

    AssertEqual(932, entries.Length, "expected every candidate from the fifteen-page sample expansion");
    AssertEqual(329, entries.Count(entry => HasAnyTag(entry, "fifteen-page-sample")), "expected the scraped source candidates");
    AssertEqual(603, entries.Count(entry => HasAnyTag(entry, "fifteen-page-near-kin")), "expected the near-kin candidates");
    AssertEqual(
        308,
        entries.SelectMany(entry => entry.Tags ?? Array.Empty<string>())
            .Where(tag => tag.StartsWith("family-", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(),
        "expected every reconstructed word family");

    foreach (var entry in entries)
    {
        var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
        AssertTrue(
            translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
            $"expected '{entry.English}' to translate as '{entry.Orcish}'");
    }

    AssertTrue(
        new[] { "taken", "took" }.Select(term => entries.Single(entry => string.Equals(entry.English, term, StringComparison.OrdinalIgnoreCase)))
            .All(entry => HasAnyTag(entry, "family-taken")),
        "taken and took should preserve one Orcish family");
    AssertTrue(
        new[] { "tragedy", "tragedies" }.Select(term => entries.Single(entry => string.Equals(entry.English, term, StringComparison.OrdinalIgnoreCase)))
            .All(entry => HasAnyTag(entry, "family-tragedy")),
        "tragedy and tragedies should preserve one Orcish family");
}

static void OrcishTranslatorSupportsTwentyPageSampleVocabulary()
{
    var entries = OrcishTranslatorUtility.GetLexiconEntries()
        .Where(entry => HasAnyTag(entry, "twenty-page-sample", "twenty-page-near-kin"))
        .ToArray();

    AssertEqual(505, entries.Length, "expected every candidate from the twenty-page sample expansion");
    AssertEqual(200, entries.Count(entry => HasAnyTag(entry, "twenty-page-sample")), "expected the scraped source candidates");
    AssertEqual(305, entries.Count(entry => HasAnyTag(entry, "twenty-page-near-kin")), "expected the near-kin candidates");

    foreach (var entry in entries)
    {
        var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
            .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

        var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
        AssertTrue(
            translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
            $"expected '{entry.English}' to translate as '{entry.Orcish}'");
    }

    var abstinenceRoot = entries.Single(entry => string.Equals(entry.English, "abstinence", StringComparison.OrdinalIgnoreCase)).Orcish;
    foreach (var form in new[] { "abstain", "abstained", "abstaining", "abstains", "abstinences" })
    {
        AssertTrue(
            entries.Single(entry => string.Equals(entry.English, form, StringComparison.OrdinalIgnoreCase))
                .Orcish.StartsWith(abstinenceRoot, StringComparison.OrdinalIgnoreCase),
            $"{form} should retain the abstinence family root");
    }

    var springRoot = entries.Single(entry => string.Equals(entry.English, "spring", StringComparison.OrdinalIgnoreCase)).Orcish;
    foreach (var form in new[] { "sprang", "springing", "springs", "sprung" })
    {
        AssertTrue(
            entries.Single(entry => string.Equals(entry.English, form, StringComparison.OrdinalIgnoreCase))
                .Orcish.StartsWith(springRoot, StringComparison.OrdinalIgnoreCase),
            $"{form} should retain the spring family root");
    }
}

static void OrcishTranslatorSupportsThirtyPageSampleVocabulary()
{
    var entries = OrcishTranslatorUtility.GetLexiconEntries()
        .Where(entry => HasAnyTag(entry, "thirty-page-sample", "thirty-page-near-kin"))
        .ToArray();

    AssertEqual(1593, entries.Length, "expected every candidate from the thirty-page sample expansion");
    AssertEqual(701, entries.Count(entry => HasAnyTag(entry, "thirty-page-sample")), "expected the scraped source candidates");
    AssertEqual(892, entries.Count(entry => HasAnyTag(entry, "thirty-page-near-kin")), "expected the near-kin candidates");

    foreach (var entry in entries)
    {
        var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
            .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

        var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
        AssertTrue(
            translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
            $"expected '{entry.English}' to translate as '{entry.Orcish}'");
    }

    AssertThirtyPageFamilyRoot(entries, "alloys", "alloy", "alloyed", "alloying");
    AssertThirtyPageFamilyRoot(entries, "fed", "feed", "feeding", "feeds");
    AssertThirtyPageFamilyRoot(entries, "struck", "strikes");
    AssertThirtyPageFamilyRoot(entries, "zone", "zoned", "zones", "zoning");
}

static void AssertThirtyPageFamilyRoot(
    IReadOnlyList<OrcishLexiconEntry> entries,
    string sourceEnglish,
    params string[] nearKin)
{
    var sourceRoot = entries.Single(entry =>
        string.Equals(entry.English, sourceEnglish, StringComparison.OrdinalIgnoreCase)
        && HasAnyTag(entry, "thirty-page-sample")).Orcish;

    foreach (var english in nearKin)
    {
        AssertTrue(
            entries.Single(entry => string.Equals(entry.English, english, StringComparison.OrdinalIgnoreCase))
                .Orcish.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase),
            $"{english} should retain the {sourceEnglish} family root");
    }
}

static void OrcishTranslatorSupportsThirtyPageFollowupVocabulary()
{
    var entries = OrcishTranslatorUtility.GetLexiconEntries()
        .Where(entry => HasAnyTag(entry, "thirty-page-followup", "thirty-page-followup-near-kin"))
        .ToArray();

    AssertEqual(862, entries.Length, "expected every candidate from the thirty page followup expansion");
    AssertEqual(253, entries.Count(entry => HasAnyTag(entry, "thirty-page-followup")), "expected the scraped source candidates");
    AssertEqual(609, entries.Count(entry => HasAnyTag(entry, "thirty-page-followup-near-kin")), "expected the near-kin candidates");

    foreach (var entry in entries)
    {
        var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
            .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

        var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
        AssertTrue(
            translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
            $"expected '{entry.English}' to translate as '{entry.Orcish}'");
    }
}

static void OrcishTranslatorSupportsSixtySevenPageSampleVocabulary()
{
    var entries = OrcishTranslatorUtility.GetLexiconEntries()
        .Where(entry => HasAnyTag(entry, "sixty-seven-page-sample", "sixty-seven-page-near-kin"))
        .ToArray();

    AssertEqual(2260, entries.Length, "expected every candidate from the sixty seven page sample expansion");
    AssertEqual(761, entries.Count(entry => HasAnyTag(entry, "sixty-seven-page-sample")), "expected the scraped source candidates");
    AssertEqual(1499, entries.Count(entry => HasAnyTag(entry, "sixty-seven-page-near-kin")), "expected the near-kin candidates");

    foreach (var entry in entries)
    {
        var reviewIssues = OrcishTranslatorUtility.ReviewProposedLexiconEntry(entry)
            .Where(issue => !string.Equals(issue.Code, "exact-duplicate", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        AssertEqual(0, reviewIssues.Length, $"expected reviewed entry '{entry.English}' to remain admissible");

        var translations = OrcishTranslatorUtility.TranslateEnglishToOrcish(entry.English);
        AssertTrue(
            translations.Any(candidate => string.Equals(candidate.Translation, entry.Orcish, StringComparison.OrdinalIgnoreCase)),
            $"expected '{entry.English}' to translate as '{entry.Orcish}'");
    }
}

static void OrcishTranslatorExposesUniqueEnglishTermCount()
{
    var terms = OrcishTranslatorUtility.GetEnglishTerms();

    AssertEqual(10441, OrcishTranslatorUtility.GetEnglishTermCount(), "unexpected total English term count");
    AssertEqual(OrcishTranslatorUtility.GetEnglishTermCount(), terms.Count, "term list and count should agree");
    AssertEqual(1, terms.Count(term => string.Equals(term, "I", StringComparison.OrdinalIgnoreCase)), "I should be counted once despite multiple variants");
    AssertEqual(1, terms.Count(term => string.Equals(term, "really", StringComparison.OrdinalIgnoreCase)), "really should be counted once despite multiple variants");
    AssertEqual(1, terms.Count(term => string.Equals(term, "watch", StringComparison.OrdinalIgnoreCase)), "watch should be counted once despite multiple parts of speech");
    AssertTrue(terms.Contains("humans'", StringComparer.OrdinalIgnoreCase), "expected generated plural possessive term");
}

static void ToOrcishTranslatesTermsBeforeTrailingPunctuation()
{
    var result = RunToOrcish("yours,");

    AssertEqual(0, result.ExitCode, "to-orcish should exit successfully");
    AssertEqual("Narguk,", result.Output.Trim(), "expected yours to translate before comma restoration");
}

static void ToOrcishTranslatesDottedAbbreviationTerms()
{
    var result = RunToOrcish("p.m.");

    AssertEqual(0, result.ExitCode, "to-orcish should exit successfully");
    AssertEqual("Exenda", result.Output.Trim(), "expected dotted abbreviation to translate before punctuation stripping");
}

static void ToOrcishTranslatesTermsInsideParentheses()
{
    var result = RunToOrcish("(secret)");

    AssertEqual(0, result.ExitCode, "to-orcish should exit successfully");
    AssertEqual("(noglar)", result.Output.Trim(), "expected parenthesized terms to translate without treating parentheses as word characters");
}

static void ToOrcishTranslatesTermsInsideQuotes()
{
    var result = RunToOrcish("\"Well met. Please.\" \"Obliged,\"");

    AssertEqual(0, result.ExitCode, "to-orcish should exit successfully");
    AssertEqual("\"Mokra-narg. Mauk-drav.\" \"Tukru-drav,\"", result.Output.Trim(), "expected quoted terms to translate without treating quotes as word characters");
}

static void ToOrcishTranslatesWordsAfterNewlines()
{
    var result = RunToOrcish("The roads\nThe notice board");

    AssertEqual(0, result.ExitCode, "to-orcish should exit successfully");
    AssertEqual("Arhk lagi arhk narg-bib-dak", result.Output.Trim(), "expected words after newlines to translate as separate terms");
}

static void AppConfigurationValidationAcceptsCompleteRuntime()
{
    using var directory = TemporaryDirectory.Create();
    WriteRequiredRuntimeSidecars(directory.Path);

    var report = AppConfigurationValidationUtility.Validate(
        CreateValidAppSettings(includeCredentials: true),
        directory.Path);

    AssertFalse(report.HasIssues, "complete runtime configuration should not report issues");
}

static void SettingsJsonAcceptsCurrentSchemaVersion()
{
    using var directory = TemporaryDirectory.Create();
    File.WriteAllText(
        Path.Combine(directory.Path, "settings.json"),
        """
        {
          "schema_version": 1,
          "RPOL Site": "https://rpol.net/game.php?gi=80170",
          "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
          "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
          "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons",
          "XP Tracking": "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
        }
        """);

    var settings = AppSettingsUtility.LoadSettings(directory.Path);

    AssertEqual("https://rpol.net/game.php?gi=80170", settings["RPOL Site"], "unexpected RPOL Site after schema-versioned load");
    AssertFalse(settings.ContainsKey("schema_version"), "schema_version should be treated as settings metadata");
}

static void SettingsJsonRejectsFutureSchemaVersion()
{
    using var directory = TemporaryDirectory.Create();
    File.WriteAllText(
        Path.Combine(directory.Path, "settings.json"),
        """
        {
          "schema_version": 99,
          "RPOL Site": "https://rpol.net/game.php?gi=80170",
          "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
          "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
          "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons",
          "XP Tracking": "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
        }
        """);

    var exception = AssertThrows<InvalidOperationException>(() =>
        AppSettingsUtility.LoadSettings(directory.Path));
    AssertContains(exception.Message, "unsupported schema version 99");
}

static void AppSettingsLoadsHostedEncryptedXpTrackingUrl()
{
    using var directory = TemporaryDirectory.Create();
    var hostedSettings = CreateSignedHostedSettingsArtifact(
        new Dictionary<string, string>
        {
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
        });

    File.WriteAllText(
        Path.Combine(directory.Path, "settings.json"),
        $$"""
        {
          "schema_version": 1,
          "Hosted Local Settings": "https://bryanmiller.us/scarlethorizons/settings.local.json",
          "RPOL Site": "https://rpol.net/game.php?gi=80170",
          "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
          "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
          "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons"
        }
        """);

    string? requestedHostedSettingsUrl = null;
    using var httpClientScope = AppSettingsUtility.UseHttpClientFactoryForTests(() =>
        NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((request, _) =>
        {
            requestedHostedSettingsUrl = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(hostedSettings.HostedSettingsJson)
            });
        })));
    using var trustedHostedSettingsKeysScope = HostedSettingsTrustUtility.UseTrustedSigningKeysForTests(
        [CreateActiveHostedSettingsSigningKey(hostedSettings.PublicKeyPem)]);

    Dictionary<string, string> settings = null!;
    WithHostedSettingsIsolation(() =>
        settings = new Dictionary<string, string>(AppSettingsUtility.LoadSettings(directory.Path), StringComparer.OrdinalIgnoreCase));

    AssertEqual(
        "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
        settings["XP Tracking"],
        "hosted encrypted local settings should provide the XP Tracking URL");
    AssertEqual(
        "https://bryanmiller.us/scarlethorizons/settings.local.json",
        requestedHostedSettingsUrl ?? string.Empty,
        "unexpected hosted local settings URL");
    AssertFalse(
        File.Exists(Path.Combine(directory.Path, "settings.local.json")),
        "hosted local settings should not be persisted into the runtime directory");
}

static void AppSettingsMigrateHostedRpolCredentialsIntoCredentialManager()
{
    using var directory = TemporaryDirectory.Create();
    var hostedSettings = CreateSignedHostedSettingsArtifact(
        new Dictionary<string, string>
        {
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            ["RPOL user name"] = "example-user",
            ["RPOL password"] = "example-password"
        });

    File.WriteAllText(
        Path.Combine(directory.Path, "settings.json"),
        """
        {
          "schema_version": 1,
          "Hosted Local Settings": "https://bryanmiller.us/scarlethorizons/settings.local.json",
          "RPOL Site": "https://rpol.net/game.php?gi=80170",
          "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
          "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
          "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons"
        }
        """);

    string? requestedHostedSettingsUrl = null;
    using var httpClientScope = AppSettingsUtility.UseHttpClientFactoryForTests(() =>
        NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((request, _) =>
        {
            requestedHostedSettingsUrl = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(hostedSettings.HostedSettingsJson)
            });
        })));
    using var trustedHostedSettingsKeysScope = HostedSettingsTrustUtility.UseTrustedSigningKeysForTests(
        [CreateActiveHostedSettingsSigningKey(hostedSettings.PublicKeyPem)]);

    Dictionary<string, string> settings = null!;
    WithHostedSettingsIsolation(() =>
        settings = new Dictionary<string, string>(AppSettingsUtility.LoadSettings(directory.Path), StringComparer.OrdinalIgnoreCase));

    AssertEqual("example-user", settings["RPOL user name"], "unexpected migrated RPOL user name");
    AssertEqual("example-password", settings["RPOL password"], "unexpected migrated RPOL password");
    AssertEqual(
        "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
        settings["XP Tracking"],
        "xp tracking should remain available after hosted local settings load");
    AssertEqual(
        "https://bryanmiller.us/scarlethorizons/settings.local.json",
        requestedHostedSettingsUrl ?? string.Empty,
        "unexpected hosted local settings URL");
    AssertEqual("example-user", RuntimeSecretStoreUtility.GetRpolUserName()!, "credential manager should store RPOL user name");
    AssertEqual("example-password", RuntimeSecretStoreUtility.GetRpolPassword()!, "credential manager should store RPOL password");
    AssertFalse(
        File.Exists(Path.Combine(directory.Path, "settings.local.json")),
        "hosted local settings should not be persisted after credential migration");
}

static void AppSettingsLoadsHostedEncryptedXpTrackingUrlFromFixtureServer()
{
    using var directory = TemporaryDirectory.Create();
    var hostedSettings = CreateSignedHostedSettingsArtifact(
        new Dictionary<string, string>
        {
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
        });

    using var fixtureServer = new LoopbackHttpServer("/scarlethorizons/settings.local.json", hostedSettings.HostedSettingsJson);

    File.WriteAllText(
        Path.Combine(directory.Path, "settings.json"),
        $$"""
        {
          "schema_version": 1,
          "Hosted Local Settings": "{{fixtureServer.Url}}",
          "RPOL Site": "https://rpol.net/game.php?gi=80170",
          "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
          "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
          "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons"
        }
        """);

    using var allowlistScope = NetworkUrlAllowlistUtility.UseValidationOverrideForTests((uri, purpose) =>
    {
        if (purpose == NetworkUrlPurpose.PlayerAssistantHostedSettings
            && string.Equals(uri.AbsoluteUri, fixtureServer.Url, StringComparison.Ordinal))
        {
            return NetworkUrlAllowlistValidation.Allowed(uri);
        }

        return null;
    });
    using var trustedHostedSettingsKeysScope = HostedSettingsTrustUtility.UseTrustedSigningKeysForTests(
        [CreateActiveHostedSettingsSigningKey(hostedSettings.PublicKeyPem)]);

    Dictionary<string, string> settings = null!;
    WithHostedSettingsIsolation(() =>
        settings = new Dictionary<string, string>(AppSettingsUtility.LoadSettings(directory.Path), StringComparer.OrdinalIgnoreCase));

    AssertEqual(
        "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
        settings["XP Tracking"],
        "fixture-hosted encrypted local settings should provide the XP Tracking URL");
    AssertEqual(1, fixtureServer.RequestCount, "fixture server should receive exactly one hosted settings request");
    AssertEqual("/scarlethorizons/settings.local.json", fixtureServer.LastRequestPath, "fixture server should receive the hosted settings path");
    AssertFalse(
        File.Exists(Path.Combine(directory.Path, "settings.local.json")),
        "fixture-hosted local settings should not be persisted into the runtime directory");
}

static void AppSettingsHostedSettingsFailureLogsTamperedEnvelope()
{
    var hostedSettings = CreateSignedHostedSettingsArtifact(
        new Dictionary<string, string>
        {
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
        });
    var tamperedJson = CorruptHostedSettingsSignature(hostedSettings.HostedSettingsJson);

    using var fixtureServer = new LoopbackHttpServer("/scarlethorizons/settings.local.json", tamperedJson);
    AssertHostedSettingsFailure(
        fixtureServer.Url,
        "signature",
        [CreateActiveHostedSettingsSigningKey(hostedSettings.PublicKeyPem)],
        expectedRequestCount: 1,
        expectedRequestPath: "/scarlethorizons/settings.local.json");
}

static void AppSettingsHostedSettingsFailureLogsPlaintextPayload()
{
    const string plaintextJson =
        """
        {
          "XP Tracking": "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
        }
        """;

    using var fixtureServer = new LoopbackHttpServer("/scarlethorizons/settings.local.json", plaintextJson);
    AssertHostedSettingsFailure(
        fixtureServer.Url,
        "signed envelope",
        expectedRequestCount: 1,
        expectedRequestPath: "/scarlethorizons/settings.local.json");
}

static void AppSettingsHostedSettingsFailureLogsOversizedPayload()
{
    var oversizedPayload = new string('A', checked((int)NetworkResponseContentLimit.JsonCache.MaxBytes + 1024));

    using var fixtureServer = new LoopbackHttpServer("/scarlethorizons/settings.local.json", oversizedPayload);
    AssertHostedSettingsFailure(
        fixtureServer.Url,
        "JSON cache response exceeded",
        expectedRequestCount: 1,
        expectedRequestPath: "/scarlethorizons/settings.local.json");
}

static void AppSettingsHostedSettingsFailureLogsUnreachableFixtureServer()
{
    string fixtureUrl;
    using (var fixtureServer = new LoopbackHttpServer("/scarlethorizons/settings.local.json", "{}"))
    {
        fixtureUrl = fixtureServer.Url;
    }

    AssertHostedSettingsFailure(
        fixtureUrl,
        "The network request failed:");
}

static void HostedSettingsTrustedVersionIsEncryptedAtRest()
{
    using var directory = TemporaryDirectory.Create();
    var statePath = Path.Combine(directory.Path, "trusted-hosted-settings-state.json");

    HostedSettingsTrustUtility.ApplyTrustedHostedSettingsVersionPolicy(
        new Version(1, 0, 1),
        statePath);

    var encryptedJson = File.ReadAllText(statePath);
    AssertContains(encryptedJson, "\"format\": \"app-protected-v3\"");
    AssertContains(encryptedJson, "\"key_scope\":");
    AssertFalse(encryptedJson.Contains("1.0.1", StringComparison.Ordinal), "trusted hosted settings version should not be stored in plaintext");
}

static void HostedSettingsTrustedVersionRejectsTamperedPayload()
{
    using var directory = TemporaryDirectory.Create();
    var statePath = Path.Combine(directory.Path, "trusted-hosted-settings-state.json");

    HostedSettingsTrustUtility.ApplyTrustedHostedSettingsVersionPolicy(
        new Version(1, 0, 1),
        statePath);

    using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(statePath)))
    {
        var payload = document.RootElement.GetProperty("payload").GetString() ?? string.Empty;
        var payloadBytes = Convert.FromBase64String(payload);
        payloadBytes[^1] ^= 0x7F;
        File.WriteAllText(
            statePath,
            $$"""
            {
              "schema_version": 1,
              "format": "app-protected-v3",
              "payload": "{{Convert.ToBase64String(payloadBytes)}}"
            }
            """);
    }

    var exception = AssertThrows<InvalidOperationException>(() =>
        HostedSettingsTrustUtility.TryReadTrustedHostedSettingsVersion(statePath));
    AssertContains(exception.Message, "authenticate or decrypt");
}

static void HostedSettingsRejectsRollbackBelowTrustedVersionFloor()
{
    using var directory = TemporaryDirectory.Create();
    var statePath = Path.Combine(directory.Path, "trusted-hosted-settings-state.json");
    var signedV2 = CreateSignedHostedSettingsArtifact(
        new Dictionary<string, string>
        {
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
        },
        version: "1.0.2");
    var signedV1 = CreateSignedHostedSettingsArtifact(
        new Dictionary<string, string>
        {
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
        },
        version: "1.0.1");

    _ = HostedSettingsTrustUtility.LoadAndVerifyHostedSettings(
        signedV2.HostedSettingsJson,
        "https://bryanmiller.us/scarlethorizons/settings.local.json",
        [CreateActiveHostedSettingsSigningKey(signedV2.PublicKeyPem)],
        statePath);
    var exception = AssertThrows<InvalidOperationException>(() =>
        HostedSettingsTrustUtility.LoadAndVerifyHostedSettings(
            signedV1.HostedSettingsJson,
            "https://bryanmiller.us/scarlethorizons/settings.local.json",
            [CreateActiveHostedSettingsSigningKey(signedV1.PublicKeyPem)],
            statePath));

    AssertContains(exception.Message, "downgrade");
    AssertContains(exception.Message, "1.0.2");
}

static void HostedSettingsRejectsUnexpectedSignedContentIdentity()
{
    var signedHostedSettings = CreateSignedHostedSettingsArtifact(
        new Dictionary<string, string>
        {
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
        },
        contentId: "unexpected-hosted-settings",
        version: "1.0.0");

    var exception = AssertThrows<InvalidOperationException>(() =>
        HostedSettingsTrustUtility.LoadAndVerifyHostedSettings(
            signedHostedSettings.HostedSettingsJson,
            "https://bryanmiller.us/scarlethorizons/settings.local.json",
            [CreateActiveHostedSettingsSigningKey(signedHostedSettings.PublicKeyPem)],
            trustedHostedSettingsStatePath: null));

    AssertContains(exception.Message, "content identity");
    AssertContains(exception.Message, HostedSettingsTrustUtility.HostedSettingsContentId);
}

static void XpPasswordStoreLoadsSaltedHashSidecar()
{
    using var directory = TemporaryDirectory.Create();
    var sidecarPath = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
    XpPasswordStoreUtility.SavePasswordHashes(
        sidecarPath,
        new Dictionary<string, string>
        {
            ["Kelpie"] = "gemstone",
            ["Jelb"] = "spell-component"
        });

    var hashes = XpPasswordStoreUtility.LoadPasswordHashes(directory.Path);

    AssertEqual(2, hashes.Count, "unexpected XP password hash count");
    AssertTrue(
        XpPasswordStoreUtility.ValidatePassword("Kelpie", "gemstone", directory.Path),
        "matching XP password should validate");
    AssertFalse(
        XpPasswordStoreUtility.ValidatePassword("Kelpie", "wrong", directory.Path),
        "wrong XP password should be rejected");
}

static void XpPasswordStoreUsesUniqueSaltsAndOmitsPlaintext()
{
    using var directory = TemporaryDirectory.Create();
    var sidecarPath = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
    XpPasswordStoreUtility.SavePasswordHashes(
        sidecarPath,
        new Dictionary<string, string>
        {
            ["Kelpie"] = "shared-password",
            ["Jelb"] = "shared-password"
        });

    var raw = File.ReadAllText(sidecarPath);
    using var document = JsonDocument.Parse(raw);
    var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToArray();
    var salts = entries.Select(entry => entry.GetProperty("salt").GetString()).ToArray();
    var hashes = entries.Select(entry => entry.GetProperty("hash").GetString()).ToArray();

    AssertEqual(
        XpPasswordStoreUtility.Format,
        document.RootElement.GetProperty("format").GetString() ?? string.Empty,
        "unexpected XP password hash format");
    AssertEqual(2, salts.Distinct(StringComparer.Ordinal).Count(), "each XP password must use a unique salt");
    AssertEqual(2, hashes.Distinct(StringComparer.Ordinal).Count(), "equal passwords with unique salts must produce different hashes");
    AssertFalse(raw.Contains("shared-password", StringComparison.Ordinal), "XP hash sidecar must not contain plaintext password material");
}

static void XpPasswordStoreAcceptsFirstAndFullCharacterNames()
{
    using var directory = TemporaryDirectory.Create();
    var sidecarPath = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
    XpPasswordStoreUtility.SavePasswordHashes(
        sidecarPath,
        new Dictionary<string, string>
        {
            ["Kelpie"] = "gemstone",
            ["Jelb Garrick"] = "spell-component",
            ["Dungeon Master"] = "Lucian99!"
        });

    AssertTrue(
        XpPasswordStoreUtility.ValidatePassword("Kelpie Lawfuller", "gemstone", directory.Path),
        "full Kelpie name should validate against first-name XP credential");
    AssertTrue(
        XpPasswordStoreUtility.ValidatePassword("Jelb", "spell-component", directory.Path),
        "first Jelb name should validate against full-name XP credential");
    AssertFalse(
        XpPasswordStoreUtility.ValidatePassword("Dungeon", "Lucian99!", directory.Path),
        "Dungeon Master access should not allow a first-name shortcut");
}

static void XpPasswordStoreAcceptsHashSidecarWithUtf8Bom()
{
    using var directory = TemporaryDirectory.Create();
    var sidecarPath = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
    XpPasswordStoreUtility.SavePasswordHashes(
        sidecarPath,
        new Dictionary<string, string>
        {
            ["Kelpie Lawfuller"] = "gemstone"
        });

    var hashBytes = File.ReadAllBytes(sidecarPath);
    File.WriteAllBytes(sidecarPath, [0xEF, 0xBB, 0xBF, .. hashBytes]);

    AssertTrue(
        XpPasswordStoreUtility.ValidatePassword("Kelpie Lawfuller", "gemstone", directory.Path),
        "matching XP password should validate when hash sidecar has a UTF-8 BOM");
}

static void XpPasswordStoreRejectsLegacyEncryptedSidecar()
{
    using var directory = TemporaryDirectory.Create();
    var sidecarPath = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
    LocalSettingsUtility.SavePortableEncryptedSettings(
        sidecarPath,
        new Dictionary<string, string> { ["Kelpie"] = "gemstone" });

    var exception = AssertThrows<InvalidOperationException>(() =>
        XpPasswordStoreUtility.LoadPasswordHashes(directory.Path));

    AssertContains(exception.Message, XpPasswordStoreUtility.Format);
}

static void XpPasswordStoreMigratesEncryptedSidecar()
{
    using var directory = TemporaryDirectory.Create();
    var sidecarPath = Path.Combine(directory.Path, XpPasswordStoreUtility.FileName);
    LocalSettingsUtility.SavePortableEncryptedSettings(
        sidecarPath,
        new Dictionary<string, string>
        {
            ["Kelpie"] = "gemstone",
            ["Jelb"] = "spell-component"
        });

    var entryCount = XpPasswordStoreUtility.ConvertEncryptedSidecarToPasswordHashes(sidecarPath);

    AssertEqual(2, entryCount, "unexpected migrated XP password count");
    AssertTrue(XpPasswordStoreUtility.ValidatePassword("Kelpie", "gemstone", directory.Path), "migrated XP password should validate");
    var raw = File.ReadAllText(sidecarPath);
    AssertContains(raw, XpPasswordStoreUtility.Format);
    AssertFalse(raw.Contains("gemstone", StringComparison.Ordinal), "migration must remove plaintext password material");
    AssertFalse(raw.Contains("app-protected", StringComparison.Ordinal), "migration must remove the reversible encrypted envelope");
}

static void XpPasswordStoreReportsMissingSidecarByName()
{
    using var directory = TemporaryDirectory.Create();

    var exception = AssertThrows<FileNotFoundException>(() =>
        XpPasswordStoreUtility.LoadPasswordHashes(directory.Path));

    AssertContains(exception.Message, XpPasswordStoreUtility.FileName);
    AssertContains(exception.Message, directory.Path);
    AssertFalse(
        exception.Message.Contains("settings sidecar", StringComparison.OrdinalIgnoreCase),
        "missing XP password diagnostics should not be mistaken for settings.local.json");
}

static void AppConfigurationValidationReportsMissingUrl()
{
    using var directory = TemporaryDirectory.Create();
    WriteRequiredRuntimeSidecars(directory.Path);
    var settings = CreateValidAppSettings(includeCredentials: true);
    settings.Remove("The Cast");

    var report = AppConfigurationValidationUtility.Validate(settings, directory.Path);

    AssertTrue(report.HasIssues, "missing URL should report a configuration issue");
    AssertTrue(
        report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error
            && issue.Message.Contains("The Cast", StringComparison.Ordinal)),
        "missing The Cast URL should be an error");
    AssertEqual(
        "Settings problem: The Cast is missing or empty.",
        report.FirstUserMessage!,
        "unexpected first user-facing validation message");
    AssertContains(report.Issues[0].RepairAction, "settings.json");
    AssertContains(report.ToRemediationText(), "Repair:");
}

static void AppConfigurationValidationRejectsDisallowedNetworkHost()
{
    using var directory = TemporaryDirectory.Create();
    WriteRequiredRuntimeSidecars(directory.Path);
    var settings = CreateValidAppSettings(includeCredentials: true);
    settings["Game Intro"] = "https://unexpected.example.test/gameinfo";

    var report = AppConfigurationValidationUtility.Validate(settings, directory.Path);

    AssertTrue(report.HasIssues, "disallowed host should report a configuration issue");
    AssertTrue(
        report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error
            && issue.Message.Contains("Game Intro", StringComparison.Ordinal)
            && issue.Message.Contains("allowed network host", StringComparison.Ordinal)),
        "disallowed Game Intro host should be an allowlist error");
}

static void AppConfigurationValidationWritesRepairGuidance()
{
    using var directory = TemporaryDirectory.Create();
    var settings = CreateValidAppSettings(includeCredentials: false);
    settings["Game Intro"] = "not a url";

    var report = AppConfigurationValidationUtility.Validate(settings, directory.Path);
    AppConfigurationValidationUtility.WriteRemediationFile(report, directory.Path);

    var remediationPath = Path.Combine(directory.Path, AppConfigurationValidationUtility.RemediationFileName);
    AssertTrue(File.Exists(remediationPath), "startup remediation file should be written");
    var remediation = File.ReadAllText(remediationPath);
    AssertContains(remediation, "Player Assistant startup configuration guidance");
    AssertContains(remediation, "Game Intro is not on the allowed network host list");
    AssertContains(remediation, "URL must be absolute.");
    AssertContains(remediation, "Edit settings.json");
    AssertFalse(
        remediation.Contains("RPOL credentials", StringComparison.OrdinalIgnoreCase),
        "RPOL credential warnings should not appear unless hosted settings failed");
}

static void AppConfigurationValidationSuppressesMissingRpolCredentialsBeforeHostedSettingsFailure()
{
    using var directory = TemporaryDirectory.Create();
    WriteRequiredRuntimeSidecars(directory.Path);

    var report = AppConfigurationValidationUtility.Validate(
        CreateValidAppSettings(includeCredentials: false),
        directory.Path);

    AssertFalse(
        report.Issues.Any(issue => issue.Message.Contains("RPOL credential", StringComparison.OrdinalIgnoreCase)),
        "missing RPOL credentials should not be reported before hosted settings failure");
    AssertFalse(
        report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error),
        "missing optional RPOL credentials should not be fatal");
}

static void AppConfigurationValidationWarnsAboutMissingRpolCredentialsAfterHostedSettingsFailure()
{
    using var directory = TemporaryDirectory.Create();
    WriteRequiredRuntimeSidecars(directory.Path);

    var report = AppConfigurationValidationUtility.Validate(
        CreateValidAppSettings(includeCredentials: false),
        directory.Path,
        warnAboutMissingRpolCredentials: true);

    AssertTrue(
        report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Warning
            && issue.Message.Contains("Hosted RPOL credential data could not be loaded", StringComparison.Ordinal)),
        "missing RPOL credentials should be a warning after hosted settings failure");
    AssertFalse(
        report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error),
        "missing optional RPOL credentials should not be fatal");
}

static void AppConfigurationValidationWarnsAboutMissingSidecars()
{
    using var directory = TemporaryDirectory.Create();

    var report = AppConfigurationValidationUtility.Validate(
        CreateValidAppSettings(includeCredentials: true),
        directory.Path);

    AssertTrue(
        report.Issues.Count(issue => issue.Message.Contains("missing", StringComparison.OrdinalIgnoreCase)) >= 3,
        "missing runtime sidecars should be reported");
    AssertTrue(
        report.Issues.All(issue => issue.Severity == AppConfigurationIssueSeverity.Warning),
        "missing sidecars should warn without blocking startup");
}

static void AppSettingsLoadsRpolCredentialsFromLocalSettingsSidecar()
{
    using var directory = TemporaryDirectory.Create();
    WriteSettingsJson(directory.Path, CreateValidAppSettings(includeCredentials: false));
    WriteRequiredRuntimeSidecars(directory.Path);
    LocalSettingsUtility.SaveEncryptedSettings(
        Path.Combine(directory.Path, "settings.local.json"),
        new Dictionary<string, string>
        {
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            ["RPOL user name"] = "example-user",
            ["RPOL password"] = "example-password"
        });

    using var credentialStoreScope = RuntimeSecretStoreUtility.UseBackendForTests(new InMemoryWindowsCredentialStoreBackend());
    var settings = new Dictionary<string, string>(AppSettingsUtility.LoadSettings(directory.Path), StringComparer.OrdinalIgnoreCase);

    AssertEqual("example-user", settings["RPOL user name"], "local settings sidecar should provide RPOL user name");
    AssertEqual("example-password", settings["RPOL password"], "local settings sidecar should provide RPOL password");
    AssertEqual("example-user", RuntimeSecretStoreUtility.GetRpolUserName()!, "local settings sidecar should prime RPOL user name");
    AssertEqual("example-password", RuntimeSecretStoreUtility.GetRpolPassword()!, "local settings sidecar should prime RPOL password");
    AssertEqual(
        "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
        settings["XP Tracking"],
        "local settings sidecar should still provide XP Tracking");
    var report = AppConfigurationValidationUtility.Validate(settings, directory.Path, warnAboutMissingRpolCredentials: true);
    AssertFalse(
        report.Issues.Any(issue => issue.Message.Contains("Hosted RPOL credential data could not be loaded", StringComparison.Ordinal)),
        "loaded sidecar RPOL credentials should suppress the hosted-settings warning");
}

static void AppSettingsUsesLocalRpolCredentialsWhenCredentialStoreIsUnavailable()
{
    using var directory = TemporaryDirectory.Create();
    WriteSettingsJson(directory.Path, CreateValidAppSettings(includeCredentials: false));
    WriteRequiredRuntimeSidecars(directory.Path);
    LocalSettingsUtility.SaveEncryptedSettings(
        Path.Combine(directory.Path, "settings.local.json"),
        new Dictionary<string, string>
        {
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            ["RPOL user name"] = "sidecar-user",
            ["RPOL password"] = "sidecar-password"
        });

    using var credentialStoreScope = RuntimeSecretStoreUtility.UseBackendForTests(new ThrowingWindowsCredentialStoreBackend());
    var settings = new Dictionary<string, string>(AppSettingsUtility.LoadSettings(directory.Path), StringComparer.OrdinalIgnoreCase);

    AssertEqual("sidecar-user", settings["RPOL user name"], "local settings sidecar should provide RPOL user name when the credential store is unavailable");
    AssertEqual("sidecar-password", settings["RPOL password"], "local settings sidecar should provide RPOL password when the credential store is unavailable");
}

static void AppConfigurationValidationAcceptsValidReleaseManifest()
{
    using var directory = TemporaryDirectory.Create();
    WriteManifestedRuntime(directory.Path);

    var report = AppConfigurationValidationUtility.Validate(
        CreateValidAppSettings(includeCredentials: true),
        directory.Path);

    AssertFalse(
        report.Issues.Any(issue => issue.Message.Contains("release-manifest.json", StringComparison.Ordinal)),
        "valid release manifest should not report an integrity issue");
}

static void AppConfigurationValidationRejectsMissingManifestFile()
{
    using var directory = TemporaryDirectory.Create();
    WriteManifestedRuntime(directory.Path);
    File.Delete(Path.Combine(directory.Path, ".playwright", "package", "package.json"));

    var report = AppConfigurationValidationUtility.Validate(
        CreateValidAppSettings(includeCredentials: true),
        directory.Path);

    AssertTrue(
        report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error
            && issue.Message.Contains("release-manifest.json missing manifested file", StringComparison.Ordinal)
            && issue.Message.Contains("package.json", StringComparison.Ordinal)),
        "missing manifested file should report a release integrity error");
}

static void AppConfigurationValidationRejectsManifestHashMismatch()
{
    using var directory = TemporaryDirectory.Create();
    WriteManifestedRuntime(directory.Path);
    File.AppendAllText(Path.Combine(directory.Path, "settings.json"), "tampered");

    var report = AppConfigurationValidationUtility.Validate(
        CreateValidAppSettings(includeCredentials: true),
        directory.Path);

    AssertTrue(
        report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error
            && issue.Message.Contains("release-manifest.json SHA256 mismatch", StringComparison.Ordinal)
            && issue.Message.Contains("settings.json", StringComparison.Ordinal)),
        "modified manifested file should report a release integrity hash error");
}

static void HealthArgumentSurfacesReleaseManifestIssue()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    var manifestPath = Path.Combine(runtimeDirectory, ReleaseIntegrityManifestUtility.FileName);
    var backupPath = Path.Combine(runtimeDirectory, $"{ReleaseIntegrityManifestUtility.FileName}.test-backup-{Guid.NewGuid():N}");
    var hadManifest = File.Exists(manifestPath);

    try
    {
        if (hadManifest)
        {
            File.Move(manifestPath, backupPath);
        }

        File.WriteAllText(
            manifestPath,
            """
            {
              "schema_version": 1,
              "hash_algorithm": "SHA256",
              "files": [
                {
                  "relative_path": "missing-health-sidecar.txt",
                  "length": 1,
                  "sha256": "00"
                }
              ]
            }
            """);

        var programType = typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.Program")
            ?? throw new InvalidOperationException("Unable to find PlayerAssistant.Program type.");
        var health = (string)InvokeStaticMethod(programType, "GetHealthText")!;

        AssertContains(health, "status: error");
        AssertContains(health, "release-manifest.json missing manifested file");
        AssertContains(health, "missing-health-sidecar.txt");
    }
    finally
    {
        if (File.Exists(manifestPath))
        {
            File.Delete(manifestPath);
        }

        if (hadManifest && File.Exists(backupPath))
        {
            File.Move(backupPath, manifestPath);
        }
    }
}

static void StartupDependencyMatrixReportsBadConfigAndSidecars()
{
    using var directory = TemporaryDirectory.Create();
    File.WriteAllText(Path.Combine(directory.Path, "keyword-index.json"), string.Empty);
    File.WriteAllText(Path.Combine(directory.Path, KeywordTermsFileUtility.FileName), "scarlet");
    var settings = CreateValidAppSettings(includeCredentials: true);
    settings["RPOL Site"] = "file:///not-http";
    settings["Game Intro"] = "not a url";
    var missingRuntimeDirectory = Path.Combine(directory.Path, "missing-runtime");

    var badUrlReport = AppConfigurationValidationUtility.Validate(settings, directory.Path);
    var missingRuntimeReport = AppConfigurationValidationUtility.Validate(
        CreateValidAppSettings(includeCredentials: true),
        missingRuntimeDirectory);

    AssertTrue(
        badUrlReport.Issues.Count(issue => issue.Severity == AppConfigurationIssueSeverity.Error
            && issue.Message.Contains("allowed network host", StringComparison.Ordinal)) >= 2,
        "malformed startup URLs should be reported as configuration errors");
    AssertTrue(
        badUrlReport.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Warning
            && issue.Message.Contains("keyword-index.json is empty", StringComparison.Ordinal)),
        "empty keyword index sidecar should be reported as a warning");
    AssertTrue(
        badUrlReport.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Warning
            && issue.Message.Contains("sitemap.xml is missing", StringComparison.Ordinal)),
        "missing sitemap sidecar should be reported as a warning");
    AssertTrue(
        missingRuntimeReport.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Error
            && issue.Message.Contains("Runtime directory does not exist", StringComparison.Ordinal)),
        "missing runtime directory should be reported as a configuration error");
}

static void StartupDependencyMatrixIgnoresCorruptOptionalLocalSettings()
{
    using var directory = TemporaryDirectory.Create();
    WriteSettingsJson(directory.Path, CreateValidAppSettings(includeCredentials: false));
    var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
    File.WriteAllText(localSettingsPath, "{ not valid json");

    WithPreservedStartupLog(() =>
    {
        var settings = AppSettingsUtility.LoadSettings(directory.Path);

        AssertEqual(
            "https://rpol.net/game.php?gi=80170",
            settings["RPOL Site"],
            "base settings should still load when optional local settings are corrupt");
        AssertFalse(settings.ContainsKey("RPOL user name"), "corrupt optional local settings should not inject credentials");
        AssertFalse(File.Exists(localSettingsPath), "corrupt local settings should be moved out of the active path");

        var badLocalSettingsFiles = Directory.GetFiles(directory.Path, "settings.local.bad-*.json");
        AssertEqual(1, badLocalSettingsFiles.Length, "expected one quarantined local settings file");

        var startupLog = File.ReadAllText(GetStartupLogPath());
        AssertContains(startupLog, "local settings load");
        AssertContains(startupLog, badLocalSettingsFiles[0]);
    });
}

static void ApplicationVersionMetadataMatchesHardeningRelease()
{
    var assembly = typeof(Form1).Assembly;
    var name = assembly.GetName();
    var informationalVersion = assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
        .Single()
        .InformationalVersion;
    var fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion;

    AssertEqual(new Version(0, 9, 4, 0), name.Version!, "unexpected assembly version");
    AssertEqual("0.9.4.0", fileVersion!, "unexpected file version");
    AssertEqual("0.9.4", informationalVersion, "unexpected informational version");
}

static void ApplicationVersionArgumentReturnsVersionText()
{
    var programType = typeof(Form1).Assembly.GetType("PlayerAssistant.Program")
        ?? throw new InvalidOperationException("Unable to find PlayerAssistant.Program.");

    AssertTrue(
        (bool)(InvokeStaticMethod(programType, "IsVersionArgument", "--version") ?? false),
        "--version should be recognized as a version argument");
    AssertTrue(
        (bool)(InvokeStaticMethod(programType, "IsVersionArgument", "/version") ?? false),
        "/version should be recognized as a version argument");
    AssertFalse(
        (bool)(InvokeStaticMethod(programType, "IsVersionArgument", "--suppress-hero-images") ?? true),
        "non-version arguments should not be recognized as version arguments");

    var versionText = (string?)InvokeStaticMethod(programType, "GetVersionText")
        ?? throw new InvalidOperationException("GetVersionText returned null.");
    AssertContains(versionText, "player-assistant");
    AssertContains(versionText, "0.9.4");
}

static void StartupManifestStatusDistinguishesSkippedAndFailed()
{
    AssertEqual("downloaded", Form1.GetManifestStatus(downloaded: true, errorMessage: null), "unexpected downloaded status");
    AssertEqual("skipped", Form1.GetManifestStatus(downloaded: false, errorMessage: null), "unexpected skipped status");
    AssertEqual("failed", Form1.GetManifestStatus(downloaded: false, errorMessage: "boom"), "unexpected failed status");
}

static void StartupErrorLogEntryIncludesPhaseAndException()
{
    var entry = Form1.FormatStartupErrorLogEntry(
        "ooc thread downloads RPOL password=hunter2",
        new InvalidOperationException("Missing RPoL credentials at https://user:pass@example.test/path?password=secret-password&token=secret-token Authorization: Bearer abc123"));

    AssertContains(entry, "ooc thread downloads");
    AssertContains(entry, "InvalidOperationException");
    AssertContains(entry, "Missing RPoL credentials");
    AssertFalse(entry.Contains("hunter2", StringComparison.Ordinal), "startup log phase should redact RPOL passwords");
    AssertFalse(entry.Contains("secret-password", StringComparison.Ordinal), "startup log should redact password query values");
    AssertFalse(entry.Contains("secret-token", StringComparison.Ordinal), "startup log should redact token query values");
    AssertFalse(entry.Contains("Bearer abc123", StringComparison.Ordinal), "startup log should redact bearer tokens");
    AssertFalse(entry.Contains("user:pass@", StringComparison.Ordinal), "startup log should redact credentialed URLs");
}

static void LastCrashDiagnosticWritesRedactedExceptionDetails()
{
    WithPreservedLastCrash(() =>
    {
        var exception = new InvalidOperationException(
            "failed url=https://user:pass@example.test/path?password=secret-password&token=secret-token Authorization: Bearer abc123",
            new ApplicationException("Cookie: sessionid=abc123"));

        LastCrashDiagnosticUtility.Write("synthetic crash RPOL password=hunter2", exception);

        var crashJson = File.ReadAllText(GetLastCrashPath());
        using var document = System.Text.Json.JsonDocument.Parse(crashJson);
        var root = document.RootElement;
        var exceptionElement = root.GetProperty("exception");
        var environment = root.GetProperty("environment");

        AssertJsonNumber(root, "schema_version", LastCrashDiagnosticUtility.CurrentSchemaVersion, "last crash should include schema version");
        AssertJsonString(root, "phase", "synthetic crash RPOL password=[REDACTED]", "last crash phase should be redacted");
        AssertJsonString(exceptionElement, "type", "InvalidOperationException", "last crash should record exception type");
        AssertContains(exceptionElement.GetProperty("message").GetString() ?? string.Empty, "[REDACTED]");
        AssertFalse(crashJson.Contains("secret-password", StringComparison.Ordinal), "last crash diagnostic should redact password query values");
        AssertFalse(crashJson.Contains("secret-token", StringComparison.Ordinal), "last crash diagnostic should redact token query values");
        AssertFalse(crashJson.Contains("Bearer abc123", StringComparison.Ordinal), "last crash diagnostic should redact bearer tokens");
        AssertFalse(crashJson.Contains("sessionid=abc123", StringComparison.Ordinal), "last crash diagnostic should redact cookie headers");
        AssertFalse(crashJson.Contains("user:pass@", StringComparison.Ordinal), "last crash diagnostic should redact credentialed URLs");
        AssertJsonString(environment, "machine_name", "[REDACTED]", "last crash should redact machine name");
        AssertJsonString(environment, "user_name", "[REDACTED]", "last crash should redact user name");
    });
}

static void StartupHealthRecordsRequiredPhaseSuccess()
{
    WithPreservedStartupHealth(() =>
    {
        StartupHealthUtility.Reset();
        StartupLoggingUtility.RunRequiredPhase("synthetic required success", () => { });

        using var document = LoadStartupHealthDocument();
        var phase = FindStartupHealthPhase(document, "synthetic required success");

        AssertJsonString(phase, "status", "succeeded", "required success should be recorded as succeeded");
        AssertJsonNumberAtLeast(phase, "elapsed_milliseconds", 0, "required success elapsed time should be recorded");
        AssertJsonNumberAtLeast(phase, "refreshed_count", 0, "required success refreshed count should be present");
        AssertJsonNumber(phase, "failed_count", 0, "required success should not increment failure count");
        AssertEqual(System.Text.Json.JsonValueKind.Null, phase.GetProperty("last_exception").ValueKind, "successful phase should not include an exception");
    });
}

static void StartupHealthWritesSchemaVersion()
{
    WithPreservedStartupHealth(() =>
    {
        StartupHealthUtility.Reset();

        using var document = LoadStartupHealthDocument();
        AssertJsonNumber(
            document.RootElement,
            "schema_version",
            StartupHealthUtility.CurrentSchemaVersion,
            "startup health should include the current schema version");
        AssertTrue(document.RootElement.TryGetProperty("phases", out var phases), "startup health should include phases");
        AssertEqual(System.Text.Json.JsonValueKind.Array, phases.ValueKind, "startup health phases should remain an array");
    });
}

static void StartupHealthRecordsRequiredPhaseFailure()
{
    WithPreservedStartupHealth(() =>
    {
        StartupHealthUtility.Reset();

        var exception = AssertThrows<InvalidOperationException>(() =>
            StartupLoggingUtility.RunRequiredPhase(
                "synthetic required failure RPOL user name=secret-user",
                () => throw new InvalidOperationException("required boom Cookie: sessionid=abc123")));

        AssertEqual("required boom Cookie: sessionid=abc123", exception.Message, "required phase should rethrow the original exception");

        using var document = LoadStartupHealthDocument();
        var phase = FindStartupHealthPhase(document, "synthetic required failure RPOL user name=[REDACTED]");
        var lastException = phase.GetProperty("last_exception");

        AssertJsonString(phase, "status", "failed", "required failure should be recorded as failed");
        AssertJsonNumber(phase, "failed_count", 1, "required failure should increment failure count");
        AssertJsonString(lastException, "type", "InvalidOperationException", "required failure should record exception type");
        AssertJsonString(lastException, "message", "required boom Cookie: [REDACTED]", "required failure should redact exception message");
    });
}

static void StartupHealthRecordsOptionalPhaseFailureWithoutThrowing()
{
    WithPreservedStartupLog(() =>
    {
        WithPreservedStartupHealth(() =>
        {
            StartupHealthUtility.Reset();

            StartupLoggingUtility.RunOptionalPhaseAsync(
                "synthetic optional failure",
                () => throw new InvalidOperationException("optional boom Authorization: Bearer abc123")).GetAwaiter().GetResult();

            using var document = LoadStartupHealthDocument();
            var phase = FindStartupHealthPhase(document, "synthetic optional failure");
            var lastException = phase.GetProperty("last_exception");

            AssertJsonString(phase, "status", "failed", "optional failure should be recorded as failed");
            AssertJsonNumber(phase, "failed_count", 1, "optional failure should increment failure count");
            AssertJsonString(lastException, "message", "optional boom Authorization: Bearer [REDACTED]", "optional failure should redact exception message");
            AssertContains(File.ReadAllText(GetStartupLogPath()), "optional boom");
            AssertFalse(File.ReadAllText(GetStartupLogPath()).Contains("Bearer abc123", StringComparison.Ordinal), "startup log should redact optional phase bearer tokens");
        });
    });
}

static void RuntimeHousekeepingRemovesStaleTempAndAtomicFiles()
{
    using var directory = TemporaryDirectory.Create();
    var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    var tempDirectory = Path.Combine(directory.Path, "temp");
    Directory.CreateDirectory(tempDirectory);
    var staleTempPath = Path.Combine(tempDirectory, "old-download.tmp");
    var atomicPath = AtomicFileUtility.CreateTempPath(Path.Combine(directory.Path, "keyword-index.json"));
    File.WriteAllText(staleTempPath, "temp");
    File.WriteAllText(atomicPath, "atomic");
    SetLastWriteTimeUtc(staleTempPath, now - TimeSpan.FromHours(2));
    SetLastWriteTimeUtc(atomicPath, now - TimeSpan.FromHours(2));

    var report = RuntimeHousekeepingUtility.Clean(
        directory.Path,
        now,
        new RuntimeHousekeepingOptions
        {
            StaleTempFileAge = TimeSpan.FromHours(1),
            OrphanedAtomicFileAge = TimeSpan.FromHours(1)
        });

    AssertFalse(File.Exists(staleTempPath), "stale temp file should be removed");
    AssertFalse(File.Exists(atomicPath), "stale atomic temp file should be removed");
    AssertEqual(2, report.RemovedFileCount, "unexpected removed file count");
    AssertTrue(report.ReclaimedBytes > 0, "expected reclaimed bytes to be reported");
}

static void RuntimeHousekeepingPreservesFreshAndUnrelatedTmpFiles()
{
    using var directory = TemporaryDirectory.Create();
    var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    var tempDirectory = Path.Combine(directory.Path, "temp");
    Directory.CreateDirectory(tempDirectory);
    var freshTempPath = Path.Combine(tempDirectory, "fresh-download.tmp");
    var unrelatedTmpPath = Path.Combine(directory.Path, "cache.txt.tmp");
    File.WriteAllText(freshTempPath, "temp");
    File.WriteAllText(unrelatedTmpPath, "not an atomic temp file");
    SetLastWriteTimeUtc(freshTempPath, now - TimeSpan.FromMinutes(5));
    SetLastWriteTimeUtc(unrelatedTmpPath, now - TimeSpan.FromDays(7));

    var report = RuntimeHousekeepingUtility.Clean(
        directory.Path,
        now,
        new RuntimeHousekeepingOptions
        {
            StaleTempFileAge = TimeSpan.FromHours(1),
            OrphanedAtomicFileAge = TimeSpan.FromHours(1)
        });

    AssertTrue(File.Exists(freshTempPath), "fresh temp file should be preserved");
    AssertTrue(File.Exists(unrelatedTmpPath), "unrelated tmp file should be preserved");
    AssertEqual(0, report.RemovedFileCount, "fresh/unrelated files should not be removed");
}

static void RuntimeHousekeepingRemovesOldQuarantinedJsonOnly()
{
    using var directory = TemporaryDirectory.Create();
    var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    var oldBadPath = Path.Combine(directory.Path, "keyword-index.bad-20260601-010203-004.json");
    var freshBadPath = Path.Combine(directory.Path, "settings.bad-20260702-010203-004.json");
    var normalJsonPath = Path.Combine(directory.Path, "settings.json");
    File.WriteAllText(oldBadPath, "{}");
    File.WriteAllText(freshBadPath, "{}");
    File.WriteAllText(normalJsonPath, "{}");
    SetLastWriteTimeUtc(oldBadPath, now - TimeSpan.FromDays(15));
    SetLastWriteTimeUtc(freshBadPath, now - TimeSpan.FromDays(1));
    SetLastWriteTimeUtc(normalJsonPath, now - TimeSpan.FromDays(30));

    var report = RuntimeHousekeepingUtility.Clean(
        directory.Path,
        now,
        new RuntimeHousekeepingOptions
        {
            QuarantinedJsonRetention = TimeSpan.FromDays(14)
        });

    AssertFalse(File.Exists(oldBadPath), "old quarantined json should be removed");
    AssertTrue(File.Exists(freshBadPath), "fresh quarantined json should be preserved");
    AssertTrue(File.Exists(normalJsonPath), "normal json should be preserved");
    AssertEqual(1, report.RemovedFileCount, "unexpected removed quarantine count");
}

static void RuntimeHousekeepingRemovesOldBackupFilesOnly()
{
    using var directory = TemporaryDirectory.Create();
    var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    var oldBackupPath = Path.Combine(directory.Path, "keyword-index.bak-20260601-010203-004.json");
    var freshBackupPath = Path.Combine(directory.Path, "settings.bak-20260702-010203-004.json");
    var normalJsonPath = Path.Combine(directory.Path, "settings.json");
    File.WriteAllText(oldBackupPath, "{}");
    File.WriteAllText(freshBackupPath, "{}");
    File.WriteAllText(normalJsonPath, "{}");
    SetLastWriteTimeUtc(oldBackupPath, now - TimeSpan.FromDays(31));
    SetLastWriteTimeUtc(freshBackupPath, now - TimeSpan.FromDays(1));
    SetLastWriteTimeUtc(normalJsonPath, now - TimeSpan.FromDays(60));

    var report = RuntimeHousekeepingUtility.Clean(
        directory.Path,
        now,
        new RuntimeHousekeepingOptions
        {
            RuntimeBackupRetention = TimeSpan.FromDays(30)
        });

    AssertFalse(File.Exists(oldBackupPath), "old backup should be removed");
    AssertTrue(File.Exists(freshBackupPath), "fresh backup should be preserved");
    AssertTrue(File.Exists(normalJsonPath), "normal json should be preserved");
    AssertEqual(1, report.RemovedFileCount, "unexpected removed backup count");
}

static void RuntimeHousekeepingRotatesOversizedStartupLog()
{
    using var directory = TemporaryDirectory.Create();
    var logPath = Path.Combine(directory.Path, StartupLoggingUtility.LogFileName);
    var archivePath = Path.Combine(directory.Path, "startup-errors.log.1");
    File.WriteAllText(logPath, new string('x', 128));

    var report = RuntimeHousekeepingUtility.Clean(
        directory.Path,
        new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero),
        new RuntimeHousekeepingOptions
        {
            MaxStartupLogBytes = 32
        });

    AssertTrue(report.StartupLogRotated, "oversized startup log should be rotated");
    AssertTrue(File.Exists(logPath), "active startup log should be recreated after rotation");
    AssertTrue(File.Exists(archivePath), "startup log archive should be written");
    AssertEqual(128L, new FileInfo(archivePath).Length, "archive should contain the original log");
    AssertContains(File.ReadAllText(logPath), "rotated to startup-errors.log.1");
}

static void RuntimeHousekeepingSkipsLockedFiles()
{
    using var directory = TemporaryDirectory.Create();
    var now = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    var tempDirectory = Path.Combine(directory.Path, "temp");
    Directory.CreateDirectory(tempDirectory);
    var lockedPath = Path.Combine(tempDirectory, "locked.tmp");
    File.WriteAllText(lockedPath, "locked");
    SetLastWriteTimeUtc(lockedPath, now - TimeSpan.FromDays(2));

    using (new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        var report = RuntimeHousekeepingUtility.Clean(
            directory.Path,
            now,
            new RuntimeHousekeepingOptions
            {
                StaleTempFileAge = TimeSpan.FromHours(1)
            });

        AssertTrue(File.Exists(lockedPath), "locked file should be preserved");
        AssertEqual(1, report.SkippedFileCount, "locked file should be counted as skipped");
    }
}

static void UiOperationFailureReporterLogsStatusAndDialog()
{
    var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
    var hadStartupLog = File.Exists(startupLogPath);
    var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;
    var statusMessages = new List<string>();
    var dialogs = new List<(string Title, string Message)>();

    try
    {
        if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }

        UiOperationFailureReporter.ReportAsync(
            new UiOperationFailure(
                "login info display",
                "Login info unavailable",
                "Login Info Error",
                new InvalidOperationException("cached login info is malformed"),
                ShowDialog: true),
            statusMessages.Add,
            (title, message) => dialogs.Add((title, message))).GetAwaiter().GetResult();

        AssertEqual(1, statusMessages.Count, "expected reporter to set one status message");
        AssertEqual(
            "Login info unavailable: cached login info is malformed",
            statusMessages[0],
            "unexpected reporter status message");
        AssertEqual(1, dialogs.Count, "expected reporter to show one warning dialog");
        AssertEqual("Login Info Error", dialogs[0].Title, "unexpected dialog title");
        AssertEqual("cached login info is malformed", dialogs[0].Message, "unexpected dialog message");

        var log = File.ReadAllText(startupLogPath);
        AssertContains(log, "login info display");
        AssertContains(log, "InvalidOperationException");
        AssertContains(log, "cached login info is malformed");
    }
    finally
    {
        if (hadStartupLog)
        {
            File.WriteAllText(startupLogPath, originalStartupLog);
        }
        else if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }
    }
}

static void StatusBarActivityIndicatorTracksAsyncOperations()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        var indicator = (ToolStripStatusLabel)(GetPrivateField(form, "statusActivityToolStripStatusLabel")
            ?? throw new InvalidOperationException("status activity indicator was null."));

        AssertEqual(0, (int)(GetPrivateField(form, "_activeAsyncOperationCount") ?? -1), "activity count should start at zero");
        AssertEqual(string.Empty, indicator.Text ?? string.Empty, "activity indicator text should start empty");
        using var firstActivity = (IDisposable)(InvokePrivateMethod(form, "BeginStatusBarActivity")
            ?? throw new InvalidOperationException("first activity scope was null."));

        AssertEqual(1, (int)(GetPrivateField(form, "_activeAsyncOperationCount") ?? -1), "activity count should increment while async work is active");
        AssertFalse(string.IsNullOrWhiteSpace(indicator.Text), "activity indicator should display an animation frame while active");
        var firstFrame = indicator.Text ?? string.Empty;
        InvokePrivateMethod(form, "AdvanceStatusActivityIndicator");
        AssertFalse(
            string.Equals(firstFrame, indicator.Text, StringComparison.Ordinal),
            "activity indicator should advance animation frames");

        using (var secondActivity = (IDisposable)(InvokePrivateMethod(form, "BeginStatusBarActivity")
            ?? throw new InvalidOperationException("second activity scope was null.")))
        {
            firstActivity.Dispose();
            AssertEqual(1, (int)(GetPrivateField(form, "_activeAsyncOperationCount") ?? -1), "activity count should remain positive until all async work completes");
        }

        AssertEqual(0, (int)(GetPrivateField(form, "_activeAsyncOperationCount") ?? -1), "activity count should return to zero after all async work completes");
        AssertEqual(string.Empty, indicator.Text ?? string.Empty, "activity indicator text should clear when idle");
    });
}

static void BackgroundTaskSupervisorSuppressesDuplicatePhases()
{
    using var supervisor = new BackgroundTaskSupervisor();
    var releaseTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var startCount = 0;

    AssertTrue(
        supervisor.TryStart("duplicate phase", async cancellationToken =>
        {
            Interlocked.Increment(ref startCount);
            await releaseTask.Task.WaitAsync(cancellationToken);
        }),
        "expected first background task to start");

    WaitForCondition(() => Volatile.Read(ref startCount) == 1, "background task did not start");
    AssertTrue(supervisor.IsRunning("duplicate phase"), "expected background phase to be running");
    AssertFalse(
        supervisor.TryStart("duplicate phase", _ =>
        {
            Interlocked.Increment(ref startCount);
            return Task.CompletedTask;
        }),
        "expected duplicate background phase to be suppressed");

    releaseTask.SetResult();
    WaitForCondition(() => !supervisor.IsRunning("duplicate phase"), "background task did not complete");
    AssertEqual(1, Volatile.Read(ref startCount), "duplicate phase should not start twice");
}

static void BackgroundTaskSupervisorLogsFailures()
{
    var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
    var hadStartupLog = File.Exists(startupLogPath);
    var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

    try
    {
        if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }

        using var supervisor = new BackgroundTaskSupervisor();
        AssertTrue(
            supervisor.TryStart("supervised failure", _ => throw new InvalidOperationException("supervised boom")),
            "expected failing background task to start");
        WaitForCondition(() => !supervisor.IsRunning("supervised failure"), "failing background task did not complete");

        var log = File.ReadAllText(startupLogPath);
        AssertContains(log, "supervised failure");
        AssertContains(log, "InvalidOperationException");
        AssertContains(log, "supervised boom");
    }
    finally
    {
        if (hadStartupLog)
        {
            File.WriteAllText(startupLogPath, originalStartupLog);
        }
        else if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }
    }
}

static void BackgroundTaskSupervisorCancelsRunningTasksOnDispose()
{
    var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var supervisor = new BackgroundTaskSupervisor();

    AssertTrue(
        supervisor.TryStart("cancellable phase", async cancellationToken =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.SetResult();
                throw;
            }
        }),
        "expected cancellable background task to start");

    supervisor.Dispose();
    AssertTrue(cancellationObserved.Task.Wait(TimeSpan.FromSeconds(2)), "expected disposal to cancel running background task");
}

static void AtomicFilePromotionPreservesExistingDestinationOnLockedReplacement()
{
    using var directory = TemporaryDirectory.Create();
    var destinationPath = Path.Combine(directory.Path, "cache.txt");
    var tempPath = Path.Combine(directory.Path, "cache.txt.tmp");
    File.WriteAllText(destinationPath, "old cache");
    File.WriteAllText(tempPath, "new cache");

    using (new FileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        try
        {
            AtomicFileUtility.PromoteTempFileAsync(tempPath, destinationPath).GetAwaiter().GetResult();
            throw new InvalidOperationException("expected locked destination promotion to fail");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    AssertEqual("old cache", File.ReadAllText(destinationPath), "existing cache should survive failed promotion");
    AssertTrue(File.Exists(tempPath), "temp file should remain for caller cleanup after failed promotion");
}

static void AtomicFilePromotionCreatesBoundedRuntimeBackups()
{
    using var directory = TemporaryDirectory.Create();
    var destinationPath = Path.Combine(directory.Path, "keyword-index.json");
    File.WriteAllText(destinationPath, """{"version":0}""");

    for (var index = 1; index <= 7; index++)
    {
        AtomicFileUtility.WriteAllText(destinationPath, $$"""{"version":{{index}}}""");
        Thread.Sleep(2);
    }

    AssertEqual("""{"version":7}""", File.ReadAllText(destinationPath), "destination should contain newest content");
    var backups = Directory.GetFiles(directory.Path, "keyword-index.bak-*.json");
    AssertEqual(5, backups.Length, "runtime backup retention should keep the newest five backups");
    AssertTrue(
        backups.Any(path => File.ReadAllText(path).Contains("\"version\":6", StringComparison.Ordinal)),
        "newest previous content should be backed up");
    AssertFalse(
        backups.Any(path => File.ReadAllText(path).Contains("\"version\":0", StringComparison.Ordinal)),
        "oldest backup should be pruned");
}

static void NetworkRequestRetriesTransientFailures()
{
    var attempts = 0;
    using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler(async (_, _) =>
    {
        attempts++;
        await Task.Yield();
        return attempts == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok")
            };
    }));

    using var response = NetworkRequestUtility.SendAsync(
        httpClient,
        () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/retry"),
        policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 2, TimeSpan.Zero)).GetAwaiter().GetResult();

    AssertEqual(HttpStatusCode.OK, response.StatusCode, "expected retry to return successful response");
    AssertEqual(2, attempts, "expected transient response to be retried once");
}

static void OutboundNetworkDiagnosticsRecordsSanitizedSuccessEndpoint()
{
    var diagnosticsPath = Path.Combine(
        Path.GetDirectoryName(typeof(NetworkRequestUtility).Assembly.Location)
            ?? throw new InvalidOperationException("Unable to resolve test assembly directory."),
        OutboundNetworkDiagnosticsUtility.DiagnosticsFileName);
    WithPreservedFileAbsent(diagnosticsPath, () =>
    {
        OutboundNetworkDiagnosticsUtility.Reset();
        using var allowlistScope = NetworkUrlAllowlistUtility.UseValidationOverrideForTests((uri, purpose) =>
        {
            if (purpose == NetworkUrlPurpose.Generic
                && string.Equals(uri.Host, "example.test", StringComparison.OrdinalIgnoreCase))
            {
                return NetworkUrlAllowlistValidation.Allowed(uri);
            }

            return null;
        });
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok")
            })));

        using var response = NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://example.test/search/path?token=secret-token&password=secret-password"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult();

        AssertEqual(HttpStatusCode.OK, response.StatusCode, "expected successful response");
        var diagnosticsJson = File.ReadAllText(diagnosticsPath);
        AssertFalse(diagnosticsJson.Contains("secret-token", StringComparison.Ordinal), "outbound diagnostics should not persist token query values");
        AssertFalse(diagnosticsJson.Contains("secret-password", StringComparison.Ordinal), "outbound diagnostics should not persist password query values");

        using var document = System.Text.Json.JsonDocument.Parse(diagnosticsJson);
        var endpoint = document.RootElement.GetProperty("endpoints")[0];
        AssertEqual("Generic", endpoint.GetProperty("purpose").GetString() ?? string.Empty, "unexpected network purpose");
        AssertEqual("example.test", endpoint.GetProperty("host").GetString() ?? string.Empty, "unexpected host");
        AssertEqual("/search/path", endpoint.GetProperty("path").GetString() ?? string.Empty, "diagnostics should record path without query values");
        AssertTrue(endpoint.GetProperty("query_present").GetBoolean(), "diagnostics should remember that a query string existed");
        AssertEqual(1, endpoint.GetProperty("total_requests").GetInt32(), "expected one recorded request");
        AssertEqual(1, endpoint.GetProperty("success_count").GetInt32(), "expected one successful request");
        AssertEqual(0, endpoint.GetProperty("failure_count").GetInt32(), "expected no failures");
    });
}

static void OutboundNetworkDiagnosticsRecordsFailureCounts()
{
    var diagnosticsPath = Path.Combine(
        Path.GetDirectoryName(typeof(NetworkRequestUtility).Assembly.Location)
            ?? throw new InvalidOperationException("Unable to resolve test assembly directory."),
        OutboundNetworkDiagnosticsUtility.DiagnosticsFileName);
    WithPreservedFileAbsent(diagnosticsPath, () =>
    {
        OutboundNetworkDiagnosticsUtility.Reset();
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = "Service Unavailable"
            })));

        using var response = NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero),
            purpose: NetworkUrlPurpose.Rpol).GetAwaiter().GetResult();

        AssertEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode, "expected terminal 503 response");

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(diagnosticsPath));
        var endpoint = document.RootElement.GetProperty("endpoints")[0];
        AssertEqual("Rpol", endpoint.GetProperty("purpose").GetString() ?? string.Empty, "unexpected network purpose");
        AssertEqual("/game.php", endpoint.GetProperty("path").GetString() ?? string.Empty, "unexpected RPOL path");
        AssertEqual(1, endpoint.GetProperty("total_requests").GetInt32(), "expected one recorded request");
        AssertEqual(0, endpoint.GetProperty("success_count").GetInt32(), "expected no successes");
        AssertEqual(1, endpoint.GetProperty("failure_count").GetInt32(), "expected one failure");
        AssertEqual(503, endpoint.GetProperty("last_status_code").GetInt32(), "expected terminal HTTP status to be recorded");
        AssertEqual("failure", endpoint.GetProperty("last_outcome").GetString() ?? string.Empty, "expected failure outcome");
        AssertTrue(endpoint.GetProperty("last_failure_summary").GetString()?.Contains("HTTP 503", StringComparison.Ordinal) ?? false, "expected failure summary to capture status");
    });
}

static void NetworkRequestRejectsDisallowedHostBeforeSend()
{
    var attempts = 0;
    using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
    {
        attempts++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }));

    var exception = AssertThrows<InvalidOperationException>(() =>
        NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://unexpected.example.test/blocked"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult());

    AssertContains(exception.Message, "not allowed");
    AssertEqual(0, attempts, "disallowed requests should be rejected before the HTTP handler runs");
}

static void NetworkRequestDoesNotRetryUnauthorized()
{
    var attempts = 0;
    using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
    {
        attempts++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }));

    using var response = NetworkRequestUtility.SendAsync(
        httpClient,
        () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/auth"),
        policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 3, TimeSpan.Zero)).GetAwaiter().GetResult();

    AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode, "expected unauthorized response to be returned to caller");
    AssertEqual(1, attempts, "unauthorized response should not be retried");
}

static void NetworkCircuitBreakerOpensAfterRepeatedTerminalFailures()
{
    WithPreservedStartupLog(() =>
    {
        NetworkRequestUtility.ResetCircuitBreakersForTests();
        var attempts = 0;
        using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = "Service Unavailable"
            });
        }));

        using (NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/breaker-one"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult())
        {
        }

        using (NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/breaker-two"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult())
        {
        }

        var exception = AssertThrows<NetworkRequestException>(() =>
            NetworkRequestUtility.SendAsync(
                httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/breaker-three"),
                policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult());

        AssertEqual(NetworkFailureKind.CircuitOpen, exception.Kind, "expected repeated terminal failures to open the circuit breaker");
        AssertEqual(2, attempts, "open circuit breaker should short-circuit before sending another request");
        AssertContains(File.ReadAllText(GetStartupLogPath()), "network circuit breaker");
        NetworkRequestUtility.ResetCircuitBreakersForTests();
    });
}

static void NetworkCircuitBreakerClearsAfterSuccess()
{
    NetworkRequestUtility.ResetCircuitBreakersForTests();
    var attempts = 0;
    using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
    {
        attempts++;
        return Task.FromResult(attempts == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK));
    }));

    using (NetworkRequestUtility.SendAsync(
        httpClient,
        () => new HttpRequestMessage(HttpMethod.Get, "https://publish.obsidian.md/breaker-clear-one"),
        policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult())
    {
    }

    using (NetworkRequestUtility.SendAsync(
        httpClient,
        () => new HttpRequestMessage(HttpMethod.Get, "https://publish.obsidian.md/breaker-clear-two"),
        policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult())
    {
    }

    using var response = NetworkRequestUtility.SendAsync(
        httpClient,
        () => new HttpRequestMessage(HttpMethod.Get, "https://publish.obsidian.md/breaker-clear-three"),
        policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult();

    AssertEqual(HttpStatusCode.OK, response.StatusCode, "successful request should clear prior circuit-breaker failures");
    AssertEqual(3, attempts, "successful request should allow the next related request to be sent");
    NetworkRequestUtility.ResetCircuitBreakersForTests();
}

static void StartupDependencyMatrixClassifiesTerminalNetworkFailure()
{
    using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
        throw new HttpRequestException("synthetic DNS failure")));

    var exception = AssertThrows<NetworkRequestException>(() =>
        NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/game.php?gi=80170"),
            policy: new NetworkRequestPolicy(
                TimeSpan.FromSeconds(1),
                MaxAttempts: 1,
                TimeSpan.Zero)).GetAwaiter().GetResult());

    AssertEqual(NetworkFailureKind.Unavailable, exception.Kind, "terminal request failures should be classified as unavailable");
    AssertContains(exception.Message, "synthetic DNS failure");
}

static void NetworkRequestWrapsTimeout()
{
    using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler(async (_, cancellationToken) =>
    {
        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }));

    var exception = AssertThrows<NetworkRequestException>(() =>
        NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/timeout"),
            policy: new NetworkRequestPolicy(TimeSpan.FromMilliseconds(20), MaxAttempts: 1, TimeSpan.Zero)).GetAwaiter().GetResult());

    AssertEqual(NetworkFailureKind.TimedOut, exception.Kind, "expected timeout failures to be classified");
}

static void NetworkRequestPreservesCallerCancellation()
{
    using var cancellation = new CancellationTokenSource();
    using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler(async (_, cancellationToken) =>
    {
        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }));

    cancellation.Cancel();

    AssertThrows<OperationCanceledException>(() =>
        NetworkRequestUtility.SendAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/cancel"),
            policy: new NetworkRequestPolicy(TimeSpan.FromSeconds(1), MaxAttempts: 3, TimeSpan.Zero),
            cancellationToken: cancellation.Token).GetAwaiter().GetResult());
}

static void NetworkAllowlistRejectsCredentialedAndEscapedHosts()
{
    var credentialed = NetworkUrlAllowlistUtility.Validate("https://user:password@rpol.net/game.php", NetworkUrlPurpose.Rpol);
    var escapedHost = NetworkUrlAllowlistUtility.Validate("https://rpol%2enet/game.php", NetworkUrlPurpose.Rpol);
    var threadDisplay = NetworkUrlAllowlistUtility.Validate("https://rpol.net/display.cgi?gi=80170&ti=12&msgpage=&show=all", NetworkUrlPurpose.Rpol);

    AssertFalse(credentialed.IsAllowed, "credentialed URLs should not be allowed");
    AssertContains(credentialed.RejectionReason ?? string.Empty, "credentials");
    AssertFalse(escapedHost.IsAllowed, "escaped host URLs should not be allowed");
    AssertTrue(threadDisplay.IsAllowed, "RPOL thread display URLs should remain valid local search results");
}

static void NetworkAllowlistAcceptsObsidianPublishContentHosts()
{
    var page = NetworkUrlAllowlistUtility.Validate(
        "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
        NetworkUrlPurpose.ObsidianPublish);
    var markdown = NetworkUrlAllowlistUtility.Validate(
        "https://publish-01.obsidian.md/access/1113217a28a5bfdcc9fbe8e6d82b27ac/Intentional%20Orphans/XP%20Tracking.md",
        NetworkUrlPurpose.ObsidianPublish);
    var rejected = NetworkUrlAllowlistUtility.Validate(
        "https://help.obsidian.md/access/1113217a28a5bfdcc9fbe8e6d82b27ac/Intentional%20Orphans/XP%20Tracking.md",
        NetworkUrlPurpose.ObsidianPublish);

    AssertTrue(page.IsAllowed, "public Obsidian Publish pages should remain allowed");
    AssertTrue(markdown.IsAllowed, "Obsidian Publish generated markdown access URLs should be allowed");
    AssertTrue(
        NetworkUrlAllowlistUtility.IsObsidianPublishHost(new Uri("https://publish-01.obsidian.md/")),
        "generated Obsidian Publish content hosts should be recognized");
    AssertFalse(rejected.IsAllowed, "non-Publish obsidian.md hosts should not be accepted");
}

static void NetworkAllowlistRejectsUnexpectedHostedSettingsPath()
{
    var allowed = NetworkUrlAllowlistUtility.Validate(
        "https://bryanmiller.us/scarlethorizons/settings.local.json",
        NetworkUrlPurpose.PlayerAssistantHostedSettings);
    var rejected = NetworkUrlAllowlistUtility.Validate(
        "https://bryanmiller.us/scarlethorizons/other-settings.json",
        NetworkUrlPurpose.PlayerAssistantHostedSettings);

    AssertTrue(allowed.IsAllowed, "expected the configured hosted settings path to remain allowed");
    AssertFalse(rejected.IsAllowed, "unexpected hosted settings paths should be rejected");
    AssertContains(rejected.RejectionReason ?? string.Empty, "settings.local.json");
}

static void NetworkAllowlistRejectsUnexpectedUpdatePath()
{
    var allowedManifest = NetworkUrlAllowlistUtility.Validate(
        "https://bryanmiller.us/scarlethorizons/p-assist-updates.json",
        NetworkUrlPurpose.PlayerAssistantUpdate);
    var allowedArchive = NetworkUrlAllowlistUtility.Validate(
        "https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.zip",
        NetworkUrlPurpose.PlayerAssistantUpdate);
    var rejected = NetworkUrlAllowlistUtility.Validate(
        "https://bryanmiller.us/private/p-assist-0.9.1.zip",
        NetworkUrlPurpose.PlayerAssistantUpdate);

    AssertTrue(allowedManifest.IsAllowed, "expected the signed update manifest path to remain allowed");
    AssertTrue(allowedArchive.IsAllowed, "expected approved update archive paths to remain allowed");
    AssertFalse(rejected.IsAllowed, "unexpected update paths should be rejected");
    AssertContains(rejected.RejectionReason ?? string.Empty, "/scarlethorizons/");
}

static void NetworkAllowlistGenericPolicyRejectsUnrelatedUpdateHostPaths()
{
    var genericAllowed = NetworkUrlAllowlistUtility.Validate("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.exe");
    var regionalMap = NetworkUrlAllowlistUtility.Validate("https://bryanmiller.us/scarlethorizons/northernreaches.png");
    var blogRegionalMap = NetworkUrlAllowlistUtility.Validate("https://bryanmiller.us/blog/content/bryan/blog/images/rpg-maps/northernreaches.png");
    var genericRejected = NetworkUrlAllowlistUtility.Validate("https://bryanmiller.us/random-note.txt");

    AssertTrue(genericAllowed.IsAllowed, "generic allowlist should still permit approved update artifact paths");
    AssertTrue(regionalMap.IsAllowed, "generic allowlist should permit the hosted regional map image");
    AssertTrue(blogRegionalMap.IsAllowed, "generic allowlist should permit the hosted blog regional map image");
    AssertFalse(genericRejected.IsAllowed, "generic allowlist should reject unrelated paths on an otherwise approved host");
}

static void NetworkResponseLimitsDefineDefaults()
{
    AssertTrue(NetworkResponseContentLimit.Html.MaxBytes > 0, "HTML response limit should be positive");
    AssertTrue(NetworkResponseContentLimit.Markdown.MaxBytes > 0, "markdown response limit should be positive");
    AssertTrue(NetworkResponseContentLimit.JsonCache.MaxBytes > 0, "JSON cache response limit should be positive");
    AssertTrue(NetworkResponseContentLimit.Image.MaxBytes > 0, "image response limit should be positive");
    AssertTrue(
        NetworkResponseContentLimit.Image.MaxBytes > NetworkResponseContentLimit.Markdown.MaxBytes,
        "image downloads should allow larger payloads than markdown documents");
}

static void NetworkResponseLimitRejectsOversizedHtmlHeader()
{
    using var content = new ByteArrayContent([]);
    content.Headers.ContentLength = NetworkResponseContentLimit.Html.MaxBytes + 1;

    var exception = AssertThrows<NetworkResponseTooLargeException>(() =>
        NetworkRequestUtility.ReadStringAsync(
            content,
            NetworkResponseContentLimit.Html).GetAwaiter().GetResult());

    AssertContains(exception.Message, "HTML response");
}

static void NetworkResponseLimitRejectsOversizedMarkdownStream()
{
    using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("abcdef"));
    using var destination = new MemoryStream();
    var limit = new NetworkResponseContentLimit("markdown response", 5);

    var exception = AssertThrows<NetworkResponseTooLargeException>(() =>
        NetworkRequestUtility.CopyToAsync(source, destination, limit).GetAwaiter().GetResult());

    AssertContains(exception.Message, "markdown response");
    AssertEqual(0L, destination.Length, "oversized markdown stream should not be written after limit breach");
}

static void NetworkResponseLimitRejectsOversizedJsonCacheStream()
{
    using var content = new ChunkedHttpContent(System.Text.Encoding.UTF8.GetBytes("""{"oversized":true}"""));
    var limit = new NetworkResponseContentLimit("JSON cache response", 8);

    var exception = AssertThrows<NetworkResponseTooLargeException>(() =>
        NetworkRequestUtility.ReadBytesAsync(content, limit).GetAwaiter().GetResult());

    AssertContains(exception.Message, "JSON cache response");
}

static void NetworkResponseLimitRejectsOversizedImageHeader()
{
    using var content = new ByteArrayContent([]);
    content.Headers.ContentLength = NetworkResponseContentLimit.Image.MaxBytes + 1;

    var exception = AssertThrows<NetworkResponseTooLargeException>(() =>
        NetworkRequestUtility.CopyToAsync(
            content,
            Stream.Null,
            NetworkResponseContentLimit.Image).GetAwaiter().GetResult());

    AssertContains(exception.Message, "image response");
}

static void MarkdownAsyncFetchPreservesCallerCancellation()
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    AssertThrows<OperationCanceledException>(() =>
        MarkdownUtility.GetMarkdownFromUrlAsync(
            "https://publish.obsidian.md/cancel",
            cancellation.Token).GetAwaiter().GetResult());
}

static void RuntimeArtifactLoaderQuarantinesMalformedJson()
{
    using var directory = TemporaryDirectory.Create();
    var artifactPath = Path.Combine(directory.Path, "runtime-cache.json");
    File.WriteAllText(artifactPath, "{ not valid json");
    var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
    var hadStartupLog = File.Exists(startupLogPath);
    var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

    try
    {
        if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }

        var loaded = RuntimeArtifactUtility.TryLoadJson<Dictionary<string, string>>(
            artifactPath,
            "runtime artifact test",
            out var value);

        AssertFalse(loaded, "malformed runtime artifact should not load");
        AssertTrue(value is null, "malformed runtime artifact should return a null value");
        AssertFalse(File.Exists(artifactPath), "malformed runtime artifact should be moved out of the active path");

        var badFiles = Directory.GetFiles(directory.Path, "runtime-cache.bad-*.json");
        AssertEqual(1, badFiles.Length, "expected one quarantined runtime artifact");
        AssertEqual("{ not valid json", File.ReadAllText(badFiles[0]), "quarantined artifact should preserve original content");

        var startupLog = File.ReadAllText(startupLogPath);
        AssertContains(startupLog, "runtime artifact test");
        AssertContains(startupLog, badFiles[0]);
    }
    finally
    {
        if (hadStartupLog)
        {
            File.WriteAllText(startupLogPath, originalStartupLog);
        }
        else if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }
    }
}

static void RuntimeArtifactLoaderRestoresNewestValidBackup()
{
    using var directory = TemporaryDirectory.Create();
    var artifactPath = Path.Combine(directory.Path, "runtime-cache.json");
    var olderBackupPath = Path.Combine(directory.Path, "runtime-cache.bak-20260701-010203-001.json");
    var invalidBackupPath = Path.Combine(directory.Path, "runtime-cache.bak-20260702-010203-001.json");
    var newestBackupPath = Path.Combine(directory.Path, "runtime-cache.bak-20260703-010203-001.json");
    File.WriteAllText(artifactPath, "{ not valid json");
    File.WriteAllText(olderBackupPath, """{"value":"old"}""");
    File.WriteAllText(invalidBackupPath, "{ also invalid");
    File.WriteAllText(newestBackupPath, """{"value":"restored"}""");
    SetLastWriteTimeUtc(olderBackupPath, new DateTimeOffset(2026, 7, 1, 1, 2, 3, TimeSpan.Zero));
    SetLastWriteTimeUtc(invalidBackupPath, new DateTimeOffset(2026, 7, 2, 1, 2, 3, TimeSpan.Zero));
    SetLastWriteTimeUtc(newestBackupPath, new DateTimeOffset(2026, 7, 3, 1, 2, 3, TimeSpan.Zero));

    WithPreservedStartupLog(() =>
    {
        var loaded = RuntimeArtifactUtility.TryLoadJson<Dictionary<string, string>>(
            artifactPath,
            "runtime artifact restore test",
            out var value);

        AssertTrue(loaded, "malformed runtime artifact should restore from the newest valid backup");
        AssertTrue(value is not null, "restored runtime artifact should return a value");
        AssertEqual("restored", value!["value"], "unexpected restored value");
        AssertEqual("""{"value":"restored"}""", File.ReadAllText(artifactPath), "active artifact should be restored from backup");
        AssertEqual(0, Directory.GetFiles(directory.Path, "runtime-cache.bad-*.json").Length, "restored artifact should not be quarantined");

        var startupLog = File.ReadAllText(GetStartupLogPath());
        AssertContains(startupLog, "runtime artifact restore test");
        AssertContains(startupLog, "Restored runtime artifact");
    });
}

static void StartupDependencyMatrixLogsLockedRuntimeArtifactFailures()
{
    using var directory = TemporaryDirectory.Create();
    var artifactPath = Path.Combine(directory.Path, "locked-artifact.json");
    File.WriteAllText(artifactPath, "{}");

    WithPreservedStartupLog(() =>
    {
        using (new FileStream(artifactPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var loaded = RuntimeArtifactUtility.TryLoadJson<Dictionary<string, string>>(
                artifactPath,
                "locked runtime artifact test",
                out var value);

            AssertFalse(loaded, "locked runtime artifact should fail without throwing");
            AssertTrue(value is null, "locked runtime artifact should return no value");
            AssertTrue(File.Exists(artifactPath), "locked runtime artifact should remain active when quarantine cannot move it");
        }

        var startupLog = File.ReadAllText(GetStartupLogPath());
        AssertContains(startupLog, "locked runtime artifact test");
        AssertContains(startupLog, "runtime artifact quarantine");
    });
}

static void LoginInfoCacheLoadReturnsEmptyForMalformedJson()
{
    using var directory = TemporaryDirectory.Create();
    var loginInfoPath = Path.Combine(directory.Path, "login-info.json");
    File.WriteAllText(loginInfoPath, "{ not valid json");
    var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
    var hadStartupLog = File.Exists(startupLogPath);
    var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

    try
    {
        if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }

        var rows = (TheCastLoginInfo[]?)InvokeStaticMethod(typeof(Form1), "LoadLoginInfoJson", loginInfoPath)
            ?? throw new InvalidOperationException("LoadLoginInfoJson returned null.");

        AssertEqual(0, rows.Length, "malformed login-info cache should return an empty row set");
        AssertFalse(File.Exists(loginInfoPath), "malformed login-info cache should be moved out of the active path");

        var badFiles = Directory.GetFiles(directory.Path, "login-info.bad-*.json");
        AssertEqual(1, badFiles.Length, "expected one quarantined login-info cache");

        var startupLog = File.ReadAllText(startupLogPath);
        AssertContains(startupLog, "login info cache load");
        AssertContains(startupLog, badFiles[0]);
    }
    finally
    {
        if (hadStartupLog)
        {
            File.WriteAllText(startupLogPath, originalStartupLog);
        }
        else if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }
    }
}

static void AssetManifestLoadReturnsEmptyForMalformedJson()
{
    var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
    var hadStartupLog = File.Exists(startupLogPath);
    var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

    try
    {
        if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }

        var args = new object[] { "{ not valid json", null! };
        var loaded = (bool)(InvokeStaticMethod(
            typeof(PlayerCharacterAssetUtility),
            "TryDeserializeAssetManifest",
            args) ?? throw new InvalidOperationException("TryDeserializeAssetManifest returned null."));

        AssertFalse(loaded, "malformed asset manifest should not load");
        AssertTrue(args[1] is Dictionary<string, string> manifest && manifest.Count == 0, "malformed asset manifest should return an empty dictionary");

        var startupLog = File.ReadAllText(startupLogPath);
        AssertContains(startupLog, "asset manifest load");
        AssertContains(startupLog, "asset manifest could not be parsed");
    }
    finally
    {
        if (hadStartupLog)
        {
            File.WriteAllText(startupLogPath, originalStartupLog);
        }
        else if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }
    }
}

static void PublishedAssetFallbackResolvesTransclusionWithoutAttachmentIndex()
{
    var cachePaths = new[]
    {
        "Assets/hero-tokens/neria-token.webp",
        "PCs/Neria Silverdale.md"
    };

    var assets = (Dictionary<string, string>?)InvokeStaticMethod(
        typeof(ObsidianPublishUtility),
        "GetAssetPathsByFileName",
        (object)cachePaths)
        ?? throw new InvalidOperationException("GetAssetPathsByFileName returned null.");

    AssertEqual(
        "Assets/hero-tokens/neria-token.webp",
        assets["neria-token.webp"],
        "listing transclusion should resolve directly from the published asset cache");
    AssertFalse(assets.ContainsKey("Neria Silverdale.md"), "markdown pages should not be treated as image assets");
}

static void HeroTokenFileNameResolvesListingDisplayName()
{
    const string listingMarkdown = """
        | Name | Class | Level | Token |
        | --- | --- | --- | --- |
        | [[Neria Silverdale\|Neria]] | Paladin | 1 | ![[neria-token.webp\|70]] |
        """;

    var heroName = PlayerCharacterAssetUtility.GetHeroNameForTokenFileName(
        listingMarkdown,
        "NERIA-TOKEN.WEBP")
        ?? throw new InvalidOperationException("Expected the hero token filename to resolve.");

    AssertEqual("Neria", heroName, "token filename should resolve the listing display name case-insensitively");
}

static void FormerPcListingParsesThreeColumnHeroRows()
{
    const string listingMarkdown = """
        | Name | Class | Token |
        | --- | --- | --- |
        | [[Urvan Hall, paladin of St. Ygg\|Urvan]] | Paladin | ![[urvan-token.webp\|70]] |
        | [['Slip' Harren, Thief\|Slip]] | Thief | ![[slip-token.webp\|70]] |
        | [[Narinza Izrut\|Narinza]] | Thief | ![[narinza-token.webp\|70]] |
        """;

    var rows = PlayerCharacterAssetUtility.GetHeroRows(
        listingMarkdown,
        "https://publish.obsidian.md/scarlethorizons/PCs/Former+PCs");

    AssertEqual(3, rows.Length, "all former PC rows should parse");
    AssertEqual("Urvan", rows[0].Name, "former PC alias should become the display name");
    AssertEqual(
        "urvan-token.webp",
        rows[0].TokenFileName ?? string.Empty,
        "former PC token should parse from the Token column");
    AssertContains(rows[0].CharacterPageUrl ?? string.Empty, "Urvan+Hall");
}

static void ActiveHeroMarkdownCancellationWritesNoFiles()
{
    using var directory = TemporaryDirectory.Create();
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    AssertThrows<OperationCanceledException>(() =>
        PlayerCharacterAssetUtility.DownloadActiveHeroMarkdownAsync(
            """
            | Name | Character | Notes | Hero |
            | --- | --- | --- | --- |
            | Alice Example | [[Alice Example]] | active | ![[alice-token.webp]] |
            """,
            "https://publish.obsidian.md/example/PCs/Player+Characters+Listing",
            directory.Path,
            cancellation.Token).GetAwaiter().GetResult());

    var activeDirectory = Path.Combine(directory.Path, "active");
    AssertFalse(
        Directory.Exists(activeDirectory) && Directory.EnumerateFiles(activeDirectory).Any(),
        "canceled hero markdown refresh should not write active hero files");
}

static void FormerHeroMarkdownCancellationWritesNoInactiveFiles()
{
    using var directory = TemporaryDirectory.Create();
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    AssertThrows<OperationCanceledException>(() =>
        PlayerCharacterAssetUtility.DownloadFormerHeroMarkdownAsync(
            """
            | Name | Class | Token |
            | --- | --- | --- |
            | [[Urvan Hall\|Urvan]] | Paladin | ![[urvan-token.webp]] |
            """,
            "https://publish.obsidian.md/scarlethorizons/PCs/Former+PCs",
            directory.Path,
            cancellation.Token).GetAwaiter().GetResult());

    var inactiveDirectory = Path.Combine(directory.Path, "inactive");
    AssertFalse(
        Directory.Exists(inactiveDirectory) && Directory.EnumerateFiles(inactiveDirectory).Any(),
        "canceled former hero markdown download should not write inactive files");
}

static void PlayerCharacterRefreshCancellationClearsInProgressFlag()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        SetPrivateField(form, "_showWelcomeText", false);
        InvokePrivateAsync(
            form,
            "StartPlayerCharacterListingUpdateAsync",
            false,
            cancellation.Token).GetAwaiter().GetResult();

        AssertFalse(
            (bool)(GetPrivateField(form, "_playerCharacterListingUpdateStarted") ?? true),
            "canceled player-character refresh should clear the in-progress flag");
    });
}

static void PlayerCharacterRefreshIsNotDelayedWhenHeroImagesAreSuppressed()
{
    RunOnStaThread(() =>
    {
        using var suppressedForm = new Form1(suppressHeroImagesForThisRun: true);
        SetPrivateField(suppressedForm, "_activePlayerCharacterImagePaths", new[] { "cached-hero.webp" });
        SetPrivateField(suppressedForm, "_heroImageShowcaseCompleted", false);
        AssertFalse(
            (bool)(InvokePrivateMethod(suppressedForm, "ShouldDelayPlayerCharacterRefreshForHeroShowcase") ?? true),
            "suppressed hero images should not delay player-character markdown refresh");

        using var normalForm = new Form1(suppressHeroImagesForThisRun: false);
        SetPrivateField(normalForm, "_activePlayerCharacterImagePaths", new[] { "cached-hero.webp" });
        SetPrivateField(normalForm, "_heroImageShowcaseCompleted", false);
        AssertTrue(
            (bool)(InvokePrivateMethod(normalForm, "ShouldDelayPlayerCharacterRefreshForHeroShowcase") ?? false),
            "normal hero image startup should still wait for the showcase before refreshing player characters");
    });
}

static void GameForumStartupCancellationWritesNoManifests()
{
    RunOnStaThread(() =>
    {
        var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
        var manifestPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "game-forum-chapter-prefixes.txt"),
            Path.Combine(AppContext.BaseDirectory, "game-forum-chapter-downloads.txt"),
            Path.Combine(AppContext.BaseDirectory, "game-forum-aside-downloads.txt"),
            Path.Combine(AppContext.BaseDirectory, "game-forum-ooc-downloads.txt")
        };
        var preservedFiles = manifestPaths
            .Append(startupLogPath)
            .Select(path => (Path: path, Exists: File.Exists(path), Content: File.Exists(path) ? File.ReadAllText(path) : null))
            .ToArray();

        try
        {
            foreach (var path in manifestPaths.Append(startupLogPath))
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            using var form = new Form1(suppressHeroImagesForThisRun: true);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            AssertThrows<OperationCanceledException>(() =>
                InvokePrivateAsync(
                    form,
                    "LoadGameForumChapterPrefixesAsync",
                    cancellation.Token).GetAwaiter().GetResult());

            foreach (var manifestPath in manifestPaths)
            {
                AssertFalse(File.Exists(manifestPath), $"canceled game-forum startup should not write {Path.GetFileName(manifestPath)}");
            }

            AssertFalse(File.Exists(startupLogPath), "canceled game-forum startup should not be logged as a startup failure");
        }
        finally
        {
            foreach (var preservedFile in preservedFiles)
            {
                if (preservedFile.Exists)
                {
                    File.WriteAllText(preservedFile.Path, preservedFile.Content ?? string.Empty);
                }
                else if (File.Exists(preservedFile.Path))
                {
                    File.Delete(preservedFile.Path);
                }
            }
        }
    });
}

static void KeywordIndexLoaderQuarantinesMalformedJson()
{
    using var directory = TemporaryDirectory.Create();
    var indexPath = Path.Combine(directory.Path, "keyword-index.json");
    File.WriteAllText(indexPath, "{ not valid json");

    var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
    var hadStartupLog = File.Exists(startupLogPath);
    var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

    try
    {
        var loadTask = (Task?)InvokeStaticMethod(
            typeof(KeywordIndexCrawler),
            "LoadExistingDocumentAsync",
            indexPath,
            CancellationToken.None);
        loadTask?.GetAwaiter().GetResult();

        var badIndexFiles = Directory.GetFiles(directory.Path, "keyword-index.bad-*.json");
        AssertFalse(File.Exists(indexPath), "malformed keyword index should be moved out of the active path");
        AssertEqual(1, badIndexFiles.Length, "expected one quarantined keyword index file");
        AssertEqual("{ not valid json", File.ReadAllText(badIndexFiles[0]), "quarantined keyword index should preserve original content");

        var startupLog = File.ReadAllText(startupLogPath);
        AssertContains(startupLog, "keyword index recovery");
        AssertContains(startupLog, "could not be loaded");
        AssertContains(startupLog, badIndexFiles[0]);
    }
    finally
    {
        if (hadStartupLog)
        {
            File.WriteAllText(startupLogPath, originalStartupLog);
        }
        else if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }
    }
}

static void KeywordIndexLoaderSalvagesLegacyDisallowedUrls()
{
    using var directory = TemporaryDirectory.Create();
    var indexPath = Path.Combine(directory.Path, "keyword-index.json");
    File.WriteAllText(
        indexPath,
        """
        {
          "index_metadata": {
            "generated_at": "2026-06-30T00:00:00.0000000+00:00",
            "total_words_indexed": 10
          },
          "urls": {
            "https://rpol.net/admin/gm.cgi?gi=80170": {
              "source": "RPOL"
            },
            "https://publish.obsidian.md/scarlethorizons/Safe": {
              "source": "Obsidian wiki"
            }
          },
          "words": {
            "Kelpie": {
              "total_occurrences": 2,
              "matches": [
                {
                  "url": "https://rpol.net/admin/gm.cgi?gi=80170",
                  "count": 1,
                  "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                },
                {
                  "url": "https://publish.obsidian.md/scarlethorizons/Safe",
                  "count": 1,
                  "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                }
              ]
            }
          }
        }
        """);

    var loadTask = (Task)(InvokeStaticMethod(
        typeof(KeywordIndexCrawler),
        "LoadExistingDocumentAsync",
        indexPath,
        CancellationToken.None)
        ?? throw new InvalidOperationException("expected keyword index load task"));
    loadTask.GetAwaiter().GetResult();

    var result = loadTask
        .GetType()
        .GetProperty("Result")
        ?.GetValue(loadTask)
        ?? throw new InvalidOperationException("expected sanitized keyword index document");
    var words = result.GetType().GetProperty("Words")?.GetValue(result)
        ?? throw new InvalidOperationException("expected keyword index words");
    var kelpieEntry = ((System.Collections.IDictionary)words)["Kelpie"]
        ?? throw new InvalidOperationException("expected Kelpie entry to survive sanitization");
    var matches = kelpieEntry.GetType().GetProperty("Matches")?.GetValue(kelpieEntry)
        ?? throw new InvalidOperationException("expected Kelpie matches");

    AssertTrue(File.Exists(indexPath), "legacy keyword index should not be quarantined when useful entries can be salvaged");
    AssertEqual(0, Directory.GetFiles(directory.Path, "keyword-index.bad-*.json").Length, "legacy keyword index should not be moved aside");
    AssertEqual(1, ((System.Collections.ICollection)matches).Count, "expected only allowed matches to survive sanitization");
}

static void SitemapValidationRejectsPoisonedUrl()
{
    var exception = AssertThrows<InvalidOperationException>(() =>
        SitemapUtility.ValidateSitemapXml(
            """
            <urlset>
              <url>
                <loc>https://user:password@publish.obsidian.md/scarlethorizons/Poison</loc>
              </url>
            </urlset>
            """));

    AssertContains(exception.Message, "sitemap.xml contains a URL that is not allowed");
    AssertContains(exception.Message, "embedded credentials");
}

static void SitemapKeywordDictionaryPreservesExistingOutputOnRejectedUrl()
{
    using var directory = TemporaryDirectory.Create();
    var sitemapPath = Path.Combine(directory.Path, "sitemap.xml");
    var dictionaryPath = Path.Combine(directory.Path, "sitemap-keyword-urls.json");
    File.WriteAllText(dictionaryPath, """{"safe":"https://publish.obsidian.md/scarlethorizons/Safe"}""");
    File.WriteAllText(
        sitemapPath,
        """
        <urlset>
          <url>
            <loc>https://evil.example.test/Poison</loc>
          </url>
        </urlset>
        """);

    var exception = AssertThrows<InvalidOperationException>(() =>
        SitemapUtility.WriteKeywordUrlDictionaryAsync(sitemapPath, dictionaryPath).GetAwaiter().GetResult());

    AssertContains(exception.Message, "sitemap.xml contains a URL that is not allowed");
    AssertContains(File.ReadAllText(dictionaryPath), "https://publish.obsidian.md/scarlethorizons/Safe");
}

static void SourceIntegrityRecordsFirstAcceptedSitemap()
{
    using var directory = TemporaryDirectory.Create();
    var sitemapPath = Path.Combine(directory.Path, "sitemap.xml");
    var sitemapXml = CreateSitemapXml(
        "https://publish.obsidian.md/scarlethorizons/One",
        "https://publish.obsidian.md/scarlethorizons/Two");

    SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
        sitemapPath,
        "https://publish.obsidian.md/scarlethorizons/sitemap.xml",
        "obsidian-sitemap",
        sitemapXml,
        SourceIntegrityUtility.CreateSitemapShape(sitemapXml)).GetAwaiter().GetResult();

    var sidecarPath = SourceIntegrityUtility.GetSidecarPath(sitemapPath);
    AssertTrue(File.Exists(sidecarPath), "source integrity sidecar should be written for accepted sitemap");
    AssertContains(File.ReadAllText(sidecarPath), "\"artifact_kind\": \"obsidian-sitemap\"");
    AssertContains(File.ReadAllText(sidecarPath), "\"url_count\": 2");
}

static void SourceIntegrityRejectsCollapsedSitemapAndPreservesOutput()
{
    using var directory = TemporaryDirectory.Create();
    var sitemapPath = Path.Combine(directory.Path, "sitemap.xml");
    var originalSitemap = CreateSitemapXml(
        "https://publish.obsidian.md/scarlethorizons/One",
        "https://publish.obsidian.md/scarlethorizons/Two",
        "https://publish.obsidian.md/scarlethorizons/Three",
        "https://publish.obsidian.md/scarlethorizons/Four");
    SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
        sitemapPath,
        "https://publish.obsidian.md/scarlethorizons/sitemap.xml",
        "obsidian-sitemap",
        originalSitemap,
        SourceIntegrityUtility.CreateSitemapShape(originalSitemap)).GetAwaiter().GetResult();

    var collapsedSitemap = CreateSitemapXml("https://publish.obsidian.md/scarlethorizons/One");
    var exception = AssertThrows<InvalidOperationException>(() =>
        SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
            sitemapPath,
            "https://publish.obsidian.md/scarlethorizons/sitemap.xml",
            "obsidian-sitemap",
            collapsedSitemap,
            SourceIntegrityUtility.CreateSitemapShape(collapsedSitemap)).GetAwaiter().GetResult());

    AssertContains(exception.Message, "Authenticated source tamper detection rejected fetched content");
    AssertContains(exception.Message, "last-known-good content was preserved");
    AssertEqual(originalSitemap, File.ReadAllText(sitemapPath), "collapsed sitemap should not replace last known good sitemap");
}

static void SourceIntegrityRejectsCollapsedMarkdownAndPreservesOutput()
{
    using var directory = TemporaryDirectory.Create();
    var markdownPath = Path.Combine(directory.Path, "kelpie.md");
    var originalMarkdown = """
        # Kelpie Lawfuller
        ## Summary
        Useful notes.
        ## Links
        [[Allies]]
        [Map](https://publish.obsidian.md/scarlethorizons/Map)
        """;
    SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
        markdownPath,
        "https://publish.obsidian.md/scarlethorizons/PCs/Kelpie",
        "obsidian-markdown",
        originalMarkdown,
        SourceIntegrityUtility.CreateMarkdownShape(originalMarkdown)).GetAwaiter().GetResult();

    var collapsedMarkdown = "# Kelpie";
    var exception = AssertThrows<InvalidOperationException>(() =>
        SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
            markdownPath,
            "https://publish.obsidian.md/scarlethorizons/PCs/Kelpie",
            "obsidian-markdown",
            collapsedMarkdown,
            SourceIntegrityUtility.CreateMarkdownShape(collapsedMarkdown)).GetAwaiter().GetResult());

    AssertContains(exception.Message, "Authenticated source tamper detection rejected fetched content");
    AssertEqual(originalMarkdown, File.ReadAllText(markdownPath), "collapsed markdown should not replace last known good markdown");
}

static void SourceIntegrityRejectsCollapsedKeywordIndexAndPreservesOutput()
{
    using var directory = TemporaryDirectory.Create();
    var indexPath = Path.Combine(directory.Path, "keyword-index.json");
    var originalIndex = """{"urls":{"u1":{},"u2":{},"u3":{},"u4":{}},"words":{"safe":{}}}""";
    SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
        indexPath,
        "keyword-index-crawl",
        "keyword-index",
        originalIndex,
        SourceIntegrityUtility.CreateKeywordIndexShape(4, 1, 4)).GetAwaiter().GetResult();

    var collapsedIndex = """{"urls":{"u1":{}},"words":{}}""";
    var exception = AssertThrows<InvalidOperationException>(() =>
        SourceIntegrityUtility.ValidateAndWriteTextFileAsync(
            indexPath,
            "keyword-index-crawl",
            "keyword-index",
            collapsedIndex,
            SourceIntegrityUtility.CreateKeywordIndexShape(1, 0, 0)).GetAwaiter().GetResult());

    AssertContains(exception.Message, "Authenticated source tamper detection rejected fetched content");
    AssertEqual(originalIndex, File.ReadAllText(indexPath), "collapsed keyword index should not replace last known good index");
}

static void KeywordIndexValidationRejectsPoisonedUrlEntries()
{
    var exception = AssertThrows<InvalidOperationException>(() =>
        KeywordIndexCrawler.ValidateKeywordIndexJson(
            """
            {
              "index_metadata": {
                "generated_at": "2026-07-04T00:00:00Z",
                "total_words_indexed": 0
              },
              "urls": {
                "https://evil.example.test/Poison": {
                  "source": "Obsidian wiki"
                }
              },
              "words": {}
            }
            """));

    AssertContains(exception.Message, "keyword-index urls contains a URL that is not allowed");
    AssertContains(exception.Message, "Obsidian Publish URLs");
}

static void KeywordIndexValidationRejectsPoisonedMatchUrls()
{
    var exception = AssertThrows<InvalidOperationException>(() =>
        KeywordIndexCrawler.ValidateKeywordIndexJson(
            """
            {
              "index_metadata": {
                "generated_at": "2026-07-04T00:00:00Z",
                "total_words_indexed": 1
              },
              "urls": {
                "https://publish.obsidian.md/scarlethorizons/Safe": {
                  "source": "Obsidian wiki"
                }
              },
              "words": {
                "safe": {
                  "total_occurrences": 1,
                  "matches": [
                    {
                      "url": "file:///C:/secret.txt",
                      "count": 1,
                      "last_indexed": "2026-07-04T00:00:00Z"
                    }
                  ]
                }
              }
            }
            """));

    AssertContains(exception.Message, "keyword-index matches for 'safe' contains a URL that is not allowed");
    AssertContains(exception.Message, "Only HTTP and HTTPS");
}

static void KeywordTermsReleaseCopyGeneratesFromKeywordIndex()
{
    const string indexJson =
        """
        {
          "words": {
            "zeta": {},
            "Alpha": {},
            "beta": {}
          }
        }
        """;

    WithTemporaryKeywordIndex(
        indexJson,
        () => WithTemporaryKeywordTermsFile(
            () =>
            {
                KeywordTermsFileUtility.EnsureReleaseCopy();

                var termsPath = GetPlayerAssistantKeywordTermsPath();
                AssertTrue(File.Exists(termsPath), "expected keyword terms file to be generated");
                AssertEqual(
                    "Alpha|beta|zeta",
                    string.Join("|", File.ReadAllLines(termsPath)),
                    "generated keyword terms should be sorted from keyword index words");
            }));
}

static void KeywordTermsPublishCopyPreservesParentReleaseTerms()
{
    using var directory = TemporaryDirectory.Create();
    var publishDirectory = Path.Combine(directory.Path, "publish");
    Directory.CreateDirectory(publishDirectory);

    var parentTermsPath = Path.Combine(directory.Path, KeywordTermsFileUtility.FileName);
    var publishTermsPath = Path.Combine(publishDirectory, KeywordTermsFileUtility.FileName);
    File.WriteAllText(parentTermsPath, "parent-term");
    File.WriteAllText(publishTermsPath, "publish-term");

    KeywordTermsFileUtility.EnsureReleaseCopy(publishDirectory);

    AssertTrue(File.Exists(parentTermsPath), "running from publish should not delete parent Release keyword terms");
    AssertTrue(File.Exists(publishTermsPath), "running from publish should keep its own keyword terms");
    AssertEqual("parent-term", File.ReadAllText(parentTermsPath), "parent Release keyword terms should be unchanged");
    AssertEqual("publish-term", File.ReadAllText(publishTermsPath), "publish keyword terms should be unchanged");
}

static void RpolAuthDetectsLoginPageFallback()
{
    var loginHtml =
        """
        <html>
          <body>
            <form action='/login.cgi'>
              <input name='username'>
              <input name='password' type='password'>
            </form>
          </body>
        </html>
        """;

    AssertTrue(RpolAuthUtility.LooksLikeLoginPage(loginHtml), "expected RPOL login page markup to be detected");
    AssertTrue(
        RpolAuthUtility.LooksLikeLoginResponse("text/html; charset=utf-8", System.Text.Encoding.UTF8.GetBytes(loginHtml)),
        "expected HTML login response to be detected");
    AssertFalse(
        RpolAuthUtility.LooksLikeLoginResponse("image/png", System.Text.Encoding.UTF8.GetBytes(loginHtml)),
        "non-HTML responses should not be treated as expired auth pages");
    AssertFalse(
        RpolAuthUtility.LooksLikeLoginPage("<html><body>normal game page</body></html>"),
        "ordinary RPOL pages should not be treated as login pages");
}

static void RpolAuthDistinguishesBlockedAndRemoteFailures()
{
    var uri = new Uri("https://rpol.net/display.cgi?gi=80170");

    var forbidden = RpolAuthUtility.CreateUnsuccessfulResponseException(uri, 403, "Forbidden");
    var cloudflareChallenge = RpolAuthUtility.CreateUnsuccessfulResponseException(
        uri,
        403,
        "Forbidden",
        "https://rpol.net/display.cgi?gi=80170&__cf_chl_rt_tk=challenge-token");
    var rateLimited = RpolAuthUtility.CreateUnsuccessfulResponseException(uri, 429, "Too Many Requests");
    var unavailable = RpolAuthUtility.CreateUnsuccessfulResponseException(uri, 503, "Service Unavailable");

    AssertEqual(RpolAuthFailureKind.RpolBlocked, forbidden.Kind, "403 should be classified as RPOL blocking");
    AssertContains(forbidden.Message, "blocked authenticated access");
    AssertEqual(RpolAuthFailureKind.CloudflareChallenge, cloudflareChallenge.Kind, "Cloudflare challenge 403 should trigger headed browser recovery");
    AssertFalse(RpolAuthUtility.IsFatalAuthFailure(cloudflareChallenge), "Cloudflare challenges should be retryable instead of cached as fatal");
    AssertEqual(RpolAuthFailureKind.RpolBlocked, rateLimited.Kind, "429 should be classified as RPOL blocking");
    AssertEqual(RpolAuthFailureKind.RemoteUnavailable, unavailable.Kind, "503 should remain a transient remote failure");
}

static void RpolAuthPrefersInstalledBrowsersBeforePlaywrightChromium()
{
    var normalOptions = (BrowserTypeLaunchOptions[])(InvokeStaticMethod(
        typeof(RpolAuthUtility),
        "CreateRpolBrowserLaunchOptions",
        false) ?? throw new InvalidOperationException("CreateRpolBrowserLaunchOptions returned null."));
    var verificationOptions = (BrowserTypeLaunchOptions[])(InvokeStaticMethod(
        typeof(RpolAuthUtility),
        "CreateRpolBrowserLaunchOptions",
        true) ?? throw new InvalidOperationException("CreateRpolBrowserLaunchOptions returned null."));

    AssertEqual(3, normalOptions.Length, "normal RPOL auth should try Edge, Chrome, then Playwright Chromium");
    AssertEqual("msedge", normalOptions[0].Channel ?? string.Empty, "Edge should be tried before default Playwright Chromium");
    AssertEqual("chrome", normalOptions[1].Channel ?? string.Empty, "Chrome should be tried before default Playwright Chromium");
    AssertTrue(string.IsNullOrWhiteSpace(normalOptions[2].Channel), "default Playwright Chromium should remain the final fallback");
    AssertTrue(normalOptions.All(option => option.Headless == true), "normal RPOL auth should launch browsers headless");
    AssertTrue(verificationOptions.All(option => option.Headless == false), "manual RPOL browser verification should launch browsers headed");
}

static void RpolAuthEnforcesBrowserTlsValidation()
{
    var contextOptions = (BrowserNewContextOptions)(InvokeStaticMethod(
        typeof(RpolAuthUtility),
        "CreateBrowserContextOptions",
        null!,
        true) ?? throw new InvalidOperationException("CreateBrowserContextOptions returned null."));

    AssertFalse(contextOptions.IgnoreHTTPSErrors == true, "RPOL browser contexts must reject HTTPS certificate errors");
}

static void RpolAuthClassifiesTransportSecurityFailures()
{
    AssertTrue(
        RpolAuthUtility.IsTransportSecurityFailureMessage("net::ERR_CERT_AUTHORITY_INVALID at https://rpol.net/"),
        "invalid certificate authorities should be classified as transport-security failures");
    AssertTrue(
        RpolAuthUtility.IsTransportSecurityFailureMessage("net::ERR_CERT_COMMON_NAME_INVALID at https://rpol.net/"),
        "certificate hostname mismatches should be classified as transport-security failures");
    AssertTrue(
        RpolAuthUtility.IsTransportSecurityFailureMessage("net::ERR_SSL_VERSION_OR_CIPHER_MISMATCH"),
        "TLS protocol failures should be classified as transport-security failures");
    AssertFalse(
        RpolAuthUtility.IsTransportSecurityFailureMessage("net::ERR_CONNECTION_RESET at https://rpol.net/"),
        "ordinary network failures should not be classified as certificate failures");

    var transportException = (RpolAuthException)(InvokeStaticMethod(
        typeof(RpolAuthUtility),
        "CreateTransportSecurityException",
        new PlaywrightException("net::ERR_CERT_AUTHORITY_INVALID at https://rpol.net/game.php?gi=1"))
        ?? throw new InvalidOperationException("CreateTransportSecurityException returned null."));
    AssertEqual(
        RpolAuthFailureKind.TransportSecurityFailure,
        transportException.Kind,
        "certificate errors should become transport-security failures");
    AssertFalse(
        transportException.Message.Contains("https://", StringComparison.OrdinalIgnoreCase),
        "transport-security messages shown to users should not echo request URLs");
    AssertTrue(
        RpolAuthUtility.IsFatalAuthFailure(new RpolAuthException(
            RpolAuthFailureKind.TransportSecurityFailure,
            "TLS failure for test.")),
        "transport-security failures should stop authentication retries for the current process");
}

static void RpolAuthCachedFailureShortCircuitsHtmlFetch()
{
    ResetRpolAuthFailureCache();
    var cachedFailure = new RpolAuthException(
        RpolAuthFailureKind.MissingCredentials,
        "Missing RPoL credentials for test.");

    try
    {
        InvokeStaticMethod(typeof(RpolAuthUtility), "CacheFatalAuthFailure", cachedFailure);

        var exception = AssertThrows<RpolAuthException>(() =>
            RpolAuthUtility.GetHtmlFromUrlAsync(new Uri("https://rpol.net/display.cgi?gi=1")).GetAwaiter().GetResult());
        AssertEqual(RpolAuthFailureKind.MissingCredentials, exception.Kind, "expected cached missing-credentials failure");
        AssertEqual(cachedFailure.Message, exception.Message, "expected cached failure message to be reused");

        exception = AssertThrows<RpolAuthException>(() =>
            RpolAuthUtility.GetResponseAsync(new Uri("https://rpol.net/c-webp/example.webp")).GetAwaiter().GetResult());
        AssertEqual(RpolAuthFailureKind.MissingCredentials, exception.Kind, "expected cached missing-credentials response failure");
    }
    finally
    {
        ResetRpolAuthFailureCache();
    }
}

static void RpolAuthCachedFailureLogsOnce()
{
    ResetRpolAuthFailureCache();
    var startupLogPath = Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
    var hadStartupLog = File.Exists(startupLogPath);
    var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

    try
    {
        if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }

        var firstFailure = new RpolAuthException(
            RpolAuthFailureKind.MissingCredentials,
            "Missing RPoL credentials for test.");
        var secondFailure = new RpolAuthException(
            RpolAuthFailureKind.LoginRejected,
            "RPoL login was rejected for test.");

        InvokeStaticMethod(typeof(RpolAuthUtility), "CacheFatalAuthFailure", firstFailure);
        InvokeStaticMethod(typeof(RpolAuthUtility), "CacheFatalAuthFailure", secondFailure);

        var log = File.ReadAllText(startupLogPath);
        AssertEqual(1, CountOccurrences(log, "RPOL authentication"), "expected one RPOL auth log entry");
        AssertContains(log, "Missing RPoL credentials for test.");
        AssertFalse(log.Contains("RPoL login was rejected for test.", StringComparison.Ordinal), "second fatal auth failure should reuse first cached entry");
    }
    finally
    {
        ResetRpolAuthFailureCache();
        if (hadStartupLog)
        {
            File.WriteAllText(startupLogPath, originalStartupLog);
        }
        else if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }
    }
}

static void RpolAuthCachesBlockedAndExpiredSessionFailures()
{
    ResetRpolAuthFailureCache();

    try
    {
        var blocked = new RpolAuthException(
            RpolAuthFailureKind.RpolBlocked,
            "RPoL blocked authenticated access for test.");
        var cached = (RpolAuthException?)InvokeStaticMethod(typeof(RpolAuthUtility), "CacheFatalAuthFailure", blocked)
            ?? throw new InvalidOperationException("CacheFatalAuthFailure returned null.");

        AssertEqual(RpolAuthFailureKind.RpolBlocked, cached.Kind, "blocked RPOL access should be cacheable as a fatal auth failure");
        AssertEqual(blocked.Message, cached.Message, "blocked RPOL failure should be cached as-is");
        AssertTrue(RpolAuthUtility.IsFatalAuthFailure(blocked), "blocked RPOL access should be treated as fatal until settings or site state changes");

        ResetRpolAuthFailureCache();

        var expiredSession = new RpolAuthException(
            RpolAuthFailureKind.AuthSessionExpired,
            "RPoL returned a login page after authenticated navigation for test.");
        cached = (RpolAuthException?)InvokeStaticMethod(typeof(RpolAuthUtility), "CacheFatalAuthFailure", expiredSession)
            ?? throw new InvalidOperationException("CacheFatalAuthFailure returned null.");

        AssertEqual(RpolAuthFailureKind.AuthSessionExpired, cached.Kind, "expired auth session should be cacheable as a fatal auth failure");
        AssertTrue(RpolAuthUtility.IsFatalAuthFailure(expiredSession), "expired authenticated sessions should be treated as fatal after retry");
        AssertFalse(
            RpolAuthUtility.IsFatalAuthFailure(new RpolAuthException(RpolAuthFailureKind.RemoteUnavailable, "remote outage")),
            "remote RPOL outages should remain transient and uncached");
    }
    finally
    {
        ResetRpolAuthFailureCache();
    }
}

static void RpolStorageStateValidationAcceptsCurrentRpolCookies()
{
    using var directory = TemporaryDirectory.Create();
    var storageStatePath = Path.Combine(directory.Path, "rpol-storage-state.json");
    WriteRpolStorageState(
        storageStatePath,
        """
        {
          "cookies": [
            {
              "name": "rpol_session",
              "value": "cookie-value",
              "domain": ".rpol.net",
              "path": "/"
            }
          ],
          "origins": []
        }
        """,
        DateTimeOffset.UtcNow.AddDays(-1));

    var valid = RpolAuthUtility.TryPrepareStorageStateFile(
        storageStatePath,
        DateTimeOffset.UtcNow);

    AssertTrue(valid, "current RPOL storage state should be usable");
    AssertTrue(File.Exists(storageStatePath), "valid RPOL storage state should be preserved");
}

static void RpolStorageStateValidationDeletesMalformedState()
{
    WithPreservedStartupLog(() =>
    {
        using var directory = TemporaryDirectory.Create();
        var storageStatePath = Path.Combine(directory.Path, "rpol-storage-state.json");
        WriteRpolStorageState(storageStatePath, "{ not valid json", DateTimeOffset.UtcNow);

        var valid = RpolAuthUtility.TryPrepareStorageStateFile(
            storageStatePath,
            DateTimeOffset.UtcNow);

        AssertFalse(valid, "malformed RPOL storage state should not be usable");
        AssertFalse(File.Exists(storageStatePath), "malformed RPOL storage state should be deleted");
    });
}

static void RpolStorageStateValidationDeletesStaleState()
{
    WithPreservedStartupLog(() =>
    {
        using var directory = TemporaryDirectory.Create();
        var storageStatePath = Path.Combine(directory.Path, "rpol-storage-state.json");
        WriteRpolStorageState(
            storageStatePath,
            """
            {
              "cookies": [
                {
                  "name": "rpol_session",
                  "value": "cookie-value",
                  "domain": "rpol.net",
                  "path": "/"
                }
              ]
            }
            """,
            DateTimeOffset.UtcNow.AddDays(-45));

        var valid = RpolAuthUtility.TryPrepareStorageStateFile(
            storageStatePath,
            DateTimeOffset.UtcNow);

        AssertFalse(valid, "stale RPOL storage state should not be usable");
        AssertFalse(File.Exists(storageStatePath), "stale RPOL storage state should be deleted");
    });
}

static void RpolStorageStateValidationDeletesNonRpolState()
{
    WithPreservedStartupLog(() =>
    {
        using var directory = TemporaryDirectory.Create();
        var storageStatePath = Path.Combine(directory.Path, "rpol-storage-state.json");
        WriteRpolStorageState(
            storageStatePath,
            """
            {
              "cookies": [
                {
                  "name": "session",
                  "value": "cookie-value",
                  "domain": "example.test",
                  "path": "/"
                }
              ]
            }
            """,
            DateTimeOffset.UtcNow);

        var valid = RpolAuthUtility.TryPrepareStorageStateFile(
            storageStatePath,
            DateTimeOffset.UtcNow);

        AssertFalse(valid, "non-RPOL storage state should not be usable");
        AssertFalse(File.Exists(storageStatePath), "non-RPOL storage state should be deleted");
    });
}

static void ShowAllThreadUrlPreservesBaseQueryAndAddsShowAll()
{
    const string threadUrl = "https://rpol.net/display.cgi?gi=80170&ti=17&date=1779581880";

    var showAllUrl = RpolThreadPostUtility.GetShowAllThreadUrl(threadUrl);

    AssertEqual(
        "https://rpol.net/display.cgi?gi=80170&ti=17&date=1779581880&msgpage=&show=all",
        showAllUrl,
        "unexpected show-all thread url");
}

static void RpolThreadExportPreservesExistingOutputOnCancellation()
{
    using var directory = TemporaryDirectory.Create();
    var outputDirectory = Path.Combine(directory.Path, "thread-export");
    Directory.CreateDirectory(outputDirectory);
    var markerPath = Path.Combine(outputDirectory, "last-good.txt");
    File.WriteAllText(markerPath, "keep me");
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    AssertThrows<OperationCanceledException>(() =>
        RpolThreadPostUtility.WriteThreadPostsFromHtmlAsync(
            CreateSampleRpolThreadHtml(),
            "https://rpol.net/display.cgi?gi=80170&ti=17&show=all",
            outputDirectory,
            "Synthetic Thread",
            cancellation.Token).GetAwaiter().GetResult());

    AssertTrue(File.Exists(markerPath), "existing RPOL thread export should survive a cancelled replacement");
    AssertEqual("keep me", File.ReadAllText(markerPath), "existing RPOL thread export marker should remain unchanged");
    AssertEqual(0, Directory.GetDirectories(directory.Path, "thread-export.staging-*").Length, "cancelled export should clean staging directories");
    AssertEqual(0, Directory.GetDirectories(directory.Path, "thread-export.backup-*").Length, "cancelled export should not leave backup directories");
}

static void RpolThreadExportCommitsStagedOutput()
{
    using var directory = TemporaryDirectory.Create();
    var outputDirectory = Path.Combine(directory.Path, "thread-export");
    Directory.CreateDirectory(outputDirectory);
    File.WriteAllText(Path.Combine(outputDirectory, "stale.txt"), "old");

    var result = RpolThreadPostUtility.WriteThreadPostsFromHtmlAsync(
        CreateSampleRpolThreadHtml(),
        "https://rpol.net/display.cgi?gi=80170&ti=17&show=all",
        outputDirectory,
        "Synthetic Thread").GetAwaiter().GetResult();

    AssertEqual(2, result.PostCount, "expected staged RPOL export to include both posts");
    AssertFalse(File.Exists(Path.Combine(outputDirectory, "stale.txt")), "successful staged export should replace stale output");
    AssertTrue(File.Exists(Path.Combine(outputDirectory, "_source-show-all.html")), "successful staged export should include source HTML");
    AssertTrue(File.Exists(Path.Combine(outputDirectory, "index.html")), "successful staged export should include index.html");
    AssertTrue(File.Exists(Path.Combine(outputDirectory, "manifest.json")), "successful staged export should include manifest.json");
    AssertTrue(File.Exists(Path.Combine(outputDirectory, "001-alice.html")), "successful staged export should include the first post");
    AssertTrue(File.Exists(Path.Combine(outputDirectory, "002-bob.html")), "successful staged export should include the second post");
    AssertEqual(0, Directory.GetDirectories(directory.Path, "thread-export.staging-*").Length, "successful export should clean staging directories");
    AssertEqual(0, Directory.GetDirectories(directory.Path, "thread-export.backup-*").Length, "successful export should clean backup directories");
}

static void RpolThreadExportRejectsCollapsedSourceAndPreservesExistingOutput()
{
    using var directory = TemporaryDirectory.Create();
    var outputDirectory = Path.Combine(directory.Path, "thread-export");
    var sourceUrl = "https://rpol.net/display.cgi?gi=80170&ti=17&show=all";
    var originalResult = RpolThreadPostUtility.WriteThreadPostsFromHtmlAsync(
        CreateSampleRpolThreadHtml(),
        sourceUrl,
        outputDirectory,
        "Synthetic Thread").GetAwaiter().GetResult();

    AssertEqual(2, originalResult.PostCount, "expected baseline RPOL export to include both posts");
    var originalSourceHtml = File.ReadAllText(Path.Combine(outputDirectory, "_source-show-all.html"));
    var collapsedHtml = """
        <html><body>
        <div class='message'>
            <ul><li>msg #1</li></ul>
            <span class='messageauthor'>Alice</span>
            <div class='messagebody' id='msg1'>Only one post survived.</div>
        </div><!-- 1 -->
        </div><!-- 2 -->
        </body></html>
        """;

    var exception = AssertThrows<InvalidOperationException>(() =>
        RpolThreadPostUtility.WriteThreadPostsFromHtmlAsync(
            collapsedHtml,
            sourceUrl,
            outputDirectory,
            "Synthetic Thread").GetAwaiter().GetResult());

    AssertContains(exception.Message, "Authenticated source tamper detection rejected fetched content");
    AssertEqual(originalSourceHtml, File.ReadAllText(Path.Combine(outputDirectory, "_source-show-all.html")), "collapsed RPOL source should not replace last known good export");
    AssertTrue(File.Exists(Path.Combine(outputDirectory, "002-bob.html")), "last known good RPOL post files should remain available");
    AssertEqual(0, Directory.GetDirectories(directory.Path, "thread-export.staging-*").Length, "rejected export should clean staging directories");
}

static void DieRollExtractionKeepsOnlySavedLogLines()
{
    const string html = """
        <html><body>
        <div>18:37, Today: Dungeon Master rolled 4 using 1d6.  orcs' init for rnd 2 abandoned rock quarry. – [roll=1782257860.18744.396686]</div>
        <div>18:05, Today: Jelb Garrick rolled 6 using 2d4.  Fire Damage. – [roll=1782255916.99298.396653]</div>
        <div>15:41, Today: Kelpie Lawfuller rolled 17,8 using d20+2,d8+2.  Sword attack (held action). – [roll=1782247280.40694.396648]</div>
        <div>18:44, Today: Maximilian Yragerne rolled 16 using 1d20. dex.</div>
        <div>Dice are fun.</div>
        </body></html>
        """;

    var entries = GameForumUtility.ExtractDieRollEntries(html);

    AssertEqual(3, entries.Length, "unexpected die roll entry count");
    AssertEqual("1782257860.18744.396686", entries[0].RollId, "unexpected first roll id");
    AssertContains(entries[1].Line, "Jelb Garrick rolled 6 using 2d4.");
    AssertContains(entries[2].Line, "[roll=1782247280.40694.396648]");
}

static void DieRollExtractionHandlesLiveRpolParagraphMarkup()
{
    const string html = """
        <div class="info_box">
        <p style="margin-left: 2em; text-indent: -2em;">18:37, Today: Dungeon Master rolled 4 using 1d6.&nbsp; orcs' init for rnd 2 abandoned rock quarry.
         –&nbsp;<span class="link-colour">[roll=1782257860.18744.396686]</span></p>
        <p style="margin-left: 2em; text-indent: -2em;">18:05, Today: Jelb Garrick rolled 6 using 2d4.&nbsp; Fire Damage.
         –&nbsp;<span class="link-colour">[roll=1782255916.99298.396653]</span></p>
        <p style="margin-left: 2em; text-indent: -2em;">15:41, Today: Kelpie Lawfuller rolled 17,8 using d20+2,d8+2.&nbsp; Sword attack (held action).
         –&nbsp;<span class="link-colour">[roll=1782247280.40694.396648]</span></p>
        </div>
        """;

    var entries = GameForumUtility.ExtractDieRollEntries(html);

    AssertEqual(3, entries.Length, "unexpected die roll entry count from paragraph markup");
    AssertEqual("1782257860.18744.396686", entries[0].RollId, "unexpected first roll id from paragraph markup");
    AssertContains(entries[0].Line, "abandoned rock quarry. – [roll=1782257860.18744.396686]");
    AssertContains(entries[2].Line, "Kelpie Lawfuller rolled 17,8 using d20+2,d8+2.");
}

static void DieRollSyncAppendsOnlyUnsavedRolls()
{
    using var directory = TemporaryDirectory.Create();
    var filePath = Path.Combine(directory.Path, "Posts", "OOC", "dice-rolls.html");
    const string initialHtml = """
        <div>18:37, Today: Dungeon Master rolled 4 using 1d6.  orcs' init for rnd 2 abandoned rock quarry. – [roll=1782257860.18744.396686]</div>
        <div>18:05, Today: Jelb Garrick rolled 6 using 2d4.  Fire Damage. – [roll=1782255916.99298.396653]</div>
        """;
    const string nextHtml = """
        <div>18:05, Today: Jelb Garrick rolled 6 using 2d4.  Fire Damage. – [roll=1782255916.99298.396653]</div>
        <div>15:41, Today: Kelpie Lawfuller rolled 17,8 using d20+2,d8+2.  Sword attack (held action). – [roll=1782247280.40694.396648]</div>
        """;

    var firstAppendCount = GameForumUtility.AppendNewDieRollEntriesAsync(initialHtml, filePath).GetAwaiter().GetResult();
    var secondAppendCount = GameForumUtility.AppendNewDieRollEntriesAsync(nextHtml, filePath).GetAwaiter().GetResult();
    var savedHtml = File.ReadAllText(filePath);
    var savedEntries = GameForumUtility.ExtractDieRollEntries(savedHtml);

    AssertEqual(2, firstAppendCount, "unexpected initial append count");
    AssertEqual(1, secondAppendCount, "unexpected incremental append count");
    AssertEqual(3, savedEntries.Length, "unexpected saved die roll count");
    AssertEqual("1782257860.18744.396686", savedEntries[0].RollId, "unexpected first saved roll id");
    AssertEqual("1782247280.40694.396648", savedEntries[2].RollId, "unexpected final saved roll id");
}

static void RegionalMapDownloadsWhenMissing()
{
    var filePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "Images", "Maps", "northernreaches.png");

    AssertTrue(GameForumUtility.ShouldDownloadRegionalMap(filePath), "missing regional map should be downloaded");
}

static void RegionalMapDownloadsWhenOlderThanOneHour()
{
    using var directory = TemporaryDirectory.Create();
    var filePath = Path.Combine(directory.Path, "Images", "Maps", "northernreaches.png");
    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    WriteVisiblePng(filePath);
    File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow - TimeSpan.FromMinutes(61));

    AssertTrue(GameForumUtility.ShouldDownloadRegionalMap(filePath), "regional map older than one hour should be downloaded");
}

static void RegionalMapSkipsWhenNewerThanOneHour()
{
    using var directory = TemporaryDirectory.Create();
    var filePath = Path.Combine(directory.Path, "Images", "Maps", "northernreaches.png");
    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    WriteVisiblePng(filePath);
    File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow - TimeSpan.FromMinutes(59));

    AssertFalse(GameForumUtility.ShouldDownloadRegionalMap(filePath), "regional map newer than one hour should not be downloaded");
}

static void RegionalMapDownloadsWhenNewerButTransparent()
{
    using var directory = TemporaryDirectory.Create();
    var filePath = Path.Combine(directory.Path, "Images", "Maps", "northernreaches.png");
    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    WriteTransparentPng(filePath);
    File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow - TimeSpan.FromMinutes(1));

    AssertTrue(GameForumUtility.ShouldDownloadRegionalMap(filePath), "transparent regional map should be downloaded");
}

static void StartupStatusIncludesDownloadCountAndSize()
{
    using var directory = TemporaryDirectory.Create();
    var firstPath = Path.Combine(directory.Path, "first.bin");
    var secondPath = Path.Combine(directory.Path, "second.bin");
    File.WriteAllBytes(firstPath, new byte[1024]);
    File.WriteAllBytes(secondPath, new byte[512]);

    FileDownloadCounters.Reset();
    FileDownloadCounters.AddCompletedDownload(firstPath);
    FileDownloadCounters.AddCompletedDownload(secondPath);

    var summary = Form1.GetStartupDownloadSummary();

    AssertContains(summary, "2 files");
    AssertContains(summary, "1.5 KB");
    AssertContains(summary, "0 MB");
}

static void AdventureOutlineBuildsFromSavedIcHtml()
{
    using var directory = TemporaryDirectory.Create();
    var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
    Directory.CreateDirectory(icDirectory);

    File.WriteAllText(
        Path.Combine(icDirectory, "ch-10.html"),
        """
        <html><body>
        <h1>Ch 10 - Later Trouble.</h1>
        <span class="messageauthor">Kelpie Lawfuller</span>
        <div class="messagebody" id="msg2">Kelpie keeps watch.<br>Then moves on.</div>
        </body></html>
        """);
    File.WriteAllText(
        Path.Combine(icDirectory, "ch-2.html"),
        """
        <html><body>
        <h1>Ch 2 - Supper With Nuanda.</h1>
        <span class="messageauthor you"><a href="/gm">Dungeon Master</a></span>
        <div class="messagebody" id="msg1">Nuanda offers stew &amp; hard biscuits.</div>
        </body></html>
        """);
    File.WriteAllText(
        Path.Combine(icDirectory, "ch-2.bak-20260707.html"),
        """
        <html><body>
        <h1>Ch 2 - Old Backup.</h1>
        <span class="messageauthor">Backup</span>
        <div class="messagebody" id="msg1">This should not appear.</div>
        </body></html>
        """);

    var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
        .GetAwaiter()
        .GetResult();

    AssertContains(outline, "# Adventure Outline");
    AssertContains(outline, "## Ch 2 - Supper With Nuanda");
    AssertContains(outline, "- Dungeon Master introduces Nuanda's supper.");
    AssertContains(outline, "## Ch 10 - Later Trouble");
    AssertContains(outline, "- Kelpie keeps watch as the party moves on.");
    AssertFalse(outline.Contains("Old Backup", StringComparison.Ordinal), "backup chapter files should be ignored");
    AssertTrue(
        outline.IndexOf("## Ch 2", StringComparison.Ordinal) < outline.IndexOf("## Ch 10", StringComparison.Ordinal),
        "chapter files should sort by numeric chapter number");
}

static void AdventureOutlineParsesRpolLinkedAuthorExports()
{
    using var directory = TemporaryDirectory.Create();
    var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
    Directory.CreateDirectory(icDirectory);

    File.WriteAllText(
        Path.Combine(icDirectory, "ch-1.html"),
        """
        <html><body>
        <div class="threadheader">
            <h1>Ch 1 - Kirkilston.</h1>
        </div>
        <div class="message">
            <span class="messageauthor you"><a href="/gameinfo.php?action=viewdescription&amp;ci=396686">Dungeon Master</a></span>
            <div class="messagebody" id="msg37">Mapper: Slip?<br>Caller: Kelpie?</div>
        </div>
        <div class="message">
            <span class="messageauthor"><a href="/gameinfo.php?action=viewdescription&amp;ci=396648">Kelpie Lawfuller</a></span>
            <div class="messagebody" id="msg38">
            Kelpie was already prepared.<br>
            <span class="blue">I will take the fore</span>
            </div>
        </div>
        </body></html>
        """);

    var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
        .GetAwaiter()
        .GetResult();

    AssertContains(outline, "## Ch 1 - Kirkilston");
    AssertContains(outline, "- Dungeon Master asks a question that narrows the party's next choice.");
    AssertContains(outline, "- Kelpie takes the lead as the party sets out toward Nuanda.");
    AssertFalse(
        outline.Contains("No in-character posts were found", StringComparison.Ordinal),
        "linked author RPOL exports should produce post summaries");
}

static void AdventureOutlineSummarizesTableRolesConcisely()
{
    using var directory = TemporaryDirectory.Create();
    var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
    Directory.CreateDirectory(icDirectory);
    File.WriteAllText(
        Path.Combine(icDirectory, "ch-1.html"),
        """
        <html><body>
        <h1>Ch 1 - Kirkilston.</h1>
        <span class="messageauthor you"><a href="/gm">Dungeon Master</a></span>
        <div class="messagebody" id="msg1">
        • Mapper: Slip?<br>
        • Caller: Kelpie?<br>
        • Quartermaster: Urvan<br>
        • Chronicler: Jelb?<br>
        Whoever is acting as the Caller can let me know where the party heads off to.
        </div>
        </body></html>
        """);

    var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
    File.WriteAllText(
        outlinePath,
        """
        # Adventure Outline

        ## Ch 1 - Kirkilston

        - Dungeon Master: • Mapper: Slip? • Caller: Kelpie? • Quartermaster: Urvan • Chronicler: Jelb? Whoever is acting as the Caller can let me know where the party heads off to, any preparation they make, etc. If you are seeking Nuanda, you can get there in under an hour, and I just need a d6 roll from...
        """);

    var updated = AdventureOutlineUtility.UpdateAdventureOutlineAsync(icDirectory, outlinePath)
        .GetAwaiter()
        .GetResult();
    var outline = File.ReadAllText(outlinePath);

    AssertTrue(updated, "role-assignment outline should replace stale overlong bullets");
    AssertContains(outline, "- Dungeon Master asked players to assume the roles of Caller, Quartermaster, Mapper, and Chronicler.");
    AssertFalse(outline.Contains("Whoever is acting as the Caller", StringComparison.Ordinal), "role-assignment outline should not retain the long excerpt");
}

static void AdventureOutlineSkipsEmptyBulletMarkerPosts()
{
    using var directory = TemporaryDirectory.Create();
    var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
    Directory.CreateDirectory(icDirectory);
    File.WriteAllText(
        Path.Combine(icDirectory, "ch-1.html"),
        """
        <html><body>
        <h1>Ch 1 - Kirkilston.</h1>
        <span class="messageauthor">Kelpie Lawfuller</span>
        <div class="messagebody" id="msg1">•<br>-<br></div>
        <span class="messageauthor">Dungeon Master</span>
        <div class="messagebody" id="msg2">The party leaves town.</div>
        </body></html>
        """);

    var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
        .GetAwaiter()
        .GetResult();

    AssertContains(outline, "- Dungeon Master moves the party out of town.");
    AssertFalse(outline.Contains("Kelpie", StringComparison.Ordinal), "empty bullet marker posts should not produce outline bullets");
    AssertFalse(outline.Contains("advances the scene", StringComparison.OrdinalIgnoreCase), "outline summaries should explain how the scene advanced");
}

static void AdventureOutlineRejectsWeakGeneratedSummaries()
{
    using var directory = TemporaryDirectory.Create();
    var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
    Directory.CreateDirectory(icDirectory);
    File.WriteAllText(
        Path.Combine(icDirectory, "ch-2.html"),
        """
        <html><body>
        <h1>Ch 2 - Supper With Nuanda.</h1>
        <span class="messageauthor">Jelb Garrick</span>
        <div class="messagebody" id="msg1">Jelb agreed to check on the bread, bringing it out to cool when it was ready. I do have something of hers. Her brooch.</div>
        <span class="messageauthor">Nuanda</span>
        <div class="messagebody" id="msg2">The girl, Jelenneth. She was picking berries when bandits abducted her.</div>
        <span class="messageauthor">Urvan Hall</span>
        <div class="messagebody" id="msg3">That is...remarkable. With this information we could look for her.</div>
        </body></html>
        """);

    var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
    File.WriteAllText(
        outlinePath,
        """
        # Adventure Outline

        ## Ch 2 - Supper With Nuanda

        - Jelb advances the scene.
        - Nuanda contributes a new development to the scene.
        - Urvan presses for answers or a decision.
        - Nuanda reassures Kelpie that Morrow and her own magic protect her.
        """);

    AdventureOutlineUtility.UpdateAdventureOutlineAsync(icDirectory, outlinePath)
        .GetAwaiter()
        .GetResult();
    var outline = File.ReadAllText(outlinePath);

    AssertContains(outline, "- Jelb helps with the bread and offers Jelenneth's brooch as a focus.");
    AssertContains(outline, "- Nuanda recounts Jelenneth's abduction by bandits.");
    AssertContains(outline, "- Urvan recognizes that Nuanda's divination gives the party a lead.");

    foreach (var weakSummary in GetWeakAdventureOutlineSummaryPhrases())
    {
        AssertFalse(
            outline.Contains(weakSummary, StringComparison.OrdinalIgnoreCase),
            $"outline should not contain weak generated summary '{weakSummary}'");
    }
}

static string[] GetWeakAdventureOutlineSummaryPhrases()
{
    return
    [
        "advances the scene",
        "adds dialogue that clarifies the exchange",
        "presses for answers or a decision",
        "reveals a concern or reaction",
        "contributes a new development to the scene",
        "handles practical preparations for the party",
        "reassures Kelpie that Morrow and her own magic protect her"
    ];
}

static void AdventureOutlineFallbackSummariesPreserveSceneSpecifics()
{
    using var directory = TemporaryDirectory.Create();
    var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
    Directory.CreateDirectory(icDirectory);
    File.WriteAllText(
        Path.Combine(icDirectory, "ch-5.html"),
        """
        <html><body>
        <h1>Ch 5 - A Betentacled Escape.</h1>
        <span class="messageauthor">Maximilian</span>
        <div class="messagebody" id="msg1">Maximilian studies the recovered scroll and says it names Red Tusk and the Deep Friends.</div>
        <span class="messageauthor">Algorn Druff</span>
        <div class="messagebody" id="msg2">Algorn says the Raven's Pass trail should lead them to Nimba at The Mason's Apron.</div>
        <span class="messageauthor">Billworth Turgen</span>
        <div class="messagebody" id="msg3">Billworth checks the caravan wagons, mules, and remaining cargo before they move again.</div>
        </body></html>
        """);

    var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
        .GetAwaiter()
        .GetResult();

    AssertContains(outline, "- Maximilian connects the current threat to Red Tusk, the Deep Friends, or the Toothbreakers.");
    AssertContains(outline, "- Algorn points the party toward Raven's Pass contacts and support.");
    AssertContains(outline, "- Billworth focuses the scene on the caravan, its route, or its cargo.");

    foreach (var weakSummary in GetWeakAdventureOutlineSummaryPhrases())
    {
        AssertFalse(
            outline.Contains(weakSummary, StringComparison.OrdinalIgnoreCase),
            $"fallback outline should not contain weak generated summary '{weakSummary}'");
    }
}

static void AdventureOutlineMergesNewSavedIcBullets()
{
    using var directory = TemporaryDirectory.Create();
    var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
    Directory.CreateDirectory(icDirectory);
    File.WriteAllText(
        Path.Combine(icDirectory, "ch-1.html"),
        """
        <html><body>
        <h1>Ch 1 - Kirkilston.</h1>
        <span class="messageauthor">Dungeon Master</span>
        <div class="messagebody" id="msg1">The party leaves town.</div>
        <span class="messageauthor">Kelpie Lawfuller</span>
        <div class="messagebody" id="msg2">Kelpie takes the lead.</div>
        </body></html>
        """);
    File.WriteAllText(
        Path.Combine(icDirectory, "ch-2.html"),
        """
        <html><body>
        <h1>Ch 2 - Supper With Nuanda.</h1>
        <span class="messageauthor">Nuanda</span>
        <div class="messagebody" id="msg3">Nuanda shares what she learned.</div>
        </body></html>
        """);

    var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
    File.WriteAllText(
        outlinePath,
        """
        # Adventure Outline

        ## Ch 1 - Kirkilston

        - Dungeon Master: The party leaves town.
        """);

    var updated = AdventureOutlineUtility.UpdateAdventureOutlineAsync(icDirectory, outlinePath)
        .GetAwaiter()
        .GetResult();
    var outline = File.ReadAllText(outlinePath);

    AssertTrue(updated, "existing adventure outline should be updated with missing material");
    AssertContains(outline, "- Dungeon Master moves the party out of town.");
    AssertFalse(outline.Contains("- Dungeon Master: The party leaves town.", StringComparison.Ordinal), "stale author-prefixed excerpts should be replaced");
    AssertContains(outline, "- Kelpie takes the lead.");
    AssertContains(outline, "## Ch 2 - Supper With Nuanda");
    AssertContains(outline, "- Nuanda briefs the party.");
    AssertTrue(
        outline.IndexOf("- Kelpie takes the lead.", StringComparison.Ordinal)
            < outline.IndexOf("## Ch 2 - Supper With Nuanda", StringComparison.Ordinal),
        "missing chapter 1 bullet should remain before chapter 2");
}

static void AdventureOutlineFallsBackToObsidianMarkdown()
{
    using var directory = TemporaryDirectory.Create();
    var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
    var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
    var requestedUrl = string.Empty;

    var updated = AdventureOutlineUtility.UpdateAdventureOutlineAsync(
        icDirectory,
        outlinePath,
        "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/Adventure+Outline",
        (url, _) =>
        {
            requestedUrl = url;
            return Task.FromResult(
                """
                # Adventure Outline

                ## Ch 1 - Kirkilston

                - The party seeks Nuanda.
                """);
        }).GetAwaiter().GetResult();

    AssertTrue(updated, "fallback markdown should write adventure outline when saved IC files are unavailable");
    AssertEqual(
        "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/Adventure+Outline",
        requestedUrl,
        "unexpected fallback markdown URL");
    AssertContains(File.ReadAllText(outlinePath), "- The party seeks Nuanda.");
}

static void AdventureOutlinePrefersSavedIcHtmlOverFallback()
{
    using var directory = TemporaryDirectory.Create();
    var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
    Directory.CreateDirectory(icDirectory);
    File.WriteAllText(
        Path.Combine(icDirectory, "ch-1.html"),
        """
        <html><body>
        <h1>Ch 1 - Kirkilston.</h1>
        <span class="messageauthor">Dungeon Master</span>
        <div class="messagebody" id="msg1">Local chapter material.</div>
        </body></html>
        """);
    var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);
    var fallbackFetchCount = 0;

    AdventureOutlineUtility.UpdateAdventureOutlineAsync(
        icDirectory,
        outlinePath,
        AdventureOutlineUtility.FallbackMarkdownUrl,
        (_, _) =>
        {
            fallbackFetchCount++;
            return Task.FromResult("# Adventure Outline\n\n- Fallback material.");
        }).GetAwaiter().GetResult();

    var outline = File.ReadAllText(outlinePath);
    AssertEqual(0, fallbackFetchCount, "fallback markdown should not be fetched when saved IC HTML builds an outline");
    AssertContains(outline, "- Dungeon Master adds a concrete detail that changes the party's situation.");
    AssertFalse(outline.Contains("Fallback material", StringComparison.Ordinal), "fallback content should not replace local IC outline");
}

static void AdventureOutlineIgnoresFailedFallbackMarkdownFetch()
{
    using var directory = TemporaryDirectory.Create();
    var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
    var outlinePath = Path.Combine(directory.Path, AdventureOutlineUtility.FileName);

    var updated = AdventureOutlineUtility.UpdateAdventureOutlineAsync(
        icDirectory,
        outlinePath,
        AdventureOutlineUtility.FallbackMarkdownUrl,
        (_, _) => Task.FromResult($"{MarkdownUtility.UnresolvedUrlMessage}: fallback"))
        .GetAwaiter()
        .GetResult();

    AssertFalse(updated, "failed fallback markdown fetch should not update adventure outline");
    AssertFalse(File.Exists(outlinePath), "failed fallback markdown fetch should not write an outline file");
}

static void AdjustedPostTalliesAggregateSavedIcHtml()
{
    var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    var postsDirectory = Path.Combine(repositoryRoot, "Release", "Posts", "IC");
    var asideDirectory = Path.Combine(postsDirectory, "Aside");

    var outOfCharacterDirectory = Path.Combine(repositoryRoot, "Release", "Posts", "OOC");

    var counts = RpolThreadPostUtility.GetAdjustedPostTalliesFromSavedHtmlDirectories(
        postsDirectory,
        asideDirectory,
        outOfCharacterDirectory);

    AssertEqual(12, counts.Count, "expected adjusted author count");
    AssertEqual(62, counts[RpolThreadPostUtility.DungeonMasterAuthor], "unexpected Dungeon Master count");
    AssertEqual(0, counts.GetValueOrDefault(RpolThreadPostUtility.BillworthTurgenAuthor, 0), "unexpected Billworth count");
    AssertEqual(6, counts["Geoffroy Morin"], "unexpected Geoffroy count");
    AssertEqual(19, counts["Jelb Garrick"], "unexpected Jelb count");
    AssertEqual(28, counts["Kelpie Lawfuller"], "unexpected Kelpie count");
    AssertEqual(10, counts["Maximilian Yragerne"], "unexpected Maximilian count");
    AssertEqual(5, counts[RpolThreadPostUtility.NuandaAuthor], "unexpected Nuanda count");
    AssertEqual(6, counts[RpolThreadPostUtility.NuandaNemereAuthor], "unexpected Nuanda Nemere count");
    AssertEqual(1, counts["temp-name"], "unexpected temp-name count");
    AssertEqual(1, counts["The-Archon"], "unexpected The-Archon count");
    AssertEqual(3, counts[RpolThreadPostUtility.ThurganNewlAuthor], "unexpected Thurgan count");
    AssertEqual(17, counts["Urvan Hall"], "unexpected Urvan count");
}

static void KeywordSearchFallsBackToThePrefixedTerm()
{
    RunOnStaThread(() =>
    {
        WithTemporaryKeywordIndex(
            """
            {
              "index_metadata": {
                "total_words_indexed": 0
              },
              "words": {
                "The": {
                  "total_occurrences": 1,
                  "matches": [
                    {
                      "url": "https://example.test/the",
                      "count": 1,
                      "last_indexed": "2026-06-28T00:00:00.0000000+00:00"
                    }
                  ]
                },
                "The Coal": {
                  "total_occurrences": 1,
                  "matches": [
                    {
                      "url": "https://example.test/the-coal",
                      "count": 1,
                      "last_indexed": "2026-06-28T00:00:00.0000000+00:00"
                    }
                  ]
                },
                "The Hills": {
                  "total_occurrences": 1,
                  "matches": [
                    {
                      "url": "https://example.test/the-hills",
                      "count": 1,
                      "last_indexed": "2026-06-28T00:00:00.0000000+00:00"
                    }
                  ]
                }
              }
            }
            """,
            () =>
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                var txtSearch = GetControl<TextBox>(form, "txtSearch");
                var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                txtSearch.Text = "The Coal Hills";
                InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                AssertEqual(3, results.Length, "expected three search results from the exact and fallback lookups");
                AssertContains(string.Join("\n", results), "https://example.test/the");
                AssertContains(string.Join("\n", results), "https://example.test/the-coal");
                AssertContains(string.Join("\n", results), "https://example.test/the-hills");
            });
    });
}

static void KeywordSearchKeepsQuotedPhrasesTogether()
{
    RunOnStaThread(() =>
    {
        WithTemporaryKeywordIndex(
            """
            {
              "index_metadata": {
                "total_words_indexed": 0
              },
              "words": {
                "one": {
                  "total_occurrences": 1,
                  "matches": [
                    {
                      "url": "https://example.test/one",
                      "count": 1,
                      "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                    }
                  ]
                },
                "two": {
                  "total_occurrences": 1,
                  "matches": [
                    {
                      "url": "https://example.test/two",
                      "count": 1,
                      "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                    }
                  ]
                },
                "one two": {
                  "total_occurrences": 1,
                  "matches": [
                    {
                      "url": "https://example.test/one-two",
                      "count": 1,
                      "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                    }
                  ]
                },
                "three": {
                  "total_occurrences": 1,
                  "matches": [
                    {
                      "url": "https://example.test/three",
                      "count": 1,
                      "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                    }
                  ]
                }
              }
            }
            """,
            () =>
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                var txtSearch = GetControl<TextBox>(form, "txtSearch");
                var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                txtSearch.Text = "\"one two\" three";
                InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                AssertEqual(2, results.Length, "expected one quoted-phrase result plus one standalone result");
                AssertContains(string.Join("\n", results), "https://example.test/one-two");
                AssertContains(string.Join("\n", results), "https://example.test/three");
                AssertFalse(results.Contains("https://example.test/one", StringComparer.Ordinal), "quoted phrase should not be split into a standalone 'one' lookup");
                AssertFalse(results.Contains("https://example.test/two", StringComparer.Ordinal), "quoted phrase should not be split into a standalone 'two' lookup");
            });
    });
}

static void KeywordSearchAcceptsUrlSourceMetadata()
{
    RunOnStaThread(() =>
    {
        WithTemporaryKeywordIndex(
            """
            {
              "index_metadata": {
                "total_words_indexed": 0
              },
              "urls": {
                "https://example.test/rpol-entry": {
                  "source": "RPOL"
                },
                "https://example.test/obsidian-entry": {
                  "source": "Obsidian wiki"
                }
              },
              "words": {
                "entry": {
                  "total_occurrences": 2,
                  "matches": [
                    {
                      "url": "https://example.test/rpol-entry",
                      "count": 1,
                      "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                    },
                    {
                      "url": "https://example.test/obsidian-entry",
                      "count": 1,
                      "last_indexed": "2026-06-29T00:00:00.0000000+00:00"
                    }
                  ]
                }
              }
            }
            """,
            () =>
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                var txtSearch = GetControl<TextBox>(form, "txtSearch");
                var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                txtSearch.Text = "entry";
                InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                AssertEqual(2, results.Length, "expected both matches to be returned when url source metadata is present");
                AssertContains(string.Join("\n", results), "https://example.test/rpol-entry");
                AssertContains(string.Join("\n", results), "https://example.test/obsidian-entry");
            });
    });
}

static void KeywordSearchFiltersRpolHeroMetadataOnlyHits()
{
    RunOnStaThread(() =>
    {
        WithTemporaryKeywordIndex(
            """
            {
              "index_metadata": {
                "total_words_indexed": 0
              },
              "words": {
                "Kelpie Lawfuller": {
                  "total_occurrences": 3,
                  "matches": [
                    {
                      "url": "https://rpol.net/display.cgi?gi=80170&ti=11",
                      "count": 1,
                      "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                    },
                    {
                      "url": "https://rpol.net/display.cgi?gi=80170&ti=12",
                      "count": 1,
                      "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                    },
                    {
                      "url": "https://publish.obsidian.md/scarlethorizons/PCs/Kelpie+Lawfuller",
                      "count": 1,
                      "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                    }
                  ]
                }
              }
            }
            """,
            () =>
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                var txtSearch = GetControl<TextBox>(form, "txtSearch");
                var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");
                var bodyCheckCount = 0;

                SetPrivateField(
                    form,
                    "_playerCharacterListingMarkdown",
                    """
                    | Name | Character | Notes | Hero |
                    | --- | --- | --- | --- |
                    | Kelpie Lawfuller | [[Kelpie Lawfuller]] | active | ![[kelpie-token.webp]] |
                    """);
                SetPrivateField(
                    form,
                    "_rpolHeroNameBodyMatchProvider",
                    (Func<string, string, CancellationToken, Task<bool>>)((url, term, _) =>
                    {
                        bodyCheckCount++;
                        AssertEqual("Kelpie Lawfuller", term, "unexpected hero term passed to RPOL body filter");
                        return Task.FromResult(url.Contains("ti=12", StringComparison.Ordinal));
                    }));

                txtSearch.Text = "\"Kelpie Lawfuller\"";
                InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                AssertEqual(2, results.Length, "expected one RPOL body hit and one Obsidian hit");
                AssertEqual(2, bodyCheckCount, "expected both RPOL matches to be checked against post bodies");
                AssertContains(string.Join("\n", results), "https://rpol.net/display.cgi?gi=80170&ti=12&msgpage=&show=all");
                AssertContains(string.Join("\n", results), "https://publish.obsidian.md/scarlethorizons/PCs/Kelpie+Lawfuller");
                AssertFalse(
                    results.Contains("https://rpol.net/display.cgi?gi=80170&ti=11&msgpage=&show=all", StringComparer.Ordinal),
                    "metadata-only RPOL hit should be excluded for hero-name searches");
            });
    });
}

static void ShowMenuContainsXpItem()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
            ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
        var xpMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "xpToolStripMenuItem")
            ?? throw new InvalidOperationException("xpToolStripMenuItem was null."));

        AssertEqual("XP", xpMenuItem.Text ?? string.Empty, "unexpected XP menu item text");
        AssertTrue(
            showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(xpMenuItem),
            "Show menu should contain the XP item");
    });
}

static void ShowMenuContainsPartyItem()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
            ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
        var partyMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "partyToolStripMenuItem")
            ?? throw new InvalidOperationException("partyToolStripMenuItem was null."));

        AssertEqual("Party", partyMenuItem.Text ?? string.Empty, "unexpected Party menu item text");
        AssertTrue(
            showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(partyMenuItem),
            "Show menu should contain the Party item");
    });
}

static void ShowMenuContainsFormerPcsItem()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
            ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
        var partyMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "partyToolStripMenuItem")
            ?? throw new InvalidOperationException("partyToolStripMenuItem was null."));
        var formerPcsMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "formerPcsToolStripMenuItem")
            ?? throw new InvalidOperationException("formerPcsToolStripMenuItem was null."));

        AssertEqual("Former PCs", formerPcsMenuItem.Text ?? string.Empty, "unexpected Former PCs menu item text");
        AssertTrue(
            showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(formerPcsMenuItem),
            "Show menu should contain the Former PCs item");
        AssertEqual(
            showMenuItem.DropDownItems.IndexOf(partyMenuItem) + 1,
            showMenuItem.DropDownItems.IndexOf(formerPcsMenuItem),
            "Former PCs should appear immediately after Party");
    });
}

static void FormerPcsViewDisplaysTokenNameAndClass()
{
    using var directory = TemporaryDirectory.Create();
    var tokenPath = Path.Combine(directory.Path, "urvan-token.png");
    using (var token = new Bitmap(8, 8))
    {
        token.SetPixel(0, 0, Color.DarkRed);
        token.Save(tokenPath, ImageFormat.Png);
    }

    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        InvokePrivateMethod(
            form,
            "ShowFormerPcs",
            (object)new[] { new FormerPcSummary("Urvan", "Paladin of St. Ygg", tokenPath) });

        var panel = (Panel)(GetPrivateField(form, "_partyPanel")
            ?? throw new InvalidOperationException("Former PCs panel was null."));
        var controls = panel.Controls
            .Cast<Control>()
            .SelectMany(control => control.Controls.Cast<Control>().Prepend(control))
            .ToArray();

        AssertTrue(controls.OfType<Label>().Any(label => label.Text == "Urvan"), "former PC name should be displayed");
        AssertTrue(controls.OfType<Label>().Any(label => label.Text == "Paladin of St. Ygg"), "former PC class should be displayed");
        AssertTrue(controls.OfType<PictureBox>().Any(pictureBox => pictureBox.Image is not null), "former PC token image should be displayed");
    });
}

static void ShowMenuContainsMyHeroBriefingItem()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
            ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
        var myHeroBriefingMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "myHeroBriefingToolStripMenuItem")
            ?? throw new InvalidOperationException("myHeroBriefingToolStripMenuItem was null."));

        AssertEqual("My Hero Briefing", myHeroBriefingMenuItem.Text ?? string.Empty, "unexpected My Hero Briefing menu item text");
        AssertTrue(
            showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(myHeroBriefingMenuItem),
            "Show menu should contain the My Hero Briefing item");
        AssertTrue(
            showMenuItem.DropDownItems.IndexOf(myHeroBriefingMenuItem) > showMenuItem.DropDownItems.IndexOf((ToolStripItem)(GetPrivateField(form, "partyToolStripMenuItem")
                ?? throw new InvalidOperationException("partyToolStripMenuItem was null."))),
            "My Hero Briefing should appear after Party");
    });
}

static void ShowMenuContainsAdventureOutlineItem()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        var showMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "showToolStripMenuItem")
            ?? throw new InvalidOperationException("showToolStripMenuItem was null."));
        var adventureOutlineMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "adventureOutlineToolStripMenuItem")
            ?? throw new InvalidOperationException("adventureOutlineToolStripMenuItem was null."));

        AssertEqual("Adventure Outline", adventureOutlineMenuItem.Text ?? string.Empty, "unexpected Adventure Outline menu item text");
        AssertTrue(
            showMenuItem.DropDownItems.Cast<ToolStripItem>().Contains(adventureOutlineMenuItem),
            "Show menu should contain the Adventure Outline item");
    });
}

static void AdventureOutlineViewDisplaysGeneratedMarkdown()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        const string outline = """
        ---
        title: Adventure Outline
        aliases:
          - Scarlet Horizons Adventure Outline
        ---

        # Adventure Outline

        - Source files inspected:
          - `C:/repos/player-assistant/Release/Posts/IC/ch-1.html`

        ## Chapter 7 - The Gate Opens

        - Kelpie: Found the hidden key.

        -

        ## Ch 4 - Battle at Blightstone Pit

        - The party fights at the quarry.

        ## Ch 5 - A Betentacled Escape

        - The party escapes the pit.

        ## Ch 2 - Supper With Nuanda

        - Jelb weighs the party's options.
        - Dungeon Master frames the next choice.

        ## Ch 3 - Joining the Caravan to Raven's Pass

        - The party joins the caravan.
        """;

        InvokePrivateMethod(form, "ShowAdventureOutline", outline);

        var textBox = (RichTextBox)(GetPrivateField(form, "_adventureOutlineTextBox")
            ?? throw new InvalidOperationException("_adventureOutlineTextBox was null."));
        var adventureOutlineMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "adventureOutlineToolStripMenuItem")
            ?? throw new InvalidOperationException("adventureOutlineToolStripMenuItem was null."));

        AssertTrue(form.Controls.Contains(textBox), "adventure outline text box should be attached to the form");
        AssertTrue(textBox.ReadOnly, "adventure outline text box should be read-only");
        AssertContains(textBox.Text, "Chapter 7 - The Gate Opens");
        AssertContains(textBox.Text, "Kelpie: Found the hidden key.");
        AssertContains(textBox.Text, "Ch 2 - Supper With Nuanda");
        AssertContains(textBox.Text, "Jelb weighs the party's options.");
        AssertContains(textBox.Text, "Dungeon Master frames the next choice.");
        AssertTrue(
            textBox.Text.IndexOf("Chapter 7 - The Gate Opens", StringComparison.Ordinal)
                < textBox.Text.IndexOf("Ch 4 - Battle at Blightstone Pit", StringComparison.Ordinal),
            "adventure outline display should keep chapter order from the markdown");
        AssertTrue(
            textBox.Text.IndexOf("Ch 4 - Battle at Blightstone Pit", StringComparison.Ordinal)
                < textBox.Text.IndexOf("Ch 5 - A Betentacled Escape", StringComparison.Ordinal),
            "chapter 4 should display before chapter 5");
        AssertTrue(
            textBox.Text.IndexOf("Ch 5 - A Betentacled Escape", StringComparison.Ordinal)
                < textBox.Text.IndexOf("Ch 2 - Supper With Nuanda", StringComparison.Ordinal),
            "chapter 5 should display before the later markdown chapter 2 entry in this fixture");
        AssertTrue(
            textBox.Text.IndexOf("Ch 2 - Supper With Nuanda", StringComparison.Ordinal)
                < textBox.Text.IndexOf("Ch 3 - Joining the Caravan to Raven's Pass", StringComparison.Ordinal),
            "chapter 2 should display before chapter 3");
        AssertFalse(textBox.Text.Contains("title: Adventure Outline", StringComparison.Ordinal), "player-facing outline should hide YAML frontmatter");
        AssertFalse(textBox.Text.Contains("aliases:", StringComparison.Ordinal), "player-facing outline should hide YAML frontmatter keys");
        AssertFalse(textBox.Text.Contains("Scarlet Horizons Adventure Outline", StringComparison.Ordinal), "player-facing outline should hide YAML frontmatter values");
        AssertFalse(textBox.Lines.Any(line => line.Trim().Equals("-", StringComparison.Ordinal)), "player-facing outline should hide empty bullet marker lines");
        for (var lineIndex = 0; lineIndex < textBox.Lines.Length; lineIndex++)
        {
            if (textBox.Lines[lineIndex].Length == 0)
            {
                var lineStart = textBox.GetFirstCharIndexFromLine(lineIndex);
                if (lineStart < 0)
                {
                    continue;
                }

                textBox.Select(lineStart, 0);
                AssertFalse(textBox.SelectionBullet, "blank outline lines should not render as empty bullets");
            }
        }

        var chapterStart = textBox.Text.IndexOf("Ch 2 - Supper With Nuanda", StringComparison.Ordinal);
        textBox.Select(chapterStart, 1);
        AssertTrue(textBox.SelectionFont?.Bold == true, "chapter headings should be bold");
        AssertEqual(16f, textBox.SelectionFont?.Size ?? 0f, "chapter headings should use the enlarged adventure outline font");
        var jelbStart = textBox.Text.IndexOf("Jelb weighs the party's options.", StringComparison.Ordinal);
        textBox.Select(jelbStart, 1);
        AssertTrue(textBox.SelectionFont?.Bold == false, "summary bullet text should use regular font");
        AssertEqual(12f, textBox.SelectionFont?.Size ?? 0f, "summary bullet text should use the enlarged adventure outline font");
        var dungeonStart = textBox.Text.IndexOf("Dungeon Master frames the next choice.", StringComparison.Ordinal);
        textBox.Select(dungeonStart, 1);
        AssertTrue(textBox.SelectionFont?.Bold == false, "summary bullet text should not inherit heading bold");
        AssertEqual(12f, textBox.SelectionFont?.Size ?? 0f, "summary bullet text should keep the enlarged adventure outline font");
        AssertFalse(textBox.Text.Contains("Source files inspected", StringComparison.Ordinal), "player-facing outline should hide source file audit text");
        AssertFalse(textBox.Text.Contains("ch-1.html", StringComparison.Ordinal), "player-facing outline should hide source file paths");
        AssertFalse(adventureOutlineMenuItem.Enabled, "Adventure Outline menu item should be disabled while the outline is active");
    });
}

static void AboutMenuContainsAuthorAndUpdateItems()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        var menuStrip = (MenuStrip)(GetPrivateField(form, "menuStrip")
            ?? throw new InvalidOperationException("menuStrip was null."));
        var settingsMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "settingsToolStripMenuItem")
            ?? throw new InvalidOperationException("settingsToolStripMenuItem was null."));
        var aboutMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "aboutToolStripMenuItem")
            ?? throw new InvalidOperationException("aboutToolStripMenuItem was null."));
        var authorMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "authorToolStripMenuItem")
            ?? throw new InvalidOperationException("authorToolStripMenuItem was null."));
        var checkForUpdateMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "checkForUpdateToolStripMenuItem")
            ?? throw new InvalidOperationException("checkForUpdateToolStripMenuItem was null."));
        var versionMenuItem = (ToolStripMenuItem)(GetPrivateField(form, "versionToolStripMenuItem")
            ?? throw new InvalidOperationException("versionToolStripMenuItem was null."));

        var topLevelItems = menuStrip.Items.Cast<ToolStripItem>().ToArray();
        AssertEqual("About", aboutMenuItem.Text ?? string.Empty, "unexpected About menu text");
        AssertEqual(
            Array.IndexOf(topLevelItems, settingsMenuItem) + 1,
            Array.IndexOf(topLevelItems, aboutMenuItem),
            "About menu should be immediately to the right of Settings");
        AssertEqual("Author", authorMenuItem.Text ?? string.Empty, "unexpected Author menu item text");
        AssertEqual("Check for Updates", checkForUpdateMenuItem.Text ?? string.Empty, "unexpected update menu item text");
        AssertEqual("Version", versionMenuItem.Text ?? string.Empty, "unexpected version menu item text");
        AssertTrue(
            aboutMenuItem.DropDownItems.Cast<ToolStripItem>().SequenceEqual([authorMenuItem, checkForUpdateMenuItem, versionMenuItem]),
            "About menu should contain Author, Check for Updates, then Version");
    });
}

static void AboutAuthorTextListsDeveloperInfo()
{
    var authorText = (string)(InvokeStaticMethod(typeof(Form1), "GetAuthorInfoText")
        ?? throw new InvalidOperationException("GetAuthorInfoText returned null."));
    AssertEqual(
        string.Join(Environment.NewLine, "Bryan Miller", "kyrathasoft@gmail.com", "bryanmiller.us"),
        authorText,
        "author info text should list developer details on separate lines");
}

static void AboutVersionTextShowsAppVersion()
{
    var versionText = (string)(InvokeStaticMethod(typeof(Form1), "GetAppVersionText")
        ?? throw new InvalidOperationException("GetAppVersionText returned null."));
    AssertEqual("RPOL Scarlet Horizon Campaign Assistant 0.9.4", versionText, "unexpected About Version text");
}

static void UpdateCheckVerifiesSignedPAssistManifest()
{
    var signed = CreateSignedUpdateManifest(
        """
        {
          "schema_version": 1,
          "updates": [
            {
              "version": "0.9.0",
              "url": "https://bryanmiller.us/scarlethorizons/p-assist-0.9.0.zip",
              "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
              "installer_url": "https://bryanmiller.us/scarlethorizons/p-assist-0.9.0.exe",
              "installer_sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
            }
          ]
        }
        """);
    var update = PlayerAssistantUpdateUtility.FindLatestUpdateFromSignedManifest(
        signed.ManifestJson,
        signed.SignatureText,
        PlayerAssistantUpdateUtility.UpdateManifestUri,
        [CreateActiveSigningKey(signed.PublicKeyPem)]);

    AssertTrue(update is not null, "expected signed update archive to be detected");
    AssertEqual("0.9.0", update!.VersionText, "unexpected parsed update version text");
    AssertEqual(new Version(0, 9, 0), update.Version, "unexpected parsed update version");
    AssertEqual(
        "https://bryanmiller.us/scarlethorizons/p-assist-0.9.0.zip",
        update.DownloadUri.AbsoluteUri,
        "unexpected update download URL");
    AssertEqual(
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        update.Sha256,
        "unexpected update SHA256");
    AssertEqual(
        "https://bryanmiller.us/scarlethorizons/p-assist-0.9.0.exe",
        update.InstallerUri.AbsoluteUri,
        "unexpected installer URL");
    AssertEqual(
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
        update.InstallerSha256,
        "unexpected installer SHA256");
}

static void UpdateCheckAcceptsManifestSignatureMadeBeforeTrailingNewline()
{
    const string manifestJson =
        """
        {
          "schema_version": 1,
          "updates": [
            {
              "version": "0.9.5",
              "url": "p-assist-0.9.5.zip",
              "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
              "installer_url": "p-assist-0.9.5.exe",
              "installer_sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
            }
          ]
        }
        """;
    using var rsa = RSA.Create(2048);
    var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);
    var downloadedManifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson + Environment.NewLine);
    var signatureBytes = rsa.SignData(
        manifestBytes,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);

    var update = PlayerAssistantUpdateUtility.FindLatestUpdateFromSignedManifest(
        downloadedManifestBytes,
        Convert.ToBase64String(signatureBytes),
        PlayerAssistantUpdateUtility.UpdateManifestUri,
        [CreateActiveSigningKey(rsa.ExportSubjectPublicKeyInfoPem())]);

    AssertTrue(update is not null, "expected update manifest with trailing newline to verify");
    AssertEqual("0.9.5", update!.VersionText, "unexpected parsed update version");
}

static void UpdateCheckChoosesNewestSignedManifestEntry()
{
    var signed = CreateSignedUpdateManifest(
        """
        {
          "schema_version": 1,
          "updates": [
            {
              "version": "0.9.0",
              "url": "p-assist-0.9.0.zip",
              "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
              "installer_url": "p-assist-0.9.0.exe",
              "installer_sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
            },
            {
              "version": "0.10.0",
              "url": "p-assist-0.10.0.zip",
              "sha256": "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
              "installer_url": "p-assist-0.10.0.exe",
              "installer_sha256": "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD"
            },
            {
              "version": "99.0.0",
              "url": "https://unexpected.example.test/p-assist-99.0.0.zip",
              "sha256": "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE",
              "installer_url": "https://unexpected.example.test/p-assist-99.0.0.exe",
              "installer_sha256": "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"
            },
            {
              "version": "0.11.0",
              "url": "p-assist-0.10.0.zip",
              "sha256": "1111111111111111111111111111111111111111111111111111111111111111",
              "installer_url": "p-assist-0.11.0.exe",
              "installer_sha256": "2222222222222222222222222222222222222222222222222222222222222222"
            }
          ]
        }
        """);
    var update = PlayerAssistantUpdateUtility.FindLatestUpdateFromSignedManifest(
        signed.ManifestJson,
        signed.SignatureText,
        PlayerAssistantUpdateUtility.UpdateManifestUri,
        [CreateActiveSigningKey(signed.PublicKeyPem)]);

    AssertTrue(update is not null, "expected latest signed update archive to be detected");
    AssertEqual("0.10.0", update!.VersionText, "expected newest valid allowed archive version");
    AssertEqual(
        "https://bryanmiller.us/scarlethorizons/p-assist-0.10.0.zip",
        update.DownloadUri.AbsoluteUri,
        "unexpected newest update URL");
    AssertEqual(
        "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD",
        update.InstallerSha256,
        "unexpected newest installer SHA256");
}

static void UpdateCheckRejectsTamperedSignedManifest()
{
    const string manifestJson =
        """
        {
          "schema_version": 1,
          "updates": [
            {
              "version": "0.9.0",
              "url": "p-assist-0.9.0.zip",
              "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
              "installer_url": "p-assist-0.9.0.exe",
              "installer_sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
            }
          ]
        }
        """;
    var signed = CreateSignedUpdateManifest(manifestJson);
    var tamperedManifestJson = manifestJson.Replace("0.9.0", "0.9.1", StringComparison.Ordinal);

    var exception = AssertThrows<InvalidOperationException>(() =>
        PlayerAssistantUpdateUtility.FindLatestUpdateFromSignedManifest(
            tamperedManifestJson,
            signed.SignatureText,
            PlayerAssistantUpdateUtility.UpdateManifestUri,
            [CreateActiveSigningKey(signed.PublicKeyPem)]));

    AssertContains(exception.Message, "signature");
}

static void UpdateCheckRejectsRetiredManifestSigningKey()
{
    var signed = CreateSignedUpdateManifest(
        """
        {
          "schema_version": 1,
          "updates": [
            {
              "version": "0.9.5",
              "url": "p-assist-0.9.5.zip",
              "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
              "installer_url": "p-assist-0.9.5.exe",
              "installer_sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
            }
          ]
        }
        """);
    var retiredKeys = new[]
    {
        new UpdateManifestSigningKeyTrustEntry(
            "retired-key",
            signed.PublicKeyPem,
            NotAfterUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    };
    var nowUtc = new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero);

    var exception = AssertThrows<InvalidOperationException>(() =>
        PlayerAssistantUpdateUtility.FindLatestUpdateFromSignedManifest(
            System.Text.Encoding.UTF8.GetBytes(signed.ManifestJson),
            signed.SignatureText,
            PlayerAssistantUpdateUtility.UpdateManifestUri,
            retiredKeys,
            nowUtc));

    AssertContains(exception.Message, "retired");
    AssertContains(exception.Message, "retired-key");
}

static void UpdateCheckComparesAgainstCurrentAppVersion()
{
    var currentVersion = PlayerAssistantUpdateUtility.GetCurrentAppVersion();
    AssertEqual(new Version(0, 9, 1), currentVersion, "unexpected current app update-comparison version");

    var sameVersion = new PlayerAssistantUpdateInfo(
        new Version(0, 9, 1),
        "0.9.1",
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.zip"),
        new string('A', 64),
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.exe"),
        new string('B', 64));
    var newerVersion = new PlayerAssistantUpdateInfo(
        new Version(0, 9, 2),
        "0.9.2",
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.zip"),
        new string('C', 64),
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.exe"),
        new string('D', 64));

    AssertFalse(sameVersion.IsNewerThan(currentVersion), "same version should not be offered as an update");
    AssertTrue(newerVersion.IsNewerThan(currentVersion), "newer version should be offered as an update");
}

static void UpdateCheckReportsLatestVersionMessage()
{
    var message = (string)(InvokeStaticMethod(typeof(Form1), "GetLatestVersionMessage")
        ?? throw new InvalidOperationException("GetLatestVersionMessage returned null."));
    AssertEqual(
        "You are using the latest version of this software.",
        message,
        "unexpected no-update message text");
}

static void UpdateCheckFetchesSignedManifestFromAllowedUpdateHost()
{
    using var directory = TemporaryDirectory.Create();
    var statePath = Path.Combine(directory.Path, "trusted-update-state.json");
    var signed = CreateSignedUpdateManifest(
        """
        {
          "schema_version": 1,
          "updates": [
            {
              "version": "0.9.5",
              "url": "p-assist-0.9.5.zip",
              "sha256": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
              "installer_url": "p-assist-0.9.5.exe",
              "installer_sha256": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"
            }
          ]
        }
        """);
    var requestUris = new List<string>();
    using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((request, _) =>
    {
        requestUris.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
        if (request.RequestUri == PlayerAssistantUpdateUtility.UpdateManifestUri)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(signed.ManifestJson))
            });
        }

        if (request.RequestUri == PlayerAssistantUpdateUtility.UpdateManifestSignatureUri)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(signed.SignatureText)
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            RequestMessage = request
        });
    }));

    var update = PlayerAssistantUpdateUtility
        .CheckForLatestUpdateAsync(httpClient, [CreateActiveSigningKey(signed.PublicKeyPem)], statePath)
        .GetAwaiter()
        .GetResult();
    AssertEqual(2, requestUris.Count, "expected manifest and signature requests");
    AssertEqual(
        PlayerAssistantUpdateUtility.UpdateManifestUri.AbsoluteUri,
        requestUris[0],
        "unexpected update manifest request URL");
    AssertEqual(
        PlayerAssistantUpdateUtility.UpdateManifestSignatureUri.AbsoluteUri,
        requestUris[1],
        "unexpected update manifest signature request URL");
    AssertTrue(update is not null, "expected update archive from scripted signed manifest");
    AssertEqual("0.9.5", update!.VersionText, "unexpected fetched update version");
    AssertEqual(
        "https://bryanmiller.us/scarlethorizons/p-assist-0.9.5.exe",
        update.InstallerUri.AbsoluteUri,
        "unexpected fetched installer URL");

    var launchValidation = ExternalUrlLaunchUtility.Validate(update.DownloadUri.AbsoluteUri);
    AssertTrue(launchValidation.IsAllowed, launchValidation.RejectionReason ?? "update download URL should be launchable");
}

static void UpdateCheckRemembersHighestTrustedVersionObserved()
{
    using var directory = TemporaryDirectory.Create();
    var statePath = Path.Combine(directory.Path, "trusted-update-state.json");
    var currentVersion = new Version(0, 9, 1);
    var update = new PlayerAssistantUpdateInfo(
        new Version(0, 9, 2),
        "0.9.2",
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.zip"),
        new string('A', 64),
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.exe"),
        new string('B', 64));

    var result = PlayerAssistantUpdateUtility.ApplyTrustedUpdateVersionPolicy(update, currentVersion, statePath);
    var storedVersion = PlayerAssistantUpdateUtility.TryReadTrustedUpdateVersion(statePath);

    AssertTrue(result is not null, "expected trusted update to remain available");
    AssertEqual("0.9.2", result!.VersionText, "unexpected trusted update version");
    AssertEqual(new Version(0, 9, 2), storedVersion!, "expected highest trusted version to be recorded");
}

static void LegacyTrustedUpdateStateMigratesToProtectedFormat()
{
    using var directory = TemporaryDirectory.Create();
    var statePath = Path.Combine(directory.Path, "trusted-update-state.json");
    File.WriteAllText(
        statePath,
        """
        {
          "schema_version": 1,
          "highest_trusted_version": "0.9.2",
          "recorded_at": "2026-07-04T00:00:00.0000000+00:00"
        }
        """);

    var version = PlayerAssistantUpdateUtility.TryReadTrustedUpdateVersion(statePath);
    var protectedJson = File.ReadAllText(statePath);

    AssertEqual(new Version(0, 9, 2), version!, "unexpected migrated trusted update version");
    AssertContains(protectedJson, "\"format\": \"app-protected-v3\"");
    AssertContains(protectedJson, "\"key_scope\":");
}

static void TrustedUpdateStateIsEncryptedAtRest()
{
    using var directory = TemporaryDirectory.Create();
    var statePath = Path.Combine(directory.Path, "trusted-update-state.json");

    PlayerAssistantUpdateUtility.ApplyTrustedUpdateVersionPolicy(
        new PlayerAssistantUpdateInfo(
            new Version(0, 9, 2),
            "0.9.2",
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.zip"),
            new string('A', 64),
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.exe"),
            new string('B', 64)),
        new Version(0, 9, 1),
        statePath);

    var encryptedJson = File.ReadAllText(statePath);
    AssertContains(encryptedJson, "\"format\": \"app-protected-v3\"");
    AssertContains(encryptedJson, "\"key_scope\":");
    AssertFalse(encryptedJson.Contains("0.9.2", StringComparison.Ordinal), "trusted version should not be stored in plaintext");
}

static void TrustedUpdateStateRejectsTamperedPayload()
{
    using var directory = TemporaryDirectory.Create();
    var statePath = Path.Combine(directory.Path, "trusted-update-state.json");

    PlayerAssistantUpdateUtility.ApplyTrustedUpdateVersionPolicy(
        new PlayerAssistantUpdateInfo(
            new Version(0, 9, 2),
            "0.9.2",
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.zip"),
            new string('A', 64),
            new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.exe"),
            new string('B', 64)),
        new Version(0, 9, 1),
        statePath);

    using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(statePath)))
    {
        var payload = document.RootElement.GetProperty("payload").GetString() ?? string.Empty;
        var payloadBytes = Convert.FromBase64String(payload);
        payloadBytes[^1] ^= 0x7F;
        File.WriteAllText(
            statePath,
            $$"""
            {
              "schema_version": 1,
              "format": "app-protected-v3",
              "payload": "{{Convert.ToBase64String(payloadBytes)}}"
            }
            """);
    }

    var exception = AssertThrows<InvalidOperationException>(() =>
        PlayerAssistantUpdateUtility.TryReadTrustedUpdateVersion(statePath));
    AssertContains(exception.Message, "authenticate or decrypt");
}

static void UpdateCheckRejectsSignedManifestRollbackBelowTrustedVersionFloor()
{
    using var directory = TemporaryDirectory.Create();
    var statePath = Path.Combine(directory.Path, "trusted-update-state.json");
    var currentVersion = new Version(0, 9, 1);
    var newerTrustedUpdate = new PlayerAssistantUpdateInfo(
        new Version(0, 9, 2),
        "0.9.2",
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.zip"),
        new string('A', 64),
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.2.exe"),
        new string('B', 64));
    var rolledBackUpdate = new PlayerAssistantUpdateInfo(
        new Version(0, 9, 1),
        "0.9.1",
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.zip"),
        new string('C', 64),
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.exe"),
        new string('D', 64));

    PlayerAssistantUpdateUtility.ApplyTrustedUpdateVersionPolicy(newerTrustedUpdate, currentVersion, statePath);
    var exception = AssertThrows<InvalidOperationException>(() =>
        PlayerAssistantUpdateUtility.ApplyTrustedUpdateVersionPolicy(rolledBackUpdate, currentVersion, statePath));

    AssertContains(exception.Message, "downgrade");
    AssertContains(exception.Message, "0.9.2");
}

static void VerifiedUpdaterDownloadsInstallerToControlledPath()
{
    using var directory = TemporaryDirectory.Create();
    var update = new PlayerAssistantUpdateInfo(
        new Version(0, 9, 1),
        "0.9.1",
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.zip"),
        new string('A', 64),
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.exe"),
        new string('B', 64));
    var installerBytes = System.Text.Encoding.UTF8.GetBytes("signed installer bytes");
    var installerSha256 = Convert.ToHexString(SHA256.HashData(installerBytes));
    update = update with { InstallerSha256 = installerSha256 };

    using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((request, _) =>
    {
        AssertEqual(update.InstallerUri.AbsoluteUri, request.RequestUri?.AbsoluteUri ?? string.Empty, "unexpected installer download URL");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(installerBytes)
        });
    }));

    var result = VerifiedInstallerUpdateUtility.DownloadVerifiedInstallerAsync(
        httpClient,
        update,
        new AuthenticodeSignaturePolicy("CN=KyrathaSoft", "ABC123"),
        _ => new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "ABC123"),
        directory.Path).GetAwaiter().GetResult();

    AssertTrue(result.InstallerPath.StartsWith(directory.Path, StringComparison.OrdinalIgnoreCase), "installer should download under the controlled directory");
    AssertTrue(File.Exists(result.InstallerPath), "verified installer should exist on disk");
    AssertEqual(installerSha256, result.Sha256, "unexpected downloaded installer SHA256");
    AssertFalse(result.ReusedExistingFile, "fresh installer download should not report reuse");
}

static void UpdateHostCertificatePinningAcceptsTrustedLeafPin()
{
    var isValid = CertificatePinningUtility.ValidatePinnedRequest(
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-updates.json"),
        ["Cs2RWBFFnGtCidcPrPVbM4awHfkwOQAdfcF2KohmJFc="],
        SslPolicyErrors.None,
        new CertificatePinningPolicy(
            "bryanmiller.us",
            [
                new CertificatePinTrustEntry("Cs2RWBFFnGtCidcPrPVbM4awHfkwOQAdfcF2KohmJFc="),
                new CertificatePinTrustEntry("nWN7PSep5XDQdge5zK24CnCRXHr3KvzhKEGxsdqCX9E=")
            ]));

    AssertTrue(isValid, "expected trusted leaf pin to satisfy update host pinning");
}

static void UpdateHostCertificatePinningAcceptsTrustedIntermediatePin()
{
    var isValid = CertificatePinningUtility.ValidatePinnedRequest(
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-updates.json"),
        ["nWN7PSep5XDQdge5zK24CnCRXHr3KvzhKEGxsdqCX9E="],
        SslPolicyErrors.None,
        new CertificatePinningPolicy(
            "bryanmiller.us",
            [
                new CertificatePinTrustEntry("Cs2RWBFFnGtCidcPrPVbM4awHfkwOQAdfcF2KohmJFc="),
                new CertificatePinTrustEntry("nWN7PSep5XDQdge5zK24CnCRXHr3KvzhKEGxsdqCX9E=")
            ]));

    AssertTrue(isValid, "expected trusted intermediate pin to satisfy update host pinning");
}

static void UpdateHostCertificatePinningSupportsRotationWindow()
{
    var now = new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero);
    var isValid = CertificatePinningUtility.ValidatePinnedRequest(
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-updates.json"),
        ["ROTATEDPINAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="],
        SslPolicyErrors.None,
        new CertificatePinningPolicy(
            "bryanmiller.us",
            [
                new CertificatePinTrustEntry(
                    "OLDPINAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    NotAfterUtc: now.AddDays(7)),
                new CertificatePinTrustEntry(
                    "ROTATEDPINAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    NotBeforeUtc: now.AddDays(-7))
            ]),
        now);

    AssertTrue(isValid, "expected rotated pin within overlap window to be trusted");
}

static void UpdateHostCertificatePinningRejectsRetiredPin()
{
    var isValid = CertificatePinningUtility.ValidatePinnedRequest(
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-updates.json"),
        ["OLDPINAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="],
        SslPolicyErrors.None,
        new CertificatePinningPolicy(
            "bryanmiller.us",
            [
                new CertificatePinTrustEntry(
                    "OLDPINAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    NotAfterUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                new CertificatePinTrustEntry(
                    "NEWPINAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    NotBeforeUtc: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero))
            ]),
        new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero));

    AssertFalse(isValid, "expected retired pin outside its rotation window to be rejected");
}

static void UpdateHostCertificatePinningRejectsMismatchedPin()
{
    var isValid = CertificatePinningUtility.ValidatePinnedRequest(
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-updates.json"),
        ["AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="],
        SslPolicyErrors.None,
        new CertificatePinningPolicy(
            "bryanmiller.us",
            [
                new CertificatePinTrustEntry("Cs2RWBFFnGtCidcPrPVbM4awHfkwOQAdfcF2KohmJFc="),
                new CertificatePinTrustEntry("nWN7PSep5XDQdge5zK24CnCRXHr3KvzhKEGxsdqCX9E=")
            ]));

    AssertFalse(isValid, "expected mismatched pin to be rejected for update host");
}

static void UpdateHostCertificateValidationAllowsTrustedTlsWithPinMismatch()
{
    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        "https://bryanmiller.us/scarlethorizons/p-assist-updates.json");

    AssertTrue(
        CertificatePinningUtility.ValidateServerCertificate(
            request,
            certificate: null,
            chain: null,
            SslPolicyErrors.None),
        "trusted TLS should be accepted even when update-host pins are unavailable or mismatched because manifests are signed");
    AssertFalse(
        CertificatePinningUtility.ValidateServerCertificate(
            request,
            certificate: null,
            chain: null,
            SslPolicyErrors.RemoteCertificateChainErrors),
        "TLS validation errors should still be rejected for update checks");
}

static void CertificateValidationSkipsPinExtractionForNonUpdateHosts()
{
    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking");
    using var mapRequest = new HttpRequestMessage(
        HttpMethod.Get,
        "https://bryanmiller.us/blog/content/bryan/blog/images/rpg-maps/northernreaches.png");

    AssertTrue(
        CertificatePinningUtility.ValidateServerCertificate(
            request,
            certificate: null,
            chain: null,
            SslPolicyErrors.None),
        "non-update hosts should use normal TLS validation without requiring Player Assistant update pins");
    AssertTrue(
        CertificatePinningUtility.ValidateServerCertificate(
            mapRequest,
            certificate: null,
            chain: null,
            SslPolicyErrors.None),
        "non-update paths on the update host should use normal TLS validation without requiring update pins");
}

static void VerifiedUpdaterRejectsInstallerSha256Mismatch()
{
    using var directory = TemporaryDirectory.Create();
    var update = new PlayerAssistantUpdateInfo(
        new Version(0, 9, 1),
        "0.9.1",
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.zip"),
        new string('A', 64),
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.exe"),
        new string('B', 64));
    var installerBytes = System.Text.Encoding.UTF8.GetBytes("mismatched installer bytes");

    using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(installerBytes)
        })));

    var exception = AssertThrows<InvalidOperationException>(() =>
        VerifiedInstallerUpdateUtility.DownloadVerifiedInstallerAsync(
            httpClient,
            update,
            new AuthenticodeSignaturePolicy("CN=KyrathaSoft", "ABC123"),
            _ => new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "ABC123"),
            directory.Path).GetAwaiter().GetResult());

    AssertContains(exception.Message, "SHA256");
}

static void VerifiedUpdaterRejectsInstallerSignerMismatch()
{
    using var directory = TemporaryDirectory.Create();
    var installerBytes = System.Text.Encoding.UTF8.GetBytes("signed installer bytes");
    var update = new PlayerAssistantUpdateInfo(
        new Version(0, 9, 1),
        "0.9.1",
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.zip"),
        new string('A', 64),
        new Uri("https://bryanmiller.us/scarlethorizons/p-assist-0.9.1.exe"),
        Convert.ToHexString(SHA256.HashData(installerBytes)));

    using var httpClient = NetworkRequestUtility.CreateHttpClient(new ScriptedHttpMessageHandler((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(installerBytes)
        })));

    var exception = AssertThrows<InvalidOperationException>(() =>
        VerifiedInstallerUpdateUtility.DownloadVerifiedInstallerAsync(
            httpClient,
            update,
            new AuthenticodeSignaturePolicy("CN=KyrathaSoft", "ABC123"),
            _ => new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "XYZ999"),
            directory.Path).GetAwaiter().GetResult());

    AssertContains(exception.Message, "thumbprint");
}

static void VerifiedInstallerLaunchReverifiesBeforeExecution()
{
    using var directory = TemporaryDirectory.Create();
    var installerPath = Path.Combine(directory.Path, "p-assist-0.9.1.exe");
    var installerBytes = System.Text.Encoding.UTF8.GetBytes("signed installer bytes");
    File.WriteAllBytes(installerPath, installerBytes);

    var signature = new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "ABC123");
    var policy = new AuthenticodeSignaturePolicy("CN=KyrathaSoft", "ABC123");
    var download = new VerifiedInstallerDownloadResult(
        installerPath,
        Convert.ToHexString(SHA256.HashData(installerBytes)),
        signature,
        ReusedExistingFile: false);

    var ticket = VerifiedInstallerLaunchUtility.CreateLaunchTicket(
        download,
        policy,
        _ => signature,
        () => InstallerLaunchElevationContext.StandardUser);
    var startInfo = VerifiedInstallerLaunchUtility.CreateStartInfo(
        ticket,
        _ => signature,
        () => InstallerLaunchElevationContext.StandardUser);

    AssertEqual(Path.GetFullPath(installerPath), startInfo.FileName, "unexpected verified installer launch path");
    AssertTrue(startInfo.UseShellExecute, "verified installer launch should use shell execute");
    AssertEqual("open", startInfo.Verb, "verified installer launch should preserve the shell open verb");
    AssertEqual(directory.Path, startInfo.WorkingDirectory, "verified installer launch should use the installer directory as the working directory");
}

static void VerifiedInstallerLaunchRejectsSignerChangesAfterVerification()
{
    using var directory = TemporaryDirectory.Create();
    var installerPath = Path.Combine(directory.Path, "p-assist-0.9.1.exe");
    var installerBytes = System.Text.Encoding.UTF8.GetBytes("signed installer bytes");
    File.WriteAllBytes(installerPath, installerBytes);

    var initialSignature = new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "ABC123");
    var changedSignature = new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "XYZ999");
    var policy = new AuthenticodeSignaturePolicy("CN=KyrathaSoft", null);
    var download = new VerifiedInstallerDownloadResult(
        installerPath,
        Convert.ToHexString(SHA256.HashData(installerBytes)),
        initialSignature,
        ReusedExistingFile: false);

    var ticket = VerifiedInstallerLaunchUtility.CreateLaunchTicket(
        download,
        policy,
        _ => initialSignature,
        () => InstallerLaunchElevationContext.StandardUser);

    var exception = AssertThrows<InvalidOperationException>(() =>
        VerifiedInstallerLaunchUtility.CreateStartInfo(
            ticket,
            _ => changedSignature,
            () => InstallerLaunchElevationContext.StandardUser));

    AssertContains(exception.Message, "signer details changed");
}

static void VerifiedInstallerLaunchRejectsElevationChangesAfterVerification()
{
    using var directory = TemporaryDirectory.Create();
    var installerPath = Path.Combine(directory.Path, "p-assist-0.9.1.exe");
    var installerBytes = System.Text.Encoding.UTF8.GetBytes("signed installer bytes");
    File.WriteAllBytes(installerPath, installerBytes);

    var signature = new AuthenticodeSignatureInfo("Valid", "CN=KyrathaSoft LLC", "ABC123");
    var policy = new AuthenticodeSignaturePolicy("CN=KyrathaSoft", "ABC123");
    var download = new VerifiedInstallerDownloadResult(
        installerPath,
        Convert.ToHexString(SHA256.HashData(installerBytes)),
        signature,
        ReusedExistingFile: false);

    var ticket = VerifiedInstallerLaunchUtility.CreateLaunchTicket(
        download,
        policy,
        _ => signature,
        () => InstallerLaunchElevationContext.StandardUser);

    var exception = AssertThrows<InvalidOperationException>(() =>
        VerifiedInstallerLaunchUtility.CreateStartInfo(
            ticket,
            _ => signature,
            () => InstallerLaunchElevationContext.ElevatedAdministrator));

    AssertContains(exception.Message, "elevation context changed");
}

static (string ManifestJson, string SignatureText, string PublicKeyPem) CreateSignedUpdateManifest(string manifestJson)
{
    using var rsa = RSA.Create(2048);
    var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);
    var signatureBytes = rsa.SignData(
        manifestBytes,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    return (manifestJson, Convert.ToBase64String(signatureBytes), rsa.ExportSubjectPublicKeyInfoPem());
}

static UpdateManifestSigningKeyTrustEntry CreateActiveSigningKey(string publicKeyPem)
{
    return new UpdateManifestSigningKeyTrustEntry("test-key", publicKeyPem);
}

static (string HostedSettingsJson, string PublicKeyPem) CreateSignedHostedSettingsArtifact(
    IReadOnlyDictionary<string, string> settings,
    string version = "1.0.0",
    string contentId = HostedSettingsTrustUtility.HostedSettingsContentId)
{
    using var rsa = RSA.Create(2048);
    return (
        HostedSettingsTrustUtility.CreateSignedHostedSettingsJson(settings, version, rsa, contentId),
        rsa.ExportSubjectPublicKeyInfoPem());
}

static HostedSettingsSigningKeyTrustEntry CreateActiveHostedSettingsSigningKey(string publicKeyPem)
{
    return new HostedSettingsSigningKeyTrustEntry("hosted-settings-test-key", publicKeyPem);
}

static void SearchEnterTriggersClickWhenEnabled()
{
    RunOnStaThread(() =>
    {
        WithTemporaryKeywordIndex(
            """
            {
              "index_metadata": {
                "total_words_indexed": 0
              },
              "words": {
                "entry": {
                  "total_occurrences": 1,
                  "matches": [
                    {
                      "url": "https://example.test/entry",
                      "count": 1,
                      "last_indexed": "2026-06-30T00:00:00.0000000+00:00"
                    }
                  ]
                }
              }
            }
            """,
            () =>
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                var txtSearch = GetControl<TextBox>(form, "txtSearch");
                var btnSearch = GetControl<Button>(form, "btnSearch");
                var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                txtSearch.Text = "entry";
                AssertTrue(btnSearch.Enabled, "expected search button to be enabled for a valid search term");

                InvokePrivateMethod(
                    form,
                    "TxtSearch_KeyDown",
                    txtSearch,
                    new KeyEventArgs(Keys.Enter));

                var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                AssertEqual(1, results.Length, "expected Enter to trigger the existing search click path");
                AssertContains(string.Join("\n", results), "https://example.test/entry");
            });
    });
}

static void KeywordSearchUppercasesEncryptedIndexResultsWithoutChangingLaunchUrl()
{
    RunOnStaThread(() =>
    {
        WithTemporaryKeywordIndex(
            """
            {
              "index_metadata": {
                "total_words_indexed": 0
              },
              "words": {
                "nimba": {
                  "total_occurrences": 2,
                  "matches": [
                    {
                      "url": "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                      "count": 1,
                      "last_indexed": "2026-07-07T00:00:00.0000000+00:00"
                    },
                    {
                      "url": "https://publish.obsidian.md/scarlethorizons/NPCs/Nuanda+Armstrong",
                      "count": 1,
                      "last_indexed": "2026-07-07T00:00:00.0000000+00:00"
                    }
                  ]
                }
              }
            }
            """,
            () => WithTemporaryEncryptedTextIndex(
                """
                [
                  {
                    "url": "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                    "encrypted_sections": 2,
                    "frontmatter_tags": ["npc", "spy"]
                  }
                ]
                """,
                () =>
                {
                    using var form = new Form1(suppressHeroImagesForThisRun: true);
                    var txtSearch = GetControl<TextBox>(form, "txtSearch");
                    var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                    txtSearch.Text = "nimba";
                    InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                    var results = lstSearchResults.Items.Cast<object>().ToArray();
                    AssertEqual(2, results.Length, "expected both keyword-index matches to be returned");
                    AssertEqual(
                        "HTTPS://PUBLISH.OBSIDIAN.MD/SCARLETHORIZONS/NPCS/NIMBA+ARMSTRONG",
                        results[0]?.ToString() ?? string.Empty,
                        "encrypted index result should display in uppercase");
                    AssertEqual(
                        "https://publish.obsidian.md/scarlethorizons/NPCs/Nuanda+Armstrong",
                        results[1]?.ToString() ?? string.Empty,
                        "non-encrypted index result should display normally");

                    var launchUrl = InvokePrivateMethod(form, "GetSearchResultLaunchUrl", results[0]);
                    AssertEqual(
                        "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                        launchUrl?.ToString() ?? string.Empty,
                        "uppercase display item should retain the original launch URL");
            }));
    });
}

static void KeywordSearchUppercasesOnlineObsidianFallbackResults()
{
    RunOnStaThread(() =>
    {
        WithTemporaryKeywordIndex(
            """
            {
              "index_metadata": {
                "total_words_indexed": 0
              },
              "words": {}
            }
            """,
            () =>
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                var txtSearch = GetControl<TextBox>(form, "txtSearch");
                var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

                SetPrivateField(
                    form,
                    "_showLocalIndexMissPrompt",
                    (Func<string[], DialogResult>)(_ => DialogResult.Yes));
                SetPrivateField(
                    form,
                    "_showOnlineSearchCompletedMessage",
                    (Action<string[], int>)((_, _) => { }));
                SetPrivateField(
                    form,
                    "_onlineSearchProvider",
                    (Func<string[], CancellationToken, Task<string[]>>)((_, _) => Task.FromResult(new[]
                    {
                        "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                        "https://rpol.net/display.cgi?gi=80170&ti=12&msgpage=&show=all"
                    })));

                txtSearch.Text = "not indexed locally";
                InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                var results = lstSearchResults.Items.Cast<object>().ToArray();
                AssertEqual(2, results.Length, "expected online fallback to populate both provider results");
                AssertEqual(
                    "HTTPS://PUBLISH.OBSIDIAN.MD/SCARLETHORIZONS/NPCS/NIMBA+ARMSTRONG",
                    results[0]?.ToString() ?? string.Empty,
                    "online Obsidian fallback result should display in uppercase");
                AssertEqual(
                    "https://rpol.net/display.cgi?gi=80170&ti=12&msgpage=&show=all",
                    results[1]?.ToString() ?? string.Empty,
                    "non-Obsidian online fallback result should display normally");

                var launchUrl = InvokePrivateMethod(form, "GetSearchResultLaunchUrl", results[0]);
                AssertEqual(
                    "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                    launchUrl?.ToString() ?? string.Empty,
                    "uppercase online Obsidian item should retain the original launch URL");
            });
    });
}

static void KeywordSearchBackfillsOnlineHitsIntoKeywordIndex()
{
    WithTemporaryKeywordIndex(
        """
        {
          "index_metadata": {
            "total_words_indexed": 0
          },
          "words": {}
        }
        """,
        () =>
        {
            Form1.BackfillKeywordIndexWithOnlineResultsAsync(
                ["Nimba Armstrong"],
                ["https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong"],
                CancellationToken.None).GetAwaiter().GetResult();

            using var document = JsonDocument.Parse(File.ReadAllText(GetPlayerAssistantIndexPath()));
            var words = document.RootElement.GetProperty("words");
            AssertTrue(words.TryGetProperty("Nimba Armstrong", out var nimbaEntry), "online hit should add the missing search term to keyword-index.json");
            AssertEqual(1, nimbaEntry.GetProperty("total_occurrences").GetInt32(), "backfilled keyword should record one occurrence");

            var match = nimbaEntry.GetProperty("matches").EnumerateArray().Single();
            AssertEqual(
                "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
                match.GetProperty("url").GetString() ?? string.Empty,
                "backfilled keyword should store the online hit URL");
            AssertEqual(1, match.GetProperty("count").GetInt32(), "backfilled match should use a count of one");
            AssertFalse(
                string.IsNullOrWhiteSpace(match.GetProperty("last_indexed").GetString()),
                "backfilled match should record a last-indexed timestamp");
        });
}

static void KeywordSearchOffersOnlineFallbackOnLocalMiss()
{
    RunOnStaThread(() =>
    {
        WithTemporaryKeywordIndex(
            """
            {
              "index_metadata": {
                "total_words_indexed": 0
              },
              "words": {}
            }
            """,
            () =>
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                var txtSearch = GetControl<TextBox>(form, "txtSearch");
                var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");
                var promptCallCount = 0;
                var onlineSearchCallCount = 0;
                var onlineSearchCompletedCallCount = 0;

                SetPrivateField(
                    form,
                    "_showLocalIndexMissPrompt",
                    (Func<string[], DialogResult>)(terms =>
                    {
                        promptCallCount++;
                        AssertEqual("not indexed locally", terms[0], "unexpected prompt term");
                        return DialogResult.Yes;
                    }));
                SetPrivateField(
                    form,
                    "_onlineSearchProvider",
                    (Func<string[], CancellationToken, Task<string[]>>)((terms, _) =>
                    {
                        onlineSearchCallCount++;
                        AssertEqual("not indexed locally", terms[0], "unexpected online search term");
                        return Task.FromResult(new[]
                        {
                            "https://example.test/online-result"
                        });
                    }));
                SetPrivateField(
                    form,
                    "_showOnlineSearchCompletedMessage",
                    (Action<string[], int>)((terms, resultCount) =>
                    {
                        onlineSearchCompletedCallCount++;
                        AssertEqual("not indexed locally", terms[0], "unexpected completed-message term");
                        AssertEqual(1, resultCount, "unexpected completed-message result count");
                    }));

                txtSearch.Text = "\"not indexed locally\"";
                InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

                var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                AssertEqual(1, promptCallCount, "expected the local-index miss prompt to be shown once");
                AssertEqual(1, onlineSearchCallCount, "expected online search to run once");
                AssertEqual(1, onlineSearchCompletedCallCount, "expected the online-search completion message to be shown once");
                AssertEqual(1, results.Length, "expected online fallback to populate one result");
                AssertContains(string.Join("\n", results), "https://example.test/online-result");
            });
    });
}

static void KeywordSearchCancelsPreviousOnlineFallback()
{
    RunOnStaThread(() =>
    {
        WithTemporaryKeywordIndex(
            """
            {
              "index_metadata": {
                "total_words_indexed": 0
              },
              "words": {}
            }
            """,
            () =>
            {
                using var form = new Form1(suppressHeroImagesForThisRun: true);
                var txtSearch = GetControl<TextBox>(form, "txtSearch");
                var btnSearch = GetControl<Button>(form, "btnSearch");
                var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");
                var firstSearchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var secondSearchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationToken firstSearchToken = default;
                var onlineSearchCallCount = 0;

                SetPrivateField(
                    form,
                    "_showLocalIndexMissPrompt",
                    (Func<string[], DialogResult>)(_ => DialogResult.Yes));
                SetPrivateField(
                    form,
                    "_showOnlineSearchCompletedMessage",
                    (Action<string[], int>)((_, _) => { }));
                SetPrivateField(
                    form,
                    "_onlineSearchProvider",
                    (Func<string[], CancellationToken, Task<string[]>>)(async (terms, cancellationToken) =>
                    {
                        onlineSearchCallCount++;
                        if (onlineSearchCallCount == 1)
                        {
                            firstSearchToken = cancellationToken;
                            firstSearchStarted.SetResult();
                            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                        }

                        secondSearchStarted.SetResult();
                        cancellationToken.ThrowIfCancellationRequested();
                        return ["https://example.test/current-search"];
                    }));

                txtSearch.Text = "\"first missing\"";
                _ = InvokePrivateAsync(form, "PerformSearchAsync");
                AssertTrue(firstSearchStarted.Task.Wait(TimeSpan.FromSeconds(2)), "first search did not reach online fallback");

                txtSearch.Text = "\"second missing\"";
                var secondSearch = InvokePrivateAsync(form, "PerformSearchAsync");
                secondSearch.GetAwaiter().GetResult();

                var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
                AssertTrue(firstSearchToken.IsCancellationRequested, "starting a second search should cancel the first search token");
                AssertTrue(secondSearchStarted.Task.IsCompleted, "second search did not reach online fallback");
                AssertEqual(2, onlineSearchCallCount, "expected both online search attempts to start");
                AssertTrue(btnSearch.Enabled, "search button should be re-enabled after current search completes");
                AssertEqual(1, results.Length, "only current search results should remain");
                AssertContains(string.Join("\n", results), "https://example.test/current-search");
            });
    });
}

static void KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheart()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        var txtSearch = GetControl<TextBox>(form, "txtSearch");
        var rdoRPOL = GetControl<RadioButton>(form, "rdoRPOL");
        var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

        rdoRPOL.Checked = true;
        txtSearch.Text = "whiteheart";
        InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

        var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
        AssertEqual(0, results.Length, "expected RPOL-only search to exclude the Obsidian-only whiteheart entry");
    });
}

static void KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheartStiffwhiskers()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        var txtSearch = GetControl<TextBox>(form, "txtSearch");
        var rdoRPOL = GetControl<RadioButton>(form, "rdoRPOL");
        var lstSearchResults = GetControl<ListBox>(form, "lstSearchResults");

        rdoRPOL.Checked = true;
        txtSearch.Text = "whiteheart stiffwhiskers";
        InvokePrivateAsync(form, "PerformSearchAsync").GetAwaiter().GetResult();

        var results = lstSearchResults.Items.Cast<object>().Select(item => item?.ToString()).ToArray();
        AssertEqual(0, results.Length, "expected RPOL-only search to exclude the Obsidian-only whiteheart stiffwhiskers entry");
    });
}

static void KeywordSearchExpandsHeroFirstAndFullNames()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        SetPrivateField(
            form,
            "_playerCharacterListingMarkdown",
            """
            | Name | Character | Notes | Hero |
            | ---- | --------- | ----- | ---- |
            | [[Kelpie Lawfuller]] | Fighter | active | ![[kelpie-token.webp]] |
            | [[Jelb Garrick]] | Illusionist | active | ![[jelb-token.webp]] |
            """);

        var kelpieAliases = ((string[]?)InvokePrivateMethod(form, "GetHeroSearchTermAliases", "Kelpie")
            ?? []).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var jelbAliases = ((string[]?)InvokePrivateMethod(form, "GetHeroSearchTermAliases", "Jelb Garrick")
            ?? []).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

        AssertEqual(2, kelpieAliases.Length, "Kelpie first-name search should produce first and full-name aliases");
        AssertEqual("Kelpie", kelpieAliases[0], "unexpected Kelpie first-name alias");
        AssertEqual("Kelpie Lawfuller", kelpieAliases[1], "unexpected Kelpie full-name alias");
        AssertEqual(2, jelbAliases.Length, "Jelb full-name search should produce first and full-name aliases");
        AssertEqual("Jelb", jelbAliases[0], "unexpected Jelb first-name alias");
        AssertEqual("Jelb Garrick", jelbAliases[1], "unexpected Jelb full-name alias");
    });
}

static void PartyHeroSheetParserReadsSummaryAndHidesXpLines()
{
    var hero = PartyHeroUtility.ParseHeroSheet(
        """
        ---
        dg-publish: true
        ---
        ![[jelb-token.webp]]

        Class: Illusionist
        HP: 4
        Level: 1
        XP: 0
        Intelligence 16 Language Native+2 Literacy Literate XP Bonus: 10%
        Attained level 03 Illusionist after XP was awarded.

        Name: Jelb Garrick
        """,
        "Jelb");

    AssertEqual("Jelb Garrick", hero.Name, "unexpected parsed hero name");
    AssertEqual("Illusionist", hero.CharacterClass, "unexpected parsed class");
    AssertEqual("3", hero.Level, "unexpected parsed level");
    AssertEqual("4", hero.HitPoints, "unexpected parsed hit points");
    AssertFalse(hero.CharacterSheetText.Contains("XP: 0", StringComparison.Ordinal), "XP total lines should be hidden from party sheet text");
    AssertContains(hero.CharacterSheetText, "XP Bonus: 10%");
}

static void MyHeroBriefingBuildsSelectedHeroSummaryBoundary()
{
    var heroes = new PartyHeroSheet[]
    {
        new("Kelpie Lawfuller", "kelpie-token.webp", "3", "Fighter", "12", "Kelpie sheet"),
        new("Jelb Garrick", "jelb-token.webp", "3", "Illusionist", "8", "Jelb sheet")
    };
    var request = new MyHeroBriefingRequest(
        heroes,
        SelectedHeroName: "Jelb Garrick",
        AuthenticatedHeroName: "Jelb",
        XpTotals: [new PcXpTotal("Jelb", 8575)],
        ThreadPosts:
        [
            new MyHeroBriefingThreadPosts(
                "Chapter 1",
                "https://rpol.net/display.cgi?gi=80170&ti=7",
                [])
        ],
        EncryptedTextIndex:
        [
            new EncryptedTextIndexEntry(
                "https://publish.obsidian.md/scarlethorizons/Secrets",
                1,
                ["illusionist"])
        ],
        QuickLinks:
        [
            new MyHeroBriefingQuickLink("Party", "app://show/party")
        ]);

    var briefing = MyHeroBriefingUtility.Build(request);

    AssertFalse(briefing.NeedsHeroSelection, "selected hero should not require a picker");
    AssertTrue(briefing.Hero is not null, "selected hero should build a hero summary");
    AssertEqual("Jelb Garrick", briefing.Hero!.Name, "unexpected briefing hero");
    AssertEqual("Illusionist", briefing.Hero.CharacterClass, "unexpected briefing class");
    AssertEqual("3", briefing.Hero.Level, "unexpected briefing level");
    AssertEqual("8", briefing.Hero.HitPoints, "unexpected briefing hit points");
    AssertEqual(8575, briefing.Hero.XpTotal ?? -1, "XP should match first-name alias");
    AssertEqual("jelb-token.webp", briefing.Hero.TokenImagePath ?? string.Empty, "unexpected token path");
    AssertEqual("Jelb Garrick", briefing.Hero.AccessContext.CharacterName ?? string.Empty, "unexpected access context character");
    AssertTrue(briefing.HeroCard is not null, "selected hero should build a current hero card");
    AssertEqual("Jelb Garrick", briefing.HeroCard!.Name, "unexpected card hero");
    AssertEqual("Illusionist", briefing.HeroCard.CharacterClass, "unexpected card class");
    AssertEqual("3", briefing.HeroCard.Level, "unexpected card level");
    AssertEqual("8", briefing.HeroCard.HitPoints, "unexpected card hit points");
    AssertEqual("XP Total: 8,575", briefing.HeroCard.XpTotalLabel, "unexpected card XP label");
    AssertEqual("jelb-token.webp", briefing.HeroCard.TokenImagePath ?? string.Empty, "unexpected card token path");
    AssertEqual("Jelb sheet", briefing.HeroCard.CharacterSheetText, "unexpected card sheet text");
    AssertEqual(2, briefing.HeroChoices.Count, "unexpected hero choice count");
    AssertEqual(MyHeroBriefingHeroIdentitySource.AuthenticatedHero, briefing.HeroIdentitySource, "authenticated identity should win before selected hero");
    AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Full Sheet" && link.Target == "app://show/party"), "briefing should include a full-sheet quick link");
    AssertTrue(briefing.QuickLinks.Any(link => link.Label == "XP" && link.Target == "app://show/xp"), "briefing should include an XP quick link");
    AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Party" && link.Target == "app://show/party"), "briefing should include a Party quick link");
    AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Adventure Outline" && link.Target == "app://show/adventure-outline"), "briefing should include an Adventure Outline quick link");
    AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Chapter 1" && link.Target == "https://rpol.net/display.cgi?gi=80170&ti=7"), "briefing should include RPOL thread quick links");
    AssertTrue(briefing.QuickLinks.Any(link => link.Label == "Party" && link.Target == "app://show/party"), "provided quick links should be retained");
    AssertEqual(briefing.QuickLinks.Count, briefing.HeroCard.QuickLinks.Count, "card quick links should mirror briefing quick links");
    AssertEqual(0, briefing.RecentActivity.Count, "activity should be left for the later backlog step");
    AssertEqual(0, briefing.LikelyResponseItems.Count, "response items should be left for the later backlog step");
    AssertEqual(1, briefing.UnlockedNotes.Count, "encrypted index input should surface unlocked notes");
    AssertEqual("Secrets", briefing.UnlockedNotes[0].Title, "unexpected unlocked note title");
}

static void MyHeroBriefingPrefersAuthenticatedHeroIdentity()
{
    var heroes = new PartyHeroSheet[]
    {
        new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet"),
        new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
    };

    var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
        heroes,
        SelectedHeroName: "Kelpie Lawfuller",
        AuthenticatedHeroName: "Jelb"));

    AssertTrue(briefing.Hero is not null, "authenticated hero should resolve a briefing hero");
    AssertEqual("Jelb Garrick", briefing.Hero!.Name, "authenticated first-name identity should select Jelb");
    AssertEqual(MyHeroBriefingHeroIdentitySource.AuthenticatedHero, briefing.HeroIdentitySource, "unexpected identity source");
    AssertFalse(briefing.NeedsHeroSelection, "resolved authenticated hero should not need a picker");
}

static void MyHeroBriefingRequiresExplicitDungeonMasterHeroSelection()
{
    var heroes = new PartyHeroSheet[]
    {
        new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet"),
        new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
    };
    var unresolved = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
        heroes,
        AuthenticatedHeroName: "Dungeon Master",
        IsDungeonMaster: true));
    var selected = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
        heroes,
        SelectedHeroName: "Kelpie",
        AuthenticatedHeroName: "Dungeon Master",
        IsDungeonMaster: true));

    AssertTrue(unresolved.Hero is null, "DM briefing should not infer a hero from Dungeon Master identity");
    AssertTrue(unresolved.NeedsHeroSelection, "DM briefing should request explicit hero selection");
    AssertEqual(MyHeroBriefingHeroIdentitySource.None, unresolved.HeroIdentitySource, "unexpected unresolved DM identity source");
    AssertEqual("Choose a hero to build My Hero Briefing for Dungeon Master view.", unresolved.StatusMessage, "unexpected DM picker status");
    AssertTrue(selected.Hero is not null, "explicit DM selection should resolve a hero");
    AssertEqual("Kelpie Lawfuller", selected.Hero!.Name, "unexpected selected DM hero");
    AssertEqual(MyHeroBriefingHeroIdentitySource.SelectedHero, selected.HeroIdentitySource, "unexpected selected DM identity source");
}

static void MyHeroBriefingLeavesAmbiguousFirstNameUnresolved()
{
    var heroes = new PartyHeroSheet[]
    {
        new("Max North", null, "1", "Fighter", "5", "Max North sheet"),
        new("Max Stone", null, "2", "Thief", "7", "Max Stone sheet")
    };

    var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
        heroes,
        AuthenticatedHeroName: "Max"));

    AssertTrue(briefing.Hero is null, "ambiguous first-name identity should remain unresolved");
    AssertTrue(briefing.NeedsHeroSelection, "ambiguous identity should request explicit selection");
    AssertEqual(MyHeroBriefingHeroIdentitySource.None, briefing.HeroIdentitySource, "unexpected ambiguous identity source");
}

static void MyHeroBriefingHidesXpForUnauthenticatedSelectedHeroCard()
{
    var heroes = new PartyHeroSheet[]
    {
        new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet"),
        new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
    };

    var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
        heroes,
        SelectedHeroName: "Kelpie Lawfuller",
        XpTotals: [new PcXpTotal("Kelpie Lawfuller", 7062)]));

    AssertTrue(briefing.HeroCard is not null, "selected hero should build a current hero card");
    AssertTrue(briefing.HeroCard!.XpTotal is null, "unauthenticated selected hero should not receive raw XP totals");
    AssertEqual("XP Total: hidden", briefing.HeroCard.XpTotalLabel, "unexpected hidden XP label");
    AssertEqual(MyHeroBriefingHeroIdentitySource.SelectedHero, briefing.HeroIdentitySource, "unexpected selected identity source");
}

static void MyHeroBriefingBuildsRecentHeroActivity()
{
    var heroes = new PartyHeroSheet[]
    {
        new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
    };
    var matchingPosts = Enumerable.Range(1, 12)
        .Select(index => new RpolThreadPost(
            index,
            index % 2 == 0 ? "Dungeon Master" : "Kelpie",
            string.Empty,
            "Mon 1 Jan 2026",
            $"{index:00}:00",
            $"{index:000}.html",
            "<div></div>",
            "<p></p>",
            index == 12
                ? "Jelb Garrick considers the long corridor. " + new string('x', 220)
                : $"Jelb studies clue {index}."))
        .Concat(
        [
            new RpolThreadPost(
                13,
                "Dungeon Master",
                string.Empty,
                "Mon 1 Jan 2026",
                "13:00",
                "013.html",
                "<div></div>",
                "<p></p>",
                "A jelbian carving is unrelated."),
            new RpolThreadPost(
                14,
                "Dungeon Master",
                string.Empty,
                "Mon 1 Jan 2026",
                "14:00",
                "014.html",
                "<div></div>",
                "<p></p>",
                "Kelpie studies the same clue."),
            new RpolThreadPost(
                15,
                "Jelb",
                string.Empty,
                "Mon 1 Jan 2026",
                "15:00",
                "015.html",
                "<div></div>",
                "<p></p>",
                "I check the stonework for hidden catches.")
        ])
        .ToArray();
    var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
        heroes,
        AuthenticatedHeroName: "Jelb",
        ThreadPosts:
        [
            new MyHeroBriefingThreadPosts(
                "Chapter 1",
                "https://rpol.net/display.cgi?gi=80170&ti=7",
                matchingPosts)
        ]));

    AssertEqual(10, briefing.RecentActivity.Count, "recent activity should be capped at ten matching posts");
    AssertEqual(15, briefing.RecentActivity[0].MessageNumber, "latest hero-authored post should appear first");
    AssertEqual(4, briefing.RecentActivity[^1].MessageNumber, "oldest retained matching post should be message 4");
    AssertTrue(
        briefing.RecentActivity.All(item => item.ThreadTitle == "Chapter 1"
            && item.ThreadUrl == "https://rpol.net/display.cgi?gi=80170&ti=7"),
        "activity items should retain thread context");
    AssertTrue(
        briefing.RecentActivity.All(item => item.MessageNumber != 13 && item.MessageNumber != 14),
        "activity should exclude substring matches and unrelated hero posts");
    AssertTrue(briefing.RecentActivity.Any(item => item.MessageNumber == 15), "hero-authored posts should count as recent activity");
    AssertTrue(briefing.RecentActivity[1].Excerpt.EndsWith("...", StringComparison.Ordinal), "long excerpts should be shortened");
    AssertTrue(briefing.RecentActivity[1].Excerpt.Length <= 183, "shortened excerpts should stay bounded");
}

static void MyHeroBriefingBuildsLikelyOpenResponseItems()
{
    var heroes = new PartyHeroSheet[]
    {
        new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
    };
    var chapterPosts = new RpolThreadPost[]
    {
        CreateRpolThreadPost(1, "Dungeon Master", "Before Jelb posts."),
        CreateRpolThreadPost(2, "Jelb", "Jelb watches the door."),
        CreateRpolThreadPost(3, "Kelpie", "Should we open it?"),
        CreateRpolThreadPost(4, "Dungeon Master", "Jelb hears a faint click."),
        CreateRpolThreadPost(5, "Nuanda", "The corridor stays quiet."),
        CreateRpolThreadPost(6, "Jelb", "Jelb studies the lock."),
        CreateRpolThreadPost(7, "Dungeon Master", "The lock gives way."),
        CreateRpolThreadPost(8, "Kelpie", "Jelb, do you want the lantern?")
    };
    var noHeroPostThread = new RpolThreadPost[]
    {
        CreateRpolThreadPost(1, "Kelpie", "Jelb might know this."),
        CreateRpolThreadPost(2, "Dungeon Master", "What happens next?")
    };

    var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
        heroes,
        AuthenticatedHeroName: "Jelb",
        ThreadPosts:
        [
            new MyHeroBriefingThreadPosts(
                "Chapter 1",
                "https://rpol.net/display.cgi?gi=80170&ti=7",
                chapterPosts),
            new MyHeroBriefingThreadPosts(
                "Chapter 2",
                "https://rpol.net/display.cgi?gi=80170&ti=8",
                noHeroPostThread)
        ]));

    AssertEqual(2, briefing.LikelyResponseItems.Count, "only posts after the hero's latest post should be response candidates");
    AssertEqual(8, briefing.LikelyResponseItems[0].MessageNumber, "direct mention should rank first");
    AssertEqual("Direct mention after your last post", briefing.LikelyResponseItems[0].Reason, "unexpected direct-mention reason");
    AssertEqual(7, briefing.LikelyResponseItems[1].MessageNumber, "neutral follow-up should remain after direct mentions and questions");
    AssertEqual("Recent post after your last post", briefing.LikelyResponseItems[1].Reason, "weak evidence should stay neutral");
    AssertTrue(
        briefing.LikelyResponseItems.All(item => item.ThreadTitle == "Chapter 1"
            && item.ThreadUrl == "https://rpol.net/display.cgi?gi=80170&ti=7"),
        "response items should be grouped by retaining thread context and ignore threads without a hero post");
}

static RpolThreadPost CreateRpolThreadPost(int messageNumber, string author, string bodyText)
{
    return new RpolThreadPost(
        messageNumber,
        author,
        string.Empty,
        "Mon 1 Jan 2026",
        $"{messageNumber:00}:00",
        $"{messageNumber:000}.html",
        "<div></div>",
        "<p></p>",
        bodyText);
}

static void MyHeroBriefingSurfacesRelevantUnlockedNotes()
{
    var heroes = new PartyHeroSheet[]
    {
        new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
    };
    var encryptedIndex = new EncryptedTextIndexEntry[]
    {
        new(
            "https://publish.obsidian.md/scarlethorizons/Secrets/Illusionist+Clue",
            2,
            ["Class Illusionist"]),
        new(
            "https://publish.obsidian.md/scarlethorizons/Secrets/Jelb+Only",
            1,
            ["Hero Jelb"]),
        new(
            "https://publish.obsidian.md/scarlethorizons/Secrets/High+Level",
            1,
            ["Level 4"]),
        new(
            "https://publish.obsidian.md/scarlethorizons/Secrets/Fighter+Only",
            1,
            ["Class Fighter"]),
        new(
            "https://publish.obsidian.md/scarlethorizons/Secrets/Public",
            0,
            ["Class Illusionist"])
    };

    var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
        heroes,
        AuthenticatedHeroName: "Jelb",
        EncryptedTextIndex: encryptedIndex));

    AssertEqual(2, briefing.UnlockedNotes.Count, "only notes unlocked by hero tags should be surfaced");
    AssertTrue(
        briefing.UnlockedNotes.Any(note =>
            note.Title == "Illusionist Clue"
            && note.Url == "https://publish.obsidian.md/scarlethorizons/Secrets/Illusionist+Clue"
            && note.Excerpt == "2 unlocked encrypted sections may be relevant."),
        "class-matched encrypted note should be included");
    AssertTrue(
        briefing.UnlockedNotes.Any(note =>
            note.Title == "Jelb Only"
            && note.Excerpt == "1 unlocked encrypted section may be relevant."),
        "hero-name matched encrypted note should be included");
    AssertFalse(
        briefing.UnlockedNotes.Any(note => note.Title is "High Level" or "Fighter Only" or "Public"),
        "locked notes and entries without encrypted sections should remain hidden");
}

static void MyHeroBriefingRequestsHeroSelectionWhenNoHeroSelected()
{
    var heroes = new PartyHeroSheet[]
    {
        new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet"),
        new("Jelb Garrick", null, "3", "Illusionist", "8", "Jelb sheet")
    };

    var briefing = MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(heroes));

    AssertTrue(briefing.Hero is null, "briefing should not choose a hero before identity resolution exists");
    AssertTrue(briefing.NeedsHeroSelection, "briefing should request a hero selection");
    AssertEqual(2, briefing.HeroChoices.Count, "unexpected hero choice count");
    AssertEqual(MyHeroBriefingHeroIdentitySource.None, briefing.HeroIdentitySource, "unexpected unresolved identity source");
    AssertEqual("Choose a hero to build My Hero Briefing.", briefing.StatusMessage, "unexpected picker status");
}

static void MyHeroBriefingDisplayTextIncludesFocusedSections()
{
    var briefing = CreateMyHeroBriefingDisplayFixture();
    var text = (string)(InvokeStaticMethod(typeof(Form1), "FormatMyHeroBriefingForDisplay", briefing)
        ?? throw new InvalidOperationException("briefing display text was null."));

    AssertContains(text, "My Hero Briefing");
    AssertContains(text, "Current Hero");
    AssertContains(text, "Jelb Garrick");
    AssertContains(text, "Class: Illusionist");
    AssertContains(text, "Level: 3");
    AssertContains(text, "HP: 8");
    AssertContains(text, "XP: 1,234 XP");
    AssertContains(text, "Likely Open Response Items");
    AssertContains(text, "*First, the app finds the hero's latest authored post in each thread.*");
    AssertContains(text, "*Then it looks at later posts in that same thread by other authors.*");
    AssertContains(text, "*Those later posts are ranked as:*");
    AssertContains(text, "*- Direct mention after your last post when the post mentions the hero by name or first name.*");
    AssertContains(text, "*- Question-like post after your last post when the post contains a ?.*");
    AssertContains(text, "*- Recent post after your last post when it is simply a later post in that thread.*");
    AssertContains(text, "Direct mention after your last post");
    AssertContains(text, "Recent Hero Activity");
    AssertContains(text, "Relevant Unlocked Notes");
    AssertContains(text, "Jelb Only");
    AssertContains(text, "Quick Links");
}

static void MyHeroBriefingStylesLikelyResponseKey()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        InvokePrivateMethod(form, "ShowMyHeroBriefing", CreateMyHeroBriefingDisplayFixture());
        var textBox = (RichTextBox)(GetPrivateField(form, "_myHeroBriefingTextBox")
            ?? throw new InvalidOperationException("my hero briefing text box was null."));
        const string keyLine = "*First, the app finds the hero's latest authored post in each thread.*";
        var start = textBox.Text.IndexOf(keyLine, StringComparison.Ordinal);

        AssertTrue(start >= 0, "expected likely response key line to be present");
        textBox.Select(start, 1);
        AssertEqual(Color.FromArgb(246, 241, 222), textBox.SelectionBackColor, "unexpected likely response key background color");
    });
}

static MyHeroBriefing CreateMyHeroBriefingDisplayFixture()
{
    var heroes = new PartyHeroSheet[]
    {
        new("Jelb Garrick", "jelb-token.webp", "3", "Illusionist", "8", "Jelb sheet")
    };
    var posts = new[]
    {
        new MyHeroBriefingThreadPosts(
            "Chapter 2",
            "https://rpol.net/display.cgi?gi=80170&ti=8",
            [
                CreateRpolThreadPost(1, "Jelb", "Jelb checks the suspicious door."),
                CreateRpolThreadPost(2, "Dungeon Master", "Jelb hears a lock click. What do you do?")
            ])
    };
    var encryptedIndex = new[]
    {
        new EncryptedTextIndexEntry(
            "https://publish.obsidian.md/scarlethorizons/Secrets/Jelb+Only",
            1,
            ["Hero Jelb"])
    };
    return MyHeroBriefingUtility.Build(new MyHeroBriefingRequest(
        heroes,
        AuthenticatedHeroName: "Jelb",
        ThreadPosts: posts,
        XpTotals: [new PcXpTotal("Jelb Garrick", 1234)],
        EncryptedTextIndex: encryptedIndex));
}

static void MyHeroBriefingLoadsCachedThreadPostsFromRuntimeArtifacts()
{
    using var directory = TemporaryDirectory.Create();
    var threadDirectory = Path.Combine(directory.Path, "ch-2");
    Directory.CreateDirectory(threadDirectory);
    File.WriteAllText(
        Path.Combine(threadDirectory, "_source-show-all.html"),
        CreateRpolSourceHtml(
            (1, "Jelb", "Mon 1 Jan 2026", "01:00", "Jelb checks the door."),
            (2, "Dungeon Master", "Mon 1 Jan 2026", "01:05", "The lock clicks.")));
    var manifest = new RpolThreadSplitResult(
        "Chapter 2 - Supper With Nuanda",
        "https://rpol.net/display.cgi?gi=80170&ti=8&show=all",
        threadDirectory,
        2,
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Dungeon Master"] = 1,
            ["Jelb"] = 1
        },
        []);
    File.WriteAllText(Path.Combine(threadDirectory, "manifest.json"), JsonSerializer.Serialize(manifest));

    Form1.MyHeroBriefingPostsDirectoryOverride = directory.Path;
    try
    {
        var threadPosts = (IReadOnlyList<MyHeroBriefingThreadPosts>)(InvokeStaticMethod(typeof(Form1), "LoadMyHeroBriefingThreadPosts")
            ?? throw new InvalidOperationException("thread posts were null."));

        AssertEqual(1, threadPosts.Count, "expected one cached thread to load");
        AssertEqual("Chapter 2 - Supper With Nuanda", threadPosts[0].ThreadTitle, "unexpected thread title");
        AssertEqual("https://rpol.net/display.cgi?gi=80170&ti=8&show=all", threadPosts[0].ThreadUrl, "unexpected thread URL");
        AssertEqual(2, threadPosts[0].Posts.Count, "unexpected cached post count");
        AssertEqual("Jelb", threadPosts[0].Posts[0].Author, "unexpected first cached post author");
        AssertEqual("The lock clicks.", threadPosts[0].Posts[1].BodyText, "unexpected second cached post body");
    }
    finally
    {
        Form1.MyHeroBriefingPostsDirectoryOverride = null;
    }
}

static void MyHeroBriefingLoadsFlatCachedThreadFilesFromRuntimeArtifacts()
{
    using var directory = TemporaryDirectory.Create();
    var asideDirectory = Path.Combine(directory.Path, "Aside");
    Directory.CreateDirectory(asideDirectory);
    File.WriteAllText(
        Path.Combine(directory.Path, "ch-5.html"),
        CreateRpolSourceHtml(
            (1, "Jelb", "Mon 1 Jan 2026", "01:00", "I watch the passage."),
            (2, "Dungeon Master", "Mon 1 Jan 2026", "01:05", "Jelb hears footsteps.")));
    File.WriteAllText(
        Path.Combine(directory.Path, "ch-5.bak-20260713-191558-767.html"),
        CreateRpolSourceHtml((99, "Dungeon Master", "Mon 1 Jan 2026", "01:10", "Stale backup content.")));
    File.WriteAllText(
        Path.Combine(asideDirectory, "Aside - Searching the woods.html"),
        CreateRpolSourceHtml((3, "Kelpie", "Mon 1 Jan 2026", "01:15", "Kelpie searches the woods.")));

    Form1.MyHeroBriefingPostsDirectoryOverride = directory.Path;
    try
    {
        var threadPosts = (IReadOnlyList<MyHeroBriefingThreadPosts>)(InvokeStaticMethod(typeof(Form1), "LoadMyHeroBriefingThreadPosts")
            ?? throw new InvalidOperationException("thread posts were null."));

        AssertEqual(2, threadPosts.Count, "expected current flat chapter and aside files to load");
        AssertTrue(threadPosts.Any(thread => thread.ThreadTitle == "ch-5" && thread.Posts.Count == 2), "current chapter file should load");
        AssertTrue(threadPosts.Any(thread => thread.ThreadTitle == "Aside - Searching the woods" && thread.Posts.Count == 1), "aside file should load");
        AssertFalse(threadPosts.Any(thread => thread.Posts.Any(post => post.MessageNumber == 99)), "backup files should be ignored");
    }
    finally
    {
        Form1.MyHeroBriefingPostsDirectoryOverride = null;
    }
}

static void MyHeroBriefingEncryptedIndexLoaderToleratesMalformedJson()
{
    WithTemporaryEncryptedTextIndex(
        "{ not-json",
        () =>
        {
            var entries = (IReadOnlyList<EncryptedTextIndexEntry>)(InvokeStaticMethod(typeof(Form1), "LoadMyHeroBriefingEncryptedTextIndex")
                ?? throw new InvalidOperationException("encrypted index entries were null."));

            AssertEqual(0, entries.Count, "malformed encrypted index should be ignored");
        });
}

static string CreateRpolSourceHtml(params (int MessageNumber, string Author, string Date, string Time, string BodyText)[] posts)
{
    return string.Join(
        Environment.NewLine,
        posts.Select(post => $"""
        <div class='message'>
        <span class='messageauthor'>{WebUtility.HtmlEncode(post.Author)}</span>
        <ul><li>msg #{post.MessageNumber}</li></ul>
        {post.Date} at {post.Time}
        <div class='messagebody' id='msg{post.MessageNumber}'>{WebUtility.HtmlEncode(post.BodyText)}</div>
        </div><!-- 1 -->
        """));
}

static void PartyHeroListingSummaryOverridesStaleCachedSheet()
{
    using var directory = TemporaryDirectory.Create();
    var activeDirectory = Path.Combine(directory.Path, "active");
    Directory.CreateDirectory(activeDirectory);
    File.WriteAllText(
        PlayerCharacterAssetUtility.GetPlayerCharactersListingMarkdownCachePath(directory.Path),
        """
        | Name | Class | Level | Token | HP | Race | AC |
        | ---- | ----- | ----- | ----- | -- | ---- | -- |
        | [[Jelb Garrick, Illusionist\|Jelb]] | Illusionist | 3 | ![[jelb-token.webp\|70]] | 8 | Human | 7[12] |
        """);
    File.WriteAllText(
        Path.Combine(activeDirectory, "jelb.md"),
        """
        Class: Illusionist
        HP: 4
        Level: 1

        Name: Jelb Garrick
        """);

    var heroes = PartyHeroUtility.LoadActiveParty(directory.Path);

    AssertEqual(1, heroes.Count, "unexpected active party count");
    AssertEqual("Jelb Garrick", heroes[0].Name, "sheet name should remain the displayed party name");
    AssertEqual("Illusionist", heroes[0].CharacterClass, "listing class should be used");
    AssertEqual("3", heroes[0].Level, "listing level should override stale sheet level");
    AssertEqual("8", heroes[0].HitPoints, "listing HP should override stale sheet HP");
    AssertContains(heroes[0].CharacterSheetText, "HP: 4");
}

static void PartyHeroXpVisibilityFollowsAuthenticatedCharacter()
{
    var heroes = new PartyHeroSheet[]
    {
        new("Kelpie Lawfuller", null, "3", "Fighter", "12", "Kelpie sheet"),
        new("Jelb Garrick", null, "1", "Illusionist", "4", "Jelb sheet")
    };
    var xpTotals = new PcXpTotal[]
    {
        new("Kelpie Lawfuller", 7062),
        new("Jelb Garrick", 8575)
    };

    var kelpieView = PartyHeroUtility.WithVisibleXpTotals(
        heroes,
        xpTotals,
        "Kelpie",
        isDungeonMaster: false);
    var dmView = PartyHeroUtility.WithVisibleXpTotals(
        heroes,
        xpTotals,
        "Dungeon Master",
        isDungeonMaster: true);

    AssertEqual(7062, kelpieView[0].XpTotal ?? -1, "authenticated hero should see their own XP");
    AssertTrue(kelpieView[1].XpTotal is null, "authenticated hero should not see another hero's XP");
    AssertEqual(7062, dmView[0].XpTotal ?? -1, "DM should see Kelpie XP");
    AssertEqual(8575, dmView[1].XpTotal ?? -1, "DM should see Jelb XP");
}

static void TaggedNoteCipherDecryptsForMatchingLevelTag()
{
    var hero = new HeroAccessContext(
        Level: 8,
        CharacterClass: "Paladin",
        AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Wis"] = 12
        });

    var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
        "{Level 8}The shrine door opens at moonrise.{Level 8}",
        TaggedNoteCipherMode.Encrypt);
    var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
        encrypted,
        TaggedNoteCipherMode.Decrypt,
        hero: hero);

    AssertEqual("{Level 8}The shrine door opens at moonrise.{Level 8}", decrypted, "matching level tag should decrypt note text");
    AssertTrue(encrypted.StartsWith("{Level 8}", StringComparison.Ordinal), "encrypted note should preserve opening tags as plaintext");
    AssertTrue(encrypted.EndsWith("{Level 8}", StringComparison.Ordinal), "encrypted note should preserve closing tags as plaintext");
    AssertFalse(encrypted.Contains("The shrine door opens", StringComparison.Ordinal), "encrypted note should hide wrapped plaintext");
}

static void TaggedNoteCipherDecryptsForMatchingCharacterTag()
{
    var jelbHero = HeroAccessContext.FromPartyHeroSheet(new PartyHeroSheet(
        Name: "Jelb Stonehand",
        TokenImagePath: null,
        Level: "3",
        CharacterClass: "Fighter",
        HitPoints: "20",
        CharacterSheetText: "Name: Jelb Stonehand"));
    var otherHero = HeroAccessContext.FromPartyHeroSheet(new PartyHeroSheet(
        Name: "Kelpie Lawfuller",
        TokenImagePath: null,
        Level: "8",
        CharacterClass: "Paladin",
        HitPoints: "42",
        CharacterSheetText: "Name: Kelpie Lawfuller"));
    var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
        "{Character Jelb}sample text{Character Jelb}",
        TaggedNoteCipherMode.Encrypt);

    var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
        encrypted,
        TaggedNoteCipherMode.Decrypt,
        hero: jelbHero);

    AssertEqual("{Character Jelb}sample text{Character Jelb}", decrypted, "matching character tag should decrypt note text");
    AssertThrows<UnauthorizedAccessException>(
        () => TaggedNoteCipherUtility.TransformTaggedText(encrypted, TaggedNoteCipherMode.Decrypt, hero: otherHero));
}

static void TaggedNoteCipherRejectsUnmetClassTag()
{
    var hero = new HeroAccessContext(
        Level: 12,
        CharacterClass: "Illusionist",
        AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
    var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
        "{Class paladin}Only paladins may read this vow.{Class paladin}",
        TaggedNoteCipherMode.Encrypt);

    AssertThrows<UnauthorizedAccessException>(
        () => TaggedNoteCipherUtility.TransformTaggedText(encrypted, TaggedNoteCipherMode.Decrypt, hero: hero));
}

static void TaggedNoteCipherAcceptsEitherOrAbilityTag()
{
    var hero = new HeroAccessContext(
        Level: 4,
        CharacterClass: "Cleric",
        AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Wisdom"] = 15
        });
    var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
        "{Level 6|Wis 15}The omen points east.{Level 6|Wis 15}",
        TaggedNoteCipherMode.Encrypt);
    var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
        encrypted,
        TaggedNoteCipherMode.Decrypt,
        hero: hero);

    AssertEqual("{Level 6|Wis 15}The omen points east.{Level 6|Wis 15}", decrypted, "either-or wisdom tag should decrypt note text");
}

static void TaggedNoteCipherAcceptsBareClassAlternative()
{
    var hero = new HeroAccessContext(
        Level: 1,
        CharacterClass: "Wizard",
        AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
    var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
        "{Wizard|Level 5}The sigil means danger.{Wizard|Level 5}",
        TaggedNoteCipherMode.Encrypt);
    var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
        encrypted,
        TaggedNoteCipherMode.Decrypt,
        hero: hero);

    AssertTrue(encrypted.StartsWith("{Wizard|Level 5}", StringComparison.Ordinal), "encrypted note should preserve bare class opening tag");
    AssertTrue(encrypted.EndsWith("{Wizard|Level 5}", StringComparison.Ordinal), "encrypted note should preserve bare class closing tag");
    AssertEqual("{Wizard|Level 5}The sigil means danger.{Wizard|Level 5}", decrypted, "bare class alternative should decrypt note text");
}

static void TaggedNoteCipherAcceptsClassLevelShorthandAndFactionTag()
{
    var spyHero = new HeroAccessContext(
        Level: 4,
        CharacterClass: "Spy",
        AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
    var factionHero = new HeroAccessContext(
        Level: 1,
        CharacterClass: "Fighter",
        AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Faction"] = "Scyntarn"
        });
    var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
        "{Spy 4|Faction Scyntarn}Nimba is actually a witch, like her sister Nuanda.{Spy 4|Faction Scyntarn}",
        TaggedNoteCipherMode.Encrypt);

    var spyDecrypted = TaggedNoteCipherUtility.TransformTaggedText(
        encrypted,
        TaggedNoteCipherMode.Decrypt,
        hero: spyHero);
    var factionDecrypted = TaggedNoteCipherUtility.TransformTaggedText(
        encrypted,
        TaggedNoteCipherMode.Decrypt,
        hero: factionHero);

    AssertEqual(
        "{Spy 4|Faction Scyntarn}Nimba is actually a witch, like her sister Nuanda.{Spy 4|Faction Scyntarn}",
        spyDecrypted,
        "class level shorthand should decrypt note text");
    AssertEqual(spyDecrypted, factionDecrypted, "faction tag alternative should decrypt the same note text");
}

static void TaggedNoteCipherAcceptsGroupedAndExpressionTag()
{
    const string taggedPlaintext = "{(Level 6 && Spy 3)|Scyntarn 9}The sealed paragraph opens.{(Level 6 && Spy 3)|Scyntarn 9}";
    var spyHero = new HeroAccessContext(
        Level: 6,
        CharacterClass: "Spy",
        AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
    var scyntarnHero = new HeroAccessContext(
        Level: 1,
        CharacterClass: "Fighter",
        AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        RankedMemberships: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Scyntarn"] = 9
        });
    var deniedHero = new HeroAccessContext(
        Level: 6,
        CharacterClass: "Fighter",
        AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        RankedMemberships: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Scyntarn"] = 8
        });
    var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
        taggedPlaintext,
        TaggedNoteCipherMode.Encrypt);

    var spyDecrypted = TaggedNoteCipherUtility.TransformTaggedText(
        encrypted,
        TaggedNoteCipherMode.Decrypt,
        hero: spyHero);
    var scyntarnDecrypted = TaggedNoteCipherUtility.TransformTaggedText(
        encrypted,
        TaggedNoteCipherMode.Decrypt,
        hero: scyntarnHero);

    AssertEqual(taggedPlaintext, spyDecrypted, "level and spy class-level branch should decrypt note text");
    AssertEqual(taggedPlaintext, scyntarnDecrypted, "ranked Scyntarn branch should decrypt note text");
    AssertThrows<UnauthorizedAccessException>(
        () => TaggedNoteCipherUtility.TransformTaggedText(encrypted, TaggedNoteCipherMode.Decrypt, hero: deniedHero));
}

static void TaggedNoteCipherReportsMismatchedDecryptTags()
{
    var hero = new HeroAccessContext(
        Level: 8,
        CharacterClass: "Spy",
        AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
    var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
        "{Level 8}The ward is real.{Level 8}",
        TaggedNoteCipherMode.Encrypt);
    var mismatched = encrypted[..^"{Level 8}".Length] + "{Level 9}";
    var decrypted = TaggedNoteCipherUtility.TransformTaggedText(
        mismatched,
        TaggedNoteCipherMode.Decrypt,
        hero: hero);

    AssertEqual(
        "unable to decrypt due to non-matching opening and closing tags",
        decrypted,
        "mismatched opening and closing tags should return the player-safe decrypt failure text");
}

static void TaggedNoteCipherReportsEncryptedMarkdownBlockCounts()
{
    var validBlock = TaggedNoteCipherUtility.TransformTaggedText(
        "{Level 8}The ward is real.{Level 8}",
        TaggedNoteCipherMode.Encrypt);
    var secondValidBlock = TaggedNoteCipherUtility.TransformTaggedText(
        "{(Level 6 && Spy 3)|Scyntarn 9}The sealed paragraph opens.{(Level 6 && Spy 3)|Scyntarn 9}",
        TaggedNoteCipherMode.Encrypt);
    var mismatchedBlock = secondValidBlock[..^"{(Level 6 && Spy 3)|Scyntarn 9}".Length] + "{Scyntarn 9}";
    var wrappedValidBlock = validBlock.Replace("PAN1:", $"{Environment.NewLine}  PAN1:", StringComparison.Ordinal);
    var markdown = string.Join(
        Environment.NewLine + Environment.NewLine,
        "Plain markdown before encrypted text.",
        wrappedValidBlock,
        mismatchedBlock,
        secondValidBlock);

    var report = TaggedNoteCipherUtility.EncryptedTextReportFromMarkdown(markdown);

    AssertEqual(
        "valid encrypted blocks: 2, mismatched tags: 1",
        report,
        "encrypted markdown report should count matching and mismatched tag wrappers");
}

static void TaggedNoteCipherIndexesEncryptedMarkdownFrontmatterTags()
{
    var encryptedBlock = TaggedNoteCipherUtility.TransformTaggedText(
        "{Level 8}The ward is real.{Level 8}",
        TaggedNoteCipherMode.Encrypt);
    var secondEncryptedBlock = TaggedNoteCipherUtility.TransformTaggedText(
        "{Spy 4|Faction Scyntarn}The innkeeper knows the pass.{Spy 4|Faction Scyntarn}",
        TaggedNoteCipherMode.Encrypt);
    var markdown = string.Join(
        Environment.NewLine,
        "---",
        "tags:",
        "  - npc",
        "  - spy",
        "  - \"Scyntarn\"",
        "---",
        "Plain text.",
        encryptedBlock,
        secondEncryptedBlock);

    var entry = TaggedNoteCipherUtility.CreateEncryptedTextIndexEntry(
        "https://publish.obsidian.md/scarlethorizons/NPCs/Nimba+Armstrong",
        markdown);

    if (entry is null)
    {
        throw new InvalidOperationException("encrypted markdown should produce an index entry");
    }

    AssertEqual(2, entry.EncryptedSections, "encrypted text index should count encrypted markdown blocks");
    AssertEqual(
        "npc,spy,Scyntarn",
        string.Join(",", entry.FrontmatterTags),
        "encrypted text index should preserve frontmatter tags");
    AssertEqual(
        TaggedNoteCipherUtility.EncryptedTextIndexFileName,
        "encrypted-text-index.json",
        "encrypted text index filename should match the proposed JSON artifact name");
}

static void TaggedNoteCipherAuthenticatesVisibleTags()
{
    var originalHero = new HeroAccessContext(
        Level: 8,
        CharacterClass: "Fighter",
        AbilityScores: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
    var lowerLevelHero = originalHero with { Level = 7 };
    var encrypted = TaggedNoteCipherUtility.TransformTaggedText(
        "{Level 8}The ward is real.{Level 8}",
        TaggedNoteCipherMode.Encrypt);
    var tampered = encrypted.Replace("{Level 8}", "{Level 7}", StringComparison.Ordinal);

    AssertThrows<InvalidOperationException>(
        () => TaggedNoteCipherUtility.TransformTaggedText(tampered, TaggedNoteCipherMode.Decrypt, hero: lowerLevelHero));
}

static void XpDisplayRecognizesDungeonMasterAccess()
{
    AssertTrue(
        (bool)(InvokeStaticMethod(typeof(Form1), "IsDungeonMasterXpAccess", "Dungeon Master") ?? false),
        "Dungeon Master should unlock all XP totals");
    AssertTrue(
        (bool)(InvokeStaticMethod(typeof(Form1), "IsDungeonMasterXpAccess", "dungeon master") ?? false),
        "Dungeon Master XP access should be case-insensitive");
    AssertFalse(
        (bool)(InvokeStaticMethod(typeof(Form1), "IsDungeonMasterXpAccess", "Kelpie") ?? true),
        "ordinary PCs should not unlock all XP totals");
}

static void XpDisplayFindsTotalsByFirstAndFullCharacterNames()
{
    var totals = new PcXpTotal[]
    {
        new("Kelpie Lawfuller", 7062),
        new("Jelb", 8575)
    };

    var kelpieTotal = (PcXpTotal?)InvokeStaticMethod(
        typeof(Form1),
        "FindXpTotalForCharacter",
        totals,
        "Kelpie");
    var jelbTotal = (PcXpTotal?)InvokeStaticMethod(
        typeof(Form1),
        "FindXpTotalForCharacter",
        totals,
        "Jelb Garrick");

    if (kelpieTotal is null)
    {
        throw new InvalidOperationException("first-name Kelpie lookup should find full-name XP row");
    }

    if (jelbTotal is null)
    {
        throw new InvalidOperationException("full-name Jelb lookup should find first-name XP row");
    }

    AssertEqual(new PcXpTotal("Kelpie Lawfuller", 7062), kelpieTotal!, "unexpected Kelpie XP row");
    AssertEqual(new PcXpTotal("Jelb", 8575), jelbTotal!, "unexpected Jelb XP row");
}

static void XpDisplayStoresMultipleTotalsForDungeonMaster()
{
    RunOnStaThread(() =>
    {
        using var form = new Form1(suppressHeroImagesForThisRun: true);
        var totals = new PcXpTotal[]
        {
            new("Kelpie", 7062),
            new("Jelb", 8575)
        };

        InvokePrivateMethod(form, "ShowXpTotals", "As of 7.04.2026", totals);

        var storedTotals = (IReadOnlyList<PcXpTotal>)(GetPrivateField(form, "_xpTotals")
            ?? throw new InvalidOperationException("_xpTotals was null."));
        AssertEqual(2, storedTotals.Count, "Dungeon Master XP display should retain all requested totals");
        AssertEqual(new PcXpTotal("Kelpie", 7062), storedTotals[0], "unexpected first stored XP total");
        AssertTrue((bool)(GetPrivateField(form, "_showXpTotal") ?? false), "XP display should be active");
    });
}

static void XpTrackingParserReadsLatestTableTotals()
{
    const string markdown =
        """
        ---
        status: XP
        ---
        As of 7.04.2026

        | Name     | Class       | Level | XP Total |
        | -------- | ----------- | ----- | -------- |
        | Kelpie   | Fighter     | 3     | 7,062    |
        | Jelb     | Illusionist | 2     | 8,575    |
        | Max      | Theurge     | 1     | 3,175    |
        | Geoffroy | Cleric      | 2     | 2,950    |

        As of 7.01.2026

        | Name     | Class       | Level | XP Total |
        | -------- | ----------- | ----- | -------- |
        | Kelpie   | Fighter     | 3     | 6,562    |
        | Jelb     | Illusionist | 2     | 8,075    |
        """;

    var totals = XpTrackingUtility.ParseCurrentXpTotals(markdown).ToArray();

    AssertEqual(4, totals.Length, "expected latest XP table to contain four current PCs");
    AssertEqual(new PcXpTotal("Kelpie", 7062), totals[0], "unexpected Kelpie XP total");
    AssertEqual(new PcXpTotal("Jelb", 8575), totals[1], "unexpected Jelb XP total");
    AssertEqual(new PcXpTotal("Max", 3175), totals[2], "unexpected Max XP total");
    AssertEqual(new PcXpTotal("Geoffroy", 2950), totals[3], "unexpected Geoffroy XP total");
}

static void XpTrackingParserRejectsMissingLatestTable()
{
    var exception = AssertThrows<InvalidOperationException>(() =>
        XpTrackingUtility.ParseCurrentXpTotals(
            """
            As of 7.04.2026

            No table today.
            """));

    AssertContains(exception.Message, "latest XP tracking date does not have a markdown table");
}

static void XpTrackingFailureMessageHidesUrlAndDirectsPlayersToDm()
{
    const string trackingUrl = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking";
    var message = XpTrackingUtility.FormatUserFacingFailureMessage(
        new InvalidOperationException($"XP tracking markdown could not be fetched from {trackingUrl}."));

    AssertContains(message, "XP totals could not be loaded from the XP Tracking page.");
    AssertContains(message, "Please contact the DM");
    AssertContains(message, "Technical detail:");
    AssertFalse(message.Contains(trackingUrl, StringComparison.Ordinal), "XP failure dialog should not expose the unlisted tracking URL");
    AssertFalse(message.Contains("https://", StringComparison.OrdinalIgnoreCase), "XP failure dialog should not expose URL-shaped text");
}

static void XpTrackingMissingPcMessageDirectsPlayersToDm()
{
    var message = XpTrackingUtility.FormatMissingPcFailureMessage("Kelpie");

    AssertContains(message, "No XP total was found for 'Kelpie'.");
    AssertContains(message, "Please contact the DM");
    AssertFalse(message.Contains("https://", StringComparison.OrdinalIgnoreCase), "missing-PC message should not expose URL-shaped text");
}

static void ExternalUrlLaunchPolicyAcceptsHttpAndHttps()
{
    var http = ExternalUrlLaunchUtility.Validate(" http://rpol.net/path?q=one ");
    var https = ExternalUrlLaunchUtility.Validate("https://publish.obsidian.md/entry");

    AssertTrue(http.IsAllowed, "HTTP URLs should be allowed");
    AssertEqual("http://rpol.net/path?q=one", http.Url ?? string.Empty, "HTTP URL should be normalized before launch");
    AssertEqual("rpol.net", http.Host ?? string.Empty, "HTTP host should be exposed for confirmation");
    AssertTrue(https.IsAllowed, "HTTPS URLs should be allowed");
}

static void ExternalUrlLaunchPolicyRejectsUnsafeInputs()
{
    var relative = ExternalUrlLaunchUtility.Validate("/relative/path");
    var file = ExternalUrlLaunchUtility.Validate("file:///C:/temp/report.html");
    var credentialed = ExternalUrlLaunchUtility.Validate("https://user:pass@example.test/private");
    var disallowed = ExternalUrlLaunchUtility.Validate("https://unexpected.example.test/private");

    AssertFalse(relative.IsAllowed, "relative URLs should not be opened externally");
    AssertContains(relative.RejectionReason ?? string.Empty, "absolute URL");
    AssertFalse(file.IsAllowed, "file URLs should not be opened from search results");
    AssertContains(file.RejectionReason ?? string.Empty, "HTTP and HTTPS");
    AssertFalse(credentialed.IsAllowed, "credentialed URLs should not be opened externally");
    AssertContains(credentialed.RejectionReason ?? string.Empty, "credentials");
    AssertFalse(disallowed.IsAllowed, "non-allowlisted URLs should not be opened externally");
    AssertContains(disallowed.RejectionReason ?? string.Empty, "allowlist");
}

static void HeroImagePathsFollowListingMarkdownTable()
{
    using var directory = TemporaryDirectory.Create();
    var pcsDirectory = Path.Combine(directory.Path, "PCs");
    var activeDirectory = Path.Combine(pcsDirectory, "active");
    Directory.CreateDirectory(activeDirectory);

    var listedImagePath = Path.Combine(activeDirectory, "alice-token.webp");
    var strayImagePath = Path.Combine(activeDirectory, "stray-token.webp");
    File.WriteAllText(listedImagePath, "listed");
    File.WriteAllText(strayImagePath, "stray");

    var listingMarkdown = """
        | Name | Character | Notes | Hero |
        | --- | --- | --- | --- |
        | Alice | [[Alice]] | active | ![[alice-token.webp]] |
        | Bob | [[Bob]] | active | ![[bob-token.webp]] |
        """;

    var result = InvokeStaticMethod(
        typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.PlayerCharacterAssetUtility")
            ?? throw new InvalidOperationException("Unable to find PlayerCharacterAssetUtility type."),
        "GetListedActiveHeroImagePaths",
        listingMarkdown,
        pcsDirectory);

    var paths = ((string[])result!)
        .Select(path => Path.GetFileName(path) ?? string.Empty)
        .ToArray();

    AssertEqual(1, paths.Length, "expected only heroes listed in the markdown table to be selected");
    AssertEqual("alice-token.webp", paths[0], "unexpected hero image selected from active directory");
    AssertFalse(paths.Contains("stray-token.webp", StringComparer.OrdinalIgnoreCase), "unlisted hero image should not be selected");
}

static void HeroAssetPathsRejectEscapedTargets()
{
    using var directory = TemporaryDirectory.Create();
    var activeDirectory = Path.Combine(directory.Path, "PCs", "active");
    var utilityType = typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.PlayerCharacterAssetUtility")
        ?? throw new InvalidOperationException("Unable to find PlayerCharacterAssetUtility type.");

    var safePath = (string)(InvokeStaticMethod(
        utilityType,
        "GetActiveHeroAssetPath",
        activeDirectory,
        "alice-token.webp") ?? throw new InvalidOperationException("GetActiveHeroAssetPath returned null."));

    AssertTrue(
        safePath.StartsWith(activeDirectory, StringComparison.OrdinalIgnoreCase),
        "safe hero asset path should remain under the active PCs directory");

    AssertThrows<InvalidOperationException>(() =>
        InvokeStaticMethod(utilityType, "GetActiveHeroAssetPath", activeDirectory, "..\\escape.webp"));
    AssertThrows<InvalidOperationException>(() =>
        InvokeStaticMethod(utilityType, "GetActiveHeroAssetPath", activeDirectory, "/escape.webp"));
}

static void LocalSettingsAreEncryptedOnLoad()
{
    using var directory = TemporaryDirectory.Create();
    var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
    var plaintext = """
        {
          "RPOL user name": "example-user",
          "RPOL password": "example-password"
        }
        """;
    File.WriteAllText(localSettingsPath, plaintext);

    var settings = (Dictionary<string, string>)InvokeStaticMethod(
        typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.LocalSettingsUtility")
            ?? throw new InvalidOperationException("Unable to find LocalSettingsUtility type."),
        "LoadSettings",
        localSettingsPath)!;

    AssertEqual("example-user", settings["RPOL user name"], "unexpected user name after load");
    AssertEqual("example-password", settings["RPOL password"], "unexpected password after load");
    AssertFalse(File.ReadAllText(localSettingsPath).Contains("example-password", StringComparison.Ordinal), "plaintext password should not remain on disk");
    AssertTrue(
        (bool)InvokeStaticMethod(
            typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.LocalSettingsUtility")
                ?? throw new InvalidOperationException("Unable to find LocalSettingsUtility type."),
            "IsEncryptedSettingsFile",
            localSettingsPath)!,
        "expected the local settings file to be encrypted after load");
    var encryptedJson = File.ReadAllText(localSettingsPath);
    AssertContains(encryptedJson, "\"schema_version\": 1");
    AssertContains(encryptedJson, "\"format\": \"app-protected-v3\"");
    AssertContains(encryptedJson, "\"key_scope\":");
    AssertContains(encryptedJson, "\"install_path_bound\": true");
}

static void PortableEncryptedSettingsByteLoaderClearsSourceBuffer()
{
    var settings = new Dictionary<string, string>
    {
        ["RPOL user name"] = "example-user",
        ["RPOL password"] = "example-password"
    };
    var encryptedJson = LocalSettingsUtility.CreatePortableEncryptedSettingsJson(settings);
    var encryptedUtf8 = System.Text.Encoding.UTF8.GetBytes(encryptedJson);

    var loadedSettings = LocalSettingsUtility.LoadPortableEncryptedSettingsFromUtf8Bytes(
        encryptedUtf8,
        "test settings");

    AssertEqual("example-user", loadedSettings["RPOL user name"], "unexpected user name after byte-buffer load");
    AssertEqual("example-password", loadedSettings["RPOL password"], "unexpected password after byte-buffer load");
    AssertTrue(encryptedUtf8.All(static value => value == 0), "portable encrypted settings buffer should be cleared after load");
}

static void CredentialManagerUtf8HelpersClearTransientBuffers()
{
    var backend = new ObservedWindowsCredentialStoreBackend();
    using var backendScope = RuntimeSecretStoreUtility.UseBackendForTests(backend);

    WindowsCredentialManagerUtility.WriteSecretUtf8("PlayerAssistant/Test/Secret", "hunter2", "test");
    AssertTrue(backend.LastWriteInputBytes is not null, "expected test backend to observe the write buffer");
    AssertTrue(backend.LastWriteInputBytes!.All(static value => value == 0), "UTF-8 write buffer should be cleared after credential-manager write");

    AssertTrue(
        WindowsCredentialManagerUtility.TryReadSecretUtf8(
            "PlayerAssistant/Test/Secret",
            out var storedSecret,
            out _),
        "expected test secret to round-trip through credential manager helper");
    AssertEqual("hunter2", storedSecret ?? string.Empty, "unexpected credential-manager secret text");
    AssertTrue(backend.LastReadOutputBytes is not null, "expected test backend to expose the read buffer");
    AssertTrue(backend.LastReadOutputBytes!.All(static value => value == 0), "UTF-8 read buffer should be cleared after credential-manager read");
}

static void LocalSettingsEncryptCommandWritesPortableEnvelope()
{
    using var directory = TemporaryDirectory.Create();
    var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
    File.WriteAllText(
        localSettingsPath,
        """
        {
          "XP Tracking": "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
        }
        """);

    var programType = typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.Program")
        ?? throw new InvalidOperationException("Unable to find PlayerAssistant.Program type.");
    using var output = new StringWriter();
    var handled = (bool)InvokeStaticMethod(
        programType,
        "TryRunLocalSettingsCommand",
        new[] { "--encrypt-local-settings", localSettingsPath },
        output)!;

    AssertTrue(handled, "expected encrypt-local-settings command to be handled");
    var encryptedJson = File.ReadAllText(localSettingsPath);
    AssertContains(encryptedJson, "\"format\": \"app-protected-v2\"");
    AssertFalse(
        encryptedJson.Contains("Intentional+Orphans/XP+Tracking", StringComparison.Ordinal),
        "portable encrypted settings should not keep the plaintext URL on disk");

    var settings = LocalSettingsUtility.LoadPortableEncryptedSettings(localSettingsPath);
    AssertEqual(
        "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
        settings["XP Tracking"],
        "unexpected XP Tracking value after portable encryption");
}

static void LocalSettingsDecryptCommandWritesPlaintextJson()
{
    using var directory = TemporaryDirectory.Create();
    var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
    var outputPath = Path.Combine(directory.Path, "settings.local.plaintext.json");
    LocalSettingsUtility.SavePortableEncryptedSettings(
        localSettingsPath,
        new Dictionary<string, string>
        {
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
        });

    var programType = typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.Program")
        ?? throw new InvalidOperationException("Unable to find PlayerAssistant.Program type.");
    using var output = new StringWriter();
    var handled = (bool)InvokeStaticMethod(
        programType,
        "TryRunLocalSettingsCommand",
        new[] { "--decrypt-local-settings", localSettingsPath, outputPath },
        output)!;

    AssertTrue(handled, "expected decrypt-local-settings command to be handled");
    var plaintextJson = File.ReadAllText(outputPath);
    AssertContains(plaintextJson, "\"schema_version\": 1");
    using var document = System.Text.Json.JsonDocument.Parse(plaintextJson);
    AssertEqual(
        "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
        document.RootElement.GetProperty("XP Tracking").GetString() ?? string.Empty,
        "unexpected XP Tracking value in decrypted plaintext json");
    AssertFalse(plaintextJson.Contains("\"payload\":", StringComparison.Ordinal), "decrypted output should not keep the encrypted payload envelope");
}

static void LocalSettingsRejectsFutureSchemaVersion()
{
    using var directory = TemporaryDirectory.Create();
    var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
    File.WriteAllText(
        localSettingsPath,
        """
        {
          "schema_version": 99,
          "format": "app-protected-v3",
          "payload": "not-a-real-payload"
        }
        """);

    var exception = AssertThrows<InvalidOperationException>(() =>
        LocalSettingsUtility.LoadSettings(localSettingsPath));
    AssertContains(exception.Message, "unsupported schema version 99");
}

static void LegacyLocalSettingsMigrateToPortableEncryption()
{
    using var directory = TemporaryDirectory.Create();
    var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
    var plaintext = """
        {
          "RPOL user name": "example-user",
          "RPOL password": "example-password"
        }
        """;
    var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
    var protectedBytes = ProtectedData.Protect(plaintextBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
    var legacyEnvelope = """
        {
          "format": "dpapi-current-user",
          "payload": "__PAYLOAD__"
        }
        """.Replace("__PAYLOAD__", Convert.ToBase64String(protectedBytes), StringComparison.Ordinal);

    File.WriteAllText(localSettingsPath, legacyEnvelope);

    var settings = (Dictionary<string, string>)InvokeStaticMethod(
        typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.LocalSettingsUtility")
            ?? throw new InvalidOperationException("Unable to find LocalSettingsUtility type."),
        "LoadSettings",
        localSettingsPath)!;

    AssertEqual("example-user", settings["RPOL user name"], "unexpected user name after migrating legacy encrypted settings");
    AssertEqual("example-password", settings["RPOL password"], "unexpected password after migrating legacy encrypted settings");
    AssertTrue(
        File.ReadAllText(localSettingsPath).Contains("\"format\": \"app-protected-v3\"", StringComparison.Ordinal),
        "expected legacy encrypted settings to be rewritten using the scoped app-protected-v3 format");
}

static void V1LocalSettingsMigrateToAuthenticatedEncryption()
{
    using var directory = TemporaryDirectory.Create();
    var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
    File.WriteAllText(localSettingsPath, CreateV1LocalSettingsEnvelope("example-user", "example-password"));

    var settings = (Dictionary<string, string>)InvokeStaticMethod(
        typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.LocalSettingsUtility")
            ?? throw new InvalidOperationException("Unable to find LocalSettingsUtility type."),
        "LoadSettings",
        localSettingsPath)!;

    AssertEqual("example-user", settings["RPOL user name"], "unexpected user name after migrating v1 encrypted settings");
    AssertEqual("example-password", settings["RPOL password"], "unexpected password after migrating v1 encrypted settings");
    AssertContains(File.ReadAllText(localSettingsPath), "\"format\": \"app-protected-v3\"");
}

static void V2LocalSettingsMigrateToScopedEncryption()
{
    using var directory = TemporaryDirectory.Create();
    var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
    File.WriteAllText(localSettingsPath, CreateV2LocalSettingsEnvelope("example-user", "example-password"));

    var settings = (Dictionary<string, string>)InvokeStaticMethod(
        typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.LocalSettingsUtility")
            ?? throw new InvalidOperationException("Unable to find LocalSettingsUtility type."),
        "LoadSettings",
        localSettingsPath)!;

    AssertEqual("example-user", settings["RPOL user name"], "unexpected user name after migrating v2 encrypted settings");
    AssertEqual("example-password", settings["RPOL password"], "unexpected password after migrating v2 encrypted settings");
    var encryptedJson = File.ReadAllText(localSettingsPath);
    AssertContains(encryptedJson, "\"format\": \"app-protected-v3\"");
    AssertContains(encryptedJson, "\"key_scope\":");
}

static void ScopedLocalSettingsRejectCopiedInstallPath()
{
    using var sourceDirectory = TemporaryDirectory.Create();
    using var copiedDirectory = TemporaryDirectory.Create();
    var sourcePath = Path.Combine(sourceDirectory.Path, "settings.local.json");
    var copiedPath = Path.Combine(copiedDirectory.Path, "settings.local.json");

    LocalSettingsUtility.SaveEncryptedSettings(
        sourcePath,
        new Dictionary<string, string>
        {
            ["RPOL user name"] = "example-user",
            ["RPOL password"] = "example-password"
        });

    File.Copy(sourcePath, copiedPath);

    var exception = AssertThrows<InvalidOperationException>(() =>
        LocalSettingsUtility.LoadSettings(copiedPath));
    AssertContains(exception.Message, "different Windows user, machine, or install directory");
}

static void AuthenticatedLocalSettingsRejectTamperedPayload()
{
    using var directory = TemporaryDirectory.Create();
    var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
    LocalSettingsUtility.SaveEncryptedSettings(
        localSettingsPath,
        new Dictionary<string, string>
        {
            ["RPOL user name"] = "example-user",
            ["RPOL password"] = "example-password"
        });

    using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(localSettingsPath)))
    {
        var payload = document.RootElement.GetProperty("payload").GetString() ?? string.Empty;
        var payloadBytes = Convert.FromBase64String(payload);
        payloadBytes[^1] ^= 0x7F;
        File.WriteAllText(
            localSettingsPath,
            $$"""
            {
              "format": "app-protected-v3",
              "payload": "{{Convert.ToBase64String(payloadBytes)}}"
            }
            """);
    }

    var exception = AssertThrows<InvalidOperationException>(() =>
        LocalSettingsUtility.LoadSettings(localSettingsPath));
    AssertContains(exception.Message, "authenticate or decrypt");
}

static void LocalSettingsRestoresNewestValidBackup()
{
    using var directory = TemporaryDirectory.Create();
    var localSettingsPath = Path.Combine(directory.Path, "settings.local.json");
    var validBackupPath = Path.Combine(directory.Path, "settings.local.bak-20260701-010203-001.json");
    var invalidBackupPath = Path.Combine(directory.Path, "settings.local.bak-20260702-010203-001.json");

    LocalSettingsUtility.SaveEncryptedSettings(
        localSettingsPath,
        new Dictionary<string, string>
        {
            ["RPOL user name"] = "restored-user",
            ["RPOL password"] = "restored-password"
        });
    File.Copy(localSettingsPath, validBackupPath);
    File.WriteAllText(invalidBackupPath, "{ not valid settings");
    File.WriteAllText(localSettingsPath, "{ corrupt active settings");
    SetLastWriteTimeUtc(validBackupPath, new DateTimeOffset(2026, 7, 1, 1, 2, 3, TimeSpan.Zero));
    SetLastWriteTimeUtc(invalidBackupPath, new DateTimeOffset(2026, 7, 2, 1, 2, 3, TimeSpan.Zero));

    WithPreservedStartupLog(() =>
    {
        var settings = LocalSettingsUtility.LoadSettings(localSettingsPath);

        AssertEqual("restored-user", settings["RPOL user name"], "unexpected restored user name");
        AssertEqual("restored-password", settings["RPOL password"], "unexpected restored password");
        AssertFalse(File.ReadAllText(localSettingsPath).Contains("corrupt active", StringComparison.Ordinal), "corrupt active settings should be replaced");

        var startupLog = File.ReadAllText(GetStartupLogPath());
        AssertContains(startupLog, "local settings backup restore");
        AssertContains(startupLog, "Restored runtime artifact");
    });
}

static void RuntimePathUtilityRejectsEscapedPaths()
{
    using var directory = TemporaryDirectory.Create();

    var contained = RuntimePathUtility.CombineUnderBase(directory.Path, "child", "file.txt");
    AssertTrue(contained.StartsWith(directory.Path, StringComparison.OrdinalIgnoreCase), "contained path should remain under the base directory");

    AssertThrows<InvalidOperationException>(() =>
        RuntimePathUtility.CombineUnderBase(directory.Path, "..", "escape.txt"));
}

static void HealthArgumentReturnsStartupHealthSummary()
{
    var programType = typeof(PlayerAssistant.Form1).Assembly.GetType("PlayerAssistant.Program")
        ?? throw new InvalidOperationException("Unable to find PlayerAssistant.Program type.");
    AssertTrue(
        (bool)InvokeStaticMethod(programType, "IsHealthArgument", "--health")!,
        "expected --health to be recognized");
    var health = (string)InvokeStaticMethod(programType, "GetHealthText")!;

    AssertContains(health, "player-assistant");
    AssertContains(health, "runtime:");
    AssertContains(health, "status:");
}

static void PublishVerificationAcceptsCurrentOutput()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        var output = RunPublishVerification(directoryPath);

        AssertEqual(0, output.ExitCode, $"publish verification should pass. Output: {output.Output}");
        AssertContains(output.Output, "Publish verification passed:");
    });
}

static void PublishVerificationRejectsStaleStartupLog()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.WriteAllText(Path.Combine(directoryPath, StartupLoggingUtility.LogFileName), "stale failure");

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when startup-errors.log is present");
        AssertContains(output.Output, "startup-errors.log");
    });
}

static void PublishVerificationRejectsStartupHealthArtifact()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.WriteAllText(Path.Combine(directoryPath, StartupHealthUtility.HealthFileName), "{}");

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when startup-health.json is present");
        AssertContains(output.Output, StartupHealthUtility.HealthFileName);
    });
}

static void PublishVerificationRejectsOutboundNetworkDiagnosticsArtifact()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.WriteAllText(Path.Combine(directoryPath, OutboundNetworkDiagnosticsUtility.DiagnosticsFileName), "{}");

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when outbound-network-diagnostics.json is present");
        AssertContains(output.Output, OutboundNetworkDiagnosticsUtility.DiagnosticsFileName);
    });
}

static void PublishVerificationRejectsLastCrashArtifact()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.WriteAllText(Path.Combine(directoryPath, LastCrashDiagnosticUtility.FileName), "{}");

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when last-crash.json is present");
        AssertContains(output.Output, LastCrashDiagnosticUtility.FileName);
    });
}

static void PublishVerificationRejectsMalformedSettingsJson()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.WriteAllText(Path.Combine(directoryPath, "settings.json"), "{ not valid json");

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when settings.json is malformed");
        AssertContains(output.Output, "published settings.json is not valid JSON");
    });
}

static void PublishVerificationRejectsFutureSettingsSchema()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.WriteAllText(
            Path.Combine(directoryPath, "settings.json"),
            """
            {
              "schema_version": 99,
              "RPOL Site": "https://rpol.net/game.php?gi=80170",
              "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
              "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
              "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons"
            }
            """);

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when settings.json uses a future schema");
        AssertContains(output.Output, "published settings.json uses unsupported schema version 99");
    });
}

static void PublishVerificationAcceptsEncryptedRpolLocalSettingsSidecar()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        LocalSettingsUtility.SaveEncryptedSettings(
            Path.Combine(directoryPath, "settings.local.json"),
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
                ["RPOL user name"] = "example-user",
                ["RPOL password"] = "example-password"
            });
        WriteReleaseManifest(directoryPath);
        WriteReleaseProvenance(directoryPath);

        var output = RunPublishVerification(directoryPath);

        AssertEqual(
            0,
            output.ExitCode,
            $"publish verification should accept encrypted settings.local.json with RPOL credentials. Output: {output.Output}");
    });
}

static void PublishVerificationRejectsPlaintextRpolLocalSettingsSidecar()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.WriteAllText(
            Path.Combine(directoryPath, "settings.local.json"),
            """
            {
              "XP Tracking": "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
              "RPOL user name": "example-user",
              "RPOL password": "example-password"
            }
            """);
        WriteReleaseManifest(directoryPath);

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when settings.local.json contains plaintext RPOL credentials");
        AssertContains(output.Output, "plaintext RPOL credentials");
    });
}

static void PublishVerificationAcceptsMissingHostedLocalSettingsUrl()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        var settingsPath = Path.Combine(directoryPath, "settings.json");
        File.WriteAllText(
            settingsPath,
            File.ReadAllText(settingsPath).Replace(
                """
  "Hosted Local Settings": "https://bryanmiller.us/scarlethorizons/settings.local.json",
""",
                string.Empty,
                StringComparison.Ordinal));
        LocalSettingsUtility.SaveEncryptedSettings(
            Path.Combine(directoryPath, "settings.local.json"),
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
                ["RPOL user name"] = "example-user",
                ["RPOL password"] = "example-password"
            });
        WriteReleaseManifest(directoryPath);
        WriteReleaseProvenance(directoryPath);

        var output = RunPublishVerification(directoryPath);

        AssertEqual(
            0,
            output.ExitCode,
            $"publish verification should accept settings.json without Hosted Local Settings when encrypted local settings ships. Output: {output.Output}");
    });
}

static void PublishVerificationRejectsMissingXpPasswordSidecar()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.Delete(Path.Combine(directoryPath, XpPasswordStoreUtility.FileName));

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when xp-passwords.json is missing");
        AssertContains(output.Output, XpPasswordStoreUtility.FileName);
    });
}

static void PublishVerificationRejectsPlaintextXpPasswordSidecar()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.WriteAllText(
            Path.Combine(directoryPath, XpPasswordStoreUtility.FileName),
            """
            {
              "Kelpie": "gemstone"
            }
            """);

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when xp-passwords.json is plaintext");
        AssertContains(output.Output, XpPasswordStoreUtility.Format);
    });
}

static void PublishVerificationRejectsMalformedKeywordIndex()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.WriteAllText(
            Path.Combine(directoryPath, "keyword-index.json"),
            """
            {
              "index_metadata": {}
            }
            """);

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when keyword-index.json has no words object");
        AssertContains(output.Output, "published keyword index must contain a words object");
    });
}

static void PublishVerificationRejectsMalformedSitemap()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.WriteAllText(Path.Combine(directoryPath, "sitemap.xml"), "not xml");

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when sitemap.xml is malformed");
        AssertContains(output.Output, "sitemap.xml is not valid XML");
    });
}

static void PublishVerificationRejectsIncompletePlaywrightRuntime()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.Delete(Path.Combine(directoryPath, ".playwright", "node", "win32_x64", "node.exe"));

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when Playwright node.exe is missing");
        AssertContains(output.Output, "published Playwright node.exe");
    });
}

static void PublishVerificationRejectsMismatchedExecutableVersion()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.Copy(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            Path.Combine(directoryPath, "player-assistant.exe"),
            overwrite: true);

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when player-assistant.exe has the wrong version metadata");
        AssertContains(output.Output, "Published executable FileVersion");
    });
}

static void PublishVerificationRejectsStaleReleaseManifest()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        var manifestPath = Path.Combine(directoryPath, "release-manifest.json");
        var manifest = File.ReadAllText(manifestPath);
        var marker = "\"sha256\":";
        var markerIndex = manifest.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException("release-manifest.json did not contain a sha256 entry.");
        }

        var valueStart = manifest.IndexOf('"', markerIndex + marker.Length);
        var valueEnd = manifest.IndexOf('"', valueStart + 1);
        var tamperedManifest = manifest[..(valueStart + 1)]
            + new string('0', valueEnd - valueStart - 1)
            + manifest[valueEnd..];
        File.WriteAllText(manifestPath, tamperedManifest);

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when release-manifest.json is stale");
        AssertContains(output.Output, "release-manifest.json SHA256 mismatch");
    });
}

static void PublishVerificationRejectsMalformedRuntimeInventory()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.WriteAllText(Path.Combine(directoryPath, "release-runtime-inventory.json"), "{ not valid json");
        WriteReleaseManifest(directoryPath);

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when release-runtime-inventory.json is malformed");
        AssertContains(output.Output, "release-runtime-inventory.json is not valid JSON");
    });
}

static void PublishVerificationRejectsMalformedReleaseProvenance()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.WriteAllText(Path.Combine(directoryPath, "release-provenance.json"), "{ not valid json");

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when release-provenance.json is malformed");
        AssertContains(output.Output, "release-provenance.json is not valid JSON");
    });
}

static void PublishVerificationRejectsUnsignedExecutableWhenSigningRequired()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        var output = RunPublishVerification(directoryPath, "-RequireCodeSigning");

        AssertFalse(output.ExitCode == 0, "publish verification should fail when code signing is required for an unsigned executable");
        AssertContains(output.Output, "Published executable Authenticode signature status");
    });
}

static void InstallerScriptsTargetProgramFilesInstallPath()
{
    var installerPath = Path.Combine(GetRepositoryRoot(), "Installer", "install-player-assistant.ps1");
    var launcherPath = Path.Combine(GetRepositoryRoot(), "Installer", "install-player-assistant.cmd");
    var innoScriptPath = Path.Combine(GetRepositoryRoot(), "Installer", "player-assistant.iss");
    var builderPath = Path.Combine(GetRepositoryRoot(), "build-installer.ps1");
    var verifierPath = Path.Combine(GetRepositoryRoot(), "verify-installer-package.ps1");

    AssertTrue(File.Exists(installerPath), "installer script should exist");
    AssertTrue(File.Exists(launcherPath), "installer launcher should exist");
    AssertTrue(File.Exists(innoScriptPath), "Inno Setup script should exist");
    AssertTrue(File.Exists(builderPath), "installer package builder should exist");
    AssertTrue(File.Exists(verifierPath), "installer package verifier should exist");

    var installer = File.ReadAllText(installerPath);
    AssertContains(installer, "kyrathasoft\\player-assistant");
    AssertContains(installer, "CommonPrograms");
    AssertContains(installer, "Uninstall\\KyrathaSoft Player Assistant");
    var innoScript = File.ReadAllText(innoScriptPath);
    AssertContains(innoScript, "DefaultDirName={autopf}\\kyrathasoft\\player-assistant");
    AssertContains(innoScript, "ArchitecturesInstallIn64BitMode=x64compatible");
    AssertContains(innoScript, "#define InstallerVersion Version");
    AssertContains(innoScript, "OutputBaseFilename=p-assist-{#InstallerVersion}");
    AssertContains(innoScript, ".NET Desktop Runtime 10 x64");
    AssertContains(innoScript, "https://dotnet.microsoft.com/en-us/download/dotnet/10.0");
    AssertContains(innoScript, "IsRequiredRuntimeInstalled");
    AssertContains(innoScript, "Microsoft.WindowsDesktop.App");
    AssertContains(File.ReadAllText(builderPath), "ISCC.exe");
    AssertContains(File.ReadAllText(builderPath), "Get-InstallerVersion");
    AssertContains(File.ReadAllText(builderPath), "p-assist-$InstallerVersion.exe");
    AssertContains(File.ReadAllText(builderPath), "app-protected-v2");
    AssertContains(File.ReadAllText(verifierPath), "app-protected-v2");
}

static void InstallerPackageVerificationAcceptsCurrentPackage()
{
    var packagePath = Path.Combine(
        GetRepositoryRoot(),
        "Release",
        "installer",
        "player-assistant-0.9.1-hardening.1-installer.zip");
    if (!File.Exists(packagePath))
    {
        return;
    }

    var output = RunPowerShell(
        [
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(GetRepositoryRoot(), "verify-installer-package.ps1"),
            "-PackagePath",
            packagePath
        ],
        TimeSpan.FromSeconds(60));

    AssertEqual(0, output.ExitCode, $"installer package verification should pass. Output: {output.Output}");
    AssertContains(output.Output, "Installer package verification passed:");
}

static void InstallerPackageVerificationRejectsUnsignedPayloadWhenSigningRequired()
{
    var packagePath = Path.Combine(
        GetRepositoryRoot(),
        "Release",
        "installer",
        "player-assistant-0.9.1-hardening.1-installer.zip");
    if (!File.Exists(packagePath))
    {
        return;
    }

    var output = RunPowerShell(
        [
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(GetRepositoryRoot(), "verify-installer-package.ps1"),
            "-PackagePath",
            packagePath,
            "-RequireCodeSigning"
        ],
        TimeSpan.FromSeconds(60));

    AssertFalse(output.ExitCode == 0, "installer package verification should fail when code signing is required for an unsigned payload");
    AssertContains(output.Output, "Payload executable Authenticode signature status");
}

static void ReleaseUpdateArtifactVerificationAcceptsGeneratedSignedManifest()
{
    WithCopiedPublishDirectory(publishDirectory =>
    {
        using var outputDirectory = TemporaryDirectory.Create();
        var installerPath = Path.Combine(outputDirectory.Path, "p-assist-0.9.1.exe");
        File.Copy(Path.Combine(publishDirectory, "player-assistant.exe"), installerPath, overwrite: true);

        var buildOutput = RunPowerShell(
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Path.Combine(GetRepositoryRoot(), "build-release-update-artifacts.ps1"),
                "-OutputDir",
                outputDirectory.Path,
                "-PublishDir",
                publishDirectory,
                "-InstallerPath",
                installerPath,
                "-GenerateEphemeralSigningKey"
            ],
            TimeSpan.FromSeconds(60));

        AssertEqual(0, buildOutput.ExitCode, $"release update artifact build should pass. Output: {buildOutput.Output}");

        var verifyOutput = RunPowerShell(
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Path.Combine(GetRepositoryRoot(), "verify-release-update-artifacts.ps1"),
                "-PublishArchivePath",
                Path.Combine(outputDirectory.Path, "p-assist-0.9.1.zip"),
                "-InstallerPath",
                installerPath,
                "-ManifestPath",
                Path.Combine(outputDirectory.Path, "p-assist-updates.json"),
                "-SignaturePath",
                Path.Combine(outputDirectory.Path, "p-assist-updates.json.sig"),
                "-PublicKeyXmlPath",
                Path.Combine(outputDirectory.Path, "p-assist-updates.public-key.xml")
            ],
            TimeSpan.FromSeconds(60));

        AssertEqual(0, verifyOutput.ExitCode, $"release update artifact verification should pass. Output: {verifyOutput.Output}");
        AssertContains(verifyOutput.Output, "Release update artifacts verification passed:");
    });
}

static void ReleaseUpdateArtifactVerificationRejectsManifestHashMismatch()
{
    WithCopiedPublishDirectory(publishDirectory =>
    {
        using var outputDirectory = TemporaryDirectory.Create();
        var installerPath = Path.Combine(outputDirectory.Path, "p-assist-0.9.1.exe");
        File.Copy(Path.Combine(publishDirectory, "player-assistant.exe"), installerPath, overwrite: true);

        var buildOutput = RunPowerShell(
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Path.Combine(GetRepositoryRoot(), "build-release-update-artifacts.ps1"),
                "-OutputDir",
                outputDirectory.Path,
                "-PublishDir",
                publishDirectory,
                "-InstallerPath",
                installerPath,
                "-GenerateEphemeralSigningKey"
            ],
            TimeSpan.FromSeconds(60));

        AssertEqual(0, buildOutput.ExitCode, $"release update artifact build should pass. Output: {buildOutput.Output}");

        var manifestPath = Path.Combine(outputDirectory.Path, "p-assist-updates.json");
        var manifest = File.ReadAllText(manifestPath);
        var marker = @"""installer_sha256"": ";
        var markerIndex = manifest.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException("p-assist-updates.json did not contain installer_sha256.");
        }

        var valueStart = manifest.IndexOf('"', markerIndex + marker.Length);
        var valueEnd = manifest.IndexOf('"', valueStart + 1);
        var tamperedManifest = manifest[..(valueStart + 1)]
            + new string('0', valueEnd - valueStart - 1)
            + manifest[valueEnd..];
        File.WriteAllText(manifestPath, tamperedManifest);

        var verifyOutput = RunPowerShell(
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Path.Combine(GetRepositoryRoot(), "verify-release-update-artifacts.ps1"),
                "-PublishArchivePath",
                Path.Combine(outputDirectory.Path, "p-assist-0.9.1.zip"),
                "-InstallerPath",
                installerPath,
                "-ManifestPath",
                manifestPath,
                "-SignaturePath",
                Path.Combine(outputDirectory.Path, "p-assist-updates.json.sig"),
                "-PublicKeyXmlPath",
                Path.Combine(outputDirectory.Path, "p-assist-updates.public-key.xml")
            ],
            TimeSpan.FromSeconds(60));

        AssertFalse(verifyOutput.ExitCode == 0, "release update artifact verification should fail when the signed manifest hash is tampered");
        AssertContains(verifyOutput.Output, "signature verification failed");
    });
}

static void HardeningWorkflowBuildsAndUploadsSignedReleaseUpdateArtifacts()
{
    var workflowPath = Path.Combine(GetRepositoryRoot(), ".github", "workflows", "hardening.yml");
    var builderPath = Path.Combine(GetRepositoryRoot(), "build-release-update-artifacts.ps1");
    var verifierPath = Path.Combine(GetRepositoryRoot(), "verify-release-update-artifacts.ps1");
    var publishScriptPath = Path.Combine(GetRepositoryRoot(), "publish-player-assistant.ps1");
    var installerSmokeScriptPath = Path.Combine(GetRepositoryRoot(), "verify-installer-clean-machine-smoke.ps1");

    AssertTrue(File.Exists(workflowPath), "hardening workflow should exist");
    AssertTrue(File.Exists(builderPath), "release update artifact builder should exist");
    AssertTrue(File.Exists(verifierPath), "release update artifact verifier should exist");
    AssertTrue(File.Exists(publishScriptPath), "publish verification script should exist");
    AssertTrue(File.Exists(installerSmokeScriptPath), "installer clean-machine smoke script should exist");

    var workflow = File.ReadAllText(workflowPath);
    AssertContains(workflow, "Build signed release update artifacts");
    AssertContains(workflow, ".\\build-release-update-artifacts.ps1");
    AssertContains(workflow, "Installer clean-machine smoke");
    AssertContains(workflow, ".\\verify-installer-clean-machine-smoke.ps1");
    AssertContains(workflow, "Verify signed release update artifacts");
    AssertContains(workflow, ".\\verify-release-update-artifacts.ps1");
    AssertContains(workflow, "Upload release installer artifacts");
    AssertContains(workflow, "p-assist-updates.json");
    AssertContains(workflow, "p-assist-updates.json.sig");
    AssertContains(workflow, "p-assist-updates.public-key.xml");
    AssertContains(workflow, "p-assist-0.9.4.zip");
    AssertContains(workflow, "p-assist-0.9.4.exe");
    AssertContains(workflow, "Build Release test harness");
    AssertContains(workflow, "Verify hosted settings fetch and decrypt path");
    AssertContains(workflow, "app settings loads hosted encrypted xp tracking url from fixture server");
    AssertContains(workflow, "Verify hosted settings negative paths");
    AssertContains(workflow, "hosted settings failure");

    var builder = File.ReadAllText(builderPath);
    AssertContains(builder, "p-assist-$installerVersion.zip");
    AssertContains(builder, "p-assist-updates.json");
    AssertContains(builder, "GenerateEphemeralSigningKey");
    AssertContains(builder, "installer_sha256");

    var verifier = File.ReadAllText(verifierPath);
    AssertContains(verifier, "Signed update manifest");
    AssertContains(verifier, "p-assist-$installerVersion.zip");
    AssertContains(verifier, "p-assist-$installerVersion.exe");
    AssertContains(verifier, "Release update artifacts verification passed:");

    var publishScript = File.ReadAllText(publishScriptPath);
    AssertContains(publishScript, "build-release-update-artifacts.ps1");
    AssertContains(publishScript, "verify-release-update-artifacts.ps1");

    var installerSmokeScript = File.ReadAllText(installerSmokeScriptPath);
    AssertContains(installerSmokeScript, "--health");
    AssertContains(installerSmokeScript, "--update-preflight");
    AssertContains(installerSmokeScript, "PlayerAssistant/RPOL/UserName");
    AssertContains(installerSmokeScript, "settings.local.json");
    AssertContains(installerSmokeScript, "p-assist-updates.json");
}

static void PublishedHealthVerificationAcceptsCurrentOutput()
{
    var output = RunPublishedHealthVerification(GetCurrentPublishDirectory());

    AssertEqual(0, output.ExitCode, $"published health verification should pass. Output: {output.Output}");
    AssertContains(output.Output, "Published health verification passed.");
    AssertContains(output.Output, "Status:");
}

static void SecretScanAcceptsCurrentRepository()
{
    var output = RunSecretScan(GetRepositoryRoot(), includeHistory: true);

    AssertEqual(0, output.ExitCode, $"secret scan should pass. Output: {output.Output}");
    AssertContains(output.Output, "Secret scan passed.");
}

static void SecretScanRejectsTrackedEnvSecret()
{
    var scratchRoot = Path.Combine(GetRepositoryRoot(), "codex-scratch");
    var scratchPath = Path.Combine(scratchRoot, $"secret-scan-test-{Guid.NewGuid():N}");

    try
    {
        Directory.CreateDirectory(scratchPath);
        RunGit(scratchPath, "init");
        RunGit(scratchPath, "config", "user.email", "test@example.invalid");
        RunGit(scratchPath, "config", "user.name", "Player Assistant Test");
        File.WriteAllText(
            Path.Combine(scratchPath, ".env"),
            "OPENAI" + "_API_KEY=sk-" + "proj-synthetic-test-secret-1234567890");
        RunGit(scratchPath, "add", ".env");
        RunGit(scratchPath, "commit", "-m", "add synthetic secret");

        var output = RunSecretScan(scratchPath, includeHistory: true);

        AssertFalse(output.ExitCode == 0, "secret scan should fail for a tracked env secret");
        AssertContains(output.Output, "Secret scan findings:");
        AssertContains(output.Output, "Forbidden tracked path");
        AssertContains(output.Output, "OpenAI API key");
    }
    finally
    {
        if (Directory.Exists(scratchPath))
        {
            DeleteDirectoryTree(scratchPath);
        }
    }
}

static void ReleasePublishParityAcceptsCurrentOutput()
{
    var output = RunReleasePublishParity(GetCurrentReleaseDirectory(), GetCurrentPublishDirectory());

    AssertEqual(0, output.ExitCode, $"release/publish parity should pass. Output: {output.Output}");
    AssertContains(output.Output, "Release/publish parity verification passed.");
}

static void ReleasePublishParityRejectsMismatchedSidecar()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        File.AppendAllText(Path.Combine(directoryPath, KeywordTermsFileUtility.FileName), "\nsynthetic-parity-drift\n");

        var output = RunReleasePublishParity(GetCurrentReleaseDirectory(), directoryPath);

        AssertFalse(output.ExitCode == 0, "release/publish parity should fail when a published sidecar drifts");
        AssertContains(output.Output, "game-posts-key-terms.md SHA256 differs");
    });
}

static void DiagnosticBundleRedactsSensitiveValues()
{
    WithTemporaryDiagnosticsRuntime((rootPath, releasePath, publishPath, outputPath) =>
    {
        var output = RunDiagnosticsCollection(
            releasePath,
            publishPath,
            outputPath,
            "-NoPublishVerification",
            "-NoPlanOutputs");

        AssertEqual(0, output.ExitCode, $"diagnostic collection should pass. Output: {output.Output}");
        AssertContains(output.Output, "Diagnostic bundle created:");

        var zipPath = GetDiagnosticZipPathFromOutput(output.Output);
        AssertTrue(File.Exists(zipPath), "diagnostic bundle zip should exist");

        var entries = GetZipEntryNames(zipPath);
        var expectedEntries = new[]
        {
            "metadata.json",
            "version-metadata.json",
            "runtime-sidecars.json",
            "Release/outbound-network-diagnostics.json",
            "Release/release-provenance.json",
            "Release/release-runtime-inventory.json",
            "Release/settings.redacted.json",
            "Release/startup-errors.log",
            "Release/startup-health.json",
            "Release/last-crash.json",
            "Release/startup-remediation.txt",
            "publish/outbound-network-diagnostics.json",
            "publish/release-provenance.json",
            "publish/release-runtime-inventory.json",
            "publish/settings.redacted.json",
            "publish/startup-errors.log",
            "publish/startup-health.json",
            "publish/last-crash.json",
            "publish/startup-remediation.txt"
        };

        AssertEqual(expectedEntries.Length, entries.Length, "diagnostic bundle should contain only the expected low-impact files");
        foreach (var expectedEntry in expectedEntries)
        {
            AssertTrue(entries.Contains(expectedEntry, StringComparer.Ordinal), $"diagnostic bundle is missing {expectedEntry}");
        }

        var releaseSettings = ReadZipEntryText(zipPath, "Release/settings.redacted.json");
        AssertContains(releaseSettings, "\"RPOL user name\":  \"[REDACTED]\"");
        AssertContains(releaseSettings, "\"RPOL password\":  \"[REDACTED]\"");
        AssertFalse(releaseSettings.Contains("example-user", StringComparison.Ordinal), "diagnostic settings should not contain RPOL user name");
        AssertFalse(releaseSettings.Contains("example-password", StringComparison.Ordinal), "diagnostic settings should not contain RPOL password");

        var releaseLog = ReadZipEntryText(zipPath, "Release/startup-errors.log");
        AssertFalse(releaseLog.Contains("secret-password", StringComparison.Ordinal), "diagnostic log should redact password query values");
        AssertFalse(releaseLog.Contains("secret-token", StringComparison.Ordinal), "diagnostic log should redact token query values");
        AssertFalse(releaseLog.Contains("Bearer abc123", StringComparison.Ordinal), "diagnostic log should redact bearer tokens");
        AssertFalse(releaseLog.Contains("sessionid=abc123", StringComparison.Ordinal), "diagnostic log should redact cookie headers");
        AssertFalse(releaseLog.Contains("user:pass@", StringComparison.Ordinal), "diagnostic log should redact credentialed URLs");

        var outboundDiagnostics = ReadZipEntryText(zipPath, "Release/outbound-network-diagnostics.json");
        using (var outboundDiagnosticsDocument = System.Text.Json.JsonDocument.Parse(outboundDiagnostics))
        {
            var endpointsElement = outboundDiagnosticsDocument.RootElement.GetProperty("endpoints");
            var endpointElement = endpointsElement.ValueKind == System.Text.Json.JsonValueKind.Array
                ? endpointsElement[0]
                : endpointsElement;
            var purpose = endpointElement.GetProperty("purpose").GetString();
            AssertTrue(
                string.Equals("PlayerAssistantHostedSettings", purpose, StringComparison.Ordinal),
                "outbound diagnostics should preserve endpoint purpose metadata");
        }
        AssertFalse(outboundDiagnostics.Contains("secret-password", StringComparison.Ordinal), "outbound diagnostics should redact password query values");
        AssertFalse(outboundDiagnostics.Contains("secret-token", StringComparison.Ordinal), "outbound diagnostics should redact token query values");
        AssertFalse(outboundDiagnostics.Contains("Bearer abc123", StringComparison.Ordinal), "outbound diagnostics should redact bearer tokens");
        AssertFalse(outboundDiagnostics.Contains("sessionid=abc123", StringComparison.Ordinal), "outbound diagnostics should redact cookie headers");
        AssertFalse(outboundDiagnostics.Contains("user:pass@", StringComparison.Ordinal), "outbound diagnostics should redact credentialed URLs");

        var versionMetadata = ReadZipEntryText(zipPath, "version-metadata.json");
        AssertContains(versionMetadata, "\"authenticode_signature\":");
        AssertContains(versionMetadata, "\"status\":");

        var verifyOutput = RunDiagnosticsVerification(outputPath, zipPath);
        AssertEqual(0, verifyOutput.ExitCode, $"diagnostic verify-only should pass. Output: {verifyOutput.Output}");
        AssertContains(verifyOutput.Output, "Diagnostic bundle verification passed:");
    });
}

static void DiagnosticBundleVerifyOnlyRejectsForbiddenAuthState()
{
    WithTemporaryDiagnosticsRuntime((rootPath, releasePath, publishPath, outputPath) =>
    {
        Directory.CreateDirectory(outputPath);
        var zipPath = Path.Combine(outputPath, "malicious-diagnostics.zip");
        var forbiddenSourcePath = Path.Combine(rootPath, "rpol-storage-state.json");
        File.WriteAllText(forbiddenSourcePath, """{"cookies":[{"name":"secret"}]}""");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(forbiddenSourcePath, "rpol-storage-state.json");
        }

        var output = RunDiagnosticsVerification(outputPath, zipPath);

        AssertFalse(output.ExitCode == 0, "diagnostic verify-only should fail for forbidden auth state files");
        AssertContains(output.Output, "forbidden sensitive file");
    });
}

static void DiagnosticRetentionCleanupRemovesOldDiagnosticsAndPreservesUnrelatedScratchFiles()
{
    var scratchPath = Path.Combine(
        GetRepositoryRoot(),
        "codex-scratch",
        $"retention-test-{Guid.NewGuid():N}");
    var diagnosticsPath = Path.Combine(scratchPath, "diagnostics");
    var oldZipPath = Path.Combine(diagnosticsPath, "player-assistant-diagnostics-20260601-010101.zip");
    var newZipPath = Path.Combine(diagnosticsPath, "player-assistant-diagnostics-20260702-010101.zip");
    var oldStagingPath = Path.Combine(diagnosticsPath, "player-assistant-diagnostics-20260601-010101");
    var oldDiagnosticsTestPath = Path.Combine(scratchPath, "diagnostics-test-old");
    var oldPublishVerificationPath = Path.Combine(scratchPath, "publish-verification-old");
    var unrelatedScratchFilePath = Path.Combine(scratchPath, "candidates.txt");
    var unrelatedScratchDirectoryPath = Path.Combine(scratchPath, "manual-notes");

    try
    {
        Directory.CreateDirectory(diagnosticsPath);
        File.WriteAllText(oldZipPath, "old diagnostic zip");
        File.WriteAllText(newZipPath, "new diagnostic zip");
        Directory.CreateDirectory(oldStagingPath);
        File.WriteAllText(Path.Combine(oldStagingPath, "marker.txt"), "old staging");
        Directory.CreateDirectory(oldDiagnosticsTestPath);
        File.WriteAllText(Path.Combine(oldDiagnosticsTestPath, "marker.txt"), "old diagnostics test");
        Directory.CreateDirectory(oldPublishVerificationPath);
        File.WriteAllText(Path.Combine(oldPublishVerificationPath, "marker.txt"), "old publish verification");
        File.WriteAllText(unrelatedScratchFilePath, "lexicon backlog should stay");
        Directory.CreateDirectory(unrelatedScratchDirectoryPath);
        File.WriteAllText(Path.Combine(unrelatedScratchDirectoryPath, "marker.txt"), "manual scratch should stay");

        var oldTime = DateTimeOffset.UtcNow - TimeSpan.FromDays(30);
        SetLastWriteTimeUtc(oldZipPath, oldTime);
        SetDirectoryLastWriteTimeUtc(oldStagingPath, oldTime);
        SetDirectoryLastWriteTimeUtc(oldDiagnosticsTestPath, oldTime);
        SetDirectoryLastWriteTimeUtc(oldPublishVerificationPath, oldTime);
        SetLastWriteTimeUtc(unrelatedScratchFilePath, oldTime);
        SetDirectoryLastWriteTimeUtc(unrelatedScratchDirectoryPath, oldTime);

        var output = RunDiagnosticsRetentionCleanup(
            scratchPath,
            "-DiagnosticRetentionDays",
            "14",
            "-ScratchRetentionDays",
            "7",
            "-MaxDiagnosticZipCount",
            "10");

        AssertEqual(0, output.ExitCode, $"diagnostic retention cleanup should pass. Output: {output.Output}");
        AssertContains(output.Output, "Diagnostic retention cleanup removed");
        AssertFalse(File.Exists(oldZipPath), "old diagnostic zip should be removed");
        AssertTrue(File.Exists(newZipPath), "fresh diagnostic zip should be preserved");
        AssertFalse(Directory.Exists(oldStagingPath), "old diagnostic staging directory should be removed");
        AssertFalse(Directory.Exists(oldDiagnosticsTestPath), "old diagnostics test scratch directory should be removed");
        AssertFalse(Directory.Exists(oldPublishVerificationPath), "old publish verification scratch directory should be removed");
        AssertTrue(File.Exists(unrelatedScratchFilePath), "unrelated scratch file should be preserved");
        AssertTrue(Directory.Exists(unrelatedScratchDirectoryPath), "unrelated scratch directory should be preserved");
    }
    finally
    {
        if (Directory.Exists(scratchPath))
        {
            Directory.Delete(scratchPath, recursive: true);
        }
    }
}

static void WithCopiedPublishDirectory(Action<string> action)
{
    var directoryPath = Path.Combine(
        GetRepositoryRoot(),
        "codex-scratch",
        $"publish-verification-{Guid.NewGuid():N}");

    try
    {
        var currentPublishDirectory = Path.Combine(GetCurrentReleaseDirectory(), "publish");
        var currentPublishExecutablePath = Path.Combine(currentPublishDirectory, "player-assistant.exe");
        if (File.Exists(currentPublishExecutablePath))
        {
            CopyDirectory(currentPublishDirectory, directoryPath);
            ClearReadOnlyAttributes(directoryPath);
        }
        else
        {
            WriteManifestedRuntime(directoryPath);
            var currentReleaseDirectory = GetCurrentReleaseDirectory();
            foreach (var fileName in new[]
            {
                "player-assistant.exe",
                "player-assistant.dll",
                "player-assistant.deps.json",
                "player-assistant.runtimeconfig.json",
                "playwright.ps1",
                "Microsoft.Playwright.dll"
            })
            {
                var sourcePath = Path.Combine(currentReleaseDirectory, fileName);
                if (File.Exists(sourcePath))
                {
                    File.Copy(sourcePath, Path.Combine(directoryPath, fileName), overwrite: true);
                }
            }
        }
        File.Copy(
            Path.Combine(GetRepositoryRoot(), "settings.json"),
            Path.Combine(directoryPath, "settings.json"),
            overwrite: true);
        WriteRequiredRuntimeSidecars(directoryPath);
        var shippedLocalSettingsPath = Path.Combine(directoryPath, "settings.local.json");
        LocalSettingsUtility.SaveEncryptedSettings(
            shippedLocalSettingsPath,
            new Dictionary<string, string>
            {
                ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
                ["RPOL user name"] = "example-user",
                ["RPOL password"] = "example-password"
            });
        WriteReleaseRuntimeInventory(directoryPath);
        WriteReleaseManifest(directoryPath);
        WriteReleaseProvenance(directoryPath);
        action(directoryPath);
    }
    finally
    {
        if (Directory.Exists(directoryPath))
        {
            ClearReadOnlyAttributes(directoryPath);
            Directory.Delete(directoryPath, recursive: true);
        }
    }
}

static void ClearReadOnlyAttributes(string directoryPath)
{
    if (!Directory.Exists(directoryPath))
    {
        return;
    }

    foreach (var path in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
    {
        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
    }
}

static void SetRuntimeSidecarsReadOnly(string directoryPath)
{
    foreach (var fileName in new[] { XpPasswordStoreUtility.FileName })
    {
        var path = Path.Combine(directoryPath, fileName);
        if (File.Exists(path))
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }
    }
}

static void WithTemporaryDiagnosticsRuntime(Action<string, string, string, string> action)
{
    var rootPath = Path.Combine(
        GetRepositoryRoot(),
        "codex-scratch",
        $"diagnostics-test-{Guid.NewGuid():N}");
    var releasePath = Path.Combine(rootPath, "Release");
    var publishPath = Path.Combine(releasePath, "publish");
    var outputPath = Path.Combine(rootPath, "out");

    try
    {
        WriteDiagnosticsRuntime(releasePath, includeSensitiveLog: true);
        WriteDiagnosticsRuntime(publishPath, includeSensitiveLog: false);
        action(rootPath, releasePath, publishPath, outputPath);
    }
    finally
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }
}

static void WriteDiagnosticsRuntime(string directoryPath, bool includeSensitiveLog)
{
    Directory.CreateDirectory(directoryPath);
    File.WriteAllText(Path.Combine(directoryPath, "player-assistant.exe"), "fake executable");
    WriteSettingsJson(directoryPath, CreateValidAppSettings(includeCredentials: true));
    File.WriteAllText(
        Path.Combine(directoryPath, "startup-health.json"),
        """
        {
          "schema_version": 1,
          "phases": []
        }
        """);
    File.WriteAllText(
        Path.Combine(directoryPath, OutboundNetworkDiagnosticsUtility.DiagnosticsFileName),
        """
        {
          "schema_version": 1,
          "started_at": "2026-07-05T00:00:00.0000000+00:00",
          "updated_at": "2026-07-05T00:00:01.0000000+00:00",
          "endpoints": [
            {
              "method": "GET",
              "purpose": "PlayerAssistantHostedSettings",
              "scheme": "https",
              "host": "bryanmiller.us",
              "path": "/scarlethorizons/settings.local.json",
              "query_present": true,
              "total_requests": 2,
              "success_count": 1,
              "failure_count": 1,
              "retry_count": 1,
              "last_outcome": "failure",
              "last_status_code": null,
              "last_failure_kind": "Unavailable",
              "last_failure_summary": "The network request failed: https://user:pass@example.test/path?password=secret-password&token=secret-token Authorization: Bearer abc123 Cookie: sessionid=abc123",
              "first_observed_at": "2026-07-05T00:00:00.0000000+00:00",
              "last_observed_at": "2026-07-05T00:00:01.0000000+00:00"
            }
          ]
        }
        """);
    File.WriteAllText(
        Path.Combine(directoryPath, "last-crash.json"),
        """
        {
          "schema_version": 1,
          "phase": "synthetic crash",
          "exception": {
            "type": "InvalidOperationException",
            "message": "synthetic crash"
          }
        }
        """);
    File.WriteAllText(
        Path.Combine(directoryPath, "startup-remediation.txt"),
        """
        Player Assistant startup configuration guidance

        1. Warning: synthetic warning
           Repair: synthetic repair
        """);
    File.WriteAllText(
        Path.Combine(directoryPath, "startup-errors.log"),
        includeSensitiveLog
            ? """
              RPOL password: hunter2
              RPOL user name: example-user
              Authorization: Bearer abc123
              Cookie: sessionid=abc123
              url=https://user:pass@example.test/path?password=secret-password&token=secret-token
              """
            : "no startup errors");
    File.WriteAllText(
        Path.Combine(directoryPath, "keyword-index.json"),
        """
        {
          "words": {
            "scarlet": []
          },
          "index_metadata": {}
        }
        """);
    File.WriteAllText(Path.Combine(directoryPath, KeywordTermsFileUtility.FileName), "scarlet");
    File.WriteAllText(Path.Combine(directoryPath, "sitemap.xml"), "<urlset />");
    File.WriteAllText(Path.Combine(directoryPath, "sitemap-keyword-urls.json"), "{}");
    File.WriteAllText(Path.Combine(directoryPath, "game-forum-chapter-prefixes.txt"), "chapter");
    File.WriteAllText(Path.Combine(directoryPath, "game-forum-chapter-downloads.txt"), "chapter");
    File.WriteAllText(Path.Combine(directoryPath, "game-forum-aside-downloads.txt"), "aside");
    File.WriteAllText(Path.Combine(directoryPath, "game-forum-ooc-downloads.txt"), "ooc");
}

static (int ExitCode, string Output) RunDiagnosticsCollection(
    string releaseDirectory,
    string publishDirectory,
    string outputDirectory,
    params string[] extraArguments)
{
    var arguments = new List<string>
    {
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        Path.Combine(GetRepositoryRoot(), "collect-diagnostics.ps1"),
        "-ReleaseDir",
        releaseDirectory,
        "-PublishDir",
        publishDirectory,
        "-OutputDir",
        outputDirectory
    };
    arguments.AddRange(extraArguments);
    return RunPowerShell(arguments, TimeSpan.FromSeconds(45));
}

static (int ExitCode, string Output) RunDiagnosticsVerification(string outputDirectory, string zipPath)
{
    return RunPowerShell(
        [
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(GetRepositoryRoot(), "collect-diagnostics.ps1"),
            "-OutputDir",
            outputDirectory,
            "-VerifyOnly",
            zipPath
        ],
        TimeSpan.FromSeconds(30));
}

static (int ExitCode, string Output) RunDiagnosticsRetentionCleanup(string scratchDirectory, params string[] extraArguments)
{
    var arguments = new List<string>
    {
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        Path.Combine(GetRepositoryRoot(), "clean-diagnostics-retention.ps1"),
        "-ScratchDir",
        scratchDirectory
    };
    arguments.AddRange(extraArguments);
    return RunPowerShell(arguments, TimeSpan.FromSeconds(30));
}

static string GetDiagnosticZipPathFromOutput(string output)
{
    var line = output
        .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
        .LastOrDefault(line => line.StartsWith("Diagnostic bundle created:", StringComparison.Ordinal));
    if (line is null)
    {
        throw new InvalidOperationException($"Unable to find diagnostic bundle path in output: {output}");
    }

    return line["Diagnostic bundle created:".Length..].Trim();
}

static string[] GetZipEntryNames(string zipPath)
{
    using var archive = ZipFile.OpenRead(zipPath);
    return archive.Entries
        .Select(entry => entry.FullName.Replace('\\', '/'))
        .OrderBy(entry => entry, StringComparer.Ordinal)
        .ToArray();
}

static string ReadZipEntryText(string zipPath, string entryName)
{
    using var archive = ZipFile.OpenRead(zipPath);
    var entry = archive.Entries.SingleOrDefault(entry =>
        string.Equals(entry.FullName.Replace('\\', '/'), entryName, StringComparison.Ordinal));
    if (entry is null)
    {
        throw new InvalidOperationException($"Zip entry '{entryName}' was not found in {zipPath}.");
    }

    using var stream = entry.Open();
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static string GetCurrentPublishDirectory()
{
    var publishDirectory = Path.Combine(GetRepositoryRoot(), "Release", "publish");
    if (!Directory.Exists(publishDirectory))
    {
        throw new InvalidOperationException($"Publish directory is missing: {publishDirectory}");
    }

    return publishDirectory;
}

static string GetCurrentReleaseDirectory()
{
    var releaseDirectory = Path.Combine(GetRepositoryRoot(), "Release");
    if (!Directory.Exists(releaseDirectory))
    {
        throw new InvalidOperationException($"Release directory is missing: {releaseDirectory}");
    }

    return releaseDirectory;
}

static (int ExitCode, string Output) RunReleasePublishParity(string releaseDirectory, string publishDirectory)
{
    return RunPowerShell(
        [
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(GetRepositoryRoot(), "verify-release-publish-parity.ps1"),
            "-ReleaseDir",
            releaseDirectory,
            "-PublishDir",
            publishDirectory
        ],
        TimeSpan.FromSeconds(30));
}

static (int ExitCode, string Output) RunPublishedHealthVerification(string publishDirectory)
{
    return RunPowerShell(
        [
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(GetRepositoryRoot(), "verify-published-health.ps1"),
            "-PublishDir",
            publishDirectory
        ],
        TimeSpan.FromSeconds(30));
}

static (int ExitCode, string Output) RunSecretScan(string repoRoot, bool includeHistory)
{
    var arguments = new List<string>
    {
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        Path.Combine(GetRepositoryRoot(), "verify-secret-scan.ps1"),
        "-RepoRoot",
        repoRoot
    };

    if (includeHistory)
    {
        arguments.Add("-IncludeHistory");
    }

    return RunPowerShell(arguments, TimeSpan.FromSeconds(60));
}

static (int ExitCode, string Output) RunPublishVerification(string outputDirectory, params string[] extraArguments)
{
    var repoRoot = GetRepositoryRoot();
    var scriptPath = Path.Combine(repoRoot, "publish-player-assistant.ps1");
    if (!File.Exists(scriptPath))
    {
        throw new InvalidOperationException($"Publish script is missing: {scriptPath}");
    }

    SetRuntimeSidecarsReadOnly(outputDirectory);

    var arguments = new List<string>
    {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath,
            "-VerifyOnly",
            "-OutputDir",
            outputDirectory
    };
    arguments.AddRange(extraArguments);

    return RunPowerShell(arguments, TimeSpan.FromSeconds(30));
}

static (int ExitCode, string Output) RunToOrcish(params string[] arguments)
{
    var repoRoot = GetRepositoryRoot();
    var executablePath = Path.Combine(repoRoot, "Release", "to-orcish.exe");
    if (!File.Exists(executablePath))
    {
        throw new InvalidOperationException($"to-orcish executable is missing: {executablePath}");
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = executablePath,
        WorkingDirectory = repoRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Unable to start to-orcish process.");

    var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
    if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
    {
        process.Kill(entireProcessTree: true);
        throw new TimeoutException("to-orcish process did not exit within 30 seconds.");
    }

    return (process.ExitCode, output);
}

static (int ExitCode, string Output) RunGit(string workingDirectory, params string[] arguments)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "git",
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Unable to start git process.");

    var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
    if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
    {
        process.Kill(entireProcessTree: true);
        throw new TimeoutException("Git process did not exit within 30 seconds.");
    }

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed. Output: {output}");
    }

    return (process.ExitCode, output);
}

static (int ExitCode, string Output) RunPowerShell(IEnumerable<string> arguments, TimeSpan timeout)
{
    var powerShellExecutable = ResolvePowerShellExecutable();
    var startInfo = new ProcessStartInfo
    {
        FileName = powerShellExecutable,
        WorkingDirectory = GetRepositoryRoot(),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Unable to start PowerShell process.");

    var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
    if (!process.WaitForExit(timeout))
    {
        process.Kill(entireProcessTree: true);
        throw new TimeoutException($"PowerShell process did not exit within {timeout.TotalSeconds:0.#} seconds.");
    }

    return (process.ExitCode, output);
}

static string ResolvePowerShellExecutable()
{
    var candidates = new[]
    {
        Environment.ProcessPath,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            "pwsh.exe")
    };

    foreach (var candidate in candidates)
    {
        if (IsPowerShellExecutablePath(candidate))
        {
            return candidate;
        }
    }

    foreach (var commandName in new[] { "pwsh.exe", "powershell.exe", "powershell" })
    {
        string? resolved = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => Path.Combine(path.Trim(), commandName))
            .FirstOrDefault(IsPowerShellExecutablePath);
        if (resolved is not null)
        {
            return resolved;
        }
    }

    throw new InvalidOperationException("Unable to locate a PowerShell executable.");
}

static bool IsPowerShellExecutablePath([NotNullWhen(true)] string? path)
{
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
        return false;
    }

    var fileName = Path.GetFileName(path);
    return string.Equals(fileName, "pwsh.exe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "powershell.exe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "powershell", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "pwsh", StringComparison.OrdinalIgnoreCase);
}

static string GetRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "publish-player-assistant.ps1")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Unable to locate repository root.");
}

static void CopyDirectory(string sourceDirectory, string destinationDirectory)
{
    Directory.CreateDirectory(destinationDirectory);

    foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
    }

    foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        var destinationPath = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationDirectory);
        File.Copy(file, destinationPath, overwrite: true);
    }
}

static void DeleteDirectoryTree(string directoryPath)
{
    if (!Directory.Exists(directoryPath))
    {
        return;
    }

    foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
    {
        File.SetAttributes(filePath, FileAttributes.Normal);
    }

    foreach (var childDirectoryPath in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories))
    {
        File.SetAttributes(childDirectoryPath, FileAttributes.Directory);
    }

    Directory.Delete(directoryPath, recursive: true);
}

static void AssertTrue(bool actual, string message)
{
    if (!actual)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertFalse(bool actual, string message)
{
    if (actual)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertContains(string value, string expected)
{
    if (!value.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{value}' to contain '{expected}'.");
    }
}

static void AssertEqual<T>(T expected, T actual, string message) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}. Expected '{expected}' but was '{actual}'.");
    }
}

static TException AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException ex)
    {
        return ex;
    }
    catch (TargetInvocationException ex) when (ex.InnerException is TException innerException)
    {
        return innerException;
    }

    throw new InvalidOperationException($"Expected exception of type {typeof(TException).Name}.");
}

static int CountOccurrences(string value, string pattern)
{
    var count = 0;
    var index = 0;
    while (true)
    {
        index = value.IndexOf(pattern, index, StringComparison.Ordinal);
        if (index < 0)
        {
            return count;
        }

        count++;
        index += pattern.Length;
    }
}

static void WaitForCondition(Func<bool> condition, string message)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (condition())
        {
            return;
        }

        Thread.Sleep(10);
    }

    throw new InvalidOperationException(message);
}

static void ResetRpolAuthFailureCache()
{
    SetStaticField(typeof(RpolAuthUtility), "_cachedFatalAuthFailure", null);
    SetStaticField(typeof(RpolAuthUtility), "_cachedFatalAuthFailureLogged", false);
}

static void RunOnStaThread(Action action)
{
    Exception? capturedException = null;
    var thread = new Thread(() =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            capturedException = ex;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (capturedException is not null)
    {
        throw capturedException;
    }
}

static T GetControl<T>(Form form, string fieldName) where T : Control
{
    var field = typeof(Form1).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
    if (field?.GetValue(form) is T control)
    {
        return control;
    }

    throw new InvalidOperationException($"Unable to find control field '{fieldName}'.");
}

static Task InvokePrivateAsync(object instance, string methodName, params object[] args)
{
    var method = instance.GetType()
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .SingleOrDefault(method =>
            string.Equals(method.Name, methodName, StringComparison.Ordinal)
            && method.GetParameters().Length == args.Length);
    if (method is null)
    {
        throw new InvalidOperationException($"Unable to find method '{methodName}'.");
    }

    if (method.Invoke(instance, args) is Task task)
    {
        return task;
    }

    throw new InvalidOperationException($"Method '{methodName}' did not return a Task.");
}

static object? InvokeStaticMethod(Type type, string methodName, params object[] args)
{
    var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    if (method is null)
    {
        throw new InvalidOperationException($"Unable to find static method '{methodName}'.");
    }

    return method.Invoke(null, args);
}

static object? InvokePrivateMethod(object instance, string methodName, params object[] args)
{
    var method = instance.GetType()
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .SingleOrDefault(method =>
            string.Equals(method.Name, methodName, StringComparison.Ordinal)
            && method.GetParameters().Length == args.Length);
    if (method is null)
    {
        throw new InvalidOperationException($"Unable to find method '{methodName}'.");
    }

    return method.Invoke(instance, args);
}

static void SetPrivateField(object instance, string fieldName, object? value)
{
    var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
    if (field is null)
    {
        throw new InvalidOperationException($"Unable to find field '{fieldName}'.");
    }

    field.SetValue(instance, value);
}

static object? GetPrivateField(object instance, string fieldName)
{
    var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
    if (field is null)
    {
        throw new InvalidOperationException($"Unable to find field '{fieldName}'.");
    }

    return field.GetValue(instance);
}

static void SetStaticField(Type type, string fieldName, object? value)
{
    var field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
    if (field is null)
    {
        throw new InvalidOperationException($"Unable to find static field '{fieldName}'.");
    }

    field.SetValue(null, value);
}

static void WithTemporaryKeywordIndex(string json, Action action)
{
    var indexPath = GetPlayerAssistantIndexPath();
    var backupPath = indexPath + ".test-backup";
    var hadOriginalIndex = File.Exists(indexPath);

    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        if (hadOriginalIndex)
        {
            File.Copy(indexPath, backupPath, overwrite: true);
        }

        File.WriteAllText(indexPath, json);
        action();
    }
    finally
    {
        if (File.Exists(indexPath))
        {
            File.Delete(indexPath);
        }

        if (hadOriginalIndex)
        {
            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException($"Expected backup file '{backupPath}' to exist for restore.", backupPath);
            }

            File.Move(backupPath, indexPath, overwrite: true);
        }
    }
}

static void WithTemporaryKeywordTermsFile(Action action)
{
    var termsPath = GetPlayerAssistantKeywordTermsPath();
    var backupPath = termsPath + ".test-backup";
    var hadOriginalTerms = File.Exists(termsPath);

    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(termsPath)!);
        if (hadOriginalTerms)
        {
            File.Copy(termsPath, backupPath, overwrite: true);
            File.Delete(termsPath);
        }

        action();
    }
    finally
    {
        if (File.Exists(termsPath))
        {
            File.Delete(termsPath);
        }

        if (hadOriginalTerms)
        {
            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException($"Expected backup file '{backupPath}' to exist for restore.", backupPath);
            }

            File.Move(backupPath, termsPath, overwrite: true);
        }
    }
}

static void WithTemporaryEncryptedTextIndex(string json, Action action)
{
    var indexPath = GetPlayerAssistantEncryptedTextIndexPath();
    var backupPath = indexPath + ".test-backup";
    var hadOriginalIndex = File.Exists(indexPath);

    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        if (hadOriginalIndex)
        {
            File.Copy(indexPath, backupPath, overwrite: true);
        }

        File.WriteAllText(indexPath, json);
        action();
    }
    finally
    {
        if (File.Exists(indexPath))
        {
            File.Delete(indexPath);
        }

        if (hadOriginalIndex)
        {
            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException($"Expected backup file '{backupPath}' to exist for restore.", backupPath);
            }

            File.Move(backupPath, indexPath, overwrite: true);
        }
    }
}

static string GetPlayerAssistantIndexPath()
{
    var assemblyDirectory = Path.GetDirectoryName(typeof(Form1).Assembly.Location);
    if (string.IsNullOrWhiteSpace(assemblyDirectory))
    {
        throw new InvalidOperationException("Unable to resolve the player-assistant assembly directory.");
    }

    return Path.Combine(assemblyDirectory, "keyword-index.json");
}

static string GetPlayerAssistantEncryptedTextIndexPath()
{
    var assemblyDirectory = Path.GetDirectoryName(typeof(Form1).Assembly.Location);
    if (string.IsNullOrWhiteSpace(assemblyDirectory))
    {
        throw new InvalidOperationException("Unable to resolve the player-assistant assembly directory.");
    }

    return Path.Combine(assemblyDirectory, TaggedNoteCipherUtility.EncryptedTextIndexFileName);
}

static string GetPlayerAssistantKeywordTermsPath()
{
    var assemblyDirectory = Path.GetDirectoryName(typeof(Form1).Assembly.Location);
    if (string.IsNullOrWhiteSpace(assemblyDirectory))
    {
        throw new InvalidOperationException("Unable to resolve the player-assistant assembly directory.");
    }

    return Path.Combine(assemblyDirectory, KeywordTermsFileUtility.FileName);
}

static string GetStartupLogPath()
{
    return Path.Combine(AppContext.BaseDirectory, StartupLoggingUtility.LogFileName);
}

static string GetStartupHealthPath()
{
    return Path.Combine(AppContext.BaseDirectory, StartupHealthUtility.HealthFileName);
}

static string GetLastCrashPath()
{
    return Path.Combine(AppContext.BaseDirectory, LastCrashDiagnosticUtility.FileName);
}

static void WithPreservedStartupLog(Action action)
{
    var startupLogPath = GetStartupLogPath();
    var hadStartupLog = File.Exists(startupLogPath);
    var originalStartupLog = hadStartupLog ? File.ReadAllText(startupLogPath) : null;

    try
    {
        if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }

        action();
    }
    finally
    {
        if (hadStartupLog)
        {
            File.WriteAllText(startupLogPath, originalStartupLog);
        }
        else if (File.Exists(startupLogPath))
        {
            File.Delete(startupLogPath);
        }
    }
}

static void WithPreservedFileAbsent(string filePath, Action action)
{
    var hadFile = File.Exists(filePath);
    var originalContents = hadFile ? File.ReadAllBytes(filePath) : null;

    try
    {
        if (hadFile)
        {
            File.Delete(filePath);
        }

        action();
    }
    finally
    {
        if (hadFile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllBytes(filePath, originalContents!);
        }
    }
}

static void WithHostedSettingsIsolation(Action action)
{
    WithPreservedFileAbsent(
        RuntimePathUtility.GetApplicationPath("settings.local.json"),
        () => WithPreservedFileAbsent(
            RuntimePathUtility.GetUserDataPath("settings.local.json"),
            () => WithPreservedFileAbsent(
                RuntimePathUtility.GetUserDataPath("trusted-hosted-settings-state.json"),
                action)));
}

static void WithPreservedStartupHealth(Action action)
{
    var startupHealthPath = GetStartupHealthPath();
    var hadStartupHealth = File.Exists(startupHealthPath);
    var originalStartupHealth = hadStartupHealth ? File.ReadAllText(startupHealthPath) : null;

    try
    {
        if (File.Exists(startupHealthPath))
        {
            File.Delete(startupHealthPath);
        }

        action();
    }
    finally
    {
        if (hadStartupHealth)
        {
            File.WriteAllText(startupHealthPath, originalStartupHealth);
        }
        else if (File.Exists(startupHealthPath))
        {
            File.Delete(startupHealthPath);
        }
    }
}

static void WithPreservedLastCrash(Action action)
{
    var lastCrashPath = GetLastCrashPath();
    var hadLastCrash = File.Exists(lastCrashPath);
    var originalLastCrash = hadLastCrash ? File.ReadAllText(lastCrashPath) : null;

    try
    {
        if (File.Exists(lastCrashPath))
        {
            File.Delete(lastCrashPath);
        }

        action();
    }
    finally
    {
        if (hadLastCrash)
        {
            File.WriteAllText(lastCrashPath, originalLastCrash);
        }
        else if (File.Exists(lastCrashPath))
        {
            File.Delete(lastCrashPath);
        }
    }
}

static System.Text.Json.JsonDocument LoadStartupHealthDocument()
{
    return System.Text.Json.JsonDocument.Parse(File.ReadAllText(GetStartupHealthPath()));
}

static System.Text.Json.JsonElement FindStartupHealthPhase(
    System.Text.Json.JsonDocument document,
    string phaseName)
{
    foreach (var phase in document.RootElement.GetProperty("phases").EnumerateArray())
    {
        if (string.Equals(phase.GetProperty("phase").GetString(), phaseName, StringComparison.Ordinal))
        {
            return phase;
        }
    }

    throw new InvalidOperationException($"Startup health phase '{phaseName}' was not found.");
}

static void AssertJsonString(
    System.Text.Json.JsonElement element,
    string propertyName,
    string expected,
    string message)
{
    AssertEqual(expected, element.GetProperty(propertyName).GetString() ?? string.Empty, message);
}

static void AssertJsonNumber(
    System.Text.Json.JsonElement element,
    string propertyName,
    long expected,
    string message)
{
    AssertEqual(expected, element.GetProperty(propertyName).GetInt64(), message);
}

static void AssertJsonNumberAtLeast(
    System.Text.Json.JsonElement element,
    string propertyName,
    long minimum,
    string message)
{
    var actual = element.GetProperty(propertyName).GetInt64();
    if (actual < minimum)
    {
        throw new InvalidOperationException($"{message}. Expected at least '{minimum}' but was '{actual}'.");
    }
}

static Dictionary<string, string> CreateValidAppSettings(bool includeCredentials)
{
    var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RPOL Site"] = "https://rpol.net/game.php?gi=80170",
        ["Game Intro"] = "https://rpol.net/gameinfo.php?gi=80170",
        ["The Cast"] = "https://rpol.net/gameinfo.php?action=cast&gi=80170",
        ["Obsidian Game Vault"] = "https://publish.obsidian.md/scarlethorizons",
        ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking"
    };

    if (includeCredentials)
    {
        settings["RPOL user name"] = "example-user";
        settings["RPOL password"] = "example-password";
    }

    return settings;
}

static void WriteSettingsJson(string directoryPath, IReadOnlyDictionary<string, string> settings)
{
    Directory.CreateDirectory(directoryPath);
    var schemaVersionedSettings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["schema_version"] = 1
    };
    foreach (var setting in settings)
    {
        if (string.Equals(setting.Key, "schema_version", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        schemaVersionedSettings[setting.Key] = setting.Value;
    }

    File.WriteAllText(
        Path.Combine(directoryPath, "settings.json"),
        System.Text.Json.JsonSerializer.Serialize(
            schemaVersionedSettings,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

static void WriteRpolStorageState(string storageStatePath, string contents, DateTimeOffset lastWriteUtc)
{
    Directory.CreateDirectory(Path.GetDirectoryName(storageStatePath)!);
    File.WriteAllText(storageStatePath, contents);
    File.SetLastWriteTimeUtc(storageStatePath, lastWriteUtc.UtcDateTime);
}

static void WriteRequiredRuntimeSidecars(string directoryPath)
{
    File.WriteAllText(
        Path.Combine(directoryPath, "keyword-index.json"),
        """
        {
          "words": {
            "scarlet": []
          },
          "index_metadata": {}
        }
        """);
    File.WriteAllText(Path.Combine(directoryPath, KeywordTermsFileUtility.FileName), "scarlet");
    File.WriteAllText(Path.Combine(directoryPath, "sitemap.xml"), "<urlset />");
    File.WriteAllText(Path.Combine(directoryPath, "sitemap-keyword-urls.json"), "{}");
    XpPasswordStoreUtility.SavePasswordHashes(
        Path.Combine(directoryPath, XpPasswordStoreUtility.FileName),
        new Dictionary<string, string>
        {
            ["Kelpie"] = "gemstone"
        });
}

static void WriteManifestedRuntime(string directoryPath)
{
    Directory.CreateDirectory(directoryPath);
    WriteRequiredRuntimeSidecars(directoryPath);
    File.WriteAllText(Path.Combine(directoryPath, "player-assistant.exe"), "synthetic executable");
    File.WriteAllText(Path.Combine(directoryPath, "settings.json"), "{}");
    LocalSettingsUtility.SaveEncryptedSettings(
        Path.Combine(directoryPath, "settings.local.json"),
        new Dictionary<string, string>
        {
            ["XP Tracking"] = "https://publish.obsidian.md/scarlethorizons/Intentional+Orphans/XP+Tracking",
            ["RPOL user name"] = "example-user",
            ["RPOL password"] = "example-password"
        });

    Directory.CreateDirectory(Path.Combine(directoryPath, ".playwright", "node", "win32_x64"));
    Directory.CreateDirectory(Path.Combine(directoryPath, ".playwright", "package"));
    File.WriteAllText(Path.Combine(directoryPath, ".playwright", "node", "win32_x64", "node.exe"), "synthetic node");
    File.WriteAllText(Path.Combine(directoryPath, ".playwright", "package", "package.json"), "{}");
    File.WriteAllText(Path.Combine(directoryPath, ".playwright", "package", "browsers.json"), "{}");
    WriteReleaseRuntimeInventory(directoryPath);
    WriteReleaseManifest(directoryPath);
    WriteReleaseProvenance(directoryPath);
}

static void WriteReleaseRuntimeInventory(string directoryPath)
{
    var project = XDocument.Load(Path.Combine(GetRepositoryRoot(), "player-assistant.csproj"));
    var packages = project
        .Descendants()
        .Where(element => element.Name.LocalName == "PackageReference")
        .Select(element => new
        {
            name = element.Attribute("Include")?.Value ?? string.Empty,
            version = element.Attribute("Version")?.Value ?? string.Empty
        })
        .Where(package => !string.IsNullOrWhiteSpace(package.name))
        .OrderBy(package => package.name, StringComparer.Ordinal)
        .ToArray();

    string GetProjectProperty(string name)
    {
        return project
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == name)
            ?.Value
            ?? string.Empty;
    }

    var inventory = new
    {
        schema_version = 1,
        generated_at = DateTimeOffset.UtcNow.ToString("O"),
        app = new
        {
            version = GetProjectProperty("Version"),
            file_version = GetProjectProperty("FileVersion"),
            product_version = GetProjectProperty("InformationalVersion")
        },
        runtime = new
        {
            target_framework = GetProjectProperty("TargetFramework"),
            runtime_identifier = GetProjectProperty("RuntimeIdentifier"),
            self_contained = GetProjectProperty("SelfContained"),
            publish_single_file = "false",
            publish_runtime_identifier = "win-x64",
            publish_self_contained = "false"
        },
        packages,
        scripts = new[]
        {
            new { relative_path = "publish-player-assistant.ps1", length = 1L, sha256 = new string('A', 64) }
        },
        hash_algorithm = "SHA256"
    };

    File.WriteAllText(
        Path.Combine(directoryPath, "release-runtime-inventory.json"),
        System.Text.Json.JsonSerializer.Serialize(
            inventory,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

static void WriteReleaseManifest(string directoryPath)
{
    var assembly = typeof(Program).Assembly;
    var project = XDocument.Load(Path.Combine(GetRepositoryRoot(), "player-assistant.csproj"));
    string GetProjectProperty(string name)
    {
        return project
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == name)
            ?.Value
            ?? string.Empty;
    }

    var fileVersion = assembly
        .GetCustomAttributes(typeof(AssemblyFileVersionAttribute), inherit: false)
        .OfType<AssemblyFileVersionAttribute>()
        .FirstOrDefault()
        ?.Version
        ?? string.Empty;
    var informationalVersion = assembly
        .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), inherit: false)
        .OfType<AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()
        ?.InformationalVersion
        ?? string.Empty;

    var files = GetReleaseManifestRelativePaths()
        .Select(relativePath => GetReleaseManifestEntry(directoryPath, relativePath))
        .ToArray();
    var manifest = new
    {
        schema_version = 1,
        generated_at = DateTimeOffset.UtcNow.ToString("O"),
        app_version = GetProjectProperty("Version"),
        file_version = fileVersion,
        product_version = informationalVersion,
        hash_algorithm = "SHA256",
        files
    };

    File.WriteAllText(
        Path.Combine(directoryPath, "release-manifest.json"),
        System.Text.Json.JsonSerializer.Serialize(
            manifest,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

static void WriteReleaseProvenance(string directoryPath)
{
    var project = XDocument.Load(Path.Combine(GetRepositoryRoot(), "player-assistant.csproj"));
    string GetProjectProperty(string name)
    {
        return project
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == name)
            ?.Value
            ?? string.Empty;
    }

    var manifestEntry = GetReleaseManifestEntry(directoryPath, "release-manifest.json");
    var inventoryEntry = GetReleaseManifestEntry(directoryPath, "release-runtime-inventory.json");
    var provenance = new
    {
        schema_version = 1,
        generated_at = DateTimeOffset.UtcNow.ToString("O"),
        app = new
        {
            version = GetProjectProperty("Version"),
            file_version = GetProjectProperty("FileVersion"),
            product_version = GetProjectProperty("InformationalVersion")
        },
        git = new
        {
            commit = new string('a', 40),
            commit_short = new string('a', 12),
            branch = "test",
            tags_at_commit = Array.Empty<string>(),
            dirty = true,
            status_count = 1,
            status_sha256 = new string('B', 64)
        },
        release_manifest = manifestEntry,
        runtime_inventory = inventoryEntry,
        executable_signature = new
        {
            status = "NotSigned",
            signer_subject = (string?)null,
            thumbprint = (string?)null
        },
        hash_algorithm = "SHA256"
    };

    File.WriteAllText(
        Path.Combine(directoryPath, "release-provenance.json"),
        System.Text.Json.JsonSerializer.Serialize(
            provenance,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

static string[] GetReleaseManifestRelativePaths()
{
    return
    [
        "player-assistant.exe",
        "settings.json",
        XpPasswordStoreUtility.FileName,
        "release-runtime-inventory.json",
        "keyword-index.json",
        KeywordTermsFileUtility.FileName,
        "sitemap.xml",
        "sitemap-keyword-urls.json",
        Path.Combine(".playwright", "node", "win32_x64", "node.exe"),
        Path.Combine(".playwright", "package", "package.json"),
        Path.Combine(".playwright", "package", "browsers.json")
    ];
}

static object GetReleaseManifestEntry(string directoryPath, string relativePath)
{
    var path = Path.Combine(directoryPath, relativePath);
    if (!File.Exists(path))
    {
        throw new FileNotFoundException($"Release manifest fixture file '{relativePath}' was missing.", path);
    }

    return new
    {
        relative_path = relativePath.Replace(Path.DirectorySeparatorChar, '\\'),
        length = new FileInfo(path).Length,
        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
    };
}

static void SetLastWriteTimeUtc(string filePath, DateTimeOffset value)
{
    File.SetLastWriteTimeUtc(filePath, value.UtcDateTime);
}

static void SetDirectoryLastWriteTimeUtc(string directoryPath, DateTimeOffset value)
{
    Directory.SetLastWriteTimeUtc(directoryPath, value.UtcDateTime);
}

static void WriteVisiblePng(string filePath)
{
    using var bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
    bitmap.SetPixel(0, 0, Color.Black);
    bitmap.Save(filePath, ImageFormat.Png);

    using var padding = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None);
    padding.SetLength(600_000);
}

static void WriteTransparentPng(string filePath)
{
    using var bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
    bitmap.Save(filePath, ImageFormat.Png);
}

static string CreateSampleRpolThreadHtml()
{
    return """
        <html><body>
        <div class='message'>
            <ul><li>msg #1</li></ul>
            <span class='messageauthor'>Alice</span>
            <div class='characterdetails'>Scout</div>
            <span>Mon 1 Jan 2024 at 12:00</span>
            <div class='messagebody' id='msg1'>Hello from Alice.</div>
        </div><!-- 1 -->
        </div><!-- 2 -->
        <div class='message'>
            <ul><li>msg #2</li></ul>
            <span class='messageauthor'>Bob</span>
            <div class='characterdetails'>Wizard</div>
            <span>Tue 2 Jan 2024 at 13:30</span>
            <div class='messagebody' id='msg2'>Hello from Bob.</div>
        </div><!-- 1 -->
        </div><!-- 2 -->
        </body></html>
        """;
}

static string CreateSitemapXml(params string[] urls)
{
    var entries = string.Join(
        Environment.NewLine,
        urls.Select(url => $"  <url><loc>{System.Security.SecurityElement.Escape(url)}</loc></url>"));
    return $"<urlset>{Environment.NewLine}{entries}{Environment.NewLine}</urlset>";
}

static string CreateV1LocalSettingsEnvelope(string userName, string password)
{
    var plaintext = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
        new Dictionary<string, string>
        {
            ["RPOL user name"] = userName,
            ["RPOL password"] = password
        },
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    var key = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("PlayerAssistant.LocalSettings.v1"));
    var iv = RandomNumberGenerator.GetBytes(16);

    using var aes = Aes.Create();
    aes.Key = key;
    aes.IV = iv;
    aes.Mode = CipherMode.CBC;
    aes.Padding = PaddingMode.PKCS7;

    using var encryptor = aes.CreateEncryptor();
    var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    var payloadBytes = new byte[iv.Length + ciphertext.Length];
    Buffer.BlockCopy(iv, 0, payloadBytes, 0, iv.Length);
    Buffer.BlockCopy(ciphertext, 0, payloadBytes, iv.Length, ciphertext.Length);

    return $$"""
        {
          "format": "app-protected-v1",
          "payload": "{{Convert.ToBase64String(payloadBytes)}}"
        }
        """;
}

static string CreateV2LocalSettingsEnvelope(string userName, string password)
{
    var plaintext = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
        new Dictionary<string, string>
        {
            ["RPOL user name"] = userName,
            ["RPOL password"] = password
        },
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    var key = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("PlayerAssistant.LocalSettings.v1"));
    var authenticationKey = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("PlayerAssistant.LocalSettings.v1.hmac"));
    var iv = RandomNumberGenerator.GetBytes(16);

    using var aes = Aes.Create();
    aes.Key = key;
    aes.IV = iv;
    aes.Mode = CipherMode.CBC;
    aes.Padding = PaddingMode.PKCS7;

    using var encryptor = aes.CreateEncryptor();
    var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    var protectedContent = new byte[iv.Length + ciphertext.Length];
    Buffer.BlockCopy(iv, 0, protectedContent, 0, iv.Length);
    Buffer.BlockCopy(ciphertext, 0, protectedContent, iv.Length, ciphertext.Length);

    byte[] tag;
    using (var hmac = new HMACSHA256(authenticationKey))
    {
        tag = hmac.ComputeHash(protectedContent);
    }

    var payloadBytes = new byte[protectedContent.Length + tag.Length];
    Buffer.BlockCopy(protectedContent, 0, payloadBytes, 0, protectedContent.Length);
    Buffer.BlockCopy(tag, 0, payloadBytes, protectedContent.Length, tag.Length);

    return $$"""
        {
          "format": "app-protected-v2",
          "payload": "{{Convert.ToBase64String(payloadBytes)}}"
        }
        """;
}

static void WriteSettingsJsonWithHostedLocalSettings(string directoryPath, string hostedLocalSettingsUrl)
{
    File.WriteAllText(
        Path.Combine(directoryPath, "settings.json"),
        $$"""
        {
          "schema_version": 1,
          "Hosted Local Settings": "{{hostedLocalSettingsUrl}}",
          "RPOL Site": "https://rpol.net/game.php?gi=80170",
          "Game Intro": "https://rpol.net/gameinfo.php?gi=80170",
          "The Cast": "https://rpol.net/gameinfo.php?action=cast&gi=80170",
          "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons"
        }
        """);
}

static void AssertHostedSettingsFailure(
    string hostedLocalSettingsUrl,
    string expectedLogFragment,
    IReadOnlyList<HostedSettingsSigningKeyTrustEntry>? trustedSigningKeys = null,
    int? expectedRequestCount = null,
    string? expectedRequestPath = null)
{
    using var directory = TemporaryDirectory.Create();
    WriteSettingsJsonWithHostedLocalSettings(directory.Path, hostedLocalSettingsUrl);

    using var allowlistScope = NetworkUrlAllowlistUtility.UseValidationOverrideForTests((uri, purpose) =>
    {
        if (purpose == NetworkUrlPurpose.PlayerAssistantHostedSettings
            && string.Equals(uri.AbsoluteUri, hostedLocalSettingsUrl, StringComparison.Ordinal))
        {
            return NetworkUrlAllowlistValidation.Allowed(uri);
        }

        return null;
    });
    using var trustedHostedSettingsKeysScope = trustedSigningKeys is null
        ? null
        : HostedSettingsTrustUtility.UseTrustedSigningKeysForTests(trustedSigningKeys);

    WithHostedSettingsIsolation(() =>
        WithPreservedStartupLog(() =>
        {
            var exception = AssertThrows<InvalidOperationException>(() =>
                AppSettingsUtility.LoadSettings(directory.Path));

            AssertContains(exception.Message, "XP Tracking");
            var startupLog = File.ReadAllText(GetStartupLogPath());
            AssertContains(startupLog, "hosted local settings load");
            AssertContains(startupLog, hostedLocalSettingsUrl);
            AssertContains(startupLog, expectedLogFragment);
            AssertFalse(
                File.Exists(Path.Combine(directory.Path, "settings.local.json")),
                "failed hosted local settings should not be persisted into the runtime directory");
        }));

    if (expectedRequestCount is not null || expectedRequestPath is not null)
    {
        var requestObservation = LoopbackHttpServer.GetObservation(hostedLocalSettingsUrl);
        AssertTrue(requestObservation is not null, "expected fixture server observation for hosted settings request");

        if (expectedRequestCount is not null)
        {
            AssertEqual(expectedRequestCount.Value, requestObservation!.RequestCount, "unexpected hosted settings fixture request count");
        }

        if (expectedRequestPath is not null)
        {
            AssertEqual(expectedRequestPath, requestObservation!.LastRequestPath, "unexpected hosted settings fixture request path");
        }
    }
}

static string CorruptHostedSettingsSignature(string hostedSettingsJson)
{
    using var document = System.Text.Json.JsonDocument.Parse(hostedSettingsJson);
    var root = document.RootElement;
    var signature = root.GetProperty("signature").GetString()
        ?? throw new InvalidOperationException("Hosted settings signature was missing.");
    var signatureBytes = Convert.FromBase64String(signature);
    signatureBytes[^1] ^= 0x01;
    var tamperedSignature = Convert.ToBase64String(signatureBytes);

    return $$"""
        {
          "schema_version": {{root.GetProperty("schema_version").GetInt32()}},
          "format": "{{root.GetProperty("format").GetString()}}",
          "content_id": "{{root.GetProperty("content_id").GetString()}}",
          "version": "{{root.GetProperty("version").GetString()}}",
          "encrypted_settings": {{System.Text.Json.JsonSerializer.Serialize(root.GetProperty("encrypted_settings").GetString())}},
          "signature": "{{tamperedSignature}}"
        }
        """;
}

static void RpolSnapshotSignsAndVerifiesCanonicalPayload()
{
    var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    var payload = RpolSnapshotUtility.CreatePayload(
        new Uri("https://rpol.net/game.php?gi=80170"),
        "<html>campaign</html>",
        "text/html; charset=utf-8",
        DateTimeOffset.Parse("2026-07-16T12:00:00Z"),
        signingKey);

    AssertTrue(RpolSnapshotUtility.VerifySignature(payload, signingKey), "snapshot signature should verify");
    AssertFalse(
        RpolSnapshotUtility.VerifySignature(payload with { ContentSha256 = new string('0', 64) }, signingKey),
        "tampered snapshot metadata should fail signature verification");
}

static void AdventureOutlineFillsEveryChapterThroughLatestIcChapter()
{
    using var directory = TemporaryDirectory.Create();
    var icDirectory = Path.Combine(directory.Path, "Posts", "IC");
    Directory.CreateDirectory(icDirectory);
    File.WriteAllText(
        Path.Combine(icDirectory, "ch-1.html"),
        """
        <html><body>
        <h1>Ch 1 - Kirkilston.</h1>
        <span class="messageauthor">Dungeon Master</span>
        <div class="messagebody">The party leaves town.</div>
        </body></html>
        """);
    File.WriteAllText(
        Path.Combine(icDirectory, "ch-3.html"),
        """
        <html><body>
        <h1>Ch 3 - The Road.</h1>
        <span class="messageauthor">Kelpie</span>
        <div class="messagebody">Kelpie keeps watch while the party travels.</div>
        </body></html>
        """);

    var outline = AdventureOutlineUtility.BuildAdventureOutlineAsync(icDirectory)
        .GetAwaiter()
        .GetResult();

    AssertContains(outline, "## Ch 1 - Kirkilston");
    AssertContains(outline, "## Ch 2");
    AssertContains(outline, "- The in-character chapter source is not available yet.");
    AssertContains(outline, "## Ch 3 - The Road");
    AssertTrue(
        outline.IndexOf("## Ch 1", StringComparison.Ordinal) < outline.IndexOf("## Ch 2", StringComparison.Ordinal)
            && outline.IndexOf("## Ch 2", StringComparison.Ordinal) < outline.IndexOf("## Ch 3", StringComparison.Ordinal),
        "adventure outline chapters should form a contiguous numeric range");
}

static void RpolSnapshotRejectsAnotherGame()
{
    var exception = AssertThrows<InvalidOperationException>(() =>
        RpolSnapshotUtility.ValidateSourceUri(new Uri("https://rpol.net/game.php?gi=12345")));
    AssertContains(exception.Message, "80170");
}

static void RpolSnapshotSanitizesCredentialsAndLoginForm()
{
    using var credentialStoreScope = RuntimeSecretStoreUtility.UseBackendForTests(new InMemoryWindowsCredentialStoreBackend());
    RuntimeSecretStoreUtility.SaveRpolCredentials("admin-user", "secret-password");
    var sanitized = RpolSnapshotUtility.SanitizeHtml(
        "<html>admin-user secret-password<form action='/login.cgi'><input name='password'></form>safe</html>");

    AssertFalse(sanitized.Contains("admin-user", StringComparison.OrdinalIgnoreCase), "user name should be redacted");
    AssertFalse(sanitized.Contains("secret-password", StringComparison.Ordinal), "password should be redacted");
    AssertFalse(sanitized.Contains("login.cgi", StringComparison.OrdinalIgnoreCase), "login form should be removed");
    AssertContains(sanitized, "safe");
}

static void RpolSnapshotAcceptsSanitizedCampaignContent()
{
    var html = "<html><title>Scarlet Horizons</title><body>" + new string('x', 1200) + "</body></html>";
    AssertTrue(RpolSnapshotUtility.IsUsableSnapshotHtml(html), "campaign HTML should be accepted after sanitization");
}

static void RpolSnapshotRejectsLoginOnlyContent()
{
    var html = "<html><title>RPoL Login</title><body>" + new string('x', 1200)
        + "<form action='/login.cgi'><input name='username'><input name='password'></form></body></html>";
    AssertFalse(RpolSnapshotUtility.IsUsableSnapshotHtml(html), "login-only HTML should not be published");
}

static void RpolChallengeDetectionIgnoresPassiveCloudflareReferences()
{
    AssertFalse(
        RpolAuthUtility.LooksLikeCloudflareChallengePage("<html><body>Protected by Cloudflare</body></html>"),
        "a passive Cloudflare reference should not be treated as a browser challenge");
    AssertTrue(
        RpolAuthUtility.LooksLikeCloudflareChallengePage("<title>Just a moment...</title>"),
        "a concrete Cloudflare challenge marker should still be detected");
}

static void RpolVerificationRecognizesAuthenticatedBrowserTitle()
{
    AssertTrue(
        RpolAuthUtility.IsVerifiedRpolBrowserWindowTitle("RPoL: World of Issenda - Scarlet Horizons - Google Chrome"),
        "an authenticated RPOL window title should complete manual verification");
    AssertFalse(
        RpolAuthUtility.IsVerifiedRpolBrowserWindowTitle("Just a moment... - Google Chrome"),
        "a challenge window title should remain open");
}

static void SnapshotPublisherStateAdvancesOneTargetAndWraps()
{
    var root = new Uri("https://rpol.net/game.php?gi=80170");
    var cast = new Uri("https://rpol.net/gameinfo.php?action=cast&gi=80170");
    var state = RpolSnapshotUtility.CreatePublisherState([root, cast]);

    AssertEqual(root, RpolSnapshotUtility.GetNextSourceUri(state), "the root should be the initial publisher target");
    state = RpolSnapshotUtility.AdvancePublisherState(state);
    AssertEqual(cast, RpolSnapshotUtility.GetNextSourceUri(state), "one success should advance exactly one target");
    state = RpolSnapshotUtility.AdvancePublisherState(state);
    AssertEqual(root, RpolSnapshotUtility.GetNextSourceUri(state), "the publisher queue should wrap after the last target");
}

static void SnapshotDiscoveryApprovesGameLinks()
{
    var approved = (bool)(InvokeStaticMethod(
        typeof(RpolSnapshotUtility),
        "IsApprovedLinkLabel",
        "Game Links") ?? false);
    var unrelated = (bool)(InvokeStaticMethod(
        typeof(RpolSnapshotUtility),
        "IsApprovedLinkLabel",
        "Edit Game") ?? true);

    AssertTrue(approved, "Game Links should be included in snapshot discovery");
    AssertFalse(unrelated, "unrelated game administration links should remain excluded");
}

static void SnapshotPublisherStatePersistsItsCursor()
{
    using var directory = TemporaryDirectory.Create();
    var statePath = Path.Combine(directory.Path, "publisher-state.json");
    var state = RpolSnapshotUtility.AdvancePublisherState(
        RpolSnapshotUtility.CreatePublisherState(
        [
            new Uri("https://rpol.net/game.php?gi=80170"),
            new Uri("https://rpol.net/gameinfo.php?action=cast&gi=80170")
        ]));

    RpolSnapshotUtility.SavePublisherStateAsync(statePath, state).GetAwaiter().GetResult();
    var loaded = RpolSnapshotUtility.LoadPublisherState(statePath)
        ?? throw new InvalidOperationException("expected persisted publisher state");

    AssertEqual(1, loaded.NextIndex, "the persisted publisher cursor should be retained");
    AssertEqual(state.SourceUrls[1], loaded.SourceUrls[1], "the persisted publisher queue should be retained");
}

static void SnapshotPublisherStateRejectsInvalidCursor()
{
    var state = new RpolSnapshotPublisherState(
        1,
        ["https://rpol.net/game.php?gi=80170"],
        1);

    var exception = AssertThrows<InvalidOperationException>(() => RpolSnapshotUtility.GetNextSourceUri(state));
    AssertContains(exception.Message, "cursor");
}

static void NetworkAllowlistAcceptsOnlyBrokerApiPath()
{
    var accepted = NetworkUrlAllowlistUtility.Validate(
        "https://bryanmiller.us/scarlethorizons/api/v1/snapshots/page",
        NetworkUrlPurpose.PlayerAssistantBroker);
    var rejected = NetworkUrlAllowlistUtility.Validate(
        "https://bryanmiller.us/scarlethorizons/settings.local.json",
        NetworkUrlPurpose.PlayerAssistantBroker);

    AssertTrue(accepted.IsAllowed, "broker API path should be allowed");
    AssertFalse(rejected.IsAllowed, "non-broker paths should be rejected for broker requests");
}

static void SnapshotPublisherArgumentIsRecognized()
{
    AssertTrue(PlayerAssistant.Program.IsPublishRpolSnapshotsArgument("--publish-rpol-snapshots"), "long snapshot argument should be recognized");
    AssertTrue(PlayerAssistant.Program.IsPublishRpolSnapshotsArgument("/publish-rpol-snapshots"), "slash snapshot argument should be recognized");
}

internal sealed class TemporaryDirectory : IDisposable
{
    private TemporaryDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TemporaryDirectory Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return new TemporaryDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public ScriptedHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return _handler(request, cancellationToken);
    }
}

internal sealed class ChunkedHttpContent : HttpContent
{
    private readonly byte[] _bytes;

    public ChunkedHttpContent(byte[] bytes)
    {
        _bytes = bytes;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return stream.WriteAsync(_bytes, 0, _bytes.Length);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override Stream CreateContentReadStream(CancellationToken cancellationToken)
    {
        return new MemoryStream(_bytes, writable: false);
    }
}

internal sealed class LoopbackHttpServer : IDisposable
{
    private static readonly object ObservationSyncRoot = new();
    private static readonly Dictionary<string, RequestObservation> Observations = new(StringComparer.Ordinal);
    private readonly TcpListener _listener;
    private readonly Task _serverTask;
    private readonly string _expectedPath;
    private readonly byte[] _responseBytes;
    private readonly string _contentType;
    private int _requestCount;

    public LoopbackHttpServer(string expectedPath, string responseBody, string contentType = "application/json; charset=utf-8")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPath);
        ArgumentNullException.ThrowIfNull(responseBody);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        _expectedPath = expectedPath;
        _responseBytes = System.Text.Encoding.UTF8.GetBytes(responseBody);
        _contentType = contentType;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Url = $"http://127.0.0.1:{port}{expectedPath}";
        _serverTask = Task.Run(ServeSingleRequest);
    }

    public string Url { get; }

    public int RequestCount => Volatile.Read(ref _requestCount);

    public string LastRequestPath { get; private set; } = string.Empty;

    public static RequestObservation? GetObservation(string url)
    {
        lock (ObservationSyncRoot)
        {
            return Observations.TryGetValue(url, out var observation)
                ? observation
                : null;
        }
    }

    public void Dispose()
    {
        _listener.Stop();

        try
        {
            _serverTask.GetAwaiter().GetResult();
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ServeSingleRequest()
    {
        using var client = _listener.AcceptTcpClient();
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var requestLine = reader.ReadLine() ?? throw new InvalidOperationException("Fixture server did not receive an HTTP request line.");
        var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        LastRequestPath = requestParts.Length >= 2 ? requestParts[1] : string.Empty;

        string? headerLine;
        do
        {
            headerLine = reader.ReadLine();
        }
        while (!string.IsNullOrEmpty(headerLine));

        Interlocked.Increment(ref _requestCount);
        lock (ObservationSyncRoot)
        {
            Observations[Url] = new RequestObservation(RequestCount, LastRequestPath);
        }

        var statusLine = string.Equals(LastRequestPath, _expectedPath, StringComparison.Ordinal)
            ? "HTTP/1.1 200 OK"
            : "HTTP/1.1 404 Not Found";
        var responseBody = string.Equals(LastRequestPath, _expectedPath, StringComparison.Ordinal)
            ? _responseBytes
            : System.Text.Encoding.UTF8.GetBytes("not found");
        var responseHeaders = string.Join(
            "\r\n",
            [
                statusLine,
                $"Content-Type: {_contentType}",
                $"Content-Length: {responseBody.Length}",
                "Connection: close",
                string.Empty,
                string.Empty
            ]);
        var headerBytes = System.Text.Encoding.ASCII.GetBytes(responseHeaders);
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(responseBody, 0, responseBody.Length);
        stream.Flush();
    }

    internal sealed record RequestObservation(int RequestCount, string LastRequestPath);
}

internal sealed class InMemoryWindowsCredentialStoreBackend : IWindowsCredentialStoreBackend
{
    private readonly Dictionary<string, StoredSecret> _secrets = new(StringComparer.Ordinal);

    public bool TryRead(string targetName, out StoredSecret? storedSecret)
    {
        if (_secrets.TryGetValue(targetName, out var existingSecret))
        {
            storedSecret = existingSecret with { SecretBytes = [.. existingSecret.SecretBytes] };
            return true;
        }

        storedSecret = null;
        return false;
    }

    public void Write(string targetName, byte[] secretBytes, string? comment = null)
    {
        _secrets[targetName] = new StoredSecret([.. secretBytes], DateTimeOffset.UtcNow);
    }

    public void Delete(string targetName)
    {
        _secrets.Remove(targetName);
    }
}

internal sealed class ThrowingWindowsCredentialStoreBackend : IWindowsCredentialStoreBackend
{
    public bool TryRead(string targetName, out StoredSecret? storedSecret)
    {
        storedSecret = null;
        throw new InvalidOperationException("Credential store unavailable for test.");
    }

    public void Write(string targetName, byte[] secretBytes, string? comment = null)
    {
        throw new InvalidOperationException("Credential store unavailable for test.");
    }

    public void Delete(string targetName)
    {
        throw new InvalidOperationException("Credential store unavailable for test.");
    }
}

internal sealed class ObservedWindowsCredentialStoreBackend : IWindowsCredentialStoreBackend
{
    private readonly Dictionary<string, StoredSecret> _secrets = new(StringComparer.Ordinal);

    public byte[]? LastWriteInputBytes { get; private set; }

    public byte[]? LastReadOutputBytes { get; private set; }

    public bool TryRead(string targetName, out StoredSecret? storedSecret)
    {
        if (_secrets.TryGetValue(targetName, out var existingSecret))
        {
            LastReadOutputBytes = [.. existingSecret.SecretBytes];
            storedSecret = new StoredSecret(LastReadOutputBytes, existingSecret.LastWritten);
            return true;
        }

        LastReadOutputBytes = null;
        storedSecret = null;
        return false;
    }

    public void Write(string targetName, byte[] secretBytes, string? comment = null)
    {
        LastWriteInputBytes = secretBytes;
        _secrets[targetName] = new StoredSecret([.. secretBytes], DateTimeOffset.UtcNow);
    }

    public void Delete(string targetName)
    {
        _secrets.Remove(targetName);
    }
}
