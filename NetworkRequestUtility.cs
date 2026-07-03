using System.Net;

namespace PlayerAssistant
{
    internal enum NetworkFailureKind
    {
        Unavailable,
        TimedOut
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
                    var response = await httpClient.SendAsync(
                        request,
                        completionOption,
                        timeoutCancellation.Token).ConfigureAwait(false);

                    if (ShouldRetry(response.StatusCode) && attempt < policy.MaxAttempts)
                    {
                        response.Dispose();
                        await DelayBeforeRetryAsync(policy, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    return response;
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    if (attempt >= policy.MaxAttempts)
                    {
                        throw new NetworkRequestException(
                            NetworkFailureKind.TimedOut,
                            $"The network request timed out after {policy.Timeout.TotalSeconds:0.#} seconds.",
                            ex);
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
                    throw new NetworkRequestException(
                        NetworkFailureKind.Unavailable,
                        $"The network request failed: {ex.Message}",
                        ex);
                }
                catch (IOException ex)
                {
                    throw new NetworkRequestException(
                        NetworkFailureKind.Unavailable,
                        $"The network request failed: {ex.Message}",
                        ex);
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
    }
}
