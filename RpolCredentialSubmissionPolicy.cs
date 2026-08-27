namespace PlayerAssistant;

internal static class RpolCredentialSubmissionPolicy
{
    internal static bool TryValidateCredentialPage(Uri uri, out string reason)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "rpol.net", StringComparison.OrdinalIgnoreCase)
            || uri.Port is not (-1 or 443)
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || !string.Equals(uri.AbsolutePath, "/game.php", StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(uri.Fragment))
        {
            reason = "The live page is not the exact trusted HTTPS RPOL game page.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    internal static bool TryValidateLoginForm(
        Uri topFrameUri,
        string? action,
        string? method,
        string? target,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(topFrameUri);
        if (!TryValidateCredentialPage(topFrameUri, out reason))
        {
            return false;
        }

        if (!string.Equals(method?.Trim(), "POST", StringComparison.OrdinalIgnoreCase))
        {
            reason = "The RPOL login form method is not POST.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(target)
            && !string.Equals(target.Trim(), "_self", StringComparison.OrdinalIgnoreCase))
        {
            reason = "The RPOL login form targets a different frame.";
            return false;
        }

        if (!Uri.TryCreate(topFrameUri, action ?? string.Empty, out var actionUri)
            || actionUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(actionUri.Host, "rpol.net", StringComparison.OrdinalIgnoreCase)
            || actionUri.Port is not (-1 or 443)
            || !string.IsNullOrWhiteSpace(actionUri.UserInfo)
            || !string.Equals(actionUri.AbsolutePath, "/login.cgi", StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(actionUri.Query)
            || !string.IsNullOrWhiteSpace(actionUri.Fragment))
        {
            reason = "The RPOL login form action is not the exact trusted HTTPS login endpoint.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    internal static bool TryValidateCredentialRequest(
        Uri topFrameUri,
        Uri requestUri,
        string? method,
        bool isMainFrame,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(topFrameUri);
        ArgumentNullException.ThrowIfNull(requestUri);
        if (!isMainFrame)
        {
            reason = "The credential-bearing request did not originate from the top frame.";
            return false;
        }

        if (!TryValidateCredentialPage(topFrameUri, out reason))
        {
            reason = "The credential-bearing request originated from an untrusted page.";
            return false;
        }

        if (!string.Equals(method?.Trim(), "POST", StringComparison.OrdinalIgnoreCase)
            || requestUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(requestUri.Host, "rpol.net", StringComparison.OrdinalIgnoreCase)
            || requestUri.Port is not (-1 or 443)
            || !string.IsNullOrWhiteSpace(requestUri.UserInfo)
            || !string.Equals(requestUri.AbsolutePath, "/login.cgi", StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(requestUri.Query)
            || !string.IsNullOrWhiteSpace(requestUri.Fragment))
        {
            reason = "The credential-bearing request destination or method is not the exact trusted RPOL login endpoint.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    internal static bool CanSubmitAfterAwaitedOperation(
        Uri pageBeforeFill,
        Uri pageAfterOperation,
        Uri pageBeforeSubmit,
        string? action,
        string? method,
        string? target,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(pageBeforeFill);
        ArgumentNullException.ThrowIfNull(pageAfterOperation);
        ArgumentNullException.ThrowIfNull(pageBeforeSubmit);
        if (!pageBeforeFill.Equals(pageAfterOperation) || !pageBeforeFill.Equals(pageBeforeSubmit))
        {
            reason = "The RPOL page navigated during credential submission.";
            return false;
        }

        return TryValidateLoginForm(pageBeforeSubmit, action, method, target, out reason);
    }
}
