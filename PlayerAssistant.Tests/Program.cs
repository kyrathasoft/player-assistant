using PlayerAssistant;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
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
    ("orcish translator exposes unique english term count", OrcishTranslatorExposesUniqueEnglishTermCount),
    ("app configuration validation accepts complete runtime", AppConfigurationValidationAcceptsCompleteRuntime),
    ("settings json accepts current schema version", SettingsJsonAcceptsCurrentSchemaVersion),
    ("settings json rejects future schema version", SettingsJsonRejectsFutureSchemaVersion),
    ("app configuration validation reports missing url", AppConfigurationValidationReportsMissingUrl),
    ("app configuration validation rejects disallowed network host", AppConfigurationValidationRejectsDisallowedNetworkHost),
    ("app configuration validation writes repair guidance", AppConfigurationValidationWritesRepairGuidance),
    ("app configuration validation warns about missing rpol credentials", AppConfigurationValidationWarnsAboutMissingRpolCredentials),
    ("app configuration validation warns about missing sidecars", AppConfigurationValidationWarnsAboutMissingSidecars),
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
    ("runtime housekeeping rotates oversized startup log", RuntimeHousekeepingRotatesOversizedStartupLog),
    ("runtime housekeeping skips locked files", RuntimeHousekeepingSkipsLockedFiles),
    ("ui operation failure reporter logs status and dialog", UiOperationFailureReporterLogsStatusAndDialog),
    ("background task supervisor suppresses duplicate phases", BackgroundTaskSupervisorSuppressesDuplicatePhases),
    ("background task supervisor logs failures", BackgroundTaskSupervisorLogsFailures),
    ("background task supervisor cancels running tasks on dispose", BackgroundTaskSupervisorCancelsRunningTasksOnDispose),
    ("atomic file promotion preserves existing destination on locked replacement", AtomicFilePromotionPreservesExistingDestinationOnLockedReplacement),
    ("network request retries transient failures", NetworkRequestRetriesTransientFailures),
    ("network request rejects disallowed host before send", NetworkRequestRejectsDisallowedHostBeforeSend),
    ("network request does not retry unauthorized", NetworkRequestDoesNotRetryUnauthorized),
    ("network circuit breaker opens after repeated terminal failures", NetworkCircuitBreakerOpensAfterRepeatedTerminalFailures),
    ("network circuit breaker clears after success", NetworkCircuitBreakerClearsAfterSuccess),
    ("startup dependency matrix classifies terminal network failure", StartupDependencyMatrixClassifiesTerminalNetworkFailure),
    ("network request wraps timeout", NetworkRequestWrapsTimeout),
    ("network request preserves caller cancellation", NetworkRequestPreservesCallerCancellation),
    ("network allowlist rejects credentialed and escaped hosts", NetworkAllowlistRejectsCredentialedAndEscapedHosts),
    ("network response limits define defaults", NetworkResponseLimitsDefineDefaults),
    ("network response limit rejects oversized html header", NetworkResponseLimitRejectsOversizedHtmlHeader),
    ("network response limit rejects oversized markdown stream", NetworkResponseLimitRejectsOversizedMarkdownStream),
    ("network response limit rejects oversized json cache stream", NetworkResponseLimitRejectsOversizedJsonCacheStream),
    ("network response limit rejects oversized image header", NetworkResponseLimitRejectsOversizedImageHeader),
    ("markdown async fetch preserves caller cancellation", MarkdownAsyncFetchPreservesCallerCancellation),
    ("runtime artifact loader quarantines malformed json", RuntimeArtifactLoaderQuarantinesMalformedJson),
    ("startup dependency matrix logs locked runtime artifact failures", StartupDependencyMatrixLogsLockedRuntimeArtifactFailures),
    ("login info cache load returns empty for malformed json", LoginInfoCacheLoadReturnsEmptyForMalformedJson),
    ("asset manifest load returns empty for malformed json", AssetManifestLoadReturnsEmptyForMalformedJson),
    ("active hero markdown cancellation writes no files", ActiveHeroMarkdownCancellationWritesNoFiles),
    ("player character refresh cancellation clears in progress flag", PlayerCharacterRefreshCancellationClearsInProgressFlag),
    ("game forum startup cancellation writes no manifests", GameForumStartupCancellationWritesNoManifests),
    ("keyword index loader quarantines malformed json", KeywordIndexLoaderQuarantinesMalformedJson),
    ("sitemap validation rejects poisoned url", SitemapValidationRejectsPoisonedUrl),
    ("sitemap keyword dictionary preserves existing output on rejected url", SitemapKeywordDictionaryPreservesExistingOutputOnRejectedUrl),
    ("keyword index validation rejects poisoned url entries", KeywordIndexValidationRejectsPoisonedUrlEntries),
    ("keyword index validation rejects poisoned match urls", KeywordIndexValidationRejectsPoisonedMatchUrls),
    ("keyword terms release copy generates from keyword index", KeywordTermsReleaseCopyGeneratesFromKeywordIndex),
    ("keyword terms publish copy preserves parent release terms", KeywordTermsPublishCopyPreservesParentReleaseTerms),
    ("rpol auth detects login page fallback", RpolAuthDetectsLoginPageFallback),
    ("rpol auth distinguishes blocked and remote failures", RpolAuthDistinguishesBlockedAndRemoteFailures),
    ("rpol auth cached failure short circuits html fetch", RpolAuthCachedFailureShortCircuitsHtmlFetch),
    ("rpol auth cached failure logs once", RpolAuthCachedFailureLogsOnce),
    ("rpol auth caches blocked and expired session failures", RpolAuthCachesBlockedAndExpiredSessionFailures),
    ("rpol storage state validation accepts current rpol cookies", RpolStorageStateValidationAcceptsCurrentRpolCookies),
    ("rpol storage state validation deletes malformed state", RpolStorageStateValidationDeletesMalformedState),
    ("rpol storage state validation deletes stale state", RpolStorageStateValidationDeletesStaleState),
    ("rpol storage state validation deletes non-rpol state", RpolStorageStateValidationDeletesNonRpolState),
    ("show-all thread url preserves base query and adds show all", ShowAllThreadUrlPreservesBaseQueryAndAddsShowAll),
    ("rpol thread export preserves existing output on cancellation", RpolThreadExportPreservesExistingOutputOnCancellation),
    ("rpol thread export commits staged output", RpolThreadExportCommitsStagedOutput),
    ("die roll extraction keeps only saved-log lines", DieRollExtractionKeepsOnlySavedLogLines),
    ("die roll extraction handles live rpol paragraph markup", DieRollExtractionHandlesLiveRpolParagraphMarkup),
    ("die roll sync appends only unsaved rolls", DieRollSyncAppendsOnlyUnsavedRolls),
    ("regional map downloads when missing", RegionalMapDownloadsWhenMissing),
    ("regional map downloads when older than one hour", RegionalMapDownloadsWhenOlderThanOneHour),
    ("regional map skips when newer than one hour", RegionalMapSkipsWhenNewerThanOneHour),
    ("regional map downloads when newer but transparent", RegionalMapDownloadsWhenNewerButTransparent),
    ("startup status includes download count and size", StartupStatusIncludesDownloadCountAndSize),
    ("adjusted post tallies aggregate saved IC html", AdjustedPostTalliesAggregateSavedIcHtml),
    ("keyword search falls back to The-prefixed term", KeywordSearchFallsBackToThePrefixedTerm),
    ("keyword search keeps quoted phrases together", KeywordSearchKeepsQuotedPhrasesTogether),
    ("keyword search accepts url source metadata", KeywordSearchAcceptsUrlSourceMetadata),
    ("keyword search filters rpol hero metadata-only hits", KeywordSearchFiltersRpolHeroMetadataOnlyHits),
    ("search enter triggers click when enabled", SearchEnterTriggersClickWhenEnabled),
    ("keyword search offers online fallback on local miss", KeywordSearchOffersOnlineFallbackOnLocalMiss),
    ("keyword search cancels previous online fallback", KeywordSearchCancelsPreviousOnlineFallback),
    ("keyword search rpol scope excludes obsidian-only whiteheart", KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheart),
    ("keyword search rpol scope excludes obsidian-only whiteheart stiffwhiskers", KeywordSearchRpolScopeExcludesObsidianOnlyWhiteheartStiffwhiskers),
    ("external url launch policy accepts http and https", ExternalUrlLaunchPolicyAcceptsHttpAndHttps),
    ("external url launch policy rejects unsafe inputs", ExternalUrlLaunchPolicyRejectsUnsafeInputs),
    ("hero image paths follow listing markdown table", HeroImagePathsFollowListingMarkdownTable),
    ("hero asset paths reject escaped targets", HeroAssetPathsRejectEscapedTargets),
    ("legacy local settings migrate to scoped encryption", LegacyLocalSettingsMigrateToPortableEncryption),
    ("v1 local settings migrate to authenticated encryption", V1LocalSettingsMigrateToAuthenticatedEncryption),
    ("v2 local settings migrate to scoped encryption", V2LocalSettingsMigrateToScopedEncryption),
    ("local settings are encrypted on load", LocalSettingsAreEncryptedOnLoad),
    ("local settings rejects future schema version", LocalSettingsRejectsFutureSchemaVersion),
    ("scoped local settings reject copied install path", ScopedLocalSettingsRejectCopiedInstallPath),
    ("authenticated local settings reject tampered payload", AuthenticatedLocalSettingsRejectTamperedPayload),
    ("runtime path utility rejects escaped paths", RuntimePathUtilityRejectsEscapedPaths),
    ("health argument returns startup health summary", HealthArgumentReturnsStartupHealthSummary),
    ("publish verification accepts current output", PublishVerificationAcceptsCurrentOutput),
    ("publish verification rejects stale startup log", PublishVerificationRejectsStaleStartupLog),
    ("publish verification rejects startup health artifact", PublishVerificationRejectsStartupHealthArtifact),
    ("publish verification rejects last crash artifact", PublishVerificationRejectsLastCrashArtifact),
    ("publish verification rejects malformed settings json", PublishVerificationRejectsMalformedSettingsJson),
    ("publish verification rejects future settings schema", PublishVerificationRejectsFutureSettingsSchema),
    ("publish verification rejects future local settings schema", PublishVerificationRejectsFutureLocalSettingsSchema),
    ("publish verification rejects malformed keyword index", PublishVerificationRejectsMalformedKeywordIndex),
    ("publish verification rejects malformed sitemap", PublishVerificationRejectsMalformedSitemap),
    ("publish verification rejects incomplete playwright runtime", PublishVerificationRejectsIncompletePlaywrightRuntime),
    ("publish verification rejects mismatched executable version", PublishVerificationRejectsMismatchedExecutableVersion),
    ("publish verification rejects stale release manifest", PublishVerificationRejectsStaleReleaseManifest),
    ("publish verification rejects malformed runtime inventory", PublishVerificationRejectsMalformedRuntimeInventory),
    ("publish verification rejects malformed release provenance", PublishVerificationRejectsMalformedReleaseProvenance),
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

