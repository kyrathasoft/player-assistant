<?php

declare(strict_types=1);

final class ElvenTranslator
{
    public const MAX_INPUT_WORDS = 5000;

    /** @var array<string,string> */
    private $terms;

    /** @var int */
    private $uniqueEnglishTerms;

    /** @var int */
    private $maxEnglishPhraseWords;

    /** @var ?array<string,string> */
    private $elvenTerms;

    /** @var int */
    private $maxElvenPhraseWords = 1;

    public function __construct(string $lexiconPath)
    {
        $json = @file_get_contents($lexiconPath);
        if ($json === false) {
            throw new RuntimeException('The Elven lexicon could not be read.');
        }

        try {
            $document = json_decode($json, true, 512, JSON_THROW_ON_ERROR);
        } catch (JsonException $exception) {
            throw new RuntimeException('The Elven lexicon contains invalid JSON.', 0, $exception);
        }

        if (!is_array($document)
            || ($document['schemaVersion'] ?? null) !== 1
            || !isset($document['terms'])
            || !is_array($document['terms'])) {
            throw new RuntimeException('The Elven lexicon has an unsupported format.');
        }

        $this->terms = [];
        foreach ($document['terms'] as $english => $elvish) {
            if (!is_string($elvish) && !is_numeric($elvish)) {
                continue;
            }

            $key = $this->normalize((string)$english);
            $translation = trim((string)$elvish);
            if ($key !== '' && $translation !== '' && !isset($this->terms[$key])) {
                $this->terms[$key] = $translation;
            }
        }

        $this->uniqueEnglishTerms = (int)($document['entryCount'] ?? count($this->terms));
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

    /** @return array{translation:string,untranslatedWords:array<int,string>} */
    public function translateSentenceWithUnknownWords(string $input): array
    {
        return $this->translateWithUnknownWords($input, false);
    }

    /** @return array{translation:string,untranslatedWords:array<int,string>} */
    public function translateElvenSentenceWithUnknownWords(string $input): array
    {
        $this->buildElvenIndex();
        return $this->translateWithUnknownWords($input, true);
    }

    /** @return array{translation:string,untranslatedWords:array<int,string>} */
    private function translateWithUnknownWords(string $input, bool $reverse): array
    {
        $input = trim($input);
        if ($input === '') {
            return ['translation' => '', 'untranslatedWords' => []];
        }

        $terms = preg_split('/\s+/u', $input, -1, PREG_SPLIT_NO_EMPTY);
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

        $dictionary = $reverse ? ($this->elvenTerms ?? []) : $this->terms;
        $maximumPhraseWords = $reverse ? $this->maxElvenPhraseWords : $this->maxEnglishPhraseWords;
        $translatedTerms = [];
        $untranslatedWords = [];
        $termTotal = count($terms);

        for ($index = 0; $index < $termTotal; $index++) {
            list($translated, $consumedTerms, $untranslatedWord) = $this->translateLongestPhrase(
                $terms,
                $index,
                $dictionary,
                $maximumPhraseWords
            );
            $translatedTerms[] = $leadingPunctuation[$index]
                . $translated
                . $trailingPunctuation[$index + $consumedTerms - 1];

            if ($untranslatedWord !== null && preg_match('/\p{L}/u', $untranslatedWord) === 1) {
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

    /**
     * @param array<int,string> $terms
     * @param array<string,string> $dictionary
     * @return array{0:string,1:int,2:?string}
     */
    private function translateLongestPhrase(
        array $terms,
        int $startIndex,
        array $dictionary,
        int $maximumPhraseWords
    ): array {
        $remaining = count($terms) - $startIndex;
        $maximum = min($remaining, $maximumPhraseWords);

        for ($termCount = $maximum; $termCount >= 1; $termCount--) {
            $candidate = implode(' ', array_slice($terms, $startIndex, $termCount));
            $key = $this->normalize($candidate);
            if ($key !== '' && isset($dictionary[$key])) {
                return [$dictionary[$key], $termCount, null];
            }
        }

        return [$terms[$startIndex], 1, $terms[$startIndex]];
    }

    private function buildElvenIndex(): void
    {
        if ($this->elvenTerms !== null) {
            return;
        }

        $this->elvenTerms = [];
        foreach ($this->terms as $english => $elvish) {
            $key = $this->normalize($elvish);
            if ($key === '') {
                continue;
            }

            if (!isset($this->elvenTerms[$key])) {
                $this->elvenTerms[$key] = $english;
            }

            $this->maxElvenPhraseWords = max($this->maxElvenPhraseWords, self::countWords($elvish));
        }
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
        $value = str_replace('’', "'", trim($value));
        return function_exists('mb_strtolower')
            ? mb_strtolower($value, 'UTF-8')
            : strtolower($value);
    }
}
