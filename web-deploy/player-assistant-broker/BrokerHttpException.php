<?php

declare(strict_types=1);

final class BrokerHttpException extends RuntimeException
{
    public function __construct(
        public readonly int $status,
        public readonly string $errorName,
        string $publicMessage,
        ?Throwable $previous = null)
    {
        parent::__construct($publicMessage, 0, $previous);
    }
}
