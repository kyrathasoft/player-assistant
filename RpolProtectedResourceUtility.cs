using System.Text.RegularExpressions;

namespace PlayerAssistant;

internal enum RpolProtectedResourceKind
{
    AuthenticatedProtectedContent,
    LoginRequired,
    CloudflareChallenge,
    UntrustedNavigation,
    RemoteFailure,
    UnexpectedContent
}

internal sealed record RpolProtectedResourceClassification(
    RpolProtectedResourceKind Kind,
    Uri RequestedUri,
    Uri? FinalUri,
    int? StatusCode,
    string Reason);

internal sealed record RpolProtectedProbeEvidence(
    Uri RequestedUri,
    Uri? ResponseUri,
    Uri? SettledUri,
    int? StatusCode,
    string? ContentType,
    string? SettledHtml,
    string? MainFrameReferer,
    bool SettledAfterStabilization);

internal static partial class RpolProtectedResourceUtility
{
    internal static readonly Uri ProtectedDiceRollerUri =
        new("https://rpol.net/usermodules/diceroller.cgi?gi=80170");

    internal static RpolProtectedResourceClassification Classify(
        Uri requestedUri,
        Uri? finalUri,
        int? statusCode,
        string? contentType,
        string? html,
        string? challengeMarkers = null)
    {
        return Classify(
            requestedUri,
            finalUri,
            finalUri,
            statusCode,
            contentType,
            html,
            challengeMarkers);
    }

    internal static RpolProtectedResourceClassification ClassifyEvidence(
        RpolProtectedProbeEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!evidence.SettledAfterStabilization)
        {
            return Create(
                RpolProtectedResourceKind.RemoteFailure,
                evidence.RequestedUri,
                evidence.SettledUri,
                evidence.StatusCode,
                "The protected probe did not reach a settled navigation boundary.");
        }

        var expectedReferer = AppSettingsUtility.GameForumUrl;
        if (!string.Equals(evidence.MainFrameReferer, expectedReferer, StringComparison.Ordinal))
        {
            return Create(
                RpolProtectedResourceKind.UntrustedNavigation,
                evidence.RequestedUri,
                evidence.SettledUri,
                evidence.StatusCode,
                "The protected probe did not observe the exact expected main-frame Referer header.");
        }

