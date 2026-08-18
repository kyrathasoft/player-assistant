'use strict';

const MAX_TRANSLATOR_WORDS = 5000;
const TRANSLATOR_LANGUAGE_STORAGE_KEY = 'player-assistant.translator-language';
const TRANSLATOR_LANGUAGES = new Set(['orcish', 'elvish', 'ghukliak']);
const textEncoder = new TextEncoder();

export const initializeTranslator = ({ byId }) => {
    let translatorRequestId = 0;
    let translatorDebounce = 0;
    const worker = typeof Worker !== 'undefined'
        ? new Worker('translator-worker.js?v=90')
        : null;

    const input = byId('translator-input');
    const output = byId('translator-output');
    const languageSelect = byId('translator-language');
    const reverseToggle = byId('translator-reverse');
    const exportButton = byId('export-translation');
    const translationLoading = byId('translation-loading');
    const translationLoadingLabel = byId('translation-loading-label');
    const removePackButton = byId('translator-remove-pack');
    const retryPackButton = byId('translator-retry-pack');

    const normalizeLanguage = (value) => TRANSLATOR_LANGUAGES.has(value) ? value : 'orcish';
    const getSelectedLanguage = () => normalizeLanguage(languageSelect?.value);
    const readPreferredLanguage = () => {
        try {
            return normalizeLanguage(window.localStorage.getItem(TRANSLATOR_LANGUAGE_STORAGE_KEY));
        } catch {
            return 'orcish';
        }
    };
    const savePreferredLanguage = (language) => {
        try {
            window.localStorage.setItem(TRANSLATOR_LANGUAGE_STORAGE_KEY, normalizeLanguage(language));
        } catch {
            // Private browsing or storage policy may make preferences unavailable.
        }
    };
    const preferredLanguage = readPreferredLanguage();
    if (languageSelect && TRANSLATOR_LANGUAGES.has(preferredLanguage)) {
        languageSelect.value = preferredLanguage;
    }

    const countWords = (text) => text.match(/[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*/gu)?.length ?? 0;

    const updateTranslationCounts = () => {
        const inputCount = byId('translator-input-count');
        const outputCount = byId('translator-output-count');
        const sourceWords = countWords(input?.value || '');
        const translatedWords = countWords(output?.value || '');
        if (inputCount) inputCount.textContent = `${sourceWords.toLocaleString()} ${sourceWords === 1 ? 'word' : 'words'}`;
        if (outputCount) outputCount.textContent = `${translatedWords.toLocaleString()} ${translatedWords === 1 ? 'word' : 'words'}`;
    };

    const updateTranslatorLabels = () => {
        const language = languageSelect?.value === 'elvish'
            ? 'Elvish'
            : languageSelect?.value === 'ghukliak' ? 'Goblin (Ghukliak)' : 'Orcish';
        const reverse = reverseToggle?.checked === true;
        const reverseLabel = byId('reverse-label');
        const inputLabel = byId('translator-input-label');
        const outputLabel = byId('translator-output-label');
        if (reverseLabel) reverseLabel.textContent = `${language} to English`;
        if (inputLabel) inputLabel.textContent = reverse ? `${language} text` : 'English text';
        if (outputLabel) outputLabel.textContent = reverse ? 'English translation' : `${language} translation`;
        if (input instanceof HTMLTextAreaElement) {
            input.spellcheck = !reverse;
            input.placeholder = reverse ? `Enter ${language} text…` : 'Begin typing or paste text here…';
        }
        updateExportState();
    };

    const updateExportState = () => {
        if (!(exportButton instanceof HTMLButtonElement) || !(output instanceof HTMLTextAreaElement)) {
            return;
        }
        const allowed = reverseToggle?.checked !== true && output.value.trim().length > 0;
        exportButton.hidden = !allowed;
        exportButton.disabled = !allowed;
    };

    const setTranslationLoading = (loading, message = 'Preparing translation…') => {
        if (translationLoading) {
            translationLoading.hidden = !loading;
        }
        if (translationLoadingLabel) {
            translationLoadingLabel.textContent = message;
        }
    };

    const requestTranslation = (event) => {
        if (!(input instanceof HTMLTextAreaElement) || !(output instanceof HTMLTextAreaElement)) {
            return;
        }

        window.clearTimeout(translatorDebounce);
        const id = ++translatorRequestId;
        const source = input.value;
        updateTranslationCounts();
        if (source.trim().length === 0) {
            output.value = '';
            setTranslationLoading(false);
            updateExportState();
            updateTranslationCounts();
            return;
        }

        const sourceWordCount = countWords(source);
        if (sourceWordCount > MAX_TRANSLATOR_WORDS) {
            output.value = `Please limit translation input to ${MAX_TRANSLATOR_WORDS.toLocaleString()} words.`;
            setTranslationLoading(false);
            updateExportState();
            updateTranslationCounts();
            return;
        }

        const delay = event?.inputType === 'insertFromPaste' || source.length > 1200 ? 0 : 25;
        translatorDebounce = window.setTimeout(() => {
            setTranslationLoading(true);
            if (worker) {
                worker.postMessage({
                    type: 'translate',
                    id,
                    language: getSelectedLanguage(),
                    reverse: reverseToggle?.checked === true,
                    text: source
                });
            } else {
                output.value = 'This browser does not support background translation workers.';
                setTranslationLoading(false);
                updateExportState();
            }
        }, delay);
    };

    worker?.addEventListener('message', (event) => {
        const message = event.data || {};
        if (message.type === 'status') {
            const status = byId('lexicon-status');
            if (status) {
                status.lastElementChild.textContent = message.message;
                status.dataset.state = message.state || (message.loading ? 'loading' : 'ready');
                if (retryPackButton) {
                    const retryable = ['unavailable', 'retrying', 'stale', 'removed'].includes(status.dataset.state);
                    retryPackButton.hidden = !retryable;
                    retryPackButton.disabled = status.dataset.state === 'retrying';
                }
            }
            if (message.error && translationLoadingLabel) translationLoadingLabel.textContent = message.message;
            return;
        }

        if (message.type !== 'translation' || message.id !== translatorRequestId) {
            return;
        }

        if (output instanceof HTMLTextAreaElement) {
            output.value = message.error ? `Translation unavailable: ${message.error}` : message.translation;
        }
        setTranslationLoading(false);
        updateTranslationCounts();
        updateExportState();
    });

    const resetTranslator = () => {
        window.clearTimeout(translatorDebounce);
        translatorRequestId++;
        if (input instanceof HTMLTextAreaElement) input.value = '';
        if (output instanceof HTMLTextAreaElement) output.value = '';
        setTranslationLoading(false);
        updateTranslatorLabels();
        updateTranslationCounts();
        input?.focus();
    };

    input?.addEventListener('input', requestTranslation);
    languageSelect?.addEventListener('change', () => {
        savePreferredLanguage(getSelectedLanguage());
        if (reverseToggle instanceof HTMLInputElement) reverseToggle.checked = false;
        resetTranslator();
        worker?.postMessage({ type: 'preload', language: getSelectedLanguage() });
    });
    reverseToggle?.addEventListener('change', () => {
        resetTranslator();
    });

    exportButton?.addEventListener('click', () => {
        if (!(input instanceof HTMLTextAreaElement) || !(output instanceof HTMLTextAreaElement)) {
            return;
        }
        const languageToken = languageSelect?.value === 'elvish'
            ? 'elvish'
            : languageSelect?.value === 'ghukliak' ? 'ghukliak' : 'orcish';
        const sourceBytes = textEncoder.encode(input.value).length;
        const outputBytes = textEncoder.encode(output.value).length;
        const filename = `english-${sourceBytes}-bytes-to-${languageToken}-${outputBytes}-bytes.txt`;
        const url = URL.createObjectURL(new Blob([output.value], { type: 'text/plain;charset=utf-8' }));
        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        link.click();
        window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    });

    retryPackButton?.addEventListener('click', () => {
        worker?.postMessage({ type: 'preload', language: getSelectedLanguage() });
    });
    removePackButton?.addEventListener('click', () => {
        worker?.postMessage({ type: 'clear-pack', language: getSelectedLanguage() });
        resetTranslator();
    });

    updateTranslatorLabels();
    updateTranslationCounts();
    const preloadPreferredLanguage = () => {
        worker?.postMessage({ type: 'preload', language: preferredLanguage });
    };
    if (typeof window.requestAnimationFrame === 'function') {
        window.requestAnimationFrame(preloadPreferredLanguage);
    } else {
        window.setTimeout(preloadPreferredLanguage, 0);
    }
};
