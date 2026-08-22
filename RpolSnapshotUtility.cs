using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal sealed record RpolSnapshotPayload(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("game_id")] string GameId,
        [property: JsonPropertyName("source_url")] string SourceUrl,
        [property: JsonPropertyName("fetched_at")] string FetchedAt,
        [property: JsonPropertyName("content_type")] string ContentType,
        [property: JsonPropertyName("content_sha256")] string ContentSha256,
        [property: JsonPropertyName("content_base64")] string ContentBase64,
        [property: JsonPropertyName("signature_algorithm")] string SignatureAlgorithm,
        [property: JsonPropertyName("signature")] string Signature);

    internal sealed record RpolSnapshotPublishReport(
        int Discovered,
        int Published,
        int Failed,
        IReadOnlyList<string> Errors,
        int Attempted = 0,
        IReadOnlyList<RpolTargetOutcome>? TargetOutcomes = null,
        bool UploadCompleted = false,
        bool CursorPersisted = false,
        string? RecoveryStage = null);

    internal sealed record RpolSnapshotCursorRecovery(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("source_url")] string SourceUrl,
        [property: JsonPropertyName("payload_sha256")] string PayloadSha256,
        [property: JsonPropertyName("next_state")] RpolSnapshotPublisherState NextState,
        [property: JsonPropertyName("created_at")] string CreatedAt,
        [property: JsonPropertyName("payload")] RpolSnapshotPayload Payload,
        [property: JsonPropertyName("recovery_stage")] string RecoveryStage);

    internal sealed record RpolSnapshotPublisherState(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("source_urls")] IReadOnlyList<string> SourceUrls,
        [property: JsonPropertyName("next_index")] int NextIndex);

    internal static partial class RpolSnapshotUtility
    {
        public const string GameId = "80170";
        public const string SignatureAlgorithm = "HMAC-SHA256";
        private const int SchemaVersion = 1;
        private const int PublisherStateSchemaVersion = 1;
        private const string PublisherStateFileName = "rpol-snapshot-publisher-state.json";
        private const string CursorRecoveryFileName = "rpol-snapshot-cursor-recovery.json";
        private static readonly TimeSpan StartupFreshnessInterval = TimeSpan.FromHours(1);
        private static readonly Uri DiceRollerUri = RpolAuthUtility.ProtectedDiceRollerUri;
        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false
        });

        public static async Task<string> GetHtmlAsync(Uri sourceUri, CancellationToken cancellationToken = default)
        {
            var payload = await GetSnapshotPayloadAsync(
                sourceUri,
                allowUnavailable: false,
                verifySignature: false,
                cancellationToken)
                ?? throw new InvalidOperationException("The RPOL snapshot broker returned an empty response.");
            return Encoding.UTF8.GetString(Convert.FromBase64String(payload.ContentBase64));
        }

        public static async Task CheckForPossiblyStaleSnapshotsAsync(
            CancellationToken cancellationToken = default)
        {
            await ExecuteWithPublisherLockAsync(
                owner => CheckForPossiblyStaleSnapshotsCoreAsync(cancellationToken, owner),
                cancellationToken);
        }

        private static async Task CheckForPossiblyStaleSnapshotsCoreAsync(
            CancellationToken cancellationToken,
            RpolCrossProcessLock lockOwner)
        {
            var now = DateTimeOffset.UtcNow;
            var statePath = RuntimePathUtility.GetUserDataPath(PublisherStateFileName);
            var state = LoadPublisherState(statePath);
            if (state is not null)
            {
                var normalizedState = EnsureRequiredSourceUris(state);
                if (!ReferenceEquals(normalizedState, state))
                {
                    state = normalizedState;
                    await SavePublisherStateAsync(statePath, state, cancellationToken);
                }
            }

            var rootUri = new Uri(AppSettingsUtility.GameForumUrl);
            var rootPayload = await GetSnapshotPayloadAsync(
                rootUri,
                allowUnavailable: true,
                verifySignature: true,
                cancellationToken);
            if (state is null || rootPayload is null || IsPossiblyStale(rootPayload, now))
            {
                var discovery = await DiscoverSourceUrisAsync(cancellationToken, lockOwner);
                state = MergeDiscoveredSourceUris(state, discovery.SourceUris);
                await SavePublisherStateAsync(statePath, state, cancellationToken);
                await PublishSnapshotAsync(rootUri, discovery.RootHtml, cancellationToken);
                if (state.SourceUrls.Count > 1)
                {
                    await PublishNextSnapshotForStartupAsync(statePath, state, cancellationToken, lockOwner);
                }

                return;
            }

            var sourceUri = GetNextSourceUri(state);
            if (sourceUri == rootUri && state.SourceUrls.Count > 1)
            {
                state = AdvancePublisherState(state);
                await SavePublisherStateAsync(statePath, state, cancellationToken);
                sourceUri = GetNextSourceUri(state);
            }

            var refreshRequired = await IsSnapshotRefreshRequiredAsync(
                token => GetSnapshotPayloadAsync(
                    sourceUri,
                    allowUnavailable: true,
                    verifySignature: true,
                    token),
                now,
                cancellationToken);
            if (refreshRequired)
            {
                await PublishNextSnapshotForStartupAsync(statePath, state, cancellationToken, lockOwner);
            }
            else if (state.SourceUrls.Count > 1)
            {
                await SavePublisherStateAsync(
                    statePath,
                    AdvancePublisherState(state),
                    cancellationToken);
            }
        }

        internal static bool IsPossiblyStale(RpolSnapshotPayload payload, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(payload);
            return !DateTimeOffset.TryParse(payload.FetchedAt, out var fetchedAt)
                || fetchedAt > now.AddMinutes(5)
                || now - fetchedAt >= StartupFreshnessInterval;
        }

        internal static async Task<bool> IsSnapshotRefreshRequiredAsync(
            Func<CancellationToken, Task<RpolSnapshotPayload?>> payloadLoader,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(payloadLoader);
            try
            {
                var payload = await payloadLoader(cancellationToken);
                return payload is null || IsPossiblyStale(payload, now);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return true;
            }
        }

        internal static RpolSnapshotPublisherState MergeDiscoveredSourceUris(
            RpolSnapshotPublisherState? state,
            IEnumerable<Uri> discoveredSourceUris)
        {
            ArgumentNullException.ThrowIfNull(discoveredSourceUris);
            var normalizedState = state is null ? null : EnsureRequiredSourceUris(state);
            var existingUrls = normalizedState?.SourceUrls.ToList() ?? [];
            var discoveredState = CreatePublisherState(discoveredSourceUris);
            var newUrls = discoveredState.SourceUrls
                .Where(url => !existingUrls.Contains(url, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var merged = CreatePublisherState(existingUrls.Concat(newUrls).Select(url => new Uri(url)));
            var nextUrl = newUrls.FirstOrDefault(url =>
                    !string.Equals(url, AppSettingsUtility.GameForumUrl, StringComparison.OrdinalIgnoreCase))
                ?? normalizedState?.SourceUrls[normalizedState.NextIndex]
                ?? merged.SourceUrls.FirstOrDefault(url =>
                    !string.Equals(url, AppSettingsUtility.GameForumUrl, StringComparison.OrdinalIgnoreCase))
                ?? merged.SourceUrls[0];
            var nextIndex = merged.SourceUrls.ToList().FindIndex(url =>
                string.Equals(url, nextUrl, StringComparison.OrdinalIgnoreCase));
            return merged with { NextIndex = nextIndex };
        }

        public static Task<RpolSnapshotPublishReport> PublishAsync(
            CancellationToken cancellationToken = default,
            RpolCrossProcessLock? lockOwner = null)
        {
            return lockOwner is not null
                ? PublishCoreAsync(cancellationToken, lockOwner)
                : ExecuteWithPublisherLockAsync(
                    owner => PublishCoreAsync(cancellationToken, owner),
                    cancellationToken);
        }

        internal static async Task<T> ExecuteWithPublisherLockAsync<T>(
            Func<RpolCrossProcessLock, Task<T>> operation,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            using var operationLock = await RpolCrossProcessLock.AcquireAsync(
                RpolCrossProcessLock.AuthAndPublisherName,
                TimeSpan.FromSeconds(10),
                cancellationToken);
            return await operation(operationLock);
        }

        internal static async Task ExecuteWithPublisherLockAsync(
            Func<RpolCrossProcessLock, Task> operation,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            using var operationLock = await RpolCrossProcessLock.AcquireAsync(
                RpolCrossProcessLock.AuthAndPublisherName,
                TimeSpan.FromSeconds(10),
                cancellationToken);
            await operation(operationLock);
        }

        private static async Task<RpolSnapshotPublishReport> PublishCoreAsync(
            CancellationToken cancellationToken,
            RpolCrossProcessLock lockOwner)
        {
            var adminKey = RuntimeSecretStoreUtility.GetBrokerAdminKey();
            var signingKey = RuntimeSecretStoreUtility.GetSnapshotSigningKey();
            if (string.IsNullOrWhiteSpace(adminKey) || string.IsNullOrWhiteSpace(signingKey))
            {
                throw new InvalidOperationException(
                    "Broker administrator and snapshot signing keys are required in Windows Credential Manager.");
            }

            var statePath = RuntimePathUtility.GetUserDataPath(PublisherStateFileName);
            var recoveryPath = RuntimePathUtility.GetUserDataPath(CursorRecoveryFileName);
            var state = LoadPublisherState(statePath);
            var recovery = LoadCursorRecovery(recoveryPath);
            if (recovery is not null)
            {
                return await RecoverPendingUploadAsync(
                    recovery,
                    adminKey,
                    statePath,
                    recoveryPath,
                    cancellationToken);
            }
            RpolSnapshotDiscovery? discovery = null;
            if (state is null)
            {
                discovery = await DiscoverSourceUrisAsync(cancellationToken, lockOwner);
                state = CreatePublisherState(discovery.SourceUris);
                await SavePublisherStateAsync(statePath, state, cancellationToken);
            }
            else
            {
                var updatedState = EnsureRequiredSourceUris(state);
                if (!ReferenceEquals(updatedState, state))
                {
                    state = updatedState;
                    await SavePublisherStateAsync(statePath, state, cancellationToken);
                }
            }

            var sourceUri = GetNextSourceUri(state);
            try
            {
                string html;
                string contentType;
                if (discovery is not null && sourceUri == discovery.RootUri)
                {
                    html = discovery.RootHtml;
                    contentType = "text/html; charset=utf-8";
                }
                else
                {
                    var response = await RpolAuthUtility.GetSnapshotResponseAsync(sourceUri, cancellationToken, lockOwner);
                    html = RpolAuthUtility.DecodeHtmlBody(response.Body, response.ContentType);
                    contentType = "text/html; charset=utf-8";
                }

                if (sourceUri == DiceRollerUri)
                {
                    html = GameForumUtility.NormalizeDieRollSnapshotHtml(html);
                }

                var sanitizedHtml = PrepareSnapshotHtml(html);

                var payload = CreatePayload(
                    sourceUri,
                    sanitizedHtml,
                    contentType,
                    DateTimeOffset.UtcNow,
                    signingKey);
                var nextState = AdvancePublisherState(state);
                var cursorRecovery = new RpolSnapshotCursorRecovery(
                    1,
                    sourceUri.AbsoluteUri,
                    payload.ContentSha256,
                    nextState,
                    DateTimeOffset.UtcNow.ToString("O"),
                    payload,
                    "intent");
                var transaction = await RpolUploadRecoveryTransaction.ExecuteAsync(
                    () => SaveCursorRecoveryAsync(recoveryPath, cursorRecovery, cancellationToken),
                    () => UploadAsync(payload, adminKey, CancellationToken.None),
                    () => SaveCursorRecoveryAsync(
                        recoveryPath,
                        cursorRecovery with { RecoveryStage = "uploaded" },
                        cancellationToken),
                    () => SavePublisherStateAsync(statePath, nextState, cancellationToken),
                    () =>
                    {
                        File.Delete(recoveryPath);
                        return Task.CompletedTask;
                    },
                    cancellationToken);
                if (!transaction.Succeeded)
                {
                    var error = $"{sourceUri}: {string.Join("; ", transaction.Errors)}";
                    var uploadConfirmed = transaction.UploadCompleted;
                    return new RpolSnapshotPublishReport(
                        state.SourceUrls.Count,
                        uploadConfirmed ? 1 : 0,
                        uploadConfirmed ? 0 : 1,
                        [error],
                        Attempted: 1,
                        TargetOutcomes:
                        [
                            new RpolTargetOutcome(
                                sourceUri.AbsoluteUri,
                                uploadConfirmed ? "published-cursor-pending" : "failed",
                                error)
                        ],
                        UploadCompleted: transaction.UploadCompleted,
                        CursorPersisted: transaction.CursorPersisted,
                        RecoveryStage: transaction.RecoveryStage);
                }

                return new RpolSnapshotPublishReport(
                    state.SourceUrls.Count,
                    1,
                    0,
                    [],
                    Attempted: 1,
                    TargetOutcomes: [new RpolTargetOutcome(sourceUri.AbsoluteUri, "published", null)],
                    UploadCompleted: true,
                    CursorPersisted: true);
            }
            catch (Exception ex) when (ShouldHandlePublisherFailure(ex, cancellationToken))
            {
                var error = $"{sourceUri}: {SensitiveTextRedactionUtility.Redact(ex.Message)}";
                return new RpolSnapshotPublishReport(
                    state.SourceUrls.Count,
                    0,
                    1,
                    [error],
                    Attempted: 1,
                    TargetOutcomes: [new RpolTargetOutcome(sourceUri.AbsoluteUri, "failed", error)],
                    UploadCompleted: false,
                    CursorPersisted: false);
            }
        }

        internal static async Task<RpolSnapshotPublishReport> RecoverPendingUploadAsync(
            RpolSnapshotCursorRecovery recovery,
            string adminKey,
            string statePath,
            string recoveryPath,
            CancellationToken cancellationToken,
            Func<RpolSnapshotCursorRecovery, CancellationToken, Task<bool>>? readbackCommittedAsync = null,
            Func<RpolSnapshotPayload, string, CancellationToken, Task>? uploadAsync = null,
            Func<string, RpolSnapshotCursorRecovery, CancellationToken, Task>? saveRecoveryAsync = null,
            Func<string, RpolSnapshotPublisherState, CancellationToken, Task>? saveStateAsync = null,
            Action<string>? deleteRecovery = null)
        {
            ArgumentNullException.ThrowIfNull(recovery);
            ArgumentException.ThrowIfNullOrWhiteSpace(adminKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(recoveryPath);
            readbackCommittedAsync ??= (item, token) => IsUploadCommittedAsync(item, token);
            uploadAsync ??= (payload, key, token) => UploadAsync(payload, key, token);
            saveRecoveryAsync ??= SaveCursorRecoveryAsync;
            saveStateAsync ??= SavePublisherStateAsync;
            deleteRecovery ??= File.Delete;

            if (string.Equals(recovery.RecoveryStage, "intent", StringComparison.Ordinal))
            {
                await ReconcilePendingUploadAsync(
                    () => readbackCommittedAsync(recovery, cancellationToken),
                    () => uploadAsync(recovery.Payload, adminKey, CancellationToken.None),
                    cancellationToken);
                await saveRecoveryAsync(
                    recoveryPath,
                    recovery with { RecoveryStage = "uploaded" },
                    cancellationToken);
            }
            await saveStateAsync(statePath, recovery.NextState, cancellationToken);
            deleteRecovery(recoveryPath);
            return new RpolSnapshotPublishReport(
                recovery.NextState.SourceUrls.Count,
                1,
                0,
                [],
                Attempted: 1,
                TargetOutcomes: [new RpolTargetOutcome(recovery.SourceUrl, "published-cursor-recovered", null)],
                UploadCompleted: true,
                CursorPersisted: true,
                RecoveryStage: null);
        }

        internal static bool IsSuccessfulPublishReport(RpolSnapshotPublishReport report)
        {
            ArgumentNullException.ThrowIfNull(report);
            var attempted = report.Attempted == 0 ? report.Published + report.Failed : report.Attempted;
            return report.Discovered > 0
                && attempted == 1
                && report.Published == 1
                && report.Failed == 0
                && report.Errors.Count == 0
                && report.UploadCompleted
                && report.CursorPersisted
                && string.IsNullOrWhiteSpace(report.RecoveryStage)
                && (report.TargetOutcomes is null || report.TargetOutcomes.Count == 1);
        }

        internal static RpolSnapshotCursorRecovery? LoadCursorRecovery(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!File.Exists(path))
            {
                return null;
            }

            var recovery = JsonSerializer.Deserialize<RpolSnapshotCursorRecovery>(File.ReadAllText(path))
                ?? throw new InvalidOperationException("The RPOL cursor recovery record is empty.");
            if (recovery.SchemaVersion != 1
                || !Uri.TryCreate(recovery.SourceUrl, UriKind.Absolute, out _)
                || string.IsNullOrWhiteSpace(recovery.PayloadSha256)
                || recovery.Payload is null
                || !string.Equals(recovery.Payload.ContentSha256, recovery.PayloadSha256, StringComparison.OrdinalIgnoreCase)
                || recovery.RecoveryStage is not ("intent" or "uploaded"))
            {
                throw new InvalidOperationException("The RPOL cursor recovery record is invalid; republishing is blocked.");
            }

            ValidatePublisherState(recovery.NextState);
            return recovery;
        }

        internal static async Task SaveCursorRecoveryAsync(
            string path,
            RpolSnapshotCursorRecovery recovery,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(recovery);
            ValidatePublisherState(recovery.NextState);
            await AtomicFileUtility.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(recovery, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
        }

        internal static bool ShouldHandlePublisherFailure(
            Exception exception,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;
        }

        internal static RpolSnapshotPublisherState CreatePublisherState(IEnumerable<Uri> sourceUris)
        {
            ArgumentNullException.ThrowIfNull(sourceUris);
            var urls = sourceUris
                .Select(uri =>
                {
                    ValidateSourceUri(uri);
                    return NormalizePublisherSourceUri(uri).AbsoluteUri;
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (urls.Count == 0)
            {
                throw new InvalidOperationException("The RPOL snapshot publisher queue is empty.");
            }

            return new RpolSnapshotPublisherState(PublisherStateSchemaVersion, urls, 0);
        }

        internal static Uri GetNextSourceUri(RpolSnapshotPublisherState state)
        {
            ValidatePublisherState(state);
            return new Uri(state.SourceUrls[state.NextIndex]);
        }

        internal static RpolSnapshotPublisherState AdvancePublisherState(RpolSnapshotPublisherState state)
        {
            ValidatePublisherState(state);
            return state with { NextIndex = (state.NextIndex + 1) % state.SourceUrls.Count };
        }

        internal static RpolSnapshotPublisherState EnsureRequiredSourceUris(RpolSnapshotPublisherState state)
        {
            ValidatePublisherState(state);
            var nextSourceUrl = NormalizePublisherSourceUri(
                new Uri(state.SourceUrls[state.NextIndex])).AbsoluteUri;
            var sourceUrls = state.SourceUrls
                .Select(sourceUrl => NormalizePublisherSourceUri(new Uri(sourceUrl)).AbsoluteUri)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var nextIndex = sourceUrls.FindIndex(sourceUrl =>
                string.Equals(sourceUrl, nextSourceUrl, StringComparison.OrdinalIgnoreCase));
            if (nextIndex < 0)
            {
                throw new InvalidOperationException("The normalized RPOL snapshot publisher cursor is invalid.");
            }

            var missingUris = new[] { DiceRollerUri }
                .Where(requiredUri => !sourceUrls.Contains(
                    requiredUri.AbsoluteUri,
                    StringComparer.OrdinalIgnoreCase))
                .Select(requiredUri => requiredUri.AbsoluteUri)
                .ToArray();
            var urlsChanged = !sourceUrls.SequenceEqual(state.SourceUrls, StringComparer.OrdinalIgnoreCase);
            if (missingUris.Length == 0 && !urlsChanged && nextIndex == state.NextIndex)
            {
                return state;
            }

            sourceUrls.InsertRange(nextIndex, missingUris);
            return state with { SourceUrls = sourceUrls, NextIndex = nextIndex };
        }

        internal static Uri NormalizePublisherSourceUri(Uri sourceUri)
        {
            ValidateSourceUri(sourceUri);
            return sourceUri.AbsolutePath.EndsWith("/display.cgi", StringComparison.OrdinalIgnoreCase)
                && sourceUri.Query.Contains("ti=", StringComparison.OrdinalIgnoreCase)
                ? new Uri(RpolThreadPostUtility.GetShowAllThreadUrl(sourceUri.AbsoluteUri))
                : sourceUri;
        }

        internal static RpolSnapshotPublisherState? LoadPublisherState(string statePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
            if (!File.Exists(statePath))
            {
                return null;
            }

            var state = JsonSerializer.Deserialize<RpolSnapshotPublisherState>(File.ReadAllText(statePath))
                ?? throw new InvalidOperationException("The RPOL snapshot publisher state is empty.");
            ValidatePublisherState(state);
            return state;
        }

        internal static async Task SavePublisherStateAsync(
            string statePath,
            RpolSnapshotPublisherState state,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
            ValidatePublisherState(state);
            var directory = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await AtomicFileUtility.WriteAllTextAsync(
                statePath,
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
        }

        private static void ValidatePublisherState(RpolSnapshotPublisherState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (state.SchemaVersion != PublisherStateSchemaVersion)
            {
                throw new InvalidOperationException("The RPOL snapshot publisher state schema is unsupported.");
            }

            if (state.SourceUrls.Count == 0 || state.SourceUrls.Count > 100)
            {
                throw new InvalidOperationException("The RPOL snapshot publisher queue size is invalid.");
            }

            if (state.NextIndex < 0 || state.NextIndex >= state.SourceUrls.Count)
            {
                throw new InvalidOperationException("The RPOL snapshot publisher cursor is invalid.");
            }

            foreach (var sourceUrl in state.SourceUrls)
            {
                if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri))
                {
                    throw new InvalidOperationException("The RPOL snapshot publisher queue contains an invalid URL.");
                }

                ValidateSourceUri(sourceUri);
            }

            if (state.SourceUrls.Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.SourceUrls.Count)
            {
                throw new InvalidOperationException("The RPOL snapshot publisher queue contains duplicate URLs.");
            }
        }

        internal static RpolSnapshotPayload CreatePayload(
            Uri sourceUri,
            string html,
            string contentType,
            DateTimeOffset fetchedAt,
            string base64SigningKey)
        {
            ValidateSourceUri(sourceUri);
            var content = Encoding.UTF8.GetBytes(SanitizeHtml(html));
            var fetchedAtText = fetchedAt.ToUniversalTime().ToString("O");
            var contentHash = Convert.ToHexStringLower(SHA256.HashData(content));
            var unsigned = new RpolSnapshotPayload(
                SchemaVersion,
                GameId,
                sourceUri.AbsoluteUri,
                fetchedAtText,
                contentType,
                contentHash,
                EncodeBase64ForTransport(content),
                SignatureAlgorithm,
                string.Empty);
            return unsigned with { Signature = ComputeSignature(unsigned, base64SigningKey) };
        }

        private static string EncodeBase64ForTransport(byte[] content)
        {
            var base64 = Convert.ToBase64String(content);
            return string.Join(
                '\n',
                Enumerable.Range(0, (base64.Length + 2) / 3)
                    .Select(index => base64.Substring(
                        index * 3,
                        Math.Min(3, base64.Length - (index * 3)))));
        }

        internal static bool VerifySignature(RpolSnapshotPayload payload, string base64SigningKey)
        {
            var expected = ComputeSignature(payload with { Signature = string.Empty }, base64SigningKey);
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(payload.Signature));
        }

        internal static string SanitizeHtml(string html)
        {
            ArgumentNullException.ThrowIfNull(html);
            var sanitized = LoginFormRegex().Replace(html, string.Empty);
            var userName = RuntimeSecretStoreUtility.GetRpolUserName();
            var password = RuntimeSecretStoreUtility.GetRpolPassword();
            if (!string.IsNullOrEmpty(userName))
            {
                sanitized = sanitized.Replace(userName, "[redacted]", StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrEmpty(password))
            {
                sanitized = sanitized.Replace(password, "[redacted]", StringComparison.Ordinal);
            }

            return sanitized;
        }

        internal static string PrepareSnapshotHtml(string html)
        {
            ArgumentNullException.ThrowIfNull(html);
            var title = SnapshotTitleRegex().Match(html).Groups["title"].Value;
            if (LooksLikeSnapshotChallengePage(html, title)
                || title.Contains("login", StringComparison.OrdinalIgnoreCase)
                || title.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "RPOL returned HTML without usable Scarlet Horizons page content.");
            }

            var sanitizedHtml = SanitizeHtml(html);
            if (!IsUsableSnapshotHtml(sanitizedHtml))
            {
                throw new InvalidOperationException(
                    "RPOL returned HTML without usable Scarlet Horizons page content.");
            }

            return sanitizedHtml;
        }

        internal static bool IsUsableSnapshotHtml(string sanitizedHtml)
        {
            var title = SnapshotTitleRegex().Match(sanitizedHtml).Groups["title"].Value;
            return IsUsableCampaignSnapshotHtml(sanitizedHtml)
                || (!string.IsNullOrWhiteSpace(sanitizedHtml)
                    && sanitizedHtml.Contains("<html", StringComparison.OrdinalIgnoreCase)
                    && sanitizedHtml.Contains(
                        "<meta name=\"player-assistant-snapshot\" content=\"dice-rolls\">",
                        StringComparison.OrdinalIgnoreCase)
                    && !LooksLikeSnapshotChallengePage(sanitizedHtml, title));
        }

        internal static bool IsUsableCampaignSnapshotHtml(string sanitizedHtml)
        {
            var title = SnapshotTitleRegex().Match(sanitizedHtml).Groups["title"].Value;
            var hasCampaignIdentity = sanitizedHtml.Length >= 1024
                && CampaignGameIdRegex().IsMatch(sanitizedHtml)
                && CampaignStructureRegex().IsMatch(sanitizedHtml)
                && (title.StartsWith(
                        "View RPoL: World of Issenda - Scarlet Horizons - ",
                        StringComparison.OrdinalIgnoreCase)
                    || title.Equals(
                        "RPoL: World of Issenda - Scarlet Horizons",
                        StringComparison.OrdinalIgnoreCase)
                    || title.Equals(
                        "World of Issenda - Scarlet Horizons Information - RPoL",
                        StringComparison.OrdinalIgnoreCase));
            return !string.IsNullOrWhiteSpace(sanitizedHtml)
                && sanitizedHtml.Contains("<html", StringComparison.OrdinalIgnoreCase)
                && hasCampaignIdentity
                && !LooksLikeSnapshotChallengePage(sanitizedHtml, title);
        }

        private static bool LooksLikeSnapshotChallengePage(string html, string title)
        {
            return html.Contains("cf-challenge", StringComparison.OrdinalIgnoreCase)
                || html.Contains("cf_clearance", StringComparison.OrdinalIgnoreCase)
                || title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
                || title.Contains("Verify you are human", StringComparison.OrdinalIgnoreCase);
        }

        internal static void ValidateSourceUri(Uri sourceUri)
        {
            NetworkUrlAllowlistUtility.EnsureAllowed(sourceUri, NetworkUrlPurpose.Rpol);
            var query = sourceUri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(
                    part => Uri.UnescapeDataString(part[0]),
                    part => part.Length == 2 ? Uri.UnescapeDataString(part[1]) : string.Empty,
                    StringComparer.OrdinalIgnoreCase);
            if (!query.TryGetValue("gi", out var gameId) || !string.Equals(gameId, GameId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"RPOL snapshots are restricted to game ID {GameId}.");
            }
        }

        internal static async Task<RpolSnapshotDiscovery> DiscoverSourceUrisAsync(
            CancellationToken cancellationToken,
            RpolCrossProcessLock lockOwner)
        {
            ArgumentNullException.ThrowIfNull(lockOwner);
            var rootUri = new Uri(AppSettingsUtility.GameForumUrl);
            var rootResponse = await RpolAuthUtility.GetSnapshotResponseAsync(rootUri, cancellationToken, lockOwner);
            var rootHtml = RpolAuthUtility.DecodeHtmlBody(rootResponse.Body, rootResponse.ContentType);
            var candidates = new List<Uri>
            {
                rootUri,
                new(AppSettingsUtility.GameIntroUrl),
                new(AppSettingsUtility.TheCastUrl),
                DiceRollerUri
            };
            candidates.AddRange(HtmlUtility.GetHyperlinksFromHtml(rootHtml, rootUri)
                .Where(link => IsApprovedLinkLabel(link.Text))
                .Select(link => Uri.TryCreate(link.Url, UriKind.Absolute, out var uri)
                    ? NormalizePublisherSourceUri(uri)
                    : null)
                .OfType<Uri>());

            var sourceUris = candidates
                .Where(uri =>
                {
                    try
                    {
                        ValidateSourceUri(uri);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new RpolSnapshotDiscovery(rootUri, rootHtml, sourceUris);
        }

        private static bool IsApprovedLinkLabel(string text)
        {
            return text.Equals("Game Links", StringComparison.OrdinalIgnoreCase)
                || text.Equals("Die Roller", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Ch ", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Notice: Ch", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("OOC", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Notice: OOC", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Aside -", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Notice: Aside -", StringComparison.OrdinalIgnoreCase);
        }

        internal sealed record RpolSnapshotDiscovery(Uri RootUri, string RootHtml, List<Uri> SourceUris);

        private static async Task UploadAsync(
            RpolSnapshotPayload payload,
            string adminKey,
            CancellationToken cancellationToken)
        {
            var endpoint = CreateEndpoint("snapshots/page");
            var json = SerializePayloadForUpload(payload);
            using var response = await NetworkRequestUtility.SendAsync(
                HttpClient,
                () =>
                {
                    var request = CreateRequest(HttpMethod.Put, endpoint, null);
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                    var nonce = Guid.NewGuid().ToString("N");
                    var canonical = string.Join('\n',
                        timestamp,
                        nonce,
                        "PUT",
                        "/v1/snapshots/page",
                        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
                    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(adminKey));
                    request.Headers.Add("X-Broker-Admin-Timestamp", timestamp);
                    request.Headers.Add("X-Broker-Admin-Nonce", nonce);
                    request.Headers.Add("Idempotency-Key", payload.ContentSha256);
                    request.Headers.Add(
                        "X-Broker-Admin-Signature",
                        Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))));
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    return request;
                },
                HttpCompletionOption.ResponseHeadersRead,
                purpose: NetworkUrlPurpose.PlayerAssistantBroker,
                cancellationToken: cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await NetworkRequestUtility.ReadStringAsync(
                    response.Content,
                    NetworkResponseContentLimit.JsonCache,
                    cancellationToken);
                throw new InvalidOperationException(
                    $"The RPOL snapshot broker returned HTTP {(int)response.StatusCode}: "
                    + SensitiveTextRedactionUtility.Redact(responseBody));
            }
        }

        private static async Task<bool> IsUploadCommittedAsync(
            RpolSnapshotCursorRecovery recovery,
            CancellationToken cancellationToken)
        {
            var remotePayload = await GetSnapshotPayloadAsync(
                new Uri(recovery.SourceUrl),
                allowUnavailable: true,
                verifySignature: false,
                cancellationToken);
            if (remotePayload is null) return false;
            if (!string.Equals(remotePayload.ContentSha256, recovery.PayloadSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The RPOL recovery readback found a different payload for the same source; reupload is blocked.");
            }
            return true;
        }

        internal static async Task ReconcilePendingUploadAsync(
            Func<Task<bool>> readbackCommittedAsync,
            Func<Task> uploadAsync,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(readbackCommittedAsync);
            ArgumentNullException.ThrowIfNull(uploadAsync);
            cancellationToken.ThrowIfCancellationRequested();
            if (await readbackCommittedAsync().ConfigureAwait(false)) return;
            await uploadAsync().ConfigureAwait(false);
        }

        private static async Task<RpolSnapshotPayload?> GetSnapshotPayloadAsync(
            Uri sourceUri,
            bool allowUnavailable,
            bool verifySignature,
            CancellationToken cancellationToken)
        {
            ValidateSourceUri(sourceUri);
            var token = RuntimeSecretStoreUtility.GetBrokerToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("The Player Assistant broker token is missing from Windows Credential Manager.");
            }

            var endpoint = CreateEndpoint("snapshots/page?url=" + Uri.EscapeDataString(sourceUri.AbsoluteUri));
            using var response = await NetworkRequestUtility.SendAsync(
                HttpClient,
                () => CreateRequest(HttpMethod.Get, endpoint, token),
                HttpCompletionOption.ResponseHeadersRead,
                purpose: NetworkUrlPurpose.PlayerAssistantBroker,
                cancellationToken: cancellationToken);
            if (allowUnavailable && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"The RPOL snapshot broker returned HTTP {(int)response.StatusCode} for '{sourceUri}'.");
            }

            var json = await NetworkRequestUtility.ReadStringAsync(
                response.Content,
                NetworkResponseContentLimit.JsonCache,
                cancellationToken);
            var payload = JsonSerializer.Deserialize<RpolSnapshotPayload>(json)
                ?? throw new InvalidOperationException("The RPOL snapshot broker returned an empty response.");
            ValidateResponse(payload, sourceUri);
            if (verifySignature)
            {
                var signingKey = RuntimeSecretStoreUtility.GetSnapshotSigningKey();
                if (string.IsNullOrWhiteSpace(signingKey)
                    || !string.Equals(payload.SignatureAlgorithm, SignatureAlgorithm, StringComparison.Ordinal)
                    || !VerifySignature(payload, signingKey))
                {
                    throw new InvalidOperationException("The RPOL snapshot response signature is invalid.");
                }
            }

            return payload;
        }

        private static async Task PublishSnapshotAsync(
            Uri sourceUri,
            string html,
            CancellationToken cancellationToken)
        {
            var adminKey = RuntimeSecretStoreUtility.GetBrokerAdminKey();
            var signingKey = RuntimeSecretStoreUtility.GetSnapshotSigningKey();
            if (string.IsNullOrWhiteSpace(adminKey) || string.IsNullOrWhiteSpace(signingKey))
            {
                throw new InvalidOperationException(
                    "Broker administrator and snapshot signing keys are required in Windows Credential Manager.");
            }

            if (sourceUri == DiceRollerUri)
            {
                html = GameForumUtility.NormalizeDieRollSnapshotHtml(html);
            }

            var sanitizedHtml = PrepareSnapshotHtml(html);

            await UploadAsync(
                CreatePayload(sourceUri, sanitizedHtml, "text/html; charset=utf-8", DateTimeOffset.UtcNow, signingKey),
                adminKey,
                cancellationToken);
        }

        private static async Task PublishNextSnapshotForStartupAsync(
            string statePath,
            RpolSnapshotPublisherState state,
            CancellationToken cancellationToken,
            RpolCrossProcessLock lockOwner)
        {
            try
            {
                var report = await PublishAsync(cancellationToken, lockOwner);
                if (report.Failed == 0)
                {
                    return;
                }

                throw new InvalidOperationException(
                    "RPOL snapshot refresh failed: " + string.Join("; ", report.Errors));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                if (state.SourceUrls.Count > 1)
                {
                    await SavePublisherStateAsync(
                        statePath,
                        AdvancePublisherState(state),
                        cancellationToken);
                }

                throw;
            }
        }

        internal static string SerializePayloadForUpload(RpolSnapshotPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            return JsonSerializer.Serialize(
                payload,
                new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string? bearerToken)
        {
            var request = new HttpRequestMessage(method, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            return request;
        }

        private static Uri CreateEndpoint(string relativePath)
        {
            if (!Uri.TryCreate(AppSettingsUtility.RpolBrokerUrl, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException("The RPOL Broker setting is missing or invalid.");
            }

            return new Uri(baseUri, relativePath);
        }

        private static string ComputeSignature(RpolSnapshotPayload payload, string base64SigningKey)
        {
            var canonical = string.Join('\n',
                payload.SchemaVersion.ToString(),
                payload.GameId,
                payload.SourceUrl,
                payload.FetchedAt,
                payload.ContentType,
                payload.ContentSha256);
            using var hmac = new HMACSHA256(Convert.FromBase64String(base64SigningKey));
            return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
        }

        private static void ValidateResponse(RpolSnapshotPayload payload, Uri requestedUri)
        {
            if (payload.SchemaVersion != SchemaVersion
                || !string.Equals(payload.GameId, GameId, StringComparison.Ordinal)
                || !string.Equals(payload.SourceUrl, requestedUri.AbsoluteUri, StringComparison.Ordinal)
                || !payload.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The RPOL snapshot response metadata is invalid.");
            }

            var content = Convert.FromBase64String(payload.ContentBase64);
            if (content.Length == 0 || content.Length > NetworkResponseContentLimit.Html.MaxBytes
                || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), payload.ContentSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The RPOL snapshot response content failed validation.");
            }
        }

        [GeneratedRegex("""<form\b[^>]*action\s*=\s*(['"]?)/login\.cgi\1[^>]*>.*?</form>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex LoginFormRegex();

        [GeneratedRegex("""<title\b[^>]*>(?<title>.*?)</title>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex SnapshotTitleRegex();

        [GeneratedRegex("""class\s*=\s*(['"])[^'"]*\b(message|threadstate|info_box|two-aside|sidebar)\b[^'"]*\1""", RegexOptions.IgnoreCase)]
        private static partial Regex CampaignStructureRegex();

        [GeneratedRegex("""(?:\?|&amp;|&)gi=80170(?:&amp;|&|['"\s<>]|$)""", RegexOptions.IgnoreCase)]
        private static partial Regex CampaignGameIdRegex();
    }
}
