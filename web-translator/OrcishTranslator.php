<?php

declare(strict_types=1);

final class OrcishTranslator
{
    public const MAX_INPUT_WORDS = 5000;

    /** @var array<string,array{0:string,1:array<int,array{0:string,1:?string,2:?string,3:array<int,string>}>}> */
    private $terms;

    /** @var int */
    private $uniqueEnglishTerms;

    /** @var int */
    private $maxEnglishPhraseWords;

    public function __construct(string $lexiconPath)
    {
        $json = @file_get_contents($lexiconPath);
        if ($json === false) {
            throw new RuntimeException('The Orcish lexicon could not be read.');
        }

        try {
            $document = json_decode($json, true, 512, JSON_THROW_ON_ERROR);
        } catch (JsonException $exception) {
            throw new RuntimeException('The Orcish lexicon contains invalid JSON.', 0, $exception);
        }

        if (!is_array($document)
            || ($document['schemaVersion'] ?? null) !== 1
            || !isset($document['terms'])
            || !is_array($document['terms'])) {
            throw new RuntimeException('The Orcish lexicon has an unsupported format.');
        }

        $this->terms = $document['terms'];
        $this->uniqueEnglishTerms = (int)($document['uniqueEnglishTerms'] ?? count($this->terms));
        $this->maxEnglishPhraseWords = max(1, (int)($document['maxEnglishPhraseWords'] ?? 1));
    }

    public function getEnglishTermCount(): int
    {
        return $this->uniqueEnglishTerms;
    }

    public static function countWords(string $text): int
    {
        if (trim($text) === '') {
            return 0;
        }

        $wordCount = preg_match_all("/[\\p{L}\\p{N}]+(?:['’\\-][\\p{L}\\p{N}]+)*/u", $text);
        return $wordCount === false ? str_word_count($text) : $wordCount;
    }

    /**
     * @return array<int,array{translation:string,partOfSpeech:?string,grammarClass:?string,tags:array<int,string>}>
     */
    public function translateTerm(string $english): array
    {
        $key = $this->normalize($english);
        if ($key === '' || !isset($this->terms[$key][1]) || !is_array($this->terms[$key][1])) {
            return [];
        }

        $translations = [];
        foreach ($this->terms[$key][1] as $candidate) {
            if (!is_array($candidate) || !isset($candidate[0])) {
                continue;
            }

            $translations[] = [
                'translation' => (string)$candidate[0],
                'partOfSpeech' => isset($candidate[1]) ? (string)$candidate[1] : null,
                'grammarClass' => isset($candidate[2]) ? (string)$candidate[2] : null,
                'tags' => isset($candidate[3]) && is_array($candidate[3]) ? array_values($candidate[3]) : [],
            ];
        }

        return $translations;
    }

    public function translateSentence(string $input): string
    {
        return $this->translateSentenceWithUnknownWords($input)['translation'];
    }

    /** @return array{translation:string,untranslatedWords:array<int,string>} */
    public function translateSentenceWithUnknownWords(string $input): array
    {
        $input = trim($input);
        if ($input === '') {
            return ['translation' => '', 'untranslatedWords' => []];
        }

        $input = $this->translateFirstPersonPronouns($input);
        $terms = preg_split('/\s+/u', trim($input), -1, PREG_SPLIT_NO_EMPTY);
        if ($terms === false || count($terms) === 0) {
            return ['translation' => '', 'untranslatedWords' => []];
        }

        $leadingPunctuation = [];
        $trailingPunctuation = [];
        foreach ($terms as $index => $term) {
            $leadingPunctuation[$index] = $this->stripLeadingPunctuation($term);
            $trailingPunctuation[$index] = $this->stripTrailingPunctuation($term);
            $terms[$index] = $term;
        }

        $translatedTerms = [];
        $untranslatedWords = [];
        $termTotal = count($terms);
        for ($index = 0; $index < $termTotal; $index++) {
            $normalized = $this->normalize($terms[$index]);

            if ($normalized === 'the') {
                $translatedTerms[] = $leadingPunctuation[$index]
                    . $this->translateDefiniteArticle($terms, $index)
                    . $trailingPunctuation[$index];
                continue;
            }

            if ($normalized === 'at') {
                $translatedTerms[] = $leadingPunctuation[$index]
                    . $this->translatePrepositionByNextWordSound($terms, $index, 'ak', 'kaat')
                    . $trailingPunctuation[$index];
                continue;
            }

            if ($normalized === 'to') {
                $translatedTerms[] = $leadingPunctuation[$index]
                    . $this->translatePrepositionByNextWordSound($terms, $index, 'ur', 'kur')
                    . $trailingPunctuation[$index];
                continue;
            }

            if ($normalized === 'in') {
                $translatedTerms[] = $leadingPunctuation[$index]
                    . $this->translatePrepositionByNextWordSound($terms, $index, 'ik', "k'ik")
                    . $trailingPunctuation[$index];
                continue;
            }

            list($translated, $consumedTerms, $untranslatedWord) = $this->translateLongestPhrase($terms, $index);
            $translatedTerms[] = $leadingPunctuation[$index]
                . $translated
                . $trailingPunctuation[$index + $consumedTerms - 1];

            if ($untranslatedWord !== null && $this->shouldReportUnknownWord($untranslatedWord)) {
                $unknownKey = $this->normalize($untranslatedWord);
                if (!isset($untranslatedWords[$unknownKey])) {
                    $untranslatedWords[$unknownKey] = $untranslatedWord;
                }
            }

            $index += $consumedTerms - 1;
        }

        return [
            'translation' => $this->capitalizeSentenceStarts(implode(' ', $translatedTerms)),
            'untranslatedWords' => array_values($untranslatedWords),
        ];
    }

