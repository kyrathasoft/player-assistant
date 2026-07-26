<?php

declare(strict_types=1);

final class WordCountService
{
    public function __construct(private readonly PDO $database)
    {
        $this->database->exec(
            'CREATE TABLE IF NOT EXISTS word_count_snapshots (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                schema_version INTEGER NOT NULL,
                observed_at TEXT NOT NULL,
                counting_rule_version TEXT NOT NULL,
                wiki_pages INTEGER NOT NULL,
                wiki_words INTEGER NOT NULL,
                ic_files INTEGER NOT NULL,
                ic_words INTEGER NOT NULL,
                ooc_files INTEGER NOT NULL,
                ooc_words INTEGER NOT NULL,
                uploaded_at INTEGER NOT NULL
            )');
    }

    public function store(array $body): array
    {
        $snapshot = $this->validate($body);
        $uploadedAt = time();
        $statement = $this->database->prepare(
            'INSERT INTO word_count_snapshots (
                id, schema_version, observed_at, counting_rule_version,
                wiki_pages, wiki_words, ic_files, ic_words, ooc_files, ooc_words, uploaded_at
             ) VALUES (1, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
             ON CONFLICT(id) DO UPDATE SET
                schema_version = excluded.schema_version,
                observed_at = excluded.observed_at,
                counting_rule_version = excluded.counting_rule_version,
                wiki_pages = excluded.wiki_pages,
                wiki_words = excluded.wiki_words,
                ic_files = excluded.ic_files,
                ic_words = excluded.ic_words,
                ooc_files = excluded.ooc_files,
                ooc_words = excluded.ooc_words,
                uploaded_at = excluded.uploaded_at');
        $statement->execute([
            $snapshot['schema_version'],
            $snapshot['observed_at'],
            $snapshot['counting_rule_version'],
            $snapshot['wiki']['pages'],
            $snapshot['wiki']['words'],
            $snapshot['ic']['files'],
            $snapshot['ic']['words'],
            $snapshot['ooc']['files'],
            $snapshot['ooc']['words'],
            $uploadedAt,
        ]);

        return $this->format($snapshot, $uploadedAt);
    }

    public function latest(): array
    {
        $statement = $this->database->query(
            'SELECT schema_version, observed_at, counting_rule_version,
                    wiki_pages, wiki_words, ic_files, ic_words, ooc_files, ooc_words, uploaded_at
             FROM word_count_snapshots WHERE id = 1');
        $row = $statement->fetch();
        if (!is_array($row)) {
            throw new BrokerHttpException(
                503,
                'word_counts_unavailable',
                'No validated campaign word-count snapshot is available.');
        }

        return $this->format([
            'schema_version' => (int)$row['schema_version'],
            'observed_at' => (string)$row['observed_at'],
            'counting_rule_version' => (string)$row['counting_rule_version'],
            'wiki' => ['pages' => (int)$row['wiki_pages'], 'words' => (int)$row['wiki_words']],
            'ic' => ['files' => (int)$row['ic_files'], 'words' => (int)$row['ic_words']],
            'ooc' => ['files' => (int)$row['ooc_files'], 'words' => (int)$row['ooc_words']],
        ], (int)$row['uploaded_at']);
    }

    public function hasSnapshot(): bool
    {
        return (int)$this->database
            ->query('SELECT COUNT(*) FROM word_count_snapshots WHERE id = 1')
            ->fetchColumn() === 1;
    }

    private function validate(array $body): array
    {
        if (($body['schema_version'] ?? null) !== 1) {
            throw new BrokerHttpException(
                400,
                'invalid_word_counts',
                'The word-count snapshot schema version is invalid.');
        }

        $observedAt = $body['observed_at'] ?? null;
        if (!is_string($observedAt)
            || strlen($observedAt) > 40
            || preg_match(
                '/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,9})?(?:Z|[+-]\d{2}:\d{2})$/D',
                $observedAt) !== 1) {
            throw new BrokerHttpException(400, 'invalid_word_counts', 'The observation time is invalid.');
        }
        try {
            $observed = new DateTimeImmutable($observedAt);
        } catch (Throwable) {
            throw new BrokerHttpException(400, 'invalid_word_counts', 'The observation time is invalid.');
        }
        if ($observed->getTimestamp() > time() + 300) {
            throw new BrokerHttpException(400, 'invalid_word_counts', 'The observation time is invalid.');
        }

        $ruleVersion = $body['counting_rule_version'] ?? null;
        if (!is_string($ruleVersion)
            || trim($ruleVersion) !== $ruleVersion
            || strlen($ruleVersion) < 1
            || strlen($ruleVersion) > 100) {
            throw new BrokerHttpException(
                400,
                'invalid_word_counts',
                'The counting-rule version is invalid.');
        }

        foreach (['wiki' => 'pages', 'ic' => 'files', 'ooc' => 'files'] as $section => $unitKey) {
            $value = $body[$section] ?? null;
            if (!is_array($value)
                || !$this->isCount($value[$unitKey] ?? null, true)
                || !$this->isCount($value['words'] ?? null, false)) {
                throw new BrokerHttpException(
                    400,
                    'invalid_word_counts',
                    'The word-count snapshot contains an invalid count.');
            }
        }

        return [
            'schema_version' => 1,
            'observed_at' => $observedAt,
            'counting_rule_version' => $ruleVersion,
            'wiki' => ['pages' => $body['wiki']['pages'], 'words' => $body['wiki']['words']],
            'ic' => ['files' => $body['ic']['files'], 'words' => $body['ic']['words']],
            'ooc' => ['files' => $body['ooc']['files'], 'words' => $body['ooc']['words']],
        ];
    }

    private function isCount(mixed $value, bool $mustBePositive): bool
    {
        return is_int($value)
            && $value <= 1000000000
            && ($mustBePositive ? $value > 0 : $value >= 0);
    }

    private function format(array $snapshot, int $uploadedAt): array
    {
        return $snapshot + ['uploaded_at' => gmdate(DATE_ATOM, $uploadedAt)];
    }
}
