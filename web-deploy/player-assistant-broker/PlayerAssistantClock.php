<?php

declare(strict_types=1);

final class PlayerAssistantClock
{
    public static function nowUnix(): int
    {
        return time();
    }

    public static function utcAtom(int $unix): string
    {
        return gmdate(DATE_ATOM, $unix);
    }

    public static function parseUtc(string $value): ?int
    {
        $date = DateTimeImmutable::createFromFormat(DATE_ATOM, $value, new DateTimeZone('UTC'));
        $errors = DateTimeImmutable::getLastErrors();
        if ($date === false || ($errors !== false && ($errors['warning_count'] > 0 || $errors['error_count'] > 0))) {
            return null;
        }
        return $date->getTimestamp();
    }

    public static function centralTimeZone(): DateTimeZone
    {
        return new DateTimeZone('America/Chicago');
    }
}
