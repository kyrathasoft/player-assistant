using System.Net;
using System.Net.Security;
using System.Text;

namespace PlayerAssistant
{
    internal enum NetworkFailureKind
    {
        Unavailable,
        TimedOut,
        CircuitOpen
    }

    internal sealed class NetworkRequestException : InvalidOperationException
    {
        public NetworkRequestException(NetworkFailureKind kind, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Kind = kind;
        }

        public NetworkFailureKind Kind { get; }
    }

    internal sealed record NetworkRequestPolicy(TimeSpan Timeout, int MaxAttempts, TimeSpan RetryDelay)
    {
        public static NetworkRequestPolicy Default { get; } = new(
            TimeSpan.FromSeconds(30),
            MaxAttempts: 3,
            TimeSpan.FromMilliseconds(500));
    }

    internal sealed record NetworkResponseContentLimit(string Description, long MaxBytes)
    {
        public static NetworkResponseContentLimit Html { get; } = new("HTML response", 5L * 1024L * 1024L);
        public static NetworkResponseContentLimit Markdown { get; } = new("markdown response", 2L * 1024L * 1024L);
        public static NetworkResponseContentLimit JsonCache { get; } = new("JSON cache response", 10L * 1024L * 1024L);
        public static NetworkResponseContentLimit Image { get; } = new("image response", 25L * 1024L * 1024L);
        public static NetworkResponseContentLimit InstallerPackage { get; } = new("installer package", 250L * 1024L * 1024L);
    }

    internal sealed class NetworkResponseTooLargeException : InvalidOperationException
    {
        public NetworkResponseTooLargeException(NetworkResponseContentLimit limit, long actualBytes)
            : base($"{limit.Description} exceeded the {FormatByteCount(limit.MaxBytes)} limit ({FormatByteCount(actualBytes)} received).")
        {
            Limit = limit;
            ActualBytes = actualBytes;
        }

        public NetworkResponseContentLimit Limit { get; }

        public long ActualBytes { get; }

        private static string FormatByteCount(long bytes)
        {
            return bytes >= 1024L * 1024L
                ? $"{bytes / 1024d / 1024d:0.#} MB"
                : $"{bytes / 1024d:0.#} KB";
        }
    }

    internal static class NetworkRequestUtility
    {
        private const int CopyBufferSize = 81920;
        private const int MaxRedirects = 5;
        private const int CircuitBreakerFailureThreshold = 2;
        private static readonly TimeSpan CircuitBreakerCooldown = TimeSpan.FromMinutes(5);
        private static readonly object CircuitBreakerSyncRoot = new();
        private static readonly Dictionary<string, NetworkCircuitBreakerState> CircuitBreakers = new(StringComparer.OrdinalIgnoreCase);

        public static async Task<HttpResponseMessage> SendAsync(
            HttpClient httpClient,
            Func<HttpRequestMessage> createRequest,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            NetworkRequestPolicy? policy = null,
            NetworkUrlPurpose purpose = NetworkUrlPurpose.Generic,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentNullException.ThrowIfNull(createRequest);

            policy ??= NetworkRequestPolicy.Default;
            if (policy.MaxAttempts <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(policy), "Network request attempts must be greater than zero.");
            }

