<?php

declare(strict_types=1);

final class BrokerSchemaContract
{
    public static function manifestPath(?string $path = null): string
    {
        return $path ?? __DIR__ . '/schema-contract.json';
    }

    public static function load(?string $path = null): array
    {
        $path = self::manifestPath($path);
        if (!is_file($path)) {
            throw new RuntimeException('The broker schema contract is missing.');
        }
        $manifest = json_decode((string)file_get_contents($path), true, 512, JSON_THROW_ON_ERROR);
        if (!is_array($manifest) || (int)($manifest['contract_version'] ?? 0) !== 1) {
            throw new RuntimeException('The broker schema contract version is unsupported.');
        }
        return $manifest;
    }

    /** Returns names, columns, index columns, and trigger definitions only; never rows. */
    public static function inspect(PDO $database): array
    {
        $objects = [];
        $query = $database->query("SELECT type, name, sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type, name");
        foreach ($query->fetchAll(PDO::FETCH_ASSOC) as $object) {
            $entry = ['type' => (string)$object['type'], 'name' => (string)$object['name']];
            if ($entry['type'] === 'table') {
                $entry['definition'] = preg_replace('/\s+/', ' ', trim((string)$object['sql']));
                $columns = $database->query("PRAGMA table_info('" . str_replace("'", "''", $entry['name']) . "')")->fetchAll(PDO::FETCH_ASSOC);
                $entry['columns'] = array_map(static fn(array $column): array => [
                    'name' => (string)$column['name'], 'type' => (string)$column['type'],
                    'notnull' => (int)$column['notnull'], 'pk' => (int)$column['pk'],
                ], $columns);
            } elseif ($entry['type'] === 'index') {
                $columns = $database->query("PRAGMA index_info('" . str_replace("'", "''", $entry['name']) . "')")->fetchAll(PDO::FETCH_ASSOC);
                $entry['columns'] = array_map(static fn(array $column): string => (string)$column['name'], $columns);
            } elseif ($entry['type'] === 'trigger') {
                $entry['definition'] = preg_replace('/\s+/', ' ', trim((string)$object['sql']));
            }
            $objects[] = $entry;
        }
        return ['migration_version' => (int)$database->query('PRAGMA user_version')->fetchColumn(), 'objects' => $objects];
    }

    public static function diagnostics(array $expected, array $actual): array
    {
        $exceptions = $expected['compatibility_exceptions'] ?? [];
        $expectedObjects = self::indexObjects($expected['objects'] ?? []);
        $actualObjects = self::indexObjects($actual['objects'] ?? []);
        $issues = [];
        if ((int)($expected['migration_version'] ?? -1) !== (int)($actual['migration_version'] ?? -2)) {
            $issues[] = 'migration version mismatch';
        }
        foreach (array_diff(array_keys($expectedObjects), array_keys($actualObjects)) as $key) {
            if (!self::exceptionContains($exceptions['missing_objects'] ?? [], $key)) $issues[] = "missing object $key";
        }
        foreach (array_diff(array_keys($actualObjects), array_keys($expectedObjects)) as $key) {
            if (!self::exceptionContains($exceptions['extra_objects'] ?? [], $key)) $issues[] = "extra object $key";
        }
        foreach (array_intersect(array_keys($expectedObjects), array_keys($actualObjects)) as $key) {
            $e=$expectedObjects[$key]; $a=$actualObjects[$key];
            if (($e['type'] ?? '') === 'table') {
                $ec=self::indexColumns($e['columns'] ?? []); $ac=self::indexColumns($a['columns'] ?? []);
                foreach (array_diff(array_keys($ec),array_keys($ac)) as $column) if (!self::exceptionContains($exceptions['missing_columns'] ?? [], "$key.$column")) $issues[]="missing column $key.$column";
                foreach (array_diff(array_keys($ac),array_keys($ec)) as $column) if (!self::exceptionContains($exceptions['extra_columns'] ?? [], "$key.$column")) $issues[]="extra column $key.$column";
                foreach (array_intersect(array_keys($ec),array_keys($ac)) as $column) if ($ec[$column] !== $ac[$column]) $issues[]="column definition mismatch $key.$column";
                if (($e['definition'] ?? null) !== ($a['definition'] ?? null)) $issues[]="definition mismatch $key";
            } elseif (($e['definition'] ?? null) !== ($a['definition'] ?? null)) $issues[]="definition mismatch $key";
        }
        return $issues;
    }

    public static function assert(PDO $database, ?string $path = null): void
    {
        $issues=self::diagnostics(self::load($path), self::inspect($database));
        if ($issues) throw new RuntimeException('Broker schema drift detected: ' . implode('; ', $issues));
    }
    private static function indexObjects(array $objects): array { $r=[]; foreach($objects as $o) $r[(string)$o['type'].':'.(string)$o['name']]=$o; return $r; }
    private static function indexColumns(array $columns): array { $r=[]; foreach($columns as $c) $r[(string)$c['name']]=['type'=>(string)($c['type']??''),'notnull'=>(int)($c['notnull']??0),'pk'=>(int)($c['pk']??0)]; return $r; }
    private static function exceptionContains(array $allowed, string $value): bool { return in_array($value, array_map('strval',$allowed), true); }
}