static void OrcishTranslatorExposesUniqueEnglishTermCount()
{
    var terms = OrcishTranslatorUtility.GetEnglishTerms();

    AssertEqual(179, OrcishTranslatorUtility.GetEnglishTermCount(), "unexpected total English term count");
    AssertEqual(OrcishTranslatorUtility.GetEnglishTermCount(), terms.Count, "term list and count should agree");
    AssertEqual(1, terms.Count(term => string.Equals(term, "I", StringComparison.OrdinalIgnoreCase)), "I should be counted once despite multiple variants");
    AssertEqual(1, terms.Count(term => string.Equals(term, "really", StringComparison.OrdinalIgnoreCase)), "really should be counted once despite multiple variants");
    AssertEqual(1, terms.Count(term => string.Equals(term, "watch", StringComparison.OrdinalIgnoreCase)), "watch should be counted once despite multiple parts of speech");
    AssertTrue(terms.Contains("humans'", StringComparer.OrdinalIgnoreCase), "expected generated plural possessive term");
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
          "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons"
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
          "Obsidian Game Vault": "https://publish.obsidian.md/scarlethorizons"
        }
        """);

    var exception = AssertThrows<InvalidOperationException>(() =>
        AppSettingsUtility.LoadSettings(directory.Path));
    AssertContains(exception.Message, "unsupported schema version 99");
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
    AssertContains(remediation, "RPOL credentials are incomplete");
}

static void AppConfigurationValidationWarnsAboutMissingRpolCredentials()
{
    using var directory = TemporaryDirectory.Create();
    WriteRequiredRuntimeSidecars(directory.Path);

    var report = AppConfigurationValidationUtility.Validate(
        CreateValidAppSettings(includeCredentials: false),
        directory.Path);

    AssertTrue(
        report.Issues.Any(issue => issue.Severity == AppConfigurationIssueSeverity.Warning
            && issue.Message.Contains("RPOL credentials", StringComparison.Ordinal)),
        "missing RPOL credentials should be a warning");
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

    AssertEqual(new Version(0, 9, 0, 0), name.Version!, "unexpected assembly version");
    AssertEqual("0.9.0.5", fileVersion!, "unexpected file version");
    AssertEqual("0.9.0-hardening.5", informationalVersion, "unexpected informational version");
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
    AssertContains(versionText, "0.9.0-hardening.5");
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
            () => new HttpRequestMessage(HttpMethod.Get, "https://rpol.net/failure"),
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

    AssertFalse(credentialed.IsAllowed, "credentialed URLs should not be allowed");
    AssertContains(credentialed.RejectionReason ?? string.Empty, "credentials");
    AssertFalse(escapedHost.IsAllowed, "escaped host URLs should not be allowed");
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
    var rateLimited = RpolAuthUtility.CreateUnsuccessfulResponseException(uri, 429, "Too Many Requests");
    var unavailable = RpolAuthUtility.CreateUnsuccessfulResponseException(uri, 503, "Service Unavailable");

    AssertEqual(RpolAuthFailureKind.RpolBlocked, forbidden.Kind, "403 should be classified as RPOL blocking");
    AssertContains(forbidden.Message, "blocked authenticated access");
    AssertEqual(RpolAuthFailureKind.RpolBlocked, rateLimited.Kind, "429 should be classified as RPOL blocking");
    AssertEqual(RpolAuthFailureKind.RemoteUnavailable, unavailable.Kind, "503 should remain a transient remote failure");
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
            RpolAuthUtility.GetResponseAsync(new Uri("https://rpol.net/images/example.png")).GetAwaiter().GetResult());
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

static void PublishVerificationRejectsFutureLocalSettingsSchema()
{
    WithCopiedPublishDirectory(directoryPath =>
    {
        var localSettingsPath = Path.Combine(directoryPath, "settings.local.json");
        var localSettingsJson = File.ReadAllText(localSettingsPath);
        AssertContains(localSettingsJson, "\"schema_version\": 1");
        File.WriteAllText(
            localSettingsPath,
            localSettingsJson.Replace("\"schema_version\": 1", "\"schema_version\": 99", StringComparison.Ordinal));

        var output = RunPublishVerification(directoryPath);

        AssertFalse(output.ExitCode == 0, "publish verification should fail when settings.local.json uses a future schema");
        AssertContains(output.Output, "settings.local.json uses unsupported schema version 99");
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
            "Release/release-provenance.json",
            "Release/release-runtime-inventory.json",
            "Release/settings.redacted.json",
            "Release/settings.local.shape.json",
            "Release/startup-errors.log",
            "Release/startup-health.json",
            "Release/last-crash.json",
            "Release/startup-remediation.txt",
            "publish/release-provenance.json",
            "publish/release-runtime-inventory.json",
            "publish/settings.redacted.json",
            "publish/settings.local.shape.json",
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

        var localSettingsShape = ReadZipEntryText(zipPath, "Release/settings.local.shape.json");
        AssertContains(localSettingsShape, "\"schema_version\":  1");
        AssertContains(localSettingsShape, "\"encrypted_format\":  \"app-protected-v3\"");
        AssertContains(localSettingsShape, "\"has_payload\":  true");
        AssertContains(localSettingsShape, "\"payload_length\":");
        AssertContains(localSettingsShape, "\"install_path_bound\":  true");
        AssertFalse(localSettingsShape.Contains("very-secret-payload", StringComparison.Ordinal), "diagnostic bundle should summarize encrypted payloads instead of copying them");

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
    var sourceSettingsPath = Path.Combine(
        Path.GetDirectoryName(directoryPath) ?? GetRepositoryRoot(),
        $"{Path.GetFileName(directoryPath)}.source.settings.local.json");

    try
    {
        var publishedSettingsPath = Path.Combine(directoryPath, "settings.local.json");
        var fixtureSettings = CreateValidAppSettings(includeCredentials: true);

        CopyDirectory(GetCurrentPublishDirectory(), directoryPath);
        WriteRequiredRuntimeSidecars(directoryPath);
        LocalSettingsUtility.SaveEncryptedSettings(sourceSettingsPath, fixtureSettings);
        LocalSettingsUtility.SaveEncryptedSettings(publishedSettingsPath, fixtureSettings);
        WriteReleaseRuntimeInventory(directoryPath);
        WriteReleaseManifest(directoryPath);
        WriteReleaseProvenance(directoryPath);
        action(directoryPath);
    }
    finally
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }

        if (File.Exists(sourceSettingsPath))
        {
            File.Delete(sourceSettingsPath);
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
    LocalSettingsUtility.SaveEncryptedSettings(
        Path.Combine(directoryPath, "settings.local.json"),
        new Dictionary<string, string>
        {
            ["RPOL user name"] = "example-user",
            ["RPOL password"] = "very-secret-payload"
        });
    File.WriteAllText(
        Path.Combine(directoryPath, "startup-health.json"),
        """
        {
          "schema_version": 1,
          "phases": []
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

static (int ExitCode, string Output) RunPublishVerification(string outputDirectory)
{
    var repoRoot = GetRepositoryRoot();
    var scriptPath = Path.Combine(repoRoot, "publish-player-assistant.ps1");
    if (!File.Exists(scriptPath))
    {
        throw new InvalidOperationException($"Publish script is missing: {scriptPath}");
    }

    return RunPowerShell(
        [
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath,
            "-VerifyOnly",
            "-OutputDir",
            outputDirectory,
            "-SourceSettingsPath",
            Path.Combine(
                Path.GetDirectoryName(outputDirectory) ?? GetRepositoryRoot(),
                $"{Path.GetFileName(outputDirectory)}.source.settings.local.json")
        ],
        TimeSpan.FromSeconds(30));
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
    var startInfo = new ProcessStartInfo
    {
        FileName = "powershell",
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

static string GetPlayerAssistantIndexPath()
{
    var assemblyDirectory = Path.GetDirectoryName(typeof(Form1).Assembly.Location);
    if (string.IsNullOrWhiteSpace(assemblyDirectory))
    {
        throw new InvalidOperationException("Unable to resolve the player-assistant assembly directory.");
    }

    return Path.Combine(assemblyDirectory, "keyword-index.json");
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
        ["Obsidian Game Vault"] = "https://publish.obsidian.md/scarlethorizons"
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
}

static void WriteManifestedRuntime(string directoryPath)
{
    Directory.CreateDirectory(directoryPath);
    WriteRequiredRuntimeSidecars(directoryPath);
    File.WriteAllText(Path.Combine(directoryPath, "player-assistant.exe"), "synthetic executable");
    File.WriteAllText(Path.Combine(directoryPath, "settings.json"), "{}");
    File.WriteAllText(
        Path.Combine(directoryPath, "settings.local.json"),
        """
        {
          "format": "app-protected-v2",
          "payload": "synthetic"
        }
        """);

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
            publish_single_file = GetProjectProperty("PublishSingleFile"),
            publish_runtime_identifier = "win-x64",
            publish_self_contained = "true"
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
        app_version = assembly.GetName().Version?.ToString() ?? string.Empty,
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
        "settings.local.json",
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
