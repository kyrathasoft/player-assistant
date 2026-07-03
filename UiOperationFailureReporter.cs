namespace PlayerAssistant
{
    internal sealed record UiOperationFailure(
        string Phase,
        string StatusPrefix,
        string DialogTitle,
        Exception Exception,
        bool ShowDialog);

    internal static class UiOperationFailureReporter
    {
        public static async Task ReportAsync(
            UiOperationFailure failure,
            Action<string> setStatusMessage,
            Action<string, string> showWarningDialog)
        {
            ArgumentNullException.ThrowIfNull(failure);
            ArgumentNullException.ThrowIfNull(setStatusMessage);
            ArgumentNullException.ThrowIfNull(showWarningDialog);

            await StartupLoggingUtility.AppendAsync(failure.Phase, failure.Exception);
            ReportWithoutLogging(failure, setStatusMessage, showWarningDialog);
        }

        public static void ReportWithoutLogging(
            UiOperationFailure failure,
            Action<string> setStatusMessage,
            Action<string, string> showWarningDialog)
        {
            ArgumentNullException.ThrowIfNull(failure);
            ArgumentNullException.ThrowIfNull(setStatusMessage);
            ArgumentNullException.ThrowIfNull(showWarningDialog);

            setStatusMessage(FormatStatusMessage(failure.StatusPrefix, failure.Exception));
            if (failure.ShowDialog)
            {
                showWarningDialog(failure.DialogTitle, failure.Exception.Message);
            }
        }

        public static string FormatStatusMessage(string statusPrefix, Exception exception)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(statusPrefix);
            ArgumentNullException.ThrowIfNull(exception);

            return $"{statusPrefix}: {exception.Message}";
        }
    }
}