    private function translateFirstPersonPronouns(string $input): string
    {
        $translated = preg_replace('/(?<!\S)I\s+\{emphasis\}(?!\S)/u', 'Grrt-Ugh', $input);
        if ($translated === null) {
            return $input;
        }

        $translated = preg_replace_callback(
            '/(?<!\S)I(?!\S)/u',
            static function (): string {
                return random_int(0, 1) === 0 ? 'Ugh' : 'Grrt';
            },
            $translated
        );

        return $translated === null ? $input : $translated;
    }

    /** @param array<int,string> $terms */
    private function translateDefiniteArticle(array $terms, int $articleIndex): string
    {
        if ($articleIndex >= count($terms) - 1) {
            return 'arhk';
        }

        list($translation) = $this->translateLongestPhrase($terms, $articleIndex + 1);
        return $this->startsWithOrcishVowel($translation) ? 'karnt' : 'arhk';
    }

    /** @param array<int,string> $terms */
    private function translatePrepositionByNextWordSound(
        array $terms,
        int $prepositionIndex,
        string $beforeConsonant,
        string $beforeVowel
    ): string {
        if ($prepositionIndex >= count($terms) - 1) {
            return $beforeConsonant;
        }

        list($translation) = $this->translateLongestPhrase($terms, $prepositionIndex + 1);
        return $this->startsWithOrcishVowel($translation) ? $beforeVowel : $beforeConsonant;
    }

    /**
     * @param array<int,string> $terms
     * @return array{0:string,1:int,2:?string}
     */
    private function translateLongestPhrase(array $terms, int $startIndex): array
    {
        $remaining = count($terms) - $startIndex;
        $maximum = min($remaining, $this->maxEnglishPhraseWords);

        for ($termCount = $maximum; $termCount >= 1; $termCount--) {
            $candidate = implode(' ', array_slice($terms, $startIndex, $termCount));
            $translations = $this->translateTerm($candidate);
            if (count($translations) === 0) {
                continue;
            }

            return [
                $this->selectBestTranslation($terms, $startIndex, $termCount, $translations)['translation'],
                $termCount,
                null,
            ];
        }

        return [$terms[$startIndex], 1, $terms[$startIndex]];
    }

    private function shouldReportUnknownWord(string $word): bool
    {
        $normalized = $this->normalize($word);
        if (in_array($normalized, ['ugh', 'grrt', 'grrt-ugh'], true)) {
            return false;
        }

        return preg_match('/\p{L}/u', $word) === 1;
    }

