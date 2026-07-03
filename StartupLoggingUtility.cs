namespace PlayerAssistant
{
    internal static class StartupLoggingUtility
    {
        public const string LogFileName = "startup-errors.log";

        public static string FormatLogEntry(string phase, Exception ex)
        {
            return
                $"""
                [{DateTimeOffset.Now:O}] {phase}
                {ex}

                """;
        }

        public static string FormatLogEntry(string phase, string message)
        {
            return
                $"""
                [{DateTimeOffset.Now:O}] {phase}
                {message}

                """;
        }

        public static void Append(string phase, Exception ex)
        {
            try
            {
                File.AppendAllText(GetLogPath(), FormatLogEntry(phase, ex));
            }
            catch
            {
            }
        }

        public static void Append(string phase, string message)
        {
            try
            {
                File.AppendAllText(GetLogPath(), FormatLogEntry(phase, message));
            }
            catch
            {
            }
        }

        public static async Task AppendAsync(string phase, Exception ex)
        {
            try
            {
                await File.AppendAllTextAsync(GetLogPath(), FormatLogEntry(phase, ex));
            }
            catch
            {
            }
        }

        public static async Task AppendAsync(string phase, string message)
        {
            try
            {
                await File.AppendAllTextAsync(GetLogPath(), FormatLogEntry(phase, message));
            }
            catch
            {
            }
        }

        public static void RunRequiredPhase(string phase, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Append(phase, ex);
                throw;
            }
        }

        public static async Task RunOptionalPhaseAsync(string phase, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await AppendAsync(phase, ex);
            }
        }

        public static async Task RunRequiredPhaseAsync(string phase, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await AppendAsync(phase, ex);
                throw;
            }
        }

        private static string GetLogPath()
        {
            return Path.Combine(AppContext.BaseDirectory, LogFileName);
        }
    }
}
