using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
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
        IReadOnlyList<string> Errors);

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
        private static readonly Uri DiceRollerUri = new($"https://rpol.net/usermodules/diceroller.cgi?gi={GameId}");
        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false
        });

        public static async Task<string> GetHtmlAsync(Uri sourceUri, CancellationToken cancellationToken = default)
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
            return Encoding.UTF8.GetString(Convert.FromBase64String(payload.ContentBase64));
        }

        public static async Task<RpolSnapshotPublishReport> PublishAsync(CancellationToken cancellationToken = default)
        {
            var adminKey = RuntimeSecretStoreUtility.GetBrokerAdminKey();
            var signingKey = RuntimeSecretStoreUtility.GetSnapshotSigningKey();
            if (string.IsNullOrWhiteSpace(adminKey) || string.IsNullOrWhiteSpace(signingKey))
            {
                throw new InvalidOperationException(
                    "Broker administrator and snapshot signing keys are required in Windows Credential Manager.");
            }

            var statePath = RuntimePathUtility.GetUserDataPath(PublisherStateFileName);
            var state = LoadPublisherState(statePath);
            RpolSnapshotDiscovery? discovery = null;
            if (state is null)
            {
                discovery = await DiscoverSourceUrisAsync(cancellationToken);
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
                    var response = await RpolAuthUtility.GetSnapshotResponseAsync(sourceUri, cancellationToken);
                    html = Encoding.UTF8.GetString(response.Body);
                    contentType = response.ContentType ?? "text/html; charset=utf-8";
                }

                if (sourceUri == DiceRollerUri)
                {
                    html = GameForumUtility.NormalizeDieRollSnapshotHtml(html);
                }

                var sanitizedHtml = SanitizeHtml(html);
                if (!IsUsableSnapshotHtml(sanitizedHtml))
                {
                    throw new InvalidOperationException(
                        "RPOL returned HTML without usable Scarlet Horizons page content.");
                }

                var payload = CreatePayload(
                    sourceUri,
                    sanitizedHtml,
                    contentType,
                    DateTimeOffset.UtcNow,
                    signingKey);
                await UploadAsync(payload, adminKey, cancellationToken);
                await SavePublisherStateAsync(
                    statePath,
                    AdvancePublisherState(state),
                    cancellationToken);
                return new RpolSnapshotPublishReport(state.SourceUrls.Count, 1, 0, []);
            }
            catch (Exception ex)
            {
                var error = $"{sourceUri}: {SensitiveTextRedactionUtility.Redact(ex.Message)}";
                return new RpolSnapshotPublishReport(state.SourceUrls.Count, 0, 1, [error]);
            }
        }

        internal static RpolSnapshotPublisherState CreatePublisherState(IEnumerable<Uri> sourceUris)
        {
            ArgumentNullException.ThrowIfNull(sourceUris);
            var urls = sourceUris
                .Select(uri =>
                {
                    ValidateSourceUri(uri);
                    return uri.AbsoluteUri;
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
            var missingUris = new[] { DiceRollerUri }
                .Where(requiredUri => !state.SourceUrls.Contains(
                    requiredUri.AbsoluteUri,
                    StringComparer.OrdinalIgnoreCase))
                .Select(requiredUri => requiredUri.AbsoluteUri)
                .ToArray();
            if (missingUris.Length == 0)
            {
                return state;
            }

            var sourceUrls = state.SourceUrls.ToList();
            sourceUrls.InsertRange(state.NextIndex, missingUris);
            return state with { SourceUrls = sourceUrls };
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
                Convert.ToBase64String(content),
                SignatureAlgorithm,
                string.Empty);
            return unsigned with { Signature = ComputeSignature(unsigned, base64SigningKey) };
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

        internal static bool IsUsableSnapshotHtml(string sanitizedHtml)
        {
            return !string.IsNullOrWhiteSpace(sanitizedHtml)
                && sanitizedHtml.Length >= 1024
                && sanitizedHtml.Contains("<html", StringComparison.OrdinalIgnoreCase)
                && sanitizedHtml.Contains("Scarlet Horizons", StringComparison.OrdinalIgnoreCase)
                && !RpolAuthUtility.LooksLikeCloudflareChallengePage(sanitizedHtml)
                && !LoginFormRegex().IsMatch(sanitizedHtml);
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

        private static async Task<RpolSnapshotDiscovery> DiscoverSourceUrisAsync(CancellationToken cancellationToken)
        {
            var rootUri = new Uri(AppSettingsUtility.GameForumUrl);
            var rootHtml = await RpolAuthUtility.GetHtmlFromUrlAsync(rootUri, cancellationToken);
            var candidates = new List<Uri>
            {
                rootUri,
                new(AppSettingsUtility.GameIntroUrl),
                new(AppSettingsUtility.TheCastUrl),
                DiceRollerUri
            };
            candidates.AddRange(HtmlUtility.GetHyperlinksFromHtml(rootHtml, rootUri)
                .Where(link => IsApprovedLinkLabel(link.Text))
                .Select(link => Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) ? uri : null)
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

        private sealed record RpolSnapshotDiscovery(Uri RootUri, string RootHtml, List<Uri> SourceUris);

        private static async Task UploadAsync(
            RpolSnapshotPayload payload,
            string adminKey,
            CancellationToken cancellationToken)
        {
            var endpoint = CreateEndpoint("snapshots/page");
            var json = JsonSerializer.Serialize(payload);
            using var response = await NetworkRequestUtility.SendAsync(
                HttpClient,
                () =>
                {
                    var request = CreateRequest(HttpMethod.Put, endpoint, null);
                    request.Headers.Add("X-Broker-Admin-Key", adminKey);
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
    }
}
