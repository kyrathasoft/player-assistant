export function createCorrelationContext(candidate = '') {
    const value = String(candidate);
    const safe = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
    return safe.test(value) ? value.toLowerCase() : crypto.randomUUID();
}
export function correlationHeaders(context) { return { 'X-Correlation-ID': context }; }
