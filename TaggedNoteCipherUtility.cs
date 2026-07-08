using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PlayerAssistant
{
    internal enum TaggedNoteCipherMode
    {
        Encrypt,
        Decrypt
    }

    internal sealed record EncryptedTextIndexEntry(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("encrypted_sections")] int EncryptedSections,
        [property: JsonPropertyName("frontmatter_tags")] IReadOnlyList<string> FrontmatterTags);

    internal sealed record HeroAccessContext(
        int? Level,
        string? CharacterClass,
        IReadOnlyDictionary<string, int> AbilityScores,
        IReadOnlyDictionary<string, string>? Attributes = null,
        IReadOnlyDictionary<string, int>? RankedMemberships = null)
    {
        public static HeroAccessContext FromPartyHeroSheet(
            PartyHeroSheet hero,
            IReadOnlyDictionary<string, int>? abilityScores = null)
        {
            ArgumentNullException.ThrowIfNull(hero);

            return new HeroAccessContext(
                TryParseFirstInteger(hero.Level, out var level) ? level : null,
                hero.CharacterClass,
                abilityScores ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        }

        private static bool TryParseFirstInteger(string value, out int result)
        {
            result = 0;
            var match = Regex.Match(value, @"\d+");
            return match.Success && int.TryParse(match.Value, out result);
        }
    }

    internal static class TaggedNoteCipherUtility
    {
        public const string EncryptedTextIndexFileName = "encrypted-text-index.json";
        private const string EnvelopePrefix = "PAN1:";
        private const string MismatchedTagsMessage = "unable to decrypt due to non-matching opening and closing tags";
        private const int NonceSizeBytes = 12;
        private const int AuthenticationTagSizeBytes = 16;
        private const string KeySeed = "PlayerAssistant.TaggedNoteCipher.v1";
        private static readonly byte[] EncryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(KeySeed));
        private static readonly JsonSerializerOptions EncryptedTextIndexJsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        public static string TransformTaggedText(
            string text,
            TaggedNoteCipherMode mode,
            IEnumerable<string>? tags = null,
            HeroAccessContext? hero = null)
        {
            ArgumentNullException.ThrowIfNull(text);

            return mode switch
            {
                TaggedNoteCipherMode.Encrypt => Encrypt(text, tags),
                TaggedNoteCipherMode.Decrypt => Decrypt(text, hero),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported tagged note cipher mode.")
            };
        }

        public static string EncryptedTextReport(string url)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);

            return EncryptedTextReportFromMarkdown(MarkdownUtility.GetMarkdownFromURL(url));
        }

        public static async Task<IReadOnlyList<EncryptedTextIndexEntry>> BuildEncryptedTextIndexAsync(
            string sitemapUrl,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sitemapUrl);

            var tempSitemapPath = Path.Combine(
                Path.GetTempPath(),
                $"player-assistant-encrypted-text-index-{Guid.NewGuid():N}.xml");
            try
            {
                await SitemapUtility.DownloadSitemapAsync(sitemapUrl, tempSitemapPath, cancellationToken);
                var urls = await SitemapUtility.ReadUrlsFromSitemapAsync(tempSitemapPath, cancellationToken);
                var entries = new List<EncryptedTextIndexEntry>();

                foreach (var url in urls)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var markdown = await MarkdownUtility.GetMarkdownFromUrlAsync(url, cancellationToken);
                    if (IsMarkdownFetchFailure(markdown, url))
                    {
                        continue;
                    }

                    var entry = CreateEncryptedTextIndexEntry(url, markdown);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }

                return entries;
            }
            finally
            {
                if (File.Exists(tempSitemapPath))
                {
                    File.Delete(tempSitemapPath);
                }
            }
        }

        public static async Task SaveEncryptedTextIndexAsync(
            string sitemapUrl,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

            var entries = await BuildEncryptedTextIndexAsync(sitemapUrl, cancellationToken);
            await AtomicFileUtility.WriteFileAsync(
                destinationPath,
                destination => JsonSerializer.SerializeAsync(
                    destination,
                    entries,
                    EncryptedTextIndexJsonOptions,
                    cancellationToken),
                cancellationToken);
        }

        internal static string EncryptedTextReportFromMarkdown(string markdown)
        {
            ArgumentNullException.ThrowIfNull(markdown);

            var validEncryptedBlocks = 0;
            var mismatchedTags = 0;
            foreach (var block in FindEncryptedBlocks(markdown))
            {
                var result = TryClassifyEncryptedBlock(block);
                if (result == EncryptedBlockReportStatus.Valid)
                {
                    validEncryptedBlocks++;
                    continue;
                }

                if (result == EncryptedBlockReportStatus.MismatchedTags)
                {
                    mismatchedTags++;
                }
            }

            return $"valid encrypted blocks: {validEncryptedBlocks}, mismatched tags: {mismatchedTags}";
        }

        internal static EncryptedTextIndexEntry? CreateEncryptedTextIndexEntry(string url, string markdown)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);
            ArgumentNullException.ThrowIfNull(markdown);

            var encryptedSectionCount = FindEncryptedBlocks(markdown).Count;
            if (encryptedSectionCount == 0)
            {
                return null;
            }

            return new EncryptedTextIndexEntry(
                url,
                encryptedSectionCount,
                ExtractFrontmatterTags(markdown));
        }

        internal static IReadOnlyList<string> ExtractFrontmatterTags(string markdown)
        {
            ArgumentNullException.ThrowIfNull(markdown);

            var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            {
                return [];
            }

            var frontmatterEnd = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (frontmatterEnd < 0)
            {
                return [];
            }

            var tags = new List<string>();
            var lines = normalized[4..frontmatterEnd].Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("tags:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var inlineTags = trimmed["tags:".Length..].Trim();
                if (inlineTags.Length > 0)
                {
                    AddInlineFrontmatterTags(tags, inlineTags);
                    continue;
                }

                index = AddListFrontmatterTags(tags, lines, index + 1);
            }

            return tags
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool IsMarkdownFetchFailure(string markdown, string url)
        {
            return string.Equals(markdown, $"{MarkdownUtility.InvalidUrlMessage}: {url}", StringComparison.Ordinal)
                || string.Equals(markdown, $"{MarkdownUtility.UnresolvedUrlMessage}: {url}", StringComparison.Ordinal);
        }

        private static void AddInlineFrontmatterTags(List<string> tags, string value)
        {
            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                foreach (var item in value[1..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    tags.Add(UnquoteFrontmatterValue(item));
                }

                return;
            }

            tags.Add(UnquoteFrontmatterValue(value));
        }

        private static int AddListFrontmatterTags(List<string> tags, string[] lines, int startIndex)
        {
            var index = startIndex;
            for (; index < lines.Length; index++)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    break;
                }

                tags.Add(UnquoteFrontmatterValue(trimmed[2..].Trim()));
            }

            return index - 1;
        }

        private static string UnquoteFrontmatterValue(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length >= 2
                && ((trimmed[0] == '"' && trimmed[^1] == '"')
                    || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
            {
                return trimmed[1..^1].Trim();
            }

            return trimmed;
        }

        private static string Encrypt(string taggedPlaintext, IEnumerable<string>? tags)
        {
            var (openingTags, plaintext, closingTags) = ExtractTaggedContent(taggedPlaintext);
            if (tags is not null)
            {
                var suppliedTags = NormalizeTags(tags);
                if (!string.Equals(suppliedTags, openingTags, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Supplied access tags must match the plaintext tag wrappers.", nameof(tags));
                }
            }

            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            var ciphertext = new byte[plaintextBytes.Length];
            var authenticationTag = new byte[AuthenticationTagSizeBytes];
            var associatedData = Encoding.UTF8.GetBytes(openingTags);
            byte[]? payload = null;

            try
            {
                using var aes = new AesGcm(EncryptionKey, AuthenticationTagSizeBytes);
                aes.Encrypt(nonce, plaintextBytes, ciphertext, authenticationTag, associatedData);

                payload = new byte[nonce.Length + ciphertext.Length + authenticationTag.Length];
                Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
                Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length, ciphertext.Length);
                Buffer.BlockCopy(authenticationTag, 0, payload, nonce.Length + ciphertext.Length, authenticationTag.Length);

                return $"{openingTags}{EnvelopePrefix}{Base64UrlEncode(payload)}{closingTags}";
            }
            finally
            {
                ZeroMemory(plaintextBytes);
                ZeroMemory(ciphertext);
                ZeroMemory(authenticationTag);
                ZeroMemory(associatedData);
                ZeroMemory(payload);
            }
        }

        private static EncryptedBlockReportStatus TryClassifyEncryptedBlock(string encryptedBlock)
        {
            try
            {
                var decrypted = TransformTaggedText(encryptedBlock, TaggedNoteCipherMode.Decrypt);
                return string.Equals(decrypted, MismatchedTagsMessage, StringComparison.Ordinal)
                    ? EncryptedBlockReportStatus.MismatchedTags
                    : EncryptedBlockReportStatus.Valid;
            }
            catch (UnauthorizedAccessException)
            {
                return EncryptedBlockReportStatus.Valid;
            }
            catch (InvalidOperationException)
            {
                return EncryptedBlockReportStatus.Invalid;
            }
            catch (FormatException)
            {
                return EncryptedBlockReportStatus.Invalid;
            }
        }

        private static IReadOnlyList<string> FindEncryptedBlocks(string markdown)
        {
            var blocks = new List<string>();
            var searchIndex = 0;
            while (searchIndex < markdown.Length)
            {
                var payloadMarkerIndex = markdown.IndexOf(EnvelopePrefix, searchIndex, StringComparison.Ordinal);
                if (payloadMarkerIndex < 0)
                {
                    break;
                }

                var openingTag = TryFindOpeningTag(markdown, payloadMarkerIndex);
                var closingTag = TryFindClosingTag(markdown, payloadMarkerIndex + EnvelopePrefix.Length);
                if (openingTag is not null && closingTag is not null)
                {
                    var payload = ReadEncryptedPayload(markdown, payloadMarkerIndex);
                    if (payload.Length > EnvelopePrefix.Length)
                    {
                        blocks.Add($"{openingTag}{payload}{closingTag.Value.Tag}");
                    }

                    searchIndex = closingTag.Value.EndIndex;
                    continue;
                }

                searchIndex = payloadMarkerIndex + EnvelopePrefix.Length;
            }

            return blocks;
        }

        private static string? TryFindOpeningTag(string markdown, int payloadMarkerIndex)
        {
            var index = payloadMarkerIndex - 1;
            while (index >= 0 && char.IsWhiteSpace(markdown[index]))
            {
                index--;
            }

            if (index < 0 || markdown[index] != '}')
            {
                return null;
            }

            var tagStart = markdown.LastIndexOf('{', index);
            return tagStart < 0
                ? null
                : markdown[tagStart..(index + 1)];
        }

        private static (string Tag, int EndIndex)? TryFindClosingTag(string markdown, int payloadStartIndex)
        {
            var index = payloadStartIndex;
            while (index < markdown.Length && IsEncryptedPayloadCharacter(markdown[index]))
            {
                index++;
            }

            while (index < markdown.Length && char.IsWhiteSpace(markdown[index]))
            {
                index++;
            }

            if (index >= markdown.Length || markdown[index] != '{')
            {
                return null;
            }

            var closeIndex = markdown.IndexOf('}', index + 1);
            return closeIndex < 0
                ? null
                : (markdown[index..(closeIndex + 1)], closeIndex + 1);
        }

        private static string ReadEncryptedPayload(string markdown, int payloadMarkerIndex)
        {
            var builder = new StringBuilder(EnvelopePrefix);
            var index = payloadMarkerIndex + EnvelopePrefix.Length;
            while (index < markdown.Length && IsEncryptedPayloadCharacter(markdown[index]))
            {
                if (!char.IsWhiteSpace(markdown[index]))
                {
                    builder.Append(markdown[index]);
                }

                index++;
            }

            return builder.ToString();
        }

        private static bool IsEncryptedPayloadCharacter(char value)
        {
            return char.IsWhiteSpace(value)
                || char.IsAsciiLetterOrDigit(value)
                || value == '-'
                || value == '_';
        }

        private static string Decrypt(string encryptedText, HeroAccessContext? hero)
        {
            var taggedContent = TryExtractTaggedContent(encryptedText);
            if (!taggedContent.Success && taggedContent.HasMismatchedTags)
            {
                return MismatchedTagsMessage;
            }

            if (!taggedContent.Success)
            {
                throw new InvalidOperationException(taggedContent.ErrorMessage);
            }

            var openingTags = taggedContent.OpeningTags;
            var encryptedPayload = taggedContent.Content;
            var closingTags = taggedContent.ClosingTags;
            if (!encryptedPayload.StartsWith(EnvelopePrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Tagged note cipher payload is missing its envelope prefix.");
            }

            var tagGroups = ParseTagBlock(openingTags);
            if (!CanAccess(tagGroups, hero))
            {
                throw new UnauthorizedAccessException("The logged-in hero does not satisfy the encrypted note tags.");
            }

            var payload = Base64UrlDecode(encryptedPayload[EnvelopePrefix.Length..]);
            if (payload.Length < NonceSizeBytes + AuthenticationTagSizeBytes)
            {
                throw new InvalidOperationException("Tagged note cipher payload is too short.");
            }

            var nonce = payload[..NonceSizeBytes];
            var authenticationTag = payload[^AuthenticationTagSizeBytes..];
            var ciphertext = payload[NonceSizeBytes..^AuthenticationTagSizeBytes];
            var plaintextBytes = new byte[ciphertext.Length];
            var associatedData = Encoding.UTF8.GetBytes(openingTags);

            try
            {
                using var aes = new AesGcm(EncryptionKey, AuthenticationTagSizeBytes);
                aes.Decrypt(nonce, ciphertext, authenticationTag, plaintextBytes, associatedData);
                return $"{openingTags}{Encoding.UTF8.GetString(plaintextBytes)}{closingTags}";
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException("Tagged note cipher text could not be authenticated or decrypted.", ex);
            }
            finally
            {
                ZeroMemory(payload);
                ZeroMemory(plaintextBytes);
                ZeroMemory(associatedData);
            }
        }

        private static (string OpeningTags, string Content, string ClosingTags) ExtractTaggedContent(string value)
        {
            var taggedContent = TryExtractTaggedContent(value);
            if (!taggedContent.Success)
            {
                throw new InvalidOperationException(taggedContent.ErrorMessage);
            }

            return (
                taggedContent.OpeningTags,
                taggedContent.Content,
                taggedContent.ClosingTags);
        }

        private static TaggedContentResult TryExtractTaggedContent(string value)
        {
            var openingTags = ReadLeadingTags(value);
            if (openingTags.Length == 0)
            {
                return TaggedContentResult.Failure("Tagged note text must begin with at least one access tag.");
            }

            var contentStart = openingTags.Length;
            var closingTagStart = FindClosingTagStart(value, contentStart);
            if (closingTagStart < 0)
            {
                return TaggedContentResult.Failure("Tagged note text must end with an access tag block.", hasMismatchedTags: true);
            }

            var closingTags = value[closingTagStart..];
            if (!string.Equals(openingTags, closingTags, StringComparison.Ordinal))
            {
                return TaggedContentResult.Failure(
                    "Tagged note text must end with the same access tag block it begins with.",
                    hasMismatchedTags: true);
            }

            _ = ParseTagBlock(openingTags);
            return TaggedContentResult.Valid(
                openingTags,
                value[contentStart..closingTagStart],
                closingTags);
        }

        private static int FindClosingTagStart(string value, int contentStart)
        {
            var openIndex = value.LastIndexOf('{');
            if (openIndex < contentStart)
            {
                return -1;
            }

            while (openIndex > contentStart)
            {
                var previousOpenIndex = value.LastIndexOf('{', openIndex - 1);
                if (previousOpenIndex < contentStart)
                {
                    break;
                }

                openIndex = previousOpenIndex;
            }

            return openIndex;
        }

        private static string ReadLeadingTags(string value)
        {
            var index = 0;
            while (index < value.Length && value[index] == '{')
            {
                var closeIndex = value.IndexOf('}', index + 1);
                if (closeIndex < 0)
                {
                    throw new InvalidOperationException("Tagged note text contains an unterminated access tag.");
                }

                index = closeIndex + 1;
            }

            return value[..index];
        }

        private static string NormalizeTags(IEnumerable<string>? tags)
        {
            if (tags is null)
            {
                throw new ArgumentException("At least one access tag is required.", nameof(tags));
            }

            var normalizedTags = tags
                .Select(tag => tag?.Trim() ?? string.Empty)
                .Where(tag => tag.Length > 0)
                .ToArray();
            if (normalizedTags.Length == 0)
            {
                throw new ArgumentException("At least one access tag is required.", nameof(tags));
            }

            foreach (var tag in normalizedTags)
            {
                _ = ParseTag(tag);
            }

            return string.Concat(normalizedTags);
        }

        private static IReadOnlyList<TagGroup> ParseTagBlock(string tagBlock)
        {
            var groups = new List<TagGroup>();
            var index = 0;
            while (index < tagBlock.Length)
            {
                if (tagBlock[index] != '{')
                {
                    throw new InvalidOperationException("Tagged note cipher text contains malformed access tags.");
                }

                var closeIndex = tagBlock.IndexOf('}', index + 1);
                if (closeIndex < 0)
                {
                    throw new InvalidOperationException("Tagged note cipher text contains an unterminated access tag.");
                }

                groups.Add(ParseTag(tagBlock[index..(closeIndex + 1)]));
                index = closeIndex + 1;
            }

            if (groups.Count == 0)
            {
                throw new InvalidOperationException("Tagged note cipher text does not contain any access tags.");
            }

            return groups;
        }

        private static TagGroup ParseTag(string tag)
        {
            if (!tag.StartsWith('{') || !tag.EndsWith('}') || tag.Length <= 2)
            {
                throw new ArgumentException($"Access tag '{tag}' must use '{{tag data}}' syntax.", nameof(tag));
            }

            var parser = new TagExpressionParser(tag[1..^1]);
            var expression = parser.Parse();
            if (expression is null)
            {
                throw new ArgumentException($"Access tag '{tag}' does not contain a tag name and value.", nameof(tag));
            }

            return new TagGroup(expression);
        }

        private static TagRequirement ParseRequirement(string expression)
        {
            var parts = expression.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                return new TagRequirement("Class", parts[0]);
            }

            if (parts.Length != 2)
            {
                throw new ArgumentException($"Access tag expression '{expression}' must contain a tag name and value.", nameof(expression));
            }

            return new TagRequirement(parts[0], parts[1]);
        }

        private sealed class TagExpressionParser
        {
            private readonly string _text;
            private int _index;

            public TagExpressionParser(string text)
            {
                _text = text;
            }

            public TagExpression Parse()
            {
                SkipWhitespace();
                if (_index >= _text.Length)
                {
                    throw new ArgumentException("Access tag expression cannot be empty.", nameof(_text));
                }

                var expression = ParseOr();
                SkipWhitespace();
                if (_index != _text.Length)
                {
                    throw new ArgumentException($"Unexpected access tag syntax near '{_text[_index..]}'.", nameof(_text));
                }

                return expression;
            }

            private TagExpression ParseOr()
            {
                var left = ParseAnd();
                while (true)
                {
                    SkipWhitespace();
                    if (!TryMatch("|"))
                    {
                        return left;
                    }

                    left = new OrExpression(left, ParseAnd());
                }
            }

            private TagExpression ParseAnd()
            {
                var left = ParsePrimary();
                while (true)
                {
                    SkipWhitespace();
                    if (!TryMatch("&&"))
                    {
                        return left;
                    }

                    left = new AndExpression(left, ParsePrimary());
                }
            }

            private TagExpression ParsePrimary()
            {
                SkipWhitespace();
                if (_index >= _text.Length)
                {
                    throw new ArgumentException("Access tag expression ended unexpectedly.", nameof(_text));
                }

                if (TryMatch("("))
                {
                    var expression = ParseOr();
                    SkipWhitespace();
                    if (!TryMatch(")"))
                    {
                        throw new ArgumentException("Access tag expression contains an unclosed parenthesis.", nameof(_text));
                    }

                    return expression;
                }

                return new RequirementExpression(ParseRequirement(ReadRequirementExpression()));
            }

            private string ReadRequirementExpression()
            {
                var start = _index;
                while (_index < _text.Length)
                {
                    if (_text[_index] == '|' || _text[_index] == ')' || StartsWith("&&"))
                    {
                        break;
                    }

                    _index++;
                }

                var expression = _text[start.._index].Trim();
                if (expression.Length == 0)
                {
                    throw new ArgumentException("Access tag expression contains a missing requirement.", nameof(_text));
                }

                return expression;
            }

            private bool TryMatch(string token)
            {
                if (!StartsWith(token))
                {
                    return false;
                }

                _index += token.Length;
                return true;
            }

            private bool StartsWith(string token)
            {
                return _index + token.Length <= _text.Length
                    && string.Compare(_text, _index, token, 0, token.Length, StringComparison.Ordinal) == 0;
            }

            private void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                {
                    _index++;
                }
            }
        }

        private static bool CanAccess(IReadOnlyList<TagGroup> tagGroups, HeroAccessContext? hero)
        {
            if (hero is null)
            {
                return false;
            }

            return tagGroups.All(group => ExpressionMatches(group.Expression, hero));
        }

        private static bool ExpressionMatches(TagExpression expression, HeroAccessContext hero)
        {
            return expression switch
            {
                RequirementExpression requirement => RequirementMatches(requirement.Requirement, hero),
                AndExpression and => ExpressionMatches(and.Left, hero) && ExpressionMatches(and.Right, hero),
                OrExpression or => ExpressionMatches(or.Left, hero) || ExpressionMatches(or.Right, hero),
                _ => throw new InvalidOperationException("Unsupported tagged note access expression.")
            };
        }

        private static bool RequirementMatches(TagRequirement requirement, HeroAccessContext hero)
        {
            if (IsLevelTag(requirement.Name))
            {
                return int.TryParse(requirement.Value, out var requiredLevel)
                    && hero.Level is >= 0
                    && hero.Level.Value >= requiredLevel;
            }

            if (IsClassTag(requirement.Name))
            {
                return ClassMatches(hero.CharacterClass, requirement.Value);
            }

            var valueIsNumeric = int.TryParse(requirement.Value, out var numericValue);
            if (valueIsNumeric && ClassMatches(hero.CharacterClass, requirement.Name))
            {
                return hero.Level is >= 0 && hero.Level.Value >= numericValue;
            }

            if (valueIsNumeric && TryGetAbilityScore(hero, requirement.Name, out var abilityScore))
            {
                return abilityScore >= numericValue;
            }

            if (valueIsNumeric && TryGetRankedMembership(hero, requirement.Name, out var membershipRank))
            {
                return membershipRank >= numericValue;
            }

            return TryGetAttributeValue(hero, requirement.Name, out var attributeValue)
                && string.Equals(attributeValue, requirement.Value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLevelTag(string name)
        {
            return string.Equals(name, "Level", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Lvl", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsClassTag(string name)
        {
            return string.Equals(name, "Class", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ClassMatches(string? heroClass, string requiredClass)
        {
            if (string.IsNullOrWhiteSpace(heroClass))
            {
                return false;
            }

            return heroClass
                .Split(new[] { '/', ',', ';', '&' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(candidate => string.Equals(candidate, requiredClass, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryGetAbilityScore(HeroAccessContext hero, string tagName, out int score)
        {
            score = 0;
            var normalizedTagName = NormalizeAbilityName(tagName);
            foreach (var pair in hero.AbilityScores)
            {
                if (string.Equals(NormalizeAbilityName(pair.Key), normalizedTagName, StringComparison.OrdinalIgnoreCase))
                {
                    score = pair.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetAttributeValue(HeroAccessContext hero, string tagName, out string value)
        {
            value = string.Empty;
            if (hero.Attributes is null)
            {
                return false;
            }

            foreach (var pair in hero.Attributes)
            {
                if (string.Equals(pair.Key, tagName, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetRankedMembership(HeroAccessContext hero, string tagName, out int rank)
        {
            rank = 0;
            if (hero.RankedMemberships is null)
            {
                return false;
            }

            foreach (var pair in hero.RankedMemberships)
            {
                if (string.Equals(pair.Key, tagName, StringComparison.OrdinalIgnoreCase))
                {
                    rank = pair.Value;
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeAbilityName(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "str" or "strength" => "strength",
                "dex" or "dexterity" => "dexterity",
                "con" or "constitution" => "constitution",
                "int" or "intelligence" => "intelligence",
                "wis" or "wisdom" => "wisdom",
                "cha" or "charisma" => "charisma",
                _ => value.Trim().ToLowerInvariant()
            };
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string value)
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            return Convert.FromBase64String(base64);
        }

        private static void ZeroMemory(byte[]? bytes)
        {
            if (bytes is { Length: > 0 })
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        private sealed record TagGroup(TagExpression Expression);

        private abstract record TagExpression;

        private sealed record RequirementExpression(TagRequirement Requirement) : TagExpression;

        private sealed record AndExpression(TagExpression Left, TagExpression Right) : TagExpression;

        private sealed record OrExpression(TagExpression Left, TagExpression Right) : TagExpression;

        private sealed record TagRequirement(string Name, string Value);

        private enum EncryptedBlockReportStatus
        {
            Valid,
            MismatchedTags,
            Invalid
        }

        private sealed record TaggedContentResult(
            bool Success,
            string OpeningTags,
            string Content,
            string ClosingTags,
            bool HasMismatchedTags,
            string ErrorMessage)
        {
            public static TaggedContentResult Valid(string openingTags, string content, string closingTags)
            {
                return new TaggedContentResult(true, openingTags, content, closingTags, false, string.Empty);
            }

            public static TaggedContentResult Failure(string errorMessage, bool hasMismatchedTags = false)
            {
                return new TaggedContentResult(false, string.Empty, string.Empty, string.Empty, hasMismatchedTags, errorMessage);
            }
        }
    }
}