            for (var attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCancellation.CancelAfter(policy.Timeout);

                try
                {
                    using var request = createRequest();
                    if (request.RequestUri is null)
                    {
                        throw new InvalidOperationException("Network request URI is missing.");
                    }

                    NetworkUrlAllowlistUtility.EnsureAllowed(request.RequestUri, purpose);

                    var circuitBreakerKey = GetCircuitBreakerKey(request, purpose);
                    ThrowIfCircuitOpen(circuitBreakerKey, DateTimeOffset.Now);
                    var response = await SendWithValidatedRedirectsAsync(
                        httpClient,
                        request,
                        completionOption,
                        purpose,
                        timeoutCancellation.Token).ConfigureAwait(false);
                    if (response.RequestMessage?.RequestUri is not null)
                    {
                        NetworkUrlAllowlistUtility.EnsureAllowed(response.RequestMessage.RequestUri, purpose);
                    }

                    if (ShouldRetry(response.StatusCode) && attempt < policy.MaxAttempts)
                    {
                        OutboundNetworkDiagnosticsUtility.RecordRetry(request, purpose);
                        response.Dispose();
                        await DelayBeforeRetryAsync(policy, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (ShouldRetry(response.StatusCode))
                    {
                        OutboundNetworkDiagnosticsUtility.RecordFailure(
                            request,
                            purpose,
                            failureKind: null,
                            response.StatusCode,
                            $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim());
                        RecordCircuitFailure(
                            circuitBreakerKey,
                            $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim(),
                            DateTimeOffset.Now);
                    }
                    else
                    {
                        OutboundNetworkDiagnosticsUtility.RecordSuccess(request, purpose, response.StatusCode);
                        RecordCircuitSuccess(circuitBreakerKey);
                    }

                    return response;
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    if (attempt >= policy.MaxAttempts)
                    {
                        var exception = new NetworkRequestException(
                            NetworkFailureKind.TimedOut,
                            $"The network request timed out after {policy.Timeout.TotalSeconds:0.#} seconds.",
                            ex);
                        RecordOutboundFailureFromException(createRequest, purpose, exception);
                        RecordCircuitFailureFromException(createRequest, purpose, exception);
                        throw exception;
                    }

                    RecordOutboundRetry(createRequest, purpose);
                    await DelayBeforeRetryAsync(policy, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException) when (attempt < policy.MaxAttempts)
                {
                    RecordOutboundRetry(createRequest, purpose);
                    await DelayBeforeRetryAsync(policy, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException) when (attempt < policy.MaxAttempts)
                {
                    RecordOutboundRetry(createRequest, purpose);
                    await DelayBeforeRetryAsync(policy, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    var exception = new NetworkRequestException(
                        NetworkFailureKind.Unavailable,
                        $"The network request failed: {ex.Message}",
                        ex);
                    RecordOutboundFailureFromException(createRequest, purpose, exception);
                    RecordCircuitFailureFromException(createRequest, purpose, exception);
                    throw exception;
                }
                catch (IOException ex)
                {
                    var exception = new NetworkRequestException(
                        NetworkFailureKind.Unavailable,
                        $"The network request failed: {ex.Message}",
                        ex);
                    RecordOutboundFailureFromException(createRequest, purpose, exception);
                    RecordCircuitFailureFromException(createRequest, purpose, exception);
                    throw exception;
                }
            }
        }

        public static HttpResponseMessage Send(
            HttpClient httpClient,
            Func<HttpRequestMessage> createRequest,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            NetworkRequestPolicy? policy = null,
            NetworkUrlPurpose purpose = NetworkUrlPurpose.Generic)
        {
            return SendAsync(
                httpClient,
                createRequest,
                completionOption,
                policy,
                purpose,
                CancellationToken.None).GetAwaiter().GetResult();
        }

        public static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = static (requestMessage, certificate, chain, sslPolicyErrors) =>
                    CertificatePinningUtility.ValidateServerCertificate(
                        requestMessage,
                        certificate,
                        chain,
                        sslPolicyErrors)
            };
            return CreateHttpClient(handler);
        }

        public static async Task<string> ReadStringAsync(
            HttpContent content,
            NetworkResponseContentLimit limit,
            CancellationToken cancellationToken = default)
        {
            var bytes = await ReadBytesAsync(content, limit, cancellationToken).ConfigureAwait(false);
            return GetEncoding(content).GetString(bytes);
        }

        public static async Task<byte[]> ReadBytesAsync(
            HttpContent content,
            NetworkResponseContentLimit limit,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(limit);
            ValidateLimit(limit);
            ThrowIfContentLengthExceedsLimit(content, limit);

            await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var destination = new MemoryStream();
            await CopyToAsync(source, destination, limit, cancellationToken).ConfigureAwait(false);
            return destination.ToArray();
        }

        public static async Task<byte[]> ReadBytesAsync(
            HttpContent content,
            NetworkResponseContentLimit limit,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(timeout);
            try
            {
                return await ReadBytesAsync(content, limit, timeoutCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new NetworkRequestException(
                    NetworkFailureKind.TimedOut,
                    $"The network response body timed out after {timeout.TotalSeconds:0.#} seconds.",
                    ex);
            }
        }

        public static async Task CopyToAsync(
            HttpContent content,
            Stream destination,
            NetworkResponseContentLimit limit,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(content);
            ThrowIfContentLengthExceedsLimit(content, limit);

            await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await CopyToAsync(source, destination, limit, cancellationToken).ConfigureAwait(false);
        }

        public static async Task CopyToAsync(
            Stream source,
            Stream destination,
            NetworkResponseContentLimit limit,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(limit);
            ValidateLimit(limit);

            var buffer = new byte[CopyBufferSize];
            long totalBytes = 0;
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return;
                }

                totalBytes += bytesRead;
                if (totalBytes > limit.MaxBytes)
                {
                    throw new NetworkResponseTooLargeException(limit, totalBytes);
                }

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }
        }

        public static void EnsureByteCountWithinLimit(
            long actualBytes,
            NetworkResponseContentLimit limit)
        {
            ArgumentNullException.ThrowIfNull(limit);
            ValidateLimit(limit);
            if (actualBytes > limit.MaxBytes)
            {
                throw new NetworkResponseTooLargeException(limit, actualBytes);
            }
        }

        internal static HttpClient CreateHttpClient(HttpMessageHandler handler)
        {
            var httpClient = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PlayerAssistant/1.0");
            return httpClient;
        }

        private static async Task<HttpResponseMessage> SendWithValidatedRedirectsAsync(
            HttpClient httpClient,
            HttpRequestMessage request,
            HttpCompletionOption completionOption,
            NetworkUrlPurpose purpose,
            CancellationToken cancellationToken)
        {
            var currentRequest = request;
            for (var redirectCount = 0; ; redirectCount++)
            {
                var response = await httpClient.SendAsync(
                    currentRequest,
                    completionOption,
                    cancellationToken).ConfigureAwait(false);
                if (!IsRedirect(response.StatusCode) || response.Headers.Location is not { } location)
                {
                    return response;
                }

                if (redirectCount >= MaxRedirects)
                {
                    response.Dispose();
                    throw new InvalidOperationException($"Network request exceeded the {MaxRedirects}-redirect limit.");
                }

                var baseUri = currentRequest.RequestUri;
                if (baseUri is null || !Uri.TryCreate(baseUri, location, out var redirectUri))
                {
                    response.Dispose();
                    throw new InvalidOperationException("Network redirect target is not a valid URI.");
                }

                NetworkUrlAllowlistUtility.EnsureAllowed(redirectUri, purpose);
                var nextRequest = await CloneRequestAsync(currentRequest, redirectUri, cancellationToken).ConfigureAwait(false);
                if (!ReferenceEquals(currentRequest, request))
                {
                    currentRequest.Dispose();
                }

                response.Dispose();
                currentRequest = nextRequest;
            }
        }

        private static async Task<HttpRequestMessage> CloneRequestAsync(
            HttpRequestMessage request,
            Uri requestUri,
            CancellationToken cancellationToken)
        {
            var clone = new HttpRequestMessage(request.Method, requestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy
            };
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
                foreach (var header in request.Content.Headers)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                clone.Content = content;
            }

            return clone;
        }

