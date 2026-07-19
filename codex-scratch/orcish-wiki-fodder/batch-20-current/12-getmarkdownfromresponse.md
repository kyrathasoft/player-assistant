---
type: code
status: player-assistant-app
tags:
  - code
  - csharp
---
```
private static string GetMarkdownFromResponse(HttpResponseMessage response, string originalUrl)
{
    using (response)
    {
        response.EnsureSuccessStatusCode();
        var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!IsHtmlResponse(response, content))
        {
            return content;
        }

        var markdownUrl = GetObsidianPublishMarkdownUrl(content);
        if (markdownUrl is null)
        {
            return $"{UnresolvedUrlMessage}: {originalUrl}";
        }

        using var markdownRequest = new HttpRequestMessage(HttpMethod.Get, markdownUrl);
        markdownRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
        markdownRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        using var markdownResponse = HttpClient.Send(markdownRequest);
        markdownResponse.EnsureSuccessStatusCode();

        return markdownResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }
}

private static bool IsHtmlResponse(HttpResponseMessage response, string content)
{
    var mediaType = response.Content.Headers.ContentType?.MediaType;
    return string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
        || content.TrimStart().StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
        || content.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase);
}

private static Uri? GetObsidianPublishMarkdownUrl(string html)
{
    var match = Regex.Match(html, "window\\.preloadPage=f\\(\"(?<url>https://[^\"]+\\.md)\"\\)");
    return match.Success && Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var uri)
        ? uri
        : null;
}

```

In short: it detects HTML, finds Obsidian Publishs embedded window.preloadPage markdown URL, fetches that .md file, and returns the markdown content.

