export function createUpdateLifecycleController({ apply }) {
    let active = true;
    return {
        requestApply() {
            if (!active) return false;
            apply();
            return true;
        },
        shutdown() { active = false; }
    };
}
