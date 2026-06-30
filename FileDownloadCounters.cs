namespace PlayerAssistant
{
    internal static class FileDownloadCounters
    {
        public static int CompletedDownloadCount { get; private set; }
        public static double CompletedDownloadBytes { get; private set; }

        public static void Reset()
        {
            CompletedDownloadCount = 0;
            CompletedDownloadBytes = 0;
        }

        public static void AddCompletedDownload(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            CompletedDownloadCount++;
            CompletedDownloadBytes += new FileInfo(filePath).Length;
        }
    }
}