        private static bool IsRedirect(HttpStatusCode statusCode)
        {
            return statusCode is HttpStatusCode.Moved
                or HttpStatusCode.Redirect
                or HttpStatusCode.RedirectMethod
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect;
        }

        private static void ThrowIfContentLengthExceedsLimit(HttpContent content, NetworkResponseContentLimit limit)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(limit);
            ValidateLimit(limit);

            if (content.Headers.ContentLength is { } contentLength)
            {
                EnsureByteCountWithinLimit(contentLength, limit);
            }
        }

        private static Encoding GetEncoding(HttpContent content)
        {
            var charset = content.Headers.ContentType?.CharSet;
            if (!string.IsNullOrWhiteSpace(charset))
            {
                try
                {
                    return Encoding.GetEncoding(charset.Trim('"'));
                }
                catch (ArgumentException)
                {
                }
            }

            return Encoding.UTF8;
        }

        private static void ValidateLimit(NetworkResponseContentLimit limit)
        {
            if (limit.MaxBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit), "Network response content limits must be greater than zero.");
            }
        }

        internal static void ResetCircuitBreakersForTests()
        {
            lock (CircuitBreakerSyncRoot)
            {
                CircuitBreakers.Clear();
            }
        }

        private static string GetCircuitBreakerKey(HttpRequestMessage request, NetworkUrlPurpose purpose)
        {
            var uri = request.RequestUri;
            if (uri is null || !uri.IsAbsoluteUri)
            {
                return $"{purpose}:{request.Method.Method} <relative>";
            }

            var path = uri.AbsolutePath.Trim('/');
            var endpointFamily = path.Length == 0 ? "/" : "/" + path.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
            return $"{purpose}:{request.Method.Method} {uri.Scheme}://{uri.Authority}{endpointFamily}";
        }

        private static void ThrowIfCircuitOpen(string circuitBreakerKey, DateTimeOffset now)
        {
            lock (CircuitBreakerSyncRoot)
            {
                if (!CircuitBreakers.TryGetValue(circuitBreakerKey, out var state)
                    || state.OpenedAt is null)
                {
                    return;
                }

                var elapsed = now - state.OpenedAt.Value;
                if (elapsed >= CircuitBreakerCooldown)
                {
                    CircuitBreakers.Remove(circuitBreakerKey);
                    return;
                }

                throw new NetworkRequestException(
                    NetworkFailureKind.CircuitOpen,
                    $"Network circuit breaker is open for {circuitBreakerKey}. Last failure: {state.LastFailure}. Retry after {CircuitBreakerCooldown - elapsed:hh\\:mm\\:ss}.");
            }
        }

        private static void RecordCircuitSuccess(string circuitBreakerKey)
        {
            lock (CircuitBreakerSyncRoot)
            {
                CircuitBreakers.Remove(circuitBreakerKey);
            }
        }

        private static void RecordCircuitFailure(string circuitBreakerKey, string failure, DateTimeOffset now)
        {
            lock (CircuitBreakerSyncRoot)
            {
                var failureCount = 1;
                DateTimeOffset? openedAt = null;
                if (CircuitBreakers.TryGetValue(circuitBreakerKey, out var state))
                {
                    failureCount = state.FailureCount + 1;
                    openedAt = state.OpenedAt;
                }

                if (failureCount >= CircuitBreakerFailureThreshold && openedAt is null)
                {
                    openedAt = now;
                    StartupLoggingUtility.Append(
                        "network circuit breaker",
                        $"Opened circuit for {circuitBreakerKey} after {failureCount} terminal failure(s). Last failure: {failure}");
                }

                CircuitBreakers[circuitBreakerKey] = new NetworkCircuitBreakerState(
                    failureCount,
                    openedAt,
                    failure);
            }
        }

        private static void RecordCircuitFailureFromException(
            Func<HttpRequestMessage> createRequest,
            NetworkUrlPurpose purpose,
            NetworkRequestException exception)
        {
            using var request = createRequest();
            RecordCircuitFailure(
                GetCircuitBreakerKey(request, purpose),
                exception.Message,
                DateTimeOffset.Now);
        }

        private static void RecordOutboundFailureFromException(
            Func<HttpRequestMessage> createRequest,
            NetworkUrlPurpose purpose,
            NetworkRequestException exception)
        {
            using var request = createRequest();
            OutboundNetworkDiagnosticsUtility.RecordFailure(
                request,
                purpose,
                exception.Kind,
                statusCode: null,
                exception.Message);
        }

        private static void RecordOutboundRetry(
            Func<HttpRequestMessage> createRequest,
            NetworkUrlPurpose purpose)
        {
            using var request = createRequest();
            OutboundNetworkDiagnosticsUtility.RecordRetry(request, purpose);
        }

        private static bool ShouldRetry(HttpStatusCode statusCode)
        {
            return statusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                || (int)statusCode >= 500;
        }

        private static Task DelayBeforeRetryAsync(NetworkRequestPolicy policy, CancellationToken cancellationToken)
        {
            return policy.RetryDelay <= TimeSpan.Zero
                ? Task.CompletedTask
                : Task.Delay(policy.RetryDelay, cancellationToken);
        }

        private sealed record NetworkCircuitBreakerState(
            int FailureCount,
            DateTimeOffset? OpenedAt,
            string LastFailure);
    }
}
