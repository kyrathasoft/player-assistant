export function createMessagesActivityController({ load }) {
    let inFlight = null;
    return {
        refresh() {
            if (inFlight === null) {
                inFlight = Promise.resolve().then(load).finally(() => { inFlight = null; });
            }
            return inFlight;
        },
        shutdown() { inFlight = null; }
    };
}
