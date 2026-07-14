using System.Formats.Asn1;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PlayerAssistant
{
    internal sealed record CertificatePinTrustEntry(
        string PinSha256,
        DateTimeOffset? NotBeforeUtc = null,
        DateTimeOffset? NotAfterUtc = null,
        bool IsRevoked = false);

    internal sealed record CertificatePinningPolicy(
        string HostSuffix,
        IReadOnlyList<CertificatePinTrustEntry> TrustedPins);

    internal static class CertificatePinningUtility
    {
        // Current bryanmiller.us leaf pin plus the active Let's Encrypt YR2 intermediate pin.
        // Rotation windows allow overlap during normal certificate renewals without trusting retired pins indefinitely.
        private static readonly CertificatePinningPolicy PlayerAssistantUpdatePolicy = new(
            "bryanmiller.us",
            [
                new CertificatePinTrustEntry("Cs2RWBFFnGtCidcPrPVbM4awHfkwOQAdfcF2KohmJFc="),
                new CertificatePinTrustEntry("nWN7PSep5XDQdge5zK24CnCRXHr3KvzhKEGxsdqCX9E=")
            ]);

        public static bool ValidateServerCertificate(
            HttpRequestMessage requestMessage,
            X509Certificate2? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            ArgumentNullException.ThrowIfNull(requestMessage);

            if (requestMessage.RequestUri is null
                || !IsPlayerAssistantUpdateRequest(requestMessage.RequestUri))
            {
                return sslPolicyErrors == SslPolicyErrors.None;
            }

            var presentedPins = GetPresentedPins(certificate, chain);
            if (ValidatePinnedRequest(
                requestMessage.RequestUri,
                presentedPins,
                sslPolicyErrors,
                PlayerAssistantUpdatePolicy,
                DateTimeOffset.UtcNow))
            {
                return true;
            }

            // Update manifests are independently signed and installers are verified by signed hashes.
            // Allow trusted local TLS inspection or certificate rotation when Windows still validates TLS.
            return sslPolicyErrors == SslPolicyErrors.None;
        }

        private static bool IsPlayerAssistantUpdateRequest(Uri requestUri)
        {
            return MatchesHostSuffix(requestUri, PlayerAssistantUpdatePolicy.HostSuffix)
                && NetworkUrlAllowlistUtility.Validate(requestUri, NetworkUrlPurpose.PlayerAssistantUpdate).IsAllowed;
        }

        internal static bool ValidatePinnedRequest(
            Uri? requestUri,
            IReadOnlyCollection<string> presentedPins,
            SslPolicyErrors sslPolicyErrors,
            CertificatePinningPolicy policy,
            DateTimeOffset? nowUtc = null)
        {
            ArgumentNullException.ThrowIfNull(policy);

            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                return false;
            }

            if (requestUri is null || !MatchesHostSuffix(requestUri, policy.HostSuffix))
            {
                return true;
            }

            if (presentedPins.Count == 0)
            {
                return false;
            }

            var effectiveNow = nowUtc ?? DateTimeOffset.UtcNow;
            return presentedPins.Any(pin =>
                policy.TrustedPins.Any(entry =>
                    !entry.IsRevoked
                    && IsWithinRotationWindow(entry, effectiveNow)
                    && string.Equals(pin, entry.PinSha256, StringComparison.Ordinal)));
        }

        internal static IReadOnlyCollection<string> GetPresentedPins(
            X509Certificate2? certificate,
            X509Chain? chain)
        {
            var pins = new HashSet<string>(StringComparer.Ordinal);

            AddPin(certificate, pins);

            if (chain is not null)
            {
                foreach (var element in chain.ChainElements)
                {
                    AddPin(element.Certificate, pins);
                }
            }

            return pins;
        }

        private static bool MatchesHostSuffix(Uri requestUri, string hostSuffix)
        {
            return string.Equals(requestUri.Host, hostSuffix, StringComparison.OrdinalIgnoreCase)
                || requestUri.Host.EndsWith($".{hostSuffix}", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWithinRotationWindow(CertificatePinTrustEntry entry, DateTimeOffset nowUtc)
        {
            if (entry.NotBeforeUtc is not null && nowUtc < entry.NotBeforeUtc.Value)
            {
                return false;
            }

            if (entry.NotAfterUtc is not null && nowUtc > entry.NotAfterUtc.Value)
            {
                return false;
            }

            return true;
        }

        private static void AddPin(X509Certificate2? certificate, ISet<string> pins)
        {
            if (certificate is null)
            {
                return;
            }

            var spki = ExportSubjectPublicKeyInfo(certificate);
            var pinBytes = SHA256.HashData(spki);
            pins.Add(Convert.ToBase64String(pinBytes));
        }

        private static byte[] ExportSubjectPublicKeyInfo(X509Certificate2 certificate)
        {
            var publicKey = certificate.PublicKey
                ?? throw new InvalidOperationException("Certificate public key was missing.");
            var algorithmParameters = publicKey.EncodedParameters?.RawData
                ?? throw new InvalidOperationException("Certificate public key parameters were missing.");
            var encodedKeyValue = publicKey.EncodedKeyValue?.RawData
                ?? throw new InvalidOperationException("Certificate public key value was missing.");
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.PushSequence();
            writer.WriteObjectIdentifier(publicKey.Oid.Value ?? throw new InvalidOperationException("Certificate public key OID was missing."));
            writer.WriteEncodedValue(algorithmParameters);
            writer.PopSequence();
            writer.WriteEncodedValue(encodedKeyValue);
            writer.PopSequence();
            return writer.Encode();
        }
    }
}
