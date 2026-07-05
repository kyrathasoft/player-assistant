using System.Net.Security;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PlayerAssistant
{
    internal sealed record CertificatePinningPolicy(
        string HostSuffix,
        IReadOnlyList<string> TrustedSpkiSha256Pins);

    internal static class CertificatePinningUtility
    {
        // Current bryanmiller.us leaf pin plus the active Let's Encrypt YR2 intermediate pin.
        // Keeping both allows routine leaf renewals while still rejecting unexpected chains.
        private static readonly CertificatePinningPolicy PlayerAssistantUpdatePolicy = new(
            "bryanmiller.us",
            [
                "Cs2RWBFFnGtCidcPrPVbM4awHfkwOQAdfcF2KohmJFc=",
                "nWN7PSep5XDQdge5zK24CnCRXHr3KvzhKEGxsdqCX9E="
            ]);

        public static bool ValidateServerCertificate(
            HttpRequestMessage requestMessage,
            X509Certificate2? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            ArgumentNullException.ThrowIfNull(requestMessage);

            return ValidatePinnedRequest(
                requestMessage.RequestUri,
                GetPresentedPins(certificate, chain),
                sslPolicyErrors,
                PlayerAssistantUpdatePolicy);
        }

        internal static bool ValidatePinnedRequest(
            Uri? requestUri,
            IReadOnlyCollection<string> presentedPins,
            SslPolicyErrors sslPolicyErrors,
            CertificatePinningPolicy policy)
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

            return presentedPins.Any(pin =>
                policy.TrustedSpkiSha256Pins.Any(trustedPin =>
                    string.Equals(pin, trustedPin, StringComparison.Ordinal)));
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
