using PlayerAssistant;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PlayerAssistant.Tests;

internal static class RepositoryRootDiscovery
{
    private const string ProjectMarker = "player-assistant.csproj";

    internal static string Find(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (directory is not null)
        {
            var projectMarker = Path.Combine(directory.FullName, ProjectMarker);
            var regressionMarker = Path.Combine(directory.FullName, "web-deploy", "tests", "login-hardening-tests.php");
            if (File.Exists(projectMarker) && File.Exists(regressionMarker))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Unable to locate the Player Assistant repository from '{startDirectory}'.");
    }
}

internal sealed class BlockingTranslatorService : ITranslatorService, IDisposable
{
    public ManualResetEventSlim FirstTranslationStarted { get; } = new();

    public ManualResetEventSlim FirstTranslationCanceled { get; } = new();

    public TranslatorTargetLanguage LastTargetLanguage { get; private set; }

    public bool IsReady(TranslatorTargetLanguage targetLanguage) => true;

    public Task<int> WarmUpAsync(TranslatorTargetLanguage targetLanguage, CancellationToken cancellationToken) =>
        Task.FromResult(1);

    public async Task<string> TranslateAsync(
        string input,
        TranslatorTargetLanguage targetLanguage,
        bool targetToEnglish,
        CancellationToken cancellationToken)
    {
        LastTargetLanguage = targetLanguage;
        if (input == "hello")
        {
            FirstTranslationStarted.Set();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                FirstTranslationCanceled.Set();
                throw;
            }
        }

        return "translated:" + input;
    }

    public void Dispose()
    {
        FirstTranslationStarted.Dispose();
        FirstTranslationCanceled.Dispose();
    }
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
        LastReadOutputBytes = null; storedSecret = null; return false;
    }
    public void Write(string targetName, byte[] secretBytes, string? comment = null)
    {
        LastWriteInputBytes = secretBytes;
        _secrets[targetName] = new StoredSecret([.. secretBytes], DateTimeOffset.UtcNow);
    }
    public void Delete(string targetName) => _secrets.Remove(targetName);
}

internal sealed class FaultInjectingCredentialStoreBackend : IWindowsCredentialStoreBackend
{
    private readonly Dictionary<string, StoredSecret> _secrets = new(StringComparer.Ordinal);
    private readonly int _failOnOperation;
    private int _operationCount;

    internal FaultInjectingCredentialStoreBackend(int failOnOperation) => _failOnOperation = failOnOperation;
    internal int StoredCount => _secrets.Count;

    public bool TryRead(string targetName, out StoredSecret? storedSecret)
    {
        if (_secrets.TryGetValue(targetName, out var existing))
        {
            storedSecret = existing with { SecretBytes = [.. existing.SecretBytes] };
            return true;
        }
        storedSecret = null;
        return false;
    }

    public void Write(string targetName, byte[] secretBytes, string? comment = null)
    {
        FaultIfNeeded();
        _secrets[targetName] = new StoredSecret([.. secretBytes], DateTimeOffset.UtcNow);
    }

    public void Delete(string targetName)
    {
        FaultIfNeeded();
        _secrets.Remove(targetName);
    }

    private void FaultIfNeeded()
    {
        if (++_operationCount == _failOnOperation)
            throw new InvalidOperationException("synthetic credential-store fault");
    }
}
