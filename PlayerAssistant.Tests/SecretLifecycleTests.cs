using System.Text.Json;
using PlayerAssistant;

namespace PlayerAssistant.Tests;

internal static class SecretLifecycleTests
{
    internal static void InventoryRedactsSecretsAndRejectsNegativeSpace()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "secret-lifecycle-inventory.json");
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new InvalidOperationException("secret lifecycle inventory is missing");
        var raw = File.ReadAllText(path);
        using var document = JsonDocument.Parse(raw);
        if (!document.RootElement.TryGetProperty("entries", out var entries) || entries.GetArrayLength() < 10)
            throw new InvalidOperationException("inventory does not cover the required credential classes");
        if (raw.Contains("disposable-secret-value", StringComparison.Ordinal) || raw.Contains("BEGIN PRIVATE KEY", StringComparison.Ordinal))
            throw new InvalidOperationException("inventory contains secret material");

        const string fixtureSecret = "disposable-secret-value";
        var diagnostic = $"credential_id=fixture-credential-001 Authorization: Bearer {fixtureSecret} password={fixtureSecret} Cookie: session={fixtureSecret}";
        var redacted = SensitiveTextRedactionUtility.Redact(diagnostic);
        if (redacted.Contains(fixtureSecret, StringComparison.Ordinal))
            throw new InvalidOperationException("redaction leaked fixture secret");
        if (!redacted.Contains("fixture-credential-001", StringComparison.Ordinal))
            throw new InvalidOperationException("redaction removed an approved identifier");
        if (!redacted.Contains(SensitiveTextRedactionUtility.RedactedValue, StringComparison.Ordinal))
            throw new InvalidOperationException("redaction did not emit the approved marker");

        var safe = SensitiveTextRedactionUtility.Redact("operation=revocation credential_id=fixture-credential-001 outcome=denied");
        if (!safe.Contains("credential_id=fixture-credential-001", StringComparison.Ordinal) || safe.Contains("[REDACTED]", StringComparison.Ordinal))
            throw new InvalidOperationException("negative-space redaction changed safe diagnostic fields");
    }
}
