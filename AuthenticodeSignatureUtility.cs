using System.Diagnostics;
using System.Text.Json;

namespace PlayerAssistant
{
    internal sealed record AuthenticodeSignatureInfo(
        string Status,
        string? SignerSubject,
        string? SignerThumbprint);

    internal sealed record AuthenticodeSignaturePolicy(
        string? ExpectedSignerSubject,
        string? ExpectedSignerThumbprint);

    internal static class AuthenticodeSignatureUtility
    {
        public static AuthenticodeSignaturePolicy GetCurrentProcessSignaturePolicy()
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            {
                throw new InvalidOperationException("Unable to resolve the current application path for Authenticode verification.");
            }

            var signature = InspectSignature(processPath);
            if (!string.Equals(signature.Status, "Valid", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(signature.SignerThumbprint))
            {
                throw new InvalidOperationException(
                    "Verified in-app updates require the current application to be Authenticode signed with a valid signer certificate.");
            }

            return new AuthenticodeSignaturePolicy(
                signature.SignerSubject,
                signature.SignerThumbprint);
        }

        public static void AssertMatchesPolicy(
            string path,
            AuthenticodeSignaturePolicy policy)
        {
            AssertMatchesPolicy(path, policy, InspectSignature);
        }

        internal static void AssertMatchesPolicy(
            string path,
            AuthenticodeSignaturePolicy policy,
            Func<string, AuthenticodeSignatureInfo> inspectSignature)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(inspectSignature);

            var signature = inspectSignature(path);
            if (!string.Equals(signature.Status, "Valid", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Downloaded installer Authenticode signature status '{signature.Status}' is not valid.");
            }

            if (string.IsNullOrWhiteSpace(signature.SignerThumbprint))
            {
                throw new InvalidOperationException("Downloaded installer is missing an Authenticode signer certificate.");
            }

            if (!string.IsNullOrWhiteSpace(policy.ExpectedSignerSubject)
                && (signature.SignerSubject?.IndexOf(
                        policy.ExpectedSignerSubject,
                        StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
            {
                throw new InvalidOperationException(
                    $"Downloaded installer signer subject '{signature.SignerSubject}' did not contain expected subject '{policy.ExpectedSignerSubject}'.");
            }

            if (!string.IsNullOrWhiteSpace(policy.ExpectedSignerThumbprint))
            {
                var actualThumbprint = NormalizeThumbprint(signature.SignerThumbprint);
                var expectedThumbprint = NormalizeThumbprint(policy.ExpectedSignerThumbprint);
                if (!string.Equals(actualThumbprint, expectedThumbprint, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Downloaded installer signer thumbprint '{actualThumbprint}' did not match expected thumbprint '{expectedThumbprint}'.");
                }
            }
        }

        internal static AuthenticodeSignatureInfo InspectSignature(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            var script =
                """
                $signature = Get-AuthenticodeSignature -LiteralPath $args[0]
                [pscustomobject]@{
                  status = [string]$signature.Status
                  signer_subject = if ($signature.SignerCertificate) { [string]$signature.SignerCertificate.Subject } else { $null }
                  signer_thumbprint = if ($signature.SignerCertificate) { [string]$signature.SignerCertificate.Thumbprint } else { $null }
                } | ConvertTo-Json -Compress
                """;

            var startInfo = new ProcessStartInfo(ResolvePowerShellExecutable())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(script);
            startInfo.ArgumentList.Add(path);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start PowerShell for Authenticode verification.");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("PowerShell did not finish Authenticode verification within 30 seconds.");
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"PowerShell Authenticode verification failed for '{path}'. {error}".Trim());
            }

            var trimmedOutput = output.Trim();
            if (string.IsNullOrWhiteSpace(trimmedOutput))
            {
                throw new InvalidOperationException($"PowerShell Authenticode verification returned no output for '{path}'.");
            }

            try
            {
                using var document = JsonDocument.Parse(trimmedOutput);
                var root = document.RootElement;
                return new AuthenticodeSignatureInfo(
                    root.GetProperty("status").GetString() ?? string.Empty,
                    root.TryGetProperty("signer_subject", out var signerSubject)
                        ? signerSubject.GetString()
                        : null,
                    root.TryGetProperty("signer_thumbprint", out var signerThumbprint)
                        ? signerThumbprint.GetString()
                        : null);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"PowerShell Authenticode verification returned invalid JSON for '{path}'.",
                    ex);
            }
        }

        private static string NormalizeThumbprint(string? thumbprint)
        {
            return (thumbprint ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        }

        private static string ResolvePowerShellExecutable()
        {
            var systemPowerShell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            if (File.Exists(systemPowerShell))
            {
                return systemPowerShell;
            }

            foreach (var commandName in new[] { "pwsh.exe", "powershell.exe", "powershell" })
            {
                var resolved = Environment.GetEnvironmentVariable("PATH")?
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Select(path => Path.Combine(path.Trim(), commandName))
                    .FirstOrDefault(File.Exists);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            throw new InvalidOperationException("Unable to locate a PowerShell executable for Authenticode verification.");
        }
    }
}
