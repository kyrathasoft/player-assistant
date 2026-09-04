'use strict';

const CORRELATION_ID_PATTERN = /^[a-f0-9]{32}$/u;
const REDACTED = '[REDACTED]';
const SENSITIVE_KEYS = new Set([
    'authorization', 'cookie', 'cookies', 'csrf', 'csrf_token', 'password',
    'password_hash', 'secret', 'token', 'protected_body', 'protected_response_body'
]);

export const sanitizeCorrelationId = (value) =>
    typeof value === 'string' && CORRELATION_ID_PATTERN.test(value) ? value : null;

export const createCorrelationId = (value = null) =>
    sanitizeCorrelationId(value)
    || (globalThis.crypto?.randomUUID?.().replaceAll('-', '')
        || [...globalThis.crypto.getRandomValues(new Uint8Array(16))]
            .map((byte) => byte.toString(16).padStart(2, '0')).join(''));

export const redactCorrelationRecord = (value, key = null) => {
    const normalized = typeof key === 'string' ? key.toLowerCase().replaceAll('-', '_') : '';
    if (SENSITIVE_KEYS.has(normalized) || normalized.includes('protected_response')) return REDACTED;
    if (Array.isArray(value)) return value.map((item) => redactCorrelationRecord(item));
    if (value && typeof value === 'object') {
        return Object.fromEntries(Object.entries(value)
            .map(([name, item]) => [name, redactCorrelationRecord(item, name)]));
    }
    return typeof value === 'string' ? value.replace(/[\u0000-\u001f\u007f]/gu, ' ').slice(0, 1000) : value;
};
