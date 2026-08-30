namespace PlayerAssistant
{
    internal static class StartupLoggingUtility
    {
        public const string LogFileName = "startup-errors.log";
        private const string PhaseStatusSucceeded = "succeeded";
        private const string PhaseStatusFailed = "failed";

        public static string FormatLogEntry(string phase, Exception ex)
        {
            return
                $"""
                [{DateTimeOffset.Now:O}] {SensitiveTextRedactionUtility.Redact(phase)}
                {SensitiveTextRedactionUtility.Redact(ex.ToString())}

                """;
        }

        public static string FormatLogEntry(string phase, string message)
        {
            return
                $"""
                [{DateTimeOffset.Now:O}] {SensitiveTextRedactionUtility.Redact(phase)}
                {SensitiveTextRedactionUtility.Redact(message)}

                """;
        }

        public static void Append(string phase, Exception ex)
        {
            try
            {
                AppendBounded(FormatLogEntry(phase, ex));
            }
            catch
            {
            }
        }

        public static void Append(string phase, string message)
        {
            try
            {
                AppendBounded(FormatLogEntry(phase, message));
            }
            catch
            {
            }
        }

        public static async Task AppendAsync(string phase, Exception ex)
        {
            try
            {
                await AppendBoundedAsync(FormatLogEntry(phase, ex));
            }
            catch
            {
            }
        }

        public static async Task AppendAsync(string phase, string message)
        {
            try
            {
                await AppendBoundedAsync(FormatLogEntry(phase, message));
            }
            catch
            {
            }
        }

        public static void RunRequiredPhase(string phase, Action action)
        {
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                action();
                StartupHealthUtility.RecordPhase(
                    phase,
                    PhaseStatusSucceeded,
                    DateTimeOffset.UtcNow - startedAt);
            }
            catch (Exception ex)
            {
                StartupHealthUtility.RecordPhase(
                    phase,
                    PhaseStatusFailed,
                    DateTimeOffset.UtcNow - startedAt,
                    ex);
                LastCrashDiagnosticUtility.Write(phase, ex);
                Append(phase, ex);
                throw;
            }
        }

        public static async Task RunOptionalPhaseAsync(string phase, Func<Task> action)
        {
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                await action();
                StartupHealthUtility.RecordPhase(
                    phase,
                    PhaseStatusSucceeded,
                    DateTimeOffset.UtcNow - startedAt);
            }
            catch (Exception ex)
            {
                StartupHealthUtility.RecordPhase(
                    phase,
                    PhaseStatusFailed,
                    DateTimeOffset.UtcNow - startedAt,
                    ex);
                await AppendAsync(phase, ex);
            }
        }

        public static async Task RunRequiredPhaseAsync(string phase, Func<Task> action)
        {
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                await action();
                StartupHealthUtility.RecordPhase(
                    phase,
                    PhaseStatusSucceeded,
                    DateTimeOffset.UtcNow - startedAt);
            }
            catch (Exception ex)
            {
                StartupHealthUtility.RecordPhase(
                    phase,
                    PhaseStatusFailed,
                    DateTimeOffset.UtcNow - startedAt,
                    ex);
                LastCrashDiagnosticUtility.Write(phase, ex);
                await AppendAsync(phase, ex);
                throw;
            }
        }

        private static void AppendBounded(string entry)
        {
            var path = GetLogPath();
            var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            var combined = existing + entry;
            var limit = (int)ResourceBudgetPolicy.Load(Path.Combine(AppContext.BaseDirectory, "resource-budgets.json")).DiagnosticBytes;
            File.WriteAllText(path, combined.Length <= limit ? combined : combined[^limit..]);
        }

        private static Task AppendBoundedAsync(string entry)
        {
            AppendBounded(entry);
            return Task.CompletedTask;
        }

        private static string GetLogPath()
        {
            return RuntimePathUtility.GetWritableRuntimePath(LogFileName);
        }
    }
}
