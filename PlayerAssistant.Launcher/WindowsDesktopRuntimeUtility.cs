namespace PlayerAssistant.Launcher
{
    internal static class WindowsDesktopRuntimeUtility
    {
        internal static bool HasInstalledRuntimeDirectory(string programFilesPath, string dotnetDirectoryName, bool isX64)
        {
            if (!isX64 || string.IsNullOrWhiteSpace(programFilesPath)) return false;
            try
            {
                var sharedFxDirectory = Path.Combine(programFilesPath, dotnetDirectoryName, "shared", "Microsoft.WindowsDesktop.App");
                return Directory.Exists(sharedFxDirectory)
                    && Directory.EnumerateDirectories(sharedFxDirectory)
                        .Select(Path.GetFileName).Where(version => !string.IsNullOrWhiteSpace(version))
                        .Any(version => version!.StartsWith("10.", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }
    }
}
