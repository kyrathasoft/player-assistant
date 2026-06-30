// This file is the application entry point.
namespace PlayerAssistant
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Global\PlayerAssistant.SingleInstance";
        private const string PostsDirectoryName = "Posts";
        private const string IcDirectoryName = "IC";
        private const string OocDirectoryName = "OOC";
        private const string AsideDirectoryName = "Aside";
        private const string SuppressHeroImagesArgument = "--suppress-hero-images";

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            using Mutex singleInstanceMutex = new(true, SingleInstanceMutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "Player Assistant is already running.",
                    "Player Assistant",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            AppSettingsUtility.Load();
            UserPreferencesUtility.Load();
            FileDownloadCounters.Reset();
            EnsurePostsDirectory();
            KeywordTermsFileUtility.EnsureReleaseCopy();
            ApplicationConfiguration.Initialize();
            var suppressHeroImages = args.Any(arg =>
                string.Equals(arg, SuppressHeroImagesArgument, StringComparison.OrdinalIgnoreCase))
                || UserPreferencesUtility.SkipHeroImageParadeAtStartup;
            Application.Run(new Form1(suppressHeroImages));
        }

        private static void EnsurePostsDirectory()
        {
            var postsDirectory = Path.Combine(AppContext.BaseDirectory, PostsDirectoryName);

            Directory.CreateDirectory(postsDirectory);
            Directory.CreateDirectory(Path.Combine(postsDirectory, IcDirectoryName));
            Directory.CreateDirectory(Path.Combine(postsDirectory, IcDirectoryName, AsideDirectoryName));
            Directory.CreateDirectory(Path.Combine(postsDirectory, OocDirectoryName));
            Directory.CreateDirectory(Path.Combine(postsDirectory, OocDirectoryName, AsideDirectoryName));
        }
    }
}
