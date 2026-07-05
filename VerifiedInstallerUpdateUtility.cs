using System.Security.Cryptography;

namespace PlayerAssistant
{
    internal sealed record VerifiedInstallerDownloadResult(
        string InstallerPath,
        string Sha256,
        AuthenticodeSignatureInfo Signature,
        bool ReusedExistingFile);

    internal static class VerifiedInstallerUpdateUtility
    {
        private const string UpdatesDirectoryName = "updates";

        public static Task<VerifiedInstallerDownloadResult> DownloadVerifiedInstallerAsync(
            HttpClient httpClient,
            PlayerAssistantUpdateInfo update,
            CancellationToken cancellationToken = default)
        {
            return DownloadVerifiedInstallerAsync(
                httpClient,
                update,
                AuthenticodeSignatureUtility.GetCurrentProcessSignaturePolicy(),
                AuthenticodeSignatureUtility.InspectSignature,
                RuntimePathUtility.GetUserDataPath(UpdatesDirectoryName),
                cancellationToken);
        }

        internal static async Task<VerifiedInstallerDownloadResult> DownloadVerifiedInstallerAsync(
            HttpClient httpClient,
            PlayerAssistantUpdateInfo update,
            AuthenticodeSignaturePolicy policy,
            Func<string, AuthenticodeSignatureInfo> inspectSignature,
            string downloadDirectory,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentNullException.ThrowIfNull(update);
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(inspectSignature);
            ArgumentException.ThrowIfNullOrWhiteSpace(downloadDirectory);

            Directory.CreateDirectory(downloadDirectory);
            var destinationPath = GetInstallerPath(update, downloadDirectory);
            if (await TryReuseExistingInstallerAsync(destinationPath, update, policy, inspectSignature, cancellationToken).ConfigureAwait(false) is { } existing)
            {
                return existing;
            }

            var tempPath = AtomicFileUtility.CreateTempPath(destinationPath, ".download");
            try
            {
                using var response = await NetworkRequestUtility.SendAsync(
                    httpClient,
                    () => new HttpRequestMessage(HttpMethod.Get, update.InstallerUri),
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using (var outputStream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true))
                {
                    await NetworkRequestUtility.CopyToAsync(
                        response.Content,
                        outputStream,
                        NetworkResponseContentLimit.InstallerPackage,
                        cancellationToken).ConfigureAwait(false);
                }

                var sha256 = ComputeSha256(tempPath);
                if (!string.Equals(sha256, NormalizeSha256(update.InstallerSha256), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Downloaded installer SHA256 '{sha256}' did not match the signed manifest value '{NormalizeSha256(update.InstallerSha256)}'.");
                }

                AuthenticodeSignatureUtility.AssertMatchesPolicy(tempPath, policy, inspectSignature);
                var signature = inspectSignature(tempPath);
                await AtomicFileUtility.PromoteTempFileAsync(tempPath, destinationPath, cancellationToken).ConfigureAwait(false);
                return new VerifiedInstallerDownloadResult(destinationPath, sha256, signature, ReusedExistingFile: false);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
            }
        }

        private static async Task<VerifiedInstallerDownloadResult?> TryReuseExistingInstallerAsync(
            string destinationPath,
            PlayerAssistantUpdateInfo update,
            AuthenticodeSignaturePolicy policy,
            Func<string, AuthenticodeSignatureInfo> inspectSignature,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(destinationPath))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var sha256 = ComputeSha256(destinationPath);
            if (!string.Equals(sha256, NormalizeSha256(update.InstallerSha256), StringComparison.Ordinal))
            {
                return null;
            }

            AuthenticodeSignatureUtility.AssertMatchesPolicy(destinationPath, policy, inspectSignature);
            return new VerifiedInstallerDownloadResult(
                destinationPath,
                sha256,
                inspectSignature(destinationPath),
                ReusedExistingFile: true);
        }

        private static string GetInstallerPath(PlayerAssistantUpdateInfo update, string downloadDirectory)
        {
            var fileName = Path.GetFileName(update.InstallerUri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName)
                || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The signed update manifest did not provide a valid installer file name.");
            }

            return RuntimePathUtility.EnsurePathUnderBase(
                downloadDirectory,
                Path.Combine(downloadDirectory, fileName));
        }

        private static string ComputeSha256(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static string NormalizeSha256(string value)
        {
            return value.Trim().ToUpperInvariant();
        }
    }
}
