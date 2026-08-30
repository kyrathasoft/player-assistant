export function createAccountSessionController({ restore, onChange = () => {} }) {
    let generation = 0;
    let currentAccount = null;
    return {
        account: () => currentAccount,
        setAccount(account) {
            currentAccount = account;
            onChange(account);
        },
        beginTransition() { generation += 1; currentAccount = null; },
        async restoreSession() {
            const startedAt = generation;
            const account = await restore();
            if (startedAt !== generation) return false;
            currentAccount = account;
            onChange(account);
            return true;
        },
        shutdown() { generation += 1; currentAccount = null; }
    };
}
