using System.Diagnostics;
using Microsoft.Win32;

namespace PlayerAssistant.Launcher
{
    internal static class Program
    {
        private const string RequiredWindowsDesktopRuntimeMajorVersion = "10";
        private const string RuntimeDownloadUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/10.0";
        private const string ApplicationFileName = "player-assistant.exe";

        [STAThread]
        private static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();

            var applicationPath = Path.Combine(AppContext.BaseDirectory, ApplicationFileName);
            if (!File.Exists(applicationPath))
            {
                MessageBox.Show(
                    $"Unable to find {ApplicationFileName} beside this launcher.{Environment.NewLine}{applicationPath}",
                    "Player Assistant Launcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (!IsRequiredWindowsDesktopRuntimeInstalled())
            {
                var result = MessageBox.Show(
                    $"Player Assistant requires the .NET Windows Desktop Runtime {RequiredWindowsDesktopRuntimeMajorVersion}.x (x64).{Environment.NewLine}{Environment.NewLine}Would you like to download the installer now?",
                    "Missing .NET Runtime",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1);

                if (result == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(RuntimeDownloadUrl)
                    {
                        UseShellExecute = true
                    });
                }

                return;
            }

            Process.Start(new ProcessStartInfo(applicationPath)
            {
                UseShellExecute = true,
                Arguments = BuildArgumentString(args),
                WorkingDirectory = AppContext.BaseDirectory
            });
        }

        internal static bool IsRequiredWindowsDesktopRuntimeInstalled()
        {
            return HasInstalledRuntimeUnder(
                       RegistryHive.LocalMachine,
                       RegistryView.Registry64,
                       @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App")
                   || WindowsDesktopRuntimeUtility.HasInstalledRuntimeDirectory(
                       Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                       "dotnet",
                       isX64: true);
        }

        private static bool HasInstalledRuntimeUnder(RegistryHive hive, RegistryView view, string subKeyPath)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var sharedFxKey = baseKey.OpenSubKey(subKeyPath);
                if (sharedFxKey is null)
                {
                    return false;
                }

                return sharedFxKey.GetSubKeyNames()
                    .Any(IsRequiredRuntimeVersion);
            }
            catch
            {
                return false;
            }
        }


        private static bool IsRequiredRuntimeVersion(string version)
        {
            return version.StartsWith(
                RequiredWindowsDesktopRuntimeMajorVersion + ".",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildArgumentString(IEnumerable<string> args)
        {
            return string.Join(" ", args.Select(QuoteArgument));
        }

        private static string QuoteArgument(string argument)
        {
            return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
