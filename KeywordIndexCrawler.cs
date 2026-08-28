using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal static class KeywordIndexCrawler
    {
        private const string OutputFileName = "keyword-index.json";
        private const string ErrorLogFileName = "keyword-index-errors.log";
        private const string TempDirectoryName = "temp";
        private const string TempSitemapFileName = "keyword-index-sitemap.xml";
        private const string BadIndexFileTimestampFormat = "yyyyMMdd-HHmmss-fff";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = null,
            WriteIndented = true
        };

        public static async Task BuildIndexAsync(
            IProgress<KeywordIndexProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var terms = await LoadTermsAsync(cancellationToken).ConfigureAwait(false);
            var termMatchers = terms.ToDictionary(
                term => term,
                term => new Regex(Regex.Escape(term), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
                StringComparer.OrdinalIgnoreCase);

            var failures = new ConcurrentBag<string>();
            var outputPath = Path.Combine(GetApplicationExecutableDirectory(), OutputFileName);
            var outputFileExistedAtStartup = File.Exists(outputPath);
            var existingDocument = await LoadExistingDocumentAsync(outputPath, cancellationToken).ConfigureAwait(false);
            var indexedTerms = GetIndexedTerms(existingDocument);
            var priorityTerms = terms
                .Where(term => !indexedTerms.Contains(term))
                .ToArray();
            var orderedTerms = priorityTerms
                .Concat(terms.Where(term => !priorityTerms.Contains(term, StringComparer.OrdinalIgnoreCase)))
                .ToArray();
            var state = new KeywordIndexState(outputPath, existingDocument);
            await state.SaveAsync(cancellationToken).ConfigureAwait(false);
            if (!outputFileExistedAtStartup)
            {
                progress?.Report(KeywordIndexProgress.ForIndexFileCreated());
            }

            var rpolUrls = await DiscoverRpolUrlsAsync(cancellationToken).ConfigureAwait(false);
            var obsidianUrls = await DiscoverObsidianUrlsAsync(cancellationToken).ConfigureAwait(false);
            var crawlTargets = rpolUrls
                .Select(url => new CrawlTarget(url, KeywordIndexUrlSource.Rpol, UseRpolRateLimit: true))
                .Concat(obsidianUrls.Select(url => new CrawlTarget(url, KeywordIndexUrlSource.ObsidianWiki, UseRpolRateLimit: false)))
                .ToArray();
            var totalKeywordCount = orderedTerms.Length;
            progress?.Report(KeywordIndexProgress.ForTermsLoaded(totalKeywordCount));
            var totalRpolUrlCount = rpolUrls.Length;
            var totalObsidianUrlCount = obsidianUrls.Length;
            var totalUrlCount = totalRpolUrlCount + totalObsidianUrlCount;
            var totalWordsIndexed = 0;
            var accumulators = orderedTerms.ToDictionary(
                term => term,
                term => new KeywordMatchAccumulator(!indexedTerms.Contains(term)),
                StringComparer.OrdinalIgnoreCase);

            for (var urlIndex = 0; urlIndex < crawlTargets.Length; urlIndex++)
            {
                var crawlTarget = crawlTargets[urlIndex];
                await state.RegisterUrlAsync(
                    crawlTarget.Url,
                    crawlTarget.Source,
                    cancellationToken).ConfigureAwait(false);
                var pageContent = await CrawlPageAsync(
                    crawlTarget.Url,
                    crawlTarget.UseRpolRateLimit,
                    failures,
                    cancellationToken).ConfigureAwait(false);
                if (pageContent is not null)
                {
                    totalWordsIndexed += pageContent.WordCount;

                    for (var keywordIndex = 0; keywordIndex < orderedTerms.Length; keywordIndex++)
                    {
                        var term = orderedTerms[keywordIndex];
                        var count = termMatchers[term].Matches(pageContent.Text).Count;
                        if (count <= 0)
                        {
                            continue;
                        }

                        var accumulator = accumulators[term];
                        var match = new KeywordIndexMatch(
                            pageContent.Url,
                            count,
                            pageContent.LastIndexedUtc.ToString("O"));
                        accumulator.Matches[match.Url] = match;
                        accumulator.TotalOccurrences += count;
                        await state.UpsertKeywordMatchAsync(
                            term,
                            match,
                            accumulator.IsNewKeyword,
                            progress,
                            currentKeywordNumber: keywordIndex + 1,
                            completedKeywordCount: 0,
                            totalKeywordCount,
                            cancellationToken,
                            processedUrlCount: urlIndex + 1,
                            currentUrl: crawlTarget.Url,
                            totalUrlCount,
                            totalRpolUrlCount,
                            totalObsidianUrlCount).ConfigureAwait(false);
                    }
                }

                progress?.Report(KeywordIndexProgress.ForUrlPhase(
                    processedUrlCount: urlIndex + 1,
                    currentUrl: crawlTarget.Url,
                    totalUrlCount,
                    totalKeywordCount,
                    totalRpolUrlCount,
                    totalObsidianUrlCount));
            }

            for (var keywordIndex = 0; keywordIndex < orderedTerms.Length; keywordIndex++)
            {
                var term = orderedTerms[keywordIndex];
                var accumulator = accumulators[term];
                await state.ReplaceKeywordMatchesAsync(
                    term,
                    accumulator.Matches.Values.ToArray(),
                    accumulator.IsNewKeyword,
                    progress,
                    currentKeywordNumber: keywordIndex + 1,
                    keywordIndex,
                    totalKeywordCount,
                    cancellationToken,
                    saveEvenWithoutMatches: true,
                    processedUrlCount: totalUrlCount,
                    currentUrl: null,
                    totalUrlCount,
                    totalRpolUrlCount,
                    totalObsidianUrlCount,
                    reportUpdates: false).ConfigureAwait(false);
            }

            state.SetTotalWordsIndexed(totalWordsIndexed);
            await state.SaveAsync(cancellationToken).ConfigureAwait(false);
            await WriteFailureLogAsync(failures, cancellationToken).ConfigureAwait(false);
            progress?.Report(KeywordIndexProgress.ForCompletion(
                orderedTerms.Length,
                totalUrlCount,
                totalRpolUrlCount,
                totalObsidianUrlCount));
        }

        private static async Task<string[]> LoadTermsAsync(CancellationToken cancellationToken)
        {
            var termsPath = KeywordTermsFileUtility.TryResolvePath();
            if (string.IsNullOrWhiteSpace(termsPath) || !File.Exists(termsPath))
            {
                throw new FileNotFoundException($"Keyword terms file '{KeywordTermsFileUtility.FileName}' was not found.", termsPath);
            }

            var terms = new List<string>();
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duplicateTerms = new List<string>();
            var duplicateTermsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in await File.ReadAllLinesAsync(termsPath, cancellationToken).ConfigureAwait(false))
            {
                var term = line.Trim();
                if (term.Length == 0)
                {
                    continue;
                }

                if (seen.TryAdd(term, term))
                {
                    terms.Add(term.ToLowerInvariant());
                    continue;
                }

                var firstSeenTerm = seen[term];
                if (duplicateTermsSeen.Add(firstSeenTerm))
                {
                    duplicateTerms.Add(firstSeenTerm);
                }
            }

            if (duplicateTerms.Count > 0)
            {
                var duplicateSummary = string.Join(", ", duplicateTerms.OrderBy(term => term, StringComparer.OrdinalIgnoreCase));
                throw new InvalidOperationException(
                    $"Duplicate keyword term{(duplicateTerms.Count == 1 ? string.Empty : "s")} found in '{KeywordTermsFileUtility.FileName}' (case-insensitive): {duplicateSummary}.");
            }

            return terms.ToArray();
        }

        private static async Task<string[]> DiscoverRpolUrlsAsync(CancellationToken cancellationToken)
        {
            var baseUri = new Uri(AppSettingsUtility.GameForumUrl);
            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                NormalizeUrl(baseUri)
            };

            var hyperlinks = await HtmlUtility.GetRpolGameHyperlinksAsync(cancellationToken).ConfigureAwait(false);
            foreach (var hyperlink in hyperlinks)
            {
                var candidateUrl = IsIndexedRpolPostLinkText(hyperlink.Text)
                    ? RpolThreadPostUtility.GetShowAllThreadUrl(hyperlink.Url)
                    : hyperlink.Url;
                if (TryNormalizeRpolUrl(candidateUrl, baseUri, out var normalizedUrl))
                {
                    urls.Add(normalizedUrl);
                }
            }

            return urls.OrderBy(url => url, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static async Task<string[]> DiscoverObsidianUrlsAsync(CancellationToken cancellationToken)
        {
            var baseUri = new Uri(AppSettingsUtility.ObsidianGameVaultUrl);
            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                NormalizeUrl(baseUri)
            };

            var tempDirectory = RuntimePathUtility.GetUserDataPath(TempDirectoryName);
            Directory.CreateDirectory(tempDirectory);
            var tempSitemapPath = Path.Combine(tempDirectory, TempSitemapFileName);
            var sitemapUrl = $"{AppSettingsUtility.ObsidianGameVaultUrl.TrimEnd('/')}/sitemap.xml";

            try
            {
                await SitemapUtility.DownloadSitemapAsync(sitemapUrl, tempSitemapPath, cancellationToken).ConfigureAwait(false);
                foreach (var url in await SitemapUtility.ReadUrlsFromSitemapAsync(tempSitemapPath, cancellationToken).ConfigureAwait(false))
                {
                    if (TryNormalizeObsidianUrl(url, baseUri, out var normalizedUrl))
                    {
                        urls.Add(normalizedUrl);
                    }
                }
            }
            finally
            {
                if (File.Exists(tempSitemapPath))
                {
                    File.Delete(tempSitemapPath);
                }
            }

            return urls.OrderBy(url => url, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static async Task<CrawledPageContent?> CrawlPageAsync(
            string url,
            bool useRpolRateLimit,
            ConcurrentBag<string> failures,
            CancellationToken cancellationToken)
        {
            try
            {
                var html = useRpolRateLimit
                    ? await GameForumUtility.GetRpolHtmlWithRateLimitAsync(url, cancellationToken).ConfigureAwait(false)
                    : await HtmlUtility.GetHtmlFromUrlAsync(url, cancellationToken).ConfigureAwait(false);
                var text = HtmlUtility.GetPlainTextFromHtml(html);
                return new CrawledPageContent(
                    url,
                    CountWords(text),
                    DateTimeOffset.UtcNow,
                    text);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"[{DateTimeOffset.Now:O}] {url}{Environment.NewLine}{ex}{Environment.NewLine}");
                return null;
            }
        }

        private static async Task WriteFailureLogAsync(
            ConcurrentBag<string> failures,
            CancellationToken cancellationToken)
        {
            var failureEntries = failures
                .OrderBy(entry => entry, StringComparer.Ordinal)
                .ToArray();
            var logPath = RuntimePathUtility.GetWritableRuntimePath(ErrorLogFileName);

            if (failureEntries.Length == 0)
            {
                if (File.Exists(logPath))
                {
                    File.Delete(logPath);
                }

                return;
            }

            await File.WriteAllTextAsync(
                logPath,
                string.Concat(failureEntries),
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task<KeywordIndexDocument?> LoadExistingDocumentAsync(
            string outputPath,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(outputPath))
            {
                return null;
            }

            try
            {
                await using var stream = new FileStream(
                    outputPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 81920,
                    useAsync: true);
                return await JsonSerializer.DeserializeAsync<KeywordIndexDocument>(
                    stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false) is { } document
                    ? SanitizeExistingKeywordIndexDocument(document)
                    : null;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
            {
                var badIndexPath = PreserveBadIndexFile(outputPath);
                await StartupLoggingUtility.AppendAsync(
                    "keyword index recovery",
                    new InvalidOperationException(
                        $"Keyword index '{outputPath}' could not be loaded and was moved to '{badIndexPath}'. The index will be rebuilt.",
                        ex)).ConfigureAwait(false);
                return null;
            }
        }

        internal static void ValidateKeywordIndexJson(string keywordIndexJson)
        {
            ArgumentNullException.ThrowIfNull(keywordIndexJson);
            var document = JsonSerializer.Deserialize<KeywordIndexDocument>(keywordIndexJson, JsonOptions)
                ?? throw new InvalidOperationException("Keyword index JSON is empty or invalid.");
            ValidateKeywordIndexDocument(document);
        }

        private static string PreserveBadIndexFile(string outputPath)
        {
            var badIndexPath = GetBadIndexPath(outputPath);
            File.Move(outputPath, badIndexPath);
            return badIndexPath;
        }

        private static string GetBadIndexPath(string outputPath)
        {
            var directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
            var extension = Path.GetExtension(outputPath);
            var timestamp = DateTimeOffset.UtcNow.ToString(BadIndexFileTimestampFormat);
            var candidatePath = Path.Combine(directory, $"{fileNameWithoutExtension}.bad-{timestamp}{extension}");

            for (var suffix = 2; File.Exists(candidatePath); suffix++)
            {
                candidatePath = Path.Combine(directory, $"{fileNameWithoutExtension}.bad-{timestamp}-{suffix}{extension}");
            }

            return candidatePath;
        }

        private static HashSet<string> GetIndexedTerms(KeywordIndexDocument? existingDocument)
        {
            if (existingDocument?.Words is null)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return existingDocument.Words.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static int CountWords(string text)
        {
            return text.Length == 0
                ? 0
                : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static string GetApplicationExecutableDirectory()
        {
#pragma warning disable IL3000
            var assemblyLocation = typeof(KeywordIndexCrawler).Assembly.Location;
#pragma warning restore IL3000
            if (!string.IsNullOrWhiteSpace(assemblyLocation))
            {
                var assemblyDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyLocation));
                if (!string.IsNullOrWhiteSpace(assemblyDirectory))
                {
                    return assemblyDirectory;
                }
            }

            return Path.GetFullPath(AppContext.BaseDirectory);
        }

        private static bool TryNormalizeRpolUrl(string url, Uri baseUri, out string normalizedUrl)
        {
            normalizedUrl = string.Empty;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!uri.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!LooksLikeHtmlPage(uri))
            {
                return false;
            }

            var baseGameId = GetQueryValue(baseUri, "gi");
            var candidateGameId = GetQueryValue(uri, "gi");
            var samePath = string.Equals(uri.AbsolutePath, baseUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            var sameGame = baseGameId.Length > 0
                && candidateGameId.Length > 0
                && string.Equals(baseGameId, candidateGameId, StringComparison.OrdinalIgnoreCase);
            if (!samePath && !sameGame)
            {
                return false;
            }

            var candidateUrl = uri.AbsolutePath.EndsWith("/display.cgi", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.EndsWith("display.cgi", StringComparison.OrdinalIgnoreCase)
                ? RpolThreadPostUtility.GetShowAllThreadUrl(uri.ToString())
                : uri.ToString();

            normalizedUrl = NormalizeUrl(new Uri(candidateUrl));
            return true;
        }

        private static bool IsIndexedRpolPostLinkText(string linkText)
        {
            return linkText.StartsWith("Ch ", StringComparison.Ordinal)
                || linkText.StartsWith("Notice: Ch", StringComparison.Ordinal)
                || linkText.StartsWith("OOC", StringComparison.Ordinal)
                || linkText.StartsWith("Notice: OOC", StringComparison.Ordinal)
                || linkText.StartsWith("Aside -", StringComparison.Ordinal)
                || linkText.StartsWith("Notice: Aside -", StringComparison.Ordinal);
        }

        private static bool TryNormalizeObsidianUrl(string url, Uri baseUri, out string normalizedUrl)
        {
            normalizedUrl = string.Empty;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!uri.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var basePath = baseUri.AbsolutePath.TrimEnd('/');
            var candidatePath = uri.AbsolutePath.TrimEnd('/');
            var withinBasePath = candidatePath.Equals(basePath, StringComparison.OrdinalIgnoreCase)
                || candidatePath.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase);
            if (!withinBasePath)
            {
                return false;
            }

            if (!LooksLikeHtmlPage(uri))
            {
                return false;
            }

            normalizedUrl = NormalizeUrl(uri);
            return true;
        }

        private static bool LooksLikeHtmlPage(Uri uri)
        {
            var extension = Path.GetExtension(uri.AbsolutePath);
            return extension.Length == 0
                || extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".php", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cgi", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".asp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".aspx", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeUrl(Uri uri)
        {
            var builder = new UriBuilder(uri)
            {
                Fragment = string.Empty,
                Query = NormalizeQuery(uri.Query)
            };

            var normalized = builder.Uri.ToString().TrimEnd('/');
            return normalized.Length == 0
                ? builder.Uri.ToString()
                : normalized;
        }

        private static string NormalizeQuery(string query)
        {
            var parts = query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part =>
                {
                    var separatorIndex = part.IndexOf('=');
                    var key = separatorIndex >= 0 ? part[..separatorIndex] : part;
                    var value = separatorIndex >= 0 ? part[(separatorIndex + 1)..] : string.Empty;
                    return new KeyValuePair<string, string>(
                        Uri.UnescapeDataString(key),
                        Uri.UnescapeDataString(value));
                })
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Value.Length == 0
                    ? Uri.EscapeDataString(pair.Key) + "="
                    : $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")
                .ToArray();

            return parts.Length == 0
                ? string.Empty
                : string.Join("&", parts);
        }

        private static string GetQueryValue(Uri uri, string key)
        {
            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = part.IndexOf('=');
                var candidateKey = separatorIndex >= 0 ? part[..separatorIndex] : part;
                if (!candidateKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return separatorIndex >= 0
                    ? Uri.UnescapeDataString(part[(separatorIndex + 1)..])
                    : string.Empty;
            }

            return string.Empty;
        }

        private static KeywordIndexMatch? CreateKeywordMatch(Regex matcher, CrawledPageContent content)
        {
            var count = matcher.Matches(content.Text).Count;
            return count <= 0
                ? null
                : new KeywordIndexMatch(content.Url, count, content.LastIndexedUtc.ToString("O"));
        }

        private sealed record CrawlTarget(string Url, string Source, bool UseRpolRateLimit);

        private sealed record CrawledPageContent(
            string Url,
            int WordCount,
            DateTimeOffset LastIndexedUtc,
            string Text);

        private sealed class KeywordMatchAccumulator
        {
            public KeywordMatchAccumulator(bool isNewKeyword)
            {
                IsNewKeyword = isNewKeyword;
            }

            public bool IsNewKeyword { get; }

            public int TotalOccurrences { get; set; }

            public Dictionary<string, KeywordIndexMatch> Matches { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class KeywordIndexState
        {
            private readonly string _outputPath;
            private readonly Dictionary<string, string> _urlSources = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, MutableKeywordIndexWordEntry> _words = new(StringComparer.OrdinalIgnoreCase);
            private readonly SemaphoreSlim _gate = new(1, 1);
            private int _totalWordsIndexed;

            public KeywordIndexState(string outputPath, KeywordIndexDocument? existingDocument)
            {
                _outputPath = outputPath;
                if (existingDocument is null)
                {
                    return;
                }

                _totalWordsIndexed = existingDocument.IndexMetadata.TotalWordsIndexed;
                if (existingDocument.Urls is not null)
                {
                    foreach (var pair in existingDocument.Urls)
                    {
                        _urlSources[pair.Key] = pair.Value.Source;
                    }
                }

                foreach (var pair in existingDocument.Words)
                {
                    var entry = new MutableKeywordIndexWordEntry
                    {
                        TotalOccurrences = pair.Value.TotalOccurrences
                    };

                    foreach (var match in pair.Value.Matches)
                    {
                        entry.Matches[match.Url] = new MutableKeywordIndexMatch(match.Url, match.Count, match.LastIndexed);
                    }

                    _words[pair.Key] = entry;
                }
            }

            public void SetTotalWordsIndexed(int totalWordsIndexed)
            {
                _totalWordsIndexed = totalWordsIndexed;
            }

            public async Task RegisterUrlAsync(
                string url,
                string source,
                CancellationToken cancellationToken)
            {
                ValidateStoredUrl(url, source, "keyword-index urls");
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (_urlSources.TryGetValue(url, out var existingSource)
                        && string.Equals(existingSource, source, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _urlSources[url] = source;
                    await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _gate.Release();
                }
            }

            public async Task ReplaceKeywordMatchesAsync(
                string term,
                IReadOnlyList<KeywordIndexMatch> matches,
                bool isNewKeyword,
                IProgress<KeywordIndexProgress>? progress,
                int currentKeywordNumber,
                int completedKeywordCount,
                int totalKeywordCount,
                CancellationToken cancellationToken,
                bool saveEvenWithoutMatches,
                int processedUrlCount,
                string? currentUrl,
                int totalUrlCount,
                int totalRpolUrlCount,
                int totalObsidianUrlCount,
                bool reportUpdates)
            {
                foreach (var match in matches)
                {
                    ValidateStoredUrl(match.Url, null, $"keyword-index matches for '{term}'");
                }

                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var hadEntry = _words.TryGetValue(term, out var entry);
                    entry ??= new MutableKeywordIndexWordEntry();
                    var previousMatches = entry.Matches.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                    entry.Matches.Clear();
                    entry.TotalOccurrences = 0;
                    var reportedKeywordUpdate = false;

                    foreach (var match in matches)
                    {
                        reportedKeywordUpdate = reportUpdates;
                        entry.TotalOccurrences += match.Count;
                        entry.Matches[match.Url] = new MutableKeywordIndexMatch(match.Url, match.Count, match.LastIndexed);

                        if (!reportUpdates)
                        {
                            continue;
                        }

                        if (previousMatches.TryGetValue(match.Url, out var existingMatch))
                        {
                            progress?.Report(new KeywordIndexProgress(
                                term,
                                IsNewKeyword: false,
                                UrlCount: entry.Matches.Count,
                                TotalOccurrences: entry.TotalOccurrences,
                                CompletedKeywordCount: completedKeywordCount,
                                TotalKeywordCount: totalKeywordCount,
                                CurrentKeywordNumber: currentKeywordNumber,
                                ProcessedUrlCount: processedUrlCount,
                                CurrentUrl: currentUrl,
                                TotalUrlCount: totalUrlCount,
                                TotalRpolUrlCount: totalRpolUrlCount,
                                TotalObsidianUrlCount: totalObsidianUrlCount));
                            continue;
                        }

                        progress?.Report(new KeywordIndexProgress(
                            term,
                            isNewKeyword,
                            UrlCount: entry.Matches.Count,
                            TotalOccurrences: entry.TotalOccurrences,
                            CompletedKeywordCount: completedKeywordCount,
                            TotalKeywordCount: totalKeywordCount,
                            CurrentKeywordNumber: currentKeywordNumber,
                            ProcessedUrlCount: processedUrlCount,
                            CurrentUrl: currentUrl,
                            TotalUrlCount: totalUrlCount,
                            TotalRpolUrlCount: totalRpolUrlCount,
                            TotalObsidianUrlCount: totalObsidianUrlCount));
                    }

                    if (hadEntry || reportedKeywordUpdate || saveEvenWithoutMatches)
                    {
                        _words[term] = entry;
                        await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }

            public async Task UpsertKeywordMatchAsync(
                string term,
                KeywordIndexMatch match,
                bool isNewKeyword,
                IProgress<KeywordIndexProgress>? progress,
                int currentKeywordNumber,
                int completedKeywordCount,
                int totalKeywordCount,
                CancellationToken cancellationToken,
                int processedUrlCount,
                string currentUrl,
                int totalUrlCount,
                int totalRpolUrlCount,
                int totalObsidianUrlCount)
            {
                ValidateStoredUrl(match.Url, null, $"keyword-index match for '{term}'");
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var hadEntry = _words.TryGetValue(term, out var entry);
                    entry ??= new MutableKeywordIndexWordEntry();
                    var hadMatch = entry.Matches.TryGetValue(match.Url, out var existingMatch);
                    if (hadMatch)
                    {
                        entry.TotalOccurrences -= existingMatch!.Count;
                    }

                    entry.TotalOccurrences += match.Count;
                    entry.Matches[match.Url] = new MutableKeywordIndexMatch(match.Url, match.Count, match.LastIndexed);
                    _words[term] = entry;
                    await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
                    progress?.Report(new KeywordIndexProgress(
                        term,
                        IsNewKeyword: isNewKeyword && !hadEntry,
                        UrlCount: entry.Matches.Count,
                        TotalOccurrences: entry.TotalOccurrences,
                        CompletedKeywordCount: completedKeywordCount,
                        TotalKeywordCount: totalKeywordCount,
                        CurrentKeywordNumber: currentKeywordNumber,
                        ProcessedUrlCount: processedUrlCount,
                        CurrentUrl: currentUrl,
                        TotalUrlCount: totalUrlCount,
                        TotalRpolUrlCount: totalRpolUrlCount,
                        TotalObsidianUrlCount: totalObsidianUrlCount));
                }
                finally
                {
                    _gate.Release();
                }
            }

            public async Task SaveAsync(CancellationToken cancellationToken)
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _gate.Release();
                }
            }

            private async Task SaveLockedAsync(CancellationToken cancellationToken)
            {
                var document = new KeywordIndexDocument(
                    new KeywordIndexMetadata(DateTimeOffset.UtcNow.ToString("O"), _totalWordsIndexed),
                    _urlSources
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                        pair => pair.Key,
                        pair => new KeywordIndexUrlEntry(pair.Value),
                        StringComparer.OrdinalIgnoreCase),
                    _words.ToDictionary(
                        pair => pair.Key,
                        pair => new KeywordIndexWordEntry(
                            pair.Value.TotalOccurrences,
                            pair.Value.Matches
                                .Values
                                .OrderBy(match => match.Url, StringComparer.OrdinalIgnoreCase)
                                .ThenBy(match => match.LastIndexed, StringComparer.Ordinal)
                                .Select(match => new KeywordIndexMatch(
                                    match.Url,
                                    match.Count,
                                    match.LastIndexed))
                                .ToArray()),
                        StringComparer.OrdinalIgnoreCase));
                ValidateKeywordIndexDocument(document);

                var tempOutputPath = AtomicFileUtility.CreateTempPath(_outputPath);
                try
                {
                    await using (var stream = new FileStream(
                        tempOutputPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 81920,
                        useAsync: true))
                    {
                        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
                    }

                    var documentBytes = await File.ReadAllBytesAsync(tempOutputPath, cancellationToken).ConfigureAwait(false);
                    var sourceIntegrityRecord = SourceIntegrityUtility.ValidateContent(
                        _outputPath,
                        "keyword-index-crawl",
                        "keyword-index",
                        documentBytes,
                        SourceIntegrityUtility.CreateKeywordIndexShape(
                            document.Urls?.Count ?? 0,
                            document.Words.Count,
                            document.Words.Values.Sum(word => (long)word.TotalOccurrences)));

                    await AtomicFileUtility.PromoteTempFileAsync(tempOutputPath, _outputPath, cancellationToken).ConfigureAwait(false);
                    await SourceIntegrityUtility.WriteRecordAsync(_outputPath, sourceIntegrityRecord, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (File.Exists(tempOutputPath))
                    {
                        File.Delete(tempOutputPath);
                    }
                }
            }
        }

        private static KeywordIndexDocument ValidateKeywordIndexDocument(KeywordIndexDocument document)
        {
            if (document.Urls is not null)
            {
                foreach (var pair in document.Urls)
                {
                    ValidateStoredUrl(pair.Key, pair.Value.Source, "keyword-index urls");
                }
            }

            foreach (var word in document.Words)
            {
                foreach (var match in word.Value.Matches)
                {
                    ValidateStoredUrl(match.Url, null, $"keyword-index matches for '{word.Key}'");
                }
            }

            return document;
        }

        private static KeywordIndexDocument SanitizeExistingKeywordIndexDocument(KeywordIndexDocument document)
        {
            var urls = document.Urls is null
                ? null
                : document.Urls
                    .Where(pair => IsStoredUrlAllowed(pair.Key, pair.Value.Source))
                    .ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase);
            var words = document.Words
                .Select(pair =>
                {
                    var matches = pair.Value.Matches
                        .Where(match => IsStoredUrlAllowed(match.Url, null))
                        .ToArray();
                    return new
                    {
                        pair.Key,
                        Entry = new KeywordIndexWordEntry(
                            matches.Sum(match => match.Count),
                            matches)
                    };
                })
                .Where(pair => pair.Entry.Matches.Count > 0)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Entry,
                    StringComparer.OrdinalIgnoreCase);

            return new KeywordIndexDocument(
                document.IndexMetadata,
                urls,
                words);
        }

        private static void ValidateStoredUrl(string url, string? source, string description)
        {
            var purpose = source switch
            {
                KeywordIndexUrlSource.Rpol => NetworkUrlPurpose.Rpol,
                KeywordIndexUrlSource.ObsidianWiki => NetworkUrlPurpose.ObsidianPublish,
                _ => NetworkUrlPurpose.Generic
            };
            var validation = NetworkUrlAllowlistUtility.Validate(url, purpose);
            if (!validation.IsAllowed)
            {
                throw new InvalidOperationException(
                    $"{description} contains a URL that is not allowed: {url}. {validation.RejectionReason}");
            }
        }

        private static bool IsStoredUrlAllowed(string url, string? source)
        {
            var purpose = source switch
            {
                KeywordIndexUrlSource.Rpol => NetworkUrlPurpose.Rpol,
                KeywordIndexUrlSource.ObsidianWiki => NetworkUrlPurpose.ObsidianPublish,
                _ => NetworkUrlPurpose.Generic
            };
            return NetworkUrlAllowlistUtility.Validate(url, purpose).IsAllowed;
        }

        private sealed class MutableKeywordIndexWordEntry
        {
            public int TotalOccurrences { get; set; }

            public Dictionary<string, MutableKeywordIndexMatch> Matches { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class MutableKeywordIndexMatch
        {
            public MutableKeywordIndexMatch(string url, int count, string lastIndexed)
            {
                Url = url;
                Count = count;
                LastIndexed = lastIndexed;
            }

            public string Url { get; }

            public int Count { get; set; }

            public string LastIndexed { get; set; }
        }

        private sealed record KeywordIndexDocument(
            [property: JsonPropertyName("index_metadata")] KeywordIndexMetadata IndexMetadata,
            [property: JsonPropertyName("urls")] IReadOnlyDictionary<string, KeywordIndexUrlEntry>? Urls,
            [property: JsonPropertyName("words")] IReadOnlyDictionary<string, KeywordIndexWordEntry> Words);

        private sealed record KeywordIndexUrlEntry(
            [property: JsonPropertyName("source")] string Source);

        private sealed record KeywordIndexMetadata(
            [property: JsonPropertyName("generated_at")] string GeneratedAt,
            [property: JsonPropertyName("total_words_indexed")] int TotalWordsIndexed);

        private sealed record KeywordIndexWordEntry(
            [property: JsonPropertyName("total_occurrences")] int TotalOccurrences,
            [property: JsonPropertyName("matches")] IReadOnlyList<KeywordIndexMatch> Matches);

        private sealed record KeywordIndexMatch(
            [property: JsonPropertyName("url")] string Url,
            [property: JsonPropertyName("count")] int Count,
            [property: JsonPropertyName("last_indexed")] string LastIndexed);

        private static class KeywordIndexUrlSource
        {
            public const string Rpol = "RPOL";
            public const string ObsidianWiki = "Obsidian wiki";
        }
    }

    internal sealed record KeywordIndexProgress(
        string? Keyword,
        bool IsNewKeyword,
        int UrlCount,
        int TotalOccurrences,
        int CompletedKeywordCount,
        int TotalKeywordCount,
        int CurrentKeywordNumber = 0,
        int ProcessedUrlCount = 0,
        string? CurrentUrl = null,
        int TotalUrlCount = 0,
        int TotalRpolUrlCount = 0,
        int TotalObsidianUrlCount = 0,
        bool IsCompleted = false,
        bool IsIndexFileCreated = false,
        bool IsTermsLoaded = false)
    {
        public static KeywordIndexProgress ForTermsLoaded(int totalKeywordCount)
        {
            return new KeywordIndexProgress(
                Keyword: null,
                IsNewKeyword: false,
                UrlCount: 0,
                TotalOccurrences: 0,
                CompletedKeywordCount: 0,
                TotalKeywordCount: totalKeywordCount,
                ProcessedUrlCount: 0,
                TotalUrlCount: 0,
                IsCompleted: false,
                IsIndexFileCreated: false,
                IsTermsLoaded: true);
        }

        public static KeywordIndexProgress ForKeywordPhase(
            string keyword,
            bool isNewKeyword,
            int completedKeywordCount,
            int totalKeywordCount,
            int currentKeywordNumber,
            int processedUrlCount,
            string? currentUrl,
            int totalUrlCount,
            int totalRpolUrlCount,
            int totalObsidianUrlCount,
            int urlCount,
            int totalOccurrences)
        {
            return new KeywordIndexProgress(
                Keyword: keyword,
                IsNewKeyword: isNewKeyword,
                UrlCount: urlCount,
                TotalOccurrences: totalOccurrences,
                CompletedKeywordCount: completedKeywordCount,
                TotalKeywordCount: totalKeywordCount,
                CurrentKeywordNumber: currentKeywordNumber,
                ProcessedUrlCount: processedUrlCount,
                CurrentUrl: currentUrl,
                TotalUrlCount: totalUrlCount,
                TotalRpolUrlCount: totalRpolUrlCount,
                TotalObsidianUrlCount: totalObsidianUrlCount);
        }

        public static KeywordIndexProgress ForUrlPhase(
            int processedUrlCount,
            string currentUrl,
            int totalUrlCount,
            int totalKeywordCount,
            int totalRpolUrlCount,
            int totalObsidianUrlCount)
        {
            return new KeywordIndexProgress(
                Keyword: null,
                IsNewKeyword: false,
                UrlCount: 0,
                TotalOccurrences: 0,
                CompletedKeywordCount: 0,
                TotalKeywordCount: totalKeywordCount,
                CurrentKeywordNumber: 0,
                ProcessedUrlCount: processedUrlCount,
                CurrentUrl: currentUrl,
                TotalUrlCount: totalUrlCount,
                TotalRpolUrlCount: totalRpolUrlCount,
                TotalObsidianUrlCount: totalObsidianUrlCount);
        }

        public static KeywordIndexProgress ForCompletion(
            int totalKeywordCount,
            int totalUrlCount,
            int totalRpolUrlCount,
            int totalObsidianUrlCount)
        {
            return new KeywordIndexProgress(
                Keyword: null,
                IsNewKeyword: false,
                UrlCount: 0,
                TotalOccurrences: 0,
                CompletedKeywordCount: totalKeywordCount,
                TotalKeywordCount: totalKeywordCount,
                CurrentKeywordNumber: totalKeywordCount,
                ProcessedUrlCount: totalUrlCount,
                TotalUrlCount: totalUrlCount,
                TotalRpolUrlCount: totalRpolUrlCount,
                TotalObsidianUrlCount: totalObsidianUrlCount,
                IsCompleted: true);
        }

        public static KeywordIndexProgress ForIndexFileCreated()
        {
            return new KeywordIndexProgress(
                Keyword: null,
                IsNewKeyword: false,
                UrlCount: 0,
                TotalOccurrences: 0,
                CompletedKeywordCount: 0,
                TotalKeywordCount: 0,
                CurrentKeywordNumber: 0,
                ProcessedUrlCount: 0,
                TotalUrlCount: 0,
                IsCompleted: false,
                IsIndexFileCreated: true,
                IsTermsLoaded: false);
        }
    }
}
