export function createPresenceController({ canPoll, refresh = () => {}, setInterval = globalThis.setInterval, clearInterval = globalThis.clearInterval, intervalMs = 30000 }) {
    let timer = 0;
    return {
        start() {
            if (timer !== 0 || !canPoll()) return false;
            void refresh();
            timer = setInterval(() => { if (canPoll()) void refresh(); }, intervalMs);
            return true;
        },
        stop() {
            if (timer !== 0) clearInterval(timer);
            timer = 0;
        },
        shutdown() { this.stop(); }
    };
}
