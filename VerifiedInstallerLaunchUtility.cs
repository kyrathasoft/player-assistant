using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;

namespace PlayerAssistant
{
    internal enum InstallerLaunchElevationContext
    {
        StandardUser,
        ElevatedAdministrator
    }

    internal sealed class VerifiedInstallerLaunchTicket
    {
        public VerifiedInstallerLaunchTicket(
            string installerPath,
            string sha256,
            AuthenticodeSignatureInfo signature,
            AuthenticodeSignaturePolicy signaturePolicy,
            InstallerLaunchElevationContext elevationContext,
            string launchVerb)
        {
            InstallerPath = installerPath;
            Sha256 = sha256;
            Signature = signature;
            SignaturePolicy = signaturePolicy;
            ElevationContext = elevationContext;
            LaunchVerb = launchVerb;
        }

        public string InstallerPath { get; }

        public string Sha256 { get; }

        public AuthenticodeSignatureInfo Signature { get; }

        public AuthenticodeSignaturePolicy SignaturePolicy { get; }

        public InstallerLaunchElevationContext ElevationContext { get; }

        public string LaunchVerb { get; }
    }

    internal static class VerifiedInstallerLaunchUtility
    {
        private const string DefaultLaunchVerb = "open";

        public static VerifiedInstallerLaunchTicket CreateLaunchTicket(VerifiedInstallerDownloadResult installer)
        {
            return CreateLaunchTicket(
                installer,
                AuthenticodeSignatureUtility.GetCurrentProcessSignaturePolicy(),
                AuthenticodeSignatureUtility.InspectSignature,
                GetCurrentProcessElevationContext);
        }

        internal static VerifiedInstallerLaunchTicket CreateLaunchTicket(
            VerifiedInstallerDownloadResult installer,
            AuthenticodeSignaturePolicy signaturePolicy,
            Func<string, AuthenticodeSignatureInfo> inspectSignature,
            Func<InstallerLaunchElevationContext> getElevationContext)
        {
            ArgumentNullException.ThrowIfNull(installer);
            ArgumentNullException.ThrowIfNull(signaturePolicy);
            ArgumentNullException.ThrowIfNull(inspectSignature);
            ArgumentNullException.ThrowIfNull(getElevationContext);

            var installerPath = Path.GetFullPath(installer.InstallerPath);
            EnsureInstallerMatchesVerifiedState(installerPath, installer.Sha256, installer.Signature, signaturePolicy, inspectSignature);

            return new VerifiedInstallerLaunchTicket(
                installerPath,
                NormalizeSha256(installer.Sha256),
                installer.Signature,
                signaturePolicy,
                getElevationContext(),
                DefaultLaunchVerb);
        }

        public static ProcessStartInfo CreateStartInfo(VerifiedInstallerLaunchTicket ticket)
        {
            return CreateStartInfo(
                ticket,
                AuthenticodeSignatureUtility.InspectSignature,
                GetCurrentProcessElevationContext);
        }

        internal static ProcessStartInfo CreateStartInfo(
            VerifiedInstallerLaunchTicket ticket,
            Func<string, AuthenticodeSignatureInfo> inspectSignature,
            Func<InstallerLaunchElevationContext> getElevationContext)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            ArgumentNullException.ThrowIfNull(inspectSignature);
            ArgumentNullException.ThrowIfNull(getElevationContext);

            var installerPath = Path.GetFullPath(ticket.InstallerPath);
            EnsureInstallerMatchesVerifiedState(
                installerPath,
                ticket.Sha256,
                ticket.Signature,
                ticket.SignaturePolicy,
                inspectSignature);

            var currentElevationContext = getElevationContext();
            if (currentElevationContext != ticket.ElevationContext)
            {
                throw new InvalidOperationException(
                    $"Verified installer launch elevation context changed from '{ticket.ElevationContext}' to '{currentElevationContext}' after verification.");
            }

            return new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true,
                Verb = ticket.LaunchVerb,
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppContext.BaseDirectory
            };
        }

        internal static InstallerLaunchElevationContext GetCurrentProcessElevationContext()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator)
                ? InstallerLaunchElevationContext.ElevatedAdministrator
                : InstallerLaunchElevationContext.StandardUser;
        }

        private static void EnsureInstallerMatchesVerifiedState(
            string installerPath,
            string expectedSha256,
            AuthenticodeSignatureInfo expectedSignature,
            AuthenticodeSignaturePolicy signaturePolicy,
            Func<string, AuthenticodeSignatureInfo> inspectSignature)
        {
            if (!File.Exists(installerPath))
            {
                throw new InvalidOperationException(
                    $"Verified installer path '{installerPath}' no longer exists.");
            }

            var currentSha256 = ComputeSha256(installerPath);
            var normalizedExpectedSha256 = NormalizeSha256(expectedSha256);
            if (!string.Equals(currentSha256, normalizedExpectedSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Verified installer SHA256 changed from '{normalizedExpectedSha256}' to '{currentSha256}' after verification.");
            }

            AuthenticodeSignatureUtility.AssertMatchesPolicy(installerPath, signaturePolicy, inspectSignature);
            var currentSignature = inspectSignature(installerPath);
            if (!SignatureMatches(currentSignature, expectedSignature))
            {
                throw new InvalidOperationException(
                    "Verified installer signer details changed after verification.");
            }
        }

        private static bool SignatureMatches(
            AuthenticodeSignatureInfo currentSignature,
            AuthenticodeSignatureInfo expectedSignature)
        {
            return string.Equals(currentSignature.Status, expectedSignature.Status, StringComparison.OrdinalIgnoreCase)
                && string.Equals(currentSignature.SignerSubject, expectedSignature.SignerSubject, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    NormalizeThumbprint(currentSignature.SignerThumbprint),
                    NormalizeThumbprint(expectedSignature.SignerThumbprint),
                    StringComparison.Ordinal);
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

        private static string NormalizeThumbprint(string? thumbprint)
        {
            return (thumbprint ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        }
    }
}
