using System.Net;

namespace PlayerAssistant.Tests;

internal static partial class TestCases
{
    internal static void NetworkResponseBodyDeadlineCancelsStalledRead()
    {
        using var content = new StreamContent(new StallingResponseStream());
        var exception = AssertThrows<NetworkRequestException>(() =>
            NetworkRequestUtility.ReadBytesAsync(
                content,
                NetworkResponseContentLimit.JsonCache,
                TimeSpan.FromMilliseconds(50)).GetAwaiter().GetResult());
        AssertEqual(NetworkFailureKind.TimedOut, exception.Kind, "stalled response body should report policy timeout");
    }

    private sealed class StallingResponseStream : Stream
    {
        private bool sentPrefix;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 2;
        public override long Position { get; set; }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!sentPrefix)
            {
                sentPrefix = true;
                buffer.Span[0] = (byte)'{';
                return 1;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