        return Classify(
            evidence.RequestedUri,
            evidence.ResponseUri,
            evidence.SettledUri,
            evidence.StatusCode,
            evidence.ContentType,
            evidence.SettledHtml);
    }

    internal static RpolProtectedResourceClassification Classify(
        Uri requestedUri,
        Uri? responseUri,
        Uri? settledUri,
        int? statusCode,
        string? contentType,
        string? html,
        string? challengeMarkers = null)
    {
        ArgumentNullException.ThrowIfNull(requestedUri);

        if (!IsExactProtectedUri(requestedUri))
        {
            return Create(
                RpolProtectedResourceKind.UntrustedNavigation,
                requestedUri,
                settledUri,
                statusCode,
                "The requested RPOL protected probe URI is not the exact Dice Roller resource.");
        }

        if (responseUri is null && settledUri is null && statusCode is null)
        {
            return Create(
                RpolProtectedResourceKind.RemoteFailure,
                requestedUri,
                settledUri,
                statusCode,
                "The protected probe did not produce a response or final navigation URI.");
        }

        var responseClassification = ClassifyNavigationUri(requestedUri, responseUri, statusCode);
        if (responseClassification is not null)
        {
            return responseClassification;
        }

        var settledClassification = ClassifyNavigationUri(requestedUri, settledUri, statusCode);
        if (settledClassification is not null)
        {
            return settledClassification;
        }

        if (IsChallenge(statusCode, challengeMarkers, html))
        {
            return Create(
                RpolProtectedResourceKind.CloudflareChallenge,
                requestedUri,
                settledUri,
                statusCode,
                "The protected probe returned a browser challenge.");
        }

        if (statusCode is null or < 200 or >= 300)
        {
            return Create(
                RpolProtectedResourceKind.RemoteFailure,
                requestedUri,
                settledUri,
                statusCode,
                "The protected probe did not return a successful response.");
        }

        if (string.IsNullOrWhiteSpace(contentType)
            || !contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(html))
        {
            return Create(
                RpolProtectedResourceKind.UnexpectedContent,
                requestedUri,
                settledUri,
                statusCode,
                "The protected probe did not return a non-empty HTML response.");
        }

        // A login form can remain in RPOL's page shell after protected content
        // is available.  The exact protected-content contract is authoritative.
        if (HasDiceRollerContract(html))
        {
            return Create(
                RpolProtectedResourceKind.AuthenticatedProtectedContent,
                requestedUri,
                settledUri,
                statusCode,
                "The exact RPOL Dice Roller protected resource is accessible.");
        }

        if (LooksLikeLoginForm(html))
        {
            return Create(
                RpolProtectedResourceKind.LoginRequired,
                requestedUri,
                settledUri,
                statusCode,
                "The protected probe response contains the RPOL login form.");
        }

        return Create(
            RpolProtectedResourceKind.UnexpectedContent,
            requestedUri,
            settledUri,
            statusCode,
            "The protected probe response does not match the Dice Roller page contract.");
    }

    private static RpolProtectedResourceClassification? ClassifyNavigationUri(
        Uri requestedUri,
        Uri? navigationUri,
        int? statusCode)
    {
        if (navigationUri is null || IsExactProtectedUri(navigationUri))
        {
            return null;
        }

        if (IsTrustedLoginUri(navigationUri))
        {
            return Create(
                RpolProtectedResourceKind.LoginRequired,
                requestedUri,
                navigationUri,
                statusCode,
                "The protected probe redirected to the RPOL login endpoint.");
        }

        return Create(
            RpolProtectedResourceKind.UntrustedNavigation,
            requestedUri,
            navigationUri,
            statusCode,
            "The protected probe did not remain on the exact RPOL Dice Roller resource.");
    }

    internal static bool IsExactProtectedUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, "rpol.net", StringComparison.OrdinalIgnoreCase)
            && uri.Port is -1 or 443
            && string.IsNullOrWhiteSpace(uri.UserInfo)
            && string.Equals(uri.AbsolutePath, "/usermodules/diceroller.cgi", StringComparison.Ordinal)
            && string.Equals(uri.Query, "?gi=80170", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(uri.Fragment);
    }

    private static bool IsTrustedLoginUri(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, "rpol.net", StringComparison.OrdinalIgnoreCase)
            && uri.Port is -1 or 443
            && string.IsNullOrWhiteSpace(uri.UserInfo)
            && string.Equals(uri.AbsolutePath, "/login.cgi", StringComparison.Ordinal);
    }

    internal static bool LooksLikeLoginForm(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        foreach (Match form in FormRegex().Matches(html))
        {
            var body = form.Groups["body"].Value;
            var controls = AttributeRegex()
                .Matches(body)
                .Cast<Match>()
                .Select(match => new
                {
                    Name = match.Groups["name"].Value,
                    Value = match.Groups["value"].Value
                })
                .ToArray();
            var names = controls
                .Where(control => string.Equals(control.Name, "name", StringComparison.OrdinalIgnoreCase))
                .Select(control => control.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!names.Contains("username") || !names.Contains("password"))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool HasDiceRollerContract(string html)
    {
        var normalizedHtml = NormalizeText(html);
        var title = ProtectedPageTitleRegex()
            .Matches(html)
            .Cast<Match>()
            .Select(match => NormalizeText(match.Groups["title"].Value))
            .FirstOrDefault();
        if (title is null
            || !title.Contains("dice roller", StringComparison.OrdinalIgnoreCase)
            || !normalizedHtml.Contains("step 1: choose the dice", StringComparison.OrdinalIgnoreCase)
            || !normalizedHtml.Contains("roll the dice", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The protected Dice Roller page is authenticated even when no roll has
        // been made in the current session.  Roll-history markers are optional
        // content and must not be part of the authentication boundary.
        return true;
    }

    private static string NormalizeText(string value)
    {
        return Regex.Replace(value, "<[^>]+>", " ")
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToLowerInvariant()
            .Replace("  ", " ", StringComparison.Ordinal);
    }

    private static bool IsChallenge(int? statusCode, string? challengeMarkers, string? html)
    {
        return statusCode == 403
            ? ContainsChallengeMarker(challengeMarkers) || ContainsChallengeMarker(html)
            : ContainsExplicitChallengeMarker(challengeMarkers) || ContainsExplicitChallengeMarker(html);
    }

    private static bool ContainsChallengeMarker(string? value)
    {
        return ContainsExplicitChallengeMarker(value)
            || (!string.IsNullOrWhiteSpace(value)
                && value.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsExplicitChallengeMarker(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (value.Contains("cf-challenge", StringComparison.OrdinalIgnoreCase)
                || value.Contains("cf_clearance", StringComparison.OrdinalIgnoreCase)
                || value.Contains("__cf_chl", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Verify you are human", StringComparison.OrdinalIgnoreCase));
    }

    private static RpolProtectedResourceClassification Create(
        RpolProtectedResourceKind kind,
        Uri requestedUri,
        Uri? finalUri,
        int? statusCode,
        string reason)
    {
        return new RpolProtectedResourceClassification(kind, requestedUri, finalUri, statusCode, reason);
    }

    [GeneratedRegex("<title\\b[^>]*>(?<title>[\\s\\S]*?)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex ProtectedPageTitleRegex();

    [GeneratedRegex("<h1\\b[^>]*>(?<heading>[\\s\\S]*?)</h1>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex("<form\\b[^>]*>(?<body>[\\s\\S]*?)</form>", RegexOptions.IgnoreCase)]
    private static partial Regex FormRegex();

    [GeneratedRegex("(?<name>[A-Za-z_:][A-Za-z0-9_.:-]*)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex("<(?:pre|table|ol|ul|div|p)\\b(?<attributes>[^>]*)>(?<body>[\\s\\S]*?)</(?:pre|table|ol|ul|div|p)>", RegexOptions.IgnoreCase)]
    private static partial Regex RollRecordRegex();
}
