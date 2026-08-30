<?php

declare(strict_types=1);

final class BrokerAlertService
{
    private array $config;

    public function __construct(
        private readonly PDO $database,
        array $config = [])
    {
        $this->config = array_replace([
            'alert_email' => '',
            'from_email' => '',
            'server_error_threshold' => 3,
            'server_error_window_seconds' => 900,
            'refresh_failure_threshold' => 1,
            'health_failure_threshold' => 1,
            'alert_cooldown_seconds' => 3600,
        ], $config);
    }

    public function recordHealthFailure(string $errorCode, string $message): array
    {
        return $this->record('health_failure', $errorCode, $message, (int)$this->config['health_failure_threshold'], 900);
    }

    public function recordRefreshFailure(string $errorCode, string $message): array
    {
        return $this->record('word_count_refresh_failure', $errorCode, $message, (int)$this->config['refresh_failure_threshold'], 900);
    }

    public function recordServerError(string $errorCode, string $message): array
    {
        return $this->record(
            'server_error',
            $errorCode,
            $message,
            (int)$this->config['server_error_threshold'],
            (int)$this->config['server_error_window_seconds']);
    }

    private function record(
        string $alertType,
        string $errorCode,
        string $message,
        int $threshold,
        int $windowSeconds): array
    {
        $now = time();
        $errorCode = $this->sanitize($errorCode, 100);
        $message = $this->sanitize($message, 500);
        $threshold = max(1, $threshold);
        $windowSeconds = max(1, $windowSeconds);
        $emailSent = false;
        $alertTriggered = false;
        $count = 0;
        $this->database->exec('BEGIN IMMEDIATE');
        try {
            $statement = $this->database->prepare(
                'INSERT INTO broker_alert_events (alert_type, occurred_at, error_code, message)
                 VALUES (?, ?, ?, ?)');
            $statement->execute([$alertType, $now, $errorCode, $message]);
            $eventId = (int)$this->database->lastInsertId();
            $countStatement = $this->database->prepare(
                'SELECT COUNT(*) FROM broker_alert_events WHERE alert_type = ? AND occurred_at >= ?');
            $countStatement->execute([$alertType, $now - $windowSeconds]);
            $count = (int)$countStatement->fetchColumn();
            $cooldownStatement = $this->database->prepare(
                'SELECT COUNT(*) FROM broker_alert_events
                  WHERE alert_type = ? AND alert_sent_at IS NOT NULL AND alert_sent_at >= ?');
            $cooldownStatement->execute([$alertType, $now - max(1, (int)$this->config['alert_cooldown_seconds'])]);
            $cooldownActive = (int)$cooldownStatement->fetchColumn() > 0;
            if ($count >= $threshold && !$cooldownActive) {
                // Claim the cooldown inside the write transaction. BEGIN IMMEDIATE
                // serializes competing threshold crossings before any email work.
                $claim = $this->database->prepare(
                    'UPDATE broker_alert_events SET alert_sent_at = ?
                     WHERE id = ? AND alert_sent_at IS NULL');
                $claim->execute([$now, $eventId]);
                $alertTriggered = $claim->rowCount() === 1;
            }
            $this->database->commit();
        } catch (Throwable $error) {
            if ($this->database->inTransaction()) {
                $this->database->rollBack();
            }
            throw $error;
        }
        if ($alertTriggered) {
            $emailSent = $this->sendEmail($alertType, $errorCode, $message, $count);
        }
        return [
            'alert_type' => $alertType,
            'failure_count' => $count,
            'threshold' => $threshold,
            'alert_triggered' => $alertTriggered,
            'email_sent' => $emailSent,
        ];
    }

    private function sendEmail(string $alertType, string $errorCode, string $message, int $count): bool
    {
        $email = trim((string)$this->config['alert_email']);
        if ($email === '' || !function_exists('mail')) {
            return false;
        }
        $subject = '[Player Assistant broker] ' . $alertType;
        $body = sprintf(
            "Alert type: %s\nError code: %s\nRecent failures: %d\nMessage: %s\n",
            $alertType,
            $errorCode,
            $count,
            $message);
        $fromEmail = trim((string)$this->config['from_email']);
        if ($fromEmail === '') {
            return false;
        }
        $headers = "From: {$fromEmail}\r\n"
            . "Reply-To: {$email}\r\n"
            . "Content-Type: text/plain; charset=UTF-8\r\n";
        return @mail($email, $subject, $body, $headers, '-f' . $fromEmail);
    }

    private function sanitize(string $value, int $maximumLength): string
    {
        $value = trim(preg_replace('/[\x00-\x1F\x7F]+/', ' ', $value) ?? 'unknown');
        return substr($value === '' ? 'unknown' : $value, 0, $maximumLength);
    }
}
