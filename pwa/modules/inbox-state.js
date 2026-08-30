export function mergeInboxSnapshot(previous, incoming, cursor) {
    if (cursor === null || previous === null) return incoming;
    if (incoming.unread_count < previous.unread_count) return incoming;
    const messages = [...new Map([...previous.messages, ...incoming.messages].map((message) => [message.id, message])).values()];
    return messages.length > incoming.unread_count ? incoming : { ...incoming, messages };
}

export function createMessageDraftStore(storage, accountId) {
    const key = `player-assistant:message-draft:${accountId}`;
    return {
        load() { return storage.getItem(key) || ''; },
        save(value) { storage.setItem(key, String(value)); },
        clear() { storage.removeItem(key); }
    };
}
