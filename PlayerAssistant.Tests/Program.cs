using System.Reflection;
using PlayerAssistant;

namespace PlayerAssistant.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
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