    /**
     * @param array<int,string> $terms
     * @param array<int,array{translation:string,partOfSpeech:?string,grammarClass:?string,tags:array<int,string>}> $translations
     * @return array{translation:string,partOfSpeech:?string,grammarClass:?string,tags:array<int,string>}
     */
    private function selectBestTranslation(
        array $terms,
        int $startIndex,
        int $termCount,
        array $translations
    ): array {
        if (count($translations) === 1) {
            return $translations[0];
        }

        $previousEnglish = $startIndex > 0 ? $terms[$startIndex - 1] : null;
        $nextIndex = $startIndex + $termCount;
        $nextEnglish = $nextIndex < count($terms) ? $terms[$nextIndex] : null;

        if ($this->isLinkingVerb($previousEnglish)) {
            foreach ($translations as $translation) {
                if ($this->equalsIgnoreCase($translation['partOfSpeech'], 'adjective')
                    || $this->hasTag($translation['tags'], 'subject-complement')) {
                    return $translation;
                }
            }
        }

        if ($this->isSubjectLike($previousEnglish) || $this->equalsIgnoreCase($previousEnglish, 'to')) {
            foreach ($translations as $translation) {
                if ($this->equalsIgnoreCase($translation['partOfSpeech'], 'verb')) {
                    return $translation;
                }
            }
        }

        if ($this->isLikelyNounContext($previousEnglish, $nextEnglish)) {
            foreach ($translations as $translation) {
                if ($this->equalsIgnoreCase($translation['partOfSpeech'], 'noun')) {
                    return $translation;
                }
            }
        }

        return $translations[0];
    }

    private function isSubjectLike(?string $term): bool
    {
        return in_array($this->normalizeNullable($term), ['i', 'you', 'he', 'she', 'it', 'we', 'they'], true);
    }

    private function isLinkingVerb(?string $term): bool
    {
        return in_array($this->normalizeNullable($term), ['be', 'am', 'is', 'are', 'was', 'were', 'been', 'being'], true);
    }

    private function isLikelyNounContext(?string $previousEnglish, ?string $nextEnglish): bool
    {
        return in_array($this->normalizeNullable($previousEnglish), ['a', 'an', 'the', 'those', 'these'], true)
            || in_array($this->normalizeNullable($nextEnglish), ["'s", 'is', 'are', 'was', 'were'], true);
    }

    /** @param array<int,string> $tags */
    private function hasTag(array $tags, string $expected): bool
    {
        foreach ($tags as $tag) {
            if ($this->equalsIgnoreCase((string)$tag, $expected)) {
                return true;
            }
        }

        return false;
    }

    private function startsWithOrcishVowel(string $value): bool
    {
        if (preg_match('/[A-Za-z]/', $value, $match) !== 1) {
            return false;
        }

        return strpos('aeiou', strtolower($match[0])) !== false;
    }

    private function stripLeadingPunctuation(string &$term): string
    {
        if (preg_match('/^[\(\["\']+/u', $term, $match) !== 1) {
            return '';
        }

        $punctuation = $match[0];
        $term = substr($term, strlen($punctuation));
        return $punctuation;
    }

    private function stripTrailingPunctuation(string &$term): string
    {
        if (strpos($term, '.') !== false && count($this->translateTerm($term)) > 0) {
            return '';
        }

        if (preg_match('/[.!?,;:\)\]"\']+$/u', $term, $match) !== 1) {
            return '';
        }

        $punctuation = $match[0];
        $term = substr($term, 0, strlen($term) - strlen($punctuation));
        return $punctuation;
    }

    private function capitalizeSentenceStarts(string $text): string
    {
        $result = '';
        $capitalizeNextLetter = true;
        $length = strlen($text);

        for ($index = 0; $index < $length; $index++) {
            $character = $text[$index];
            if ($capitalizeNextLetter && preg_match('/[A-Za-z]/', $character) === 1) {
                $result .= strtoupper($character);
                $capitalizeNextLetter = false;
                continue;
            }

            $result .= $character;
            if ($character === '.' || $character === '!' || $character === '?') {
                $capitalizeNextLetter = true;
            } elseif (!ctype_space($character)
                && $character !== '"'
                && $character !== "'"
                && $character !== ')'
                && $character !== ']') {
                $capitalizeNextLetter = false;
            }
        }

        return $result;
    }

    private function normalize(string $value): string
    {
        return strtolower(trim($value));
    }

    private function normalizeNullable(?string $value): string
    {
        return $value === null ? '' : $this->normalize($value);
    }

    private function equalsIgnoreCase(?string $left, ?string $right): bool
    {
        return $left !== null && $right !== null && strcasecmp($left, $right) === 0;
    }
}
