using System.Diagnostics;

namespace PlayerAssistant
{
    internal static class ScheduledTaskLaunchUtility
    {
        internal static Func<ProcessStartInfo, Process>? ProcessFactoryForTests { get; set; }

        internal static ProcessStartInfo CreateStartInfo(string taskName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
            var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (string.IsNullOrWhiteSpace(systemDirectory))
            {
                throw new PlatformNotSupportedException("The Windows system directory is unavailable.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(systemDirectory, "schtasks.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("/Run");
            startInfo.ArgumentList.Add("/TN");
            startInfo.ArgumentList.Add(taskName);
            return startInfo;
        }

        public static async Task StartAsync(
            string taskName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startInfo = CreateStartInfo(taskName);
            using var process = ProcessFactoryForTests?.Invoke(startInfo)
                ?? new Process { StartInfo = startInfo };
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start())
            {
                throw new InvalidOperationException($"Windows did not start scheduled task '{taskName}'.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The process exited between HasExited and Kill.
                }

                throw;
            }

            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(error) ? output : error;
                throw new InvalidOperationException(
                    $"Windows could not start scheduled task '{taskName}': "
                    + SensitiveTextRedactionUtility.Redact(detail.Trim()));
            }
        }
    }
}
