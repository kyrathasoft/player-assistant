using System.Net;

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

    internal static class NetworkRequestUtility
    {
        private const int CircuitBreakerFailureThreshold = 2;
        private static readonly TimeSpan CircuitBreakerCooldown = TimeSpan.FromMinutes(5);
        private static readonly object CircuitBreakerSyncRoot = new();
        private static readonly Dictionary<string, NetworkCircuitBreakerState> CircuitBreakers = new(StringComparer.OrdinalIgnoreCase);

        public static async Task<HttpResponseMessage> SendAsync(
            HttpClient httpClient,
            Func<HttpRequestMessage> createRequest,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            NetworkRequestPolicy? policy = null,
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
                    if (request.RequestUri is not null)
                    {
                        NetworkUrlAllowlistUtility.EnsureAllowed(request.RequestUri);
                    }

                    var circuitBreakerKey = GetCircuitBreakerKey(request);
                    ThrowIfCircuitOpen(circuitBreakerKey, DateTimeOffset.Now);
                    var response = await httpClient.SendAsync(
                        request,
                        completionOption,
                        timeoutCancellation.Token).ConfigureAwait(false);
                    if (response.RequestMessage?.RequestUri is not null)
                    {
                        NetworkUrlAllowlistUtility.EnsureAllowed(response.RequestMessage.RequestUri);
                    }

                    if (ShouldRetry(response.StatusCode) && attempt < policy.MaxAttempts)
                    {
                        response.Dispose();
                        await DelayBeforeRetryAsync(policy, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (ShouldRetry(response.StatusCode))
                    {
                        RecordCircuitFailure(
                            circuitBreakerKey,
                            $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim(),
                            DateTimeOffset.Now);
                    }
                    else
                    {
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
                        RecordCircuitFailureFromException(createRequest, exception);
                        throw exception;
                    }

                    await DelayBeforeRetryAsync(policy, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException) when (attempt < policy.MaxAttempts)
                {
                    await DelayBeforeRetryAsync(policy, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException) when (attempt < policy.MaxAttempts)
                {
                    await DelayBeforeRetryAsync(policy, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    var exception = new NetworkRequestException(
                        NetworkFailureKind.Unavailable,
                        $"The network request failed: {ex.Message}",
                        ex);
                    RecordCircuitFailureFromException(createRequest, exception);
                    throw exception;
                }
                catch (IOException ex)
                {
                    var exception = new NetworkRequestException(
                        NetworkFailureKind.Unavailable,
                        $"The network request failed: {ex.Message}",
                        ex);
                    RecordCircuitFailureFromException(createRequest, exception);
                    throw exception;
                }
            }
        }

        public static HttpResponseMessage Send(
            HttpClient httpClient,
            Func<HttpRequestMessage> createRequest,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
            NetworkRequestPolicy? policy = null)
        {
            return SendAsync(
                httpClient,
                createRequest,
                completionOption,
                policy,
                CancellationToken.None).GetAwaiter().GetResult();
        }

        public static HttpClient CreateHttpClient()
        {
            var httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PlayerAssistant/1.0");
            return httpClient;
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

        internal static void ResetCircuitBreakersForTests()
        {
            lock (CircuitBreakerSyncRoot)
            {
                CircuitBreakers.Clear();
            }
        }

        private static string GetCircuitBreakerKey(HttpRequestMessage request)
        {
            var uri = request.RequestUri;
            if (uri is null || !uri.IsAbsoluteUri)
            {
                return $"{request.Method.Method} <relative>";
            }

            return $"{request.Method.Method} {uri.Scheme}://{uri.Authority}";
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
            NetworkRequestException exception)
        {
            using var request = createRequest();
            RecordCircuitFailure(
                GetCircuitBreakerKey(request),
                exception.Message,
                DateTimeOffset.Now);
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
