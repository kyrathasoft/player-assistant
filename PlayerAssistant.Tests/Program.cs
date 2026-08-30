using System.Reflection;
using PlayerAssistant;

namespace PlayerAssistant.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--trusted-update-child", var statePath, var versionText])
        {
            var version = Version.Parse(versionText);
            var update = new PlayerAssistantUpdateInfo(version, versionText,
                new Uri("https://bryanmiller.us/scarlethorizons/p-assist-" + versionText + ".zip"), new string('A', 64),
                new Uri("https://bryanmiller.us/scarlethorizons/p-assist-" + versionText + ".exe"), new string('B', 64));
            PlayerAssistantUpdateUtility.ApplyTrustedUpdateVersionPolicy(update, new Version(0, 9, 0), statePath);
            return 0;
        }

        if (args is ["--hosted-settings-child", var hostedChildStatePath, var hostedChildVersionText])
        {
            HostedSettingsTrustUtility.ApplyTrustedHostedSettingsVersionPolicy(new Version(hostedChildVersionText!), hostedChildStatePath!);
            return 0;
        }

        if (args is ["--hosted-settings-gated-child", var gatedStatePath, var gatedVersionText, var gatedAcquiredPath, var gatedReleasePath])
        {
            HostedSettingsTrustUtility.ApplyTrustedHostedSettingsVersionPolicyForChildProcess(
                new Version(gatedVersionText!), gatedStatePath!, gatedAcquiredPath!, gatedReleasePath!);
            return 0;
        }

        if (args is ["--hosted-settings-abandon-lock", var abandonedStatePath, var abandonedAcquiredPath])
        {
            using var abandonedLock = HostedSettingsTrustUtility.AcquireTrustedHostedSettingsStateLockForChildProcess(abandonedStatePath!);
            File.WriteAllText(abandonedAcquiredPath!, "acquired");
            Environment.Exit(0);
        }

        if (args is ["--cancellation-child", var pidPath])
        {
            var temporaryPidPath = pidPath + ".tmp";
            File.WriteAllText(
                temporaryPidPath,
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.Move(temporaryPidPath, pidPath);
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

        if (args is ["--rpol-normal-active-loader-child", var proofPath])
        {
            var prior = "separate-process-prior";
            var candidate = "separate-process-candidate";
            var slots = new Dictionary<string, string> { ["A"] = prior, ["B"] = candidate };
            var pointer = new RpolActiveStatePointer(
                2,
                "B",
                "A",
                Verified: false,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(candidate))));
            if (!RpolVersionedStateTransaction.TryReadNormalActiveState(pointer, slot => slots.TryGetValue(slot, out var value) ? value : null, out var loaded)
                || !string.Equals(prior, loaded, StringComparison.Ordinal))
            {
                return 1;
            }

            File.WriteAllText(proofPath, loaded);
            return 0;
        }

        var requestedTestFilter = args.Length > 0 ? string.Join(" ", args).Trim() : string.Empty;
        var tests = TestCatalog.Create();
        if (!string.IsNullOrWhiteSpace(requestedTestFilter)) tests = tests.Where(test => test.Name.Contains(requestedTestFilter, StringComparison.OrdinalIgnoreCase)).ToArray();
        var failures = new List<string>();
        foreach (var (name, test) in tests)
        {
            try
            {
                using var credentialStoreScope = RuntimeSecretStoreUtility.UseBackendForTests(new InMemoryWindowsCredentialStoreBackend());
                test(); Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                var rootException = ex is TargetInvocationException tie && tie.InnerException is not null ? tie.InnerException : ex;
                failures.Add($"{name}: {rootException}"); Console.WriteLine($"FAIL {name}: {rootException}");
            }
        }
        if (failures.Count > 0) { Console.WriteLine(); Console.WriteLine("Failures:"); foreach (var failure in failures) Console.WriteLine(failure); return 1; }
        return 0;
    }
}
