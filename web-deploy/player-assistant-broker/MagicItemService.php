<?php

declare(strict_types=1);

final class MagicItemService
{
    private const MAX_ITEMS = 100;
    private const MAX_SOURCE_BYTES = 2 * 1024 * 1024;
    private const LONGEVITY_VALUES = ['one-shot', 'limited-use', 'permanent'];

    public function __construct(private readonly string $sourcePath)
    {
        if ($this->sourcePath === '' || !is_file($this->sourcePath)) {
            throw new RuntimeException('The private magic-item source is unavailable.');
        }
    }

    public function forAccount(array $account): array
    {
        $accountId = strtolower(trim((string)($account['id'] ?? '')));
        if (preg_match('/^[a-f0-9]{32}$/', $accountId) !== 1) {
            throw new BrokerHttpException(500, 'invalid_account_identity', 'The authenticated account identity is invalid.');
        }
        $isDungeonMaster = ($account['role'] ?? null) === 'dm';

        $payload = $this->loadSource();
        $items = [];
        foreach ($payload['items'] as $item) {
            $viewers = $this->viewers((string)$item['viewable-by']);
            if ($isDungeonMaster || in_array('all', $viewers, true) || in_array($accountId, $viewers, true)) {
                $item['viewable-by'] = 'all';
                $items[] = $item;
            }
        }

        return [
            'schema_version' => 1,
            'source' => 'broker',
            'data_source' => 'broker',
            'items' => $items,
        ];
    }

    private function loadSource(): array
    {
        $size = filesize($this->sourcePath);
        if ($size === false || $size <= 0 || $size > self::MAX_SOURCE_BYTES) {
            throw new RuntimeException('The private magic-item source size is invalid.');
        }
        $json = file_get_contents($this->sourcePath);
        if (!is_string($json) || $json === '') {
            throw new RuntimeException('The private magic-item source could not be read.');
        }
        try {
            $payload = json_decode($json, true, 32, JSON_THROW_ON_ERROR);
        } catch (JsonException $exception) {
            throw new RuntimeException('The private magic-item source is not valid JSON.', 0, $exception);
        }
        if (!is_array($payload)
            || (int)($payload['schema_version'] ?? 0) !== 2
            || !is_string($payload['source'] ?? null)
            || !is_array($payload['items'] ?? null)
            || count($payload['items']) > self::MAX_ITEMS) {
            throw new RuntimeException('The private magic-item source schema is invalid.');
        }

        foreach ($payload['items'] as $item) {
            $this->validateItem($item);
        }
        return $payload;
    }

    private function validateItem(mixed $item): void
    {
        if (!is_array($item)
            || !$this->text($item['name'] ?? null, 200)
            || !$this->text($item['description'] ?? null, 10000)
            || !$this->text($item['date-acquired'] ?? null, 200)
            || !$this->text($item['meta-date-acquired'] ?? null, 100)
            || !in_array($item['longevity'] ?? null, self::LONGEVITY_VALUES, true)
            || !$this->text($item['provenance'] ?? null, 1000)
            || !$this->text($item['whereabouts'] ?? null, 500)
            || !$this->text($item['viewable-by'] ?? null, 500)) {
            throw new RuntimeException('The private magic-item source contains an invalid item.');
        }
        $viewers = $this->viewers((string)$item['viewable-by']);
        if ($viewers === [] || count($viewers) !== count(array_unique($viewers))) {
            throw new RuntimeException('The private magic-item source contains invalid viewers.');
        }
        foreach ($viewers as $viewer) {
            if ($viewer !== 'all' && preg_match('/^[a-f0-9]{32}$/', $viewer) !== 1) {
                throw new RuntimeException('The private magic-item source must use canonical account IDs.');
            }
        }
    }

    private function viewers(string $value): array
    {
        return array_values(array_filter(array_map(
            static fn(string $viewer): string => strtolower(trim($viewer)),
            explode(',', $value)), static fn(string $viewer): bool => $viewer !== ''));
    }

    private function text(mixed $value, int $maximum): bool
    {
        return is_string($value) && trim($value) !== '' && strlen($value) <= $maximum;
    }
}
