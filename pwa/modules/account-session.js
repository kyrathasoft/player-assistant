export function createAccountSessionController({ restore, onChange = () => {}, channelName = 'player-assistant-authentication' }) {
    let generation = 0;
    let currentAccount = null;
    const channel = typeof globalThis.BroadcastChannel === 'function' ? new BroadcastChannel(channelName) : null;
    const broadcast = (account) => {
        const message = JSON.stringify({ type: 'authentication-transition', accountId: account?.id || null });
        channel?.postMessage(message);
        try { globalThis.localStorage?.setItem(`${channelName}-transition`, message); } catch {}
    };
    const applyRemote = (message) => {
        try {
            const parsed = typeof message === 'string' ? JSON.parse(message) : message;
            if (parsed?.type !== 'authentication-transition') return;
            if ((parsed.accountId || null) === (currentAccount?.id || null)) return;
            generation += 1;
            currentAccount = null;
            onChange(null);
        } catch {}
    };
    channel?.addEventListener('message', (event) => applyRemote(event.data));
    globalThis.addEventListener?.('storage', (event) => {
        if (event.key === `${channelName}-transition`) applyRemote(event.newValue);
    });
    return {
        account: () => currentAccount,
        setAccount(account) {
            const changed = (account?.id || null) !== (currentAccount?.id || null);
            currentAccount = account;
            if (changed) broadcast(account);
            onChange(account);
        },
        beginTransition() {
            generation += 1;
            currentAccount = null;
            broadcast(null);
        },
        async restoreSession() {
            const startedAt = generation;
            const account = await restore();
            if (startedAt !== generation) return false;
            currentAccount = account;
            onChange(account);
            return true;
        },
        shutdown() { generation += 1; currentAccount = null; channel?.close(); }
    };
}
