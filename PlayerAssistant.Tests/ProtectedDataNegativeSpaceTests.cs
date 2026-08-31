using PlayerAssistant;

namespace PlayerAssistant.Tests;

internal static class ProtectedDataNegativeSpaceTests
{
    internal static void ObservabilityArtifactsNeverContainProtectedData()
    {
        var canaries = new[]
        {
            "fixture-password-7f2d", "fixture-bearer-8a31", "fixture-admin-key-9c42",
            "fixture-cookie-ad3e", "fixture-storage-state-be54", "fixture-private-path-cf65",
            "fixture-response-body-dg76"
        };
        var inventory = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["logging"] = "level=error message=login failed password=fixture-password-7f2d",
            ["errors"] = "exception=UnauthorizedException token=fixture-bearer-8a31",
            ["metrics"] = "metric=auth_failure admin_key=fixture-admin-key-9c42",
            ["diagnostics"] = "Cookie: session=fixture-cookie-ad3e storage_state=fixture-storage-state-be54",
            ["crash_reports"] = "path=C:\\Users\\Bryan\\private\\fixture-private-path-cf65 response_body=fixture-response-body-dg76",
            ["http_responses"] = "Authorization: " + "Bearer " + "fixture-bearer-8a31 body=fixture-response-body-dg76",
            ["browser_console"] = "error: cookie=fixture-cookie-ad3e storage state=fixture-storage-state-be54",
            ["ci_artifacts"] = "private_path=C:\\Users\\Bryan\\secret\\fixture-private-path-cf65",
            ["generated_bundles"] = "{\"password\":\"fixture-password-7f2d\",\"admin_key\":\"fixture-admin-key-9c42\"}"
        };
        if (inventory.Count != 9) throw new InvalidOperationException("protected-data inventory is incomplete");
        foreach (var (kind, raw) in inventory)
        {
            var redacted = SensitiveTextRedactionUtility.Redact(raw);
            foreach (var canary in canaries)
                if (redacted.Contains(canary, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{kind} leaked protected fixture data");
            if (!redacted.Contains(SensitiveTextRedactionUtility.RedactedValue, StringComparison.Ordinal))
                throw new InvalidOperationException($"{kind} did not emit a redaction marker");
        }
    }

    internal static void SafeIdentifiersRemainInObservabilityArtifacts()
    {
        const string safe = "correlation_id=fixture-correlation-001 endpoint=/v1/health status=401";
        var redacted = SensitiveTextRedactionUtility.Redact(safe);
        if (!string.Equals(safe, redacted, StringComparison.Ordinal))
            throw new InvalidOperationException("safe observability identifiers were altered");
    }
}
