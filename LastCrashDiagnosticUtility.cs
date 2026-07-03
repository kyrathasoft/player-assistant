using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlayerAssistant
{
    internal static class LastCrashDiagnosticUtility
    {
        public const string FileName = "last-crash.json";
        public const int CurrentSchemaVersion = 1;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static void Write(string phase, Exception exception, bool isTerminating = false, bool overwrite = true)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(phase);
            ArgumentNullException.ThrowIfNull(exception);

            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, FileName);
                if (!overwrite && File.Exists(path))
                {
                    return;
                }

                var diagnostic = new LastCrashDiagnostic(
                    CurrentSchemaVersion,
                    DateTimeOffset.Now,
                    SensitiveTextRedactionUtility.Redact(phase),
                    Program.GetVersionText(),
                    isTerminating,
                    LastCrashException.From(exception),
                    LastCrashEnvironment.Create());

                AtomicFileUtility.WriteAllText(path, JsonSerializer.Serialize(diagnostic, JsonOptions));
            }
            catch
            {
            }
        }

        private sealed record LastCrashDiagnostic(
            [property: JsonPropertyName("schema_version")] int SchemaVersion,
            [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
            [property: JsonPropertyName("phase")] string Phase,
            [property: JsonPropertyName("app_version")] string AppVersion,
            [property: JsonPropertyName("is_terminating")] bool IsTerminating,
            [property: JsonPropertyName("exception")] LastCrashException Exception,
            [property: JsonPropertyName("environment")] LastCrashEnvironment Environment);

        private sealed record LastCrashException(
            [property: JsonPropertyName("type")] string Type,
            [property: JsonPropertyName("message")] string Message,
            [property: JsonPropertyName("hresult")] int HResult,
            [property: JsonPropertyName("inner_exception")] LastCrashException? InnerException)
        {
            public static LastCrashException From(Exception exception)
            {
                return new LastCrashException(
                    exception.GetType().Name,
                    SensitiveTextRedactionUtility.Redact(exception.Message),
                    exception.HResult,
                    exception.InnerException is null ? null : From(exception.InnerException));
            }
        }

        private sealed record LastCrashEnvironment(
            [property: JsonPropertyName("process_id")] int ProcessId,
            [property: JsonPropertyName("managed_thread_id")] int ManagedThreadId,
            [property: JsonPropertyName("framework_description")] string FrameworkDescription,
            [property: JsonPropertyName("os_description")] string OsDescription,
            [property: JsonPropertyName("process_architecture")] string ProcessArchitecture,
            [property: JsonPropertyName("base_directory")] string BaseDirectory,
            [property: JsonPropertyName("machine_name")] string MachineName,
            [property: JsonPropertyName("user_name")] string UserName)
        {
            public static LastCrashEnvironment Create()
            {
                return new LastCrashEnvironment(
                    Environment.ProcessId,
                    Environment.CurrentManagedThreadId,
                    RuntimeInformation.FrameworkDescription,
                    RuntimeInformation.OSDescription,
                    RuntimeInformation.ProcessArchitecture.ToString(),
                    AppContext.BaseDirectory,
                    "[REDACTED]",
                    "[REDACTED]");
            }
        }
    }
}
