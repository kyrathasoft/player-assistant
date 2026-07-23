(() => {
    'use strict';

    const textArea = document.getElementById('source-text');
    const wordCount = document.getElementById('source-word-count');
    if (!(textArea instanceof HTMLTextAreaElement) || wordCount === null) {
        return;
    }

    const maximumWords = Number.parseInt(textArea.dataset.maxWords || '5000', 10);
    const wordPattern = /[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*/gu;

    const findWords = (value) => {
        wordPattern.lastIndex = 0;
        const matches = [];
        let match;
        while ((match = wordPattern.exec(value)) !== null) {
            matches.push(match);
        }

        return matches;
    };

    const enforceWordLimit = () => {
        let matches = findWords(textArea.value);
        if (matches.length > maximumWords) {
            const overflowStart = matches[maximumWords].index;
            const selectionStart = textArea.selectionStart;
            textArea.value = textArea.value.slice(0, overflowStart).trimEnd();
            textArea.setSelectionRange(
                Math.min(selectionStart, textArea.value.length),
                Math.min(selectionStart, textArea.value.length)
            );
            matches = findWords(textArea.value);
        }

        wordCount.textContent = `${matches.length.toLocaleString()} / ${maximumWords.toLocaleString()} words`;
    };

    textArea.addEventListener('input', enforceWordLimit);
    enforceWordLimit();

    const translatorShell = document.querySelector('[data-translator-language]');
    const language = translatorShell instanceof HTMLElement
        ? translatorShell.dataset.translatorLanguage || 'Orcish'
        : 'Orcish';
    const directionToggle = document.querySelector('[data-direction-toggle]');
    const pageHeading = document.getElementById('page-heading');
    const intro = document.getElementById('translator-intro');
    const sourceLabel = document.getElementById('source-text-label');
    const translateButton = document.getElementById('translate-button');
    const resultSection = document.getElementById('translation-result');
    const downloadTranslation = document.getElementById('download-translation');
    const downloadUntranslated = document.getElementById('download-untranslated');

    const updateDirectionLabels = (hideExistingResult = false) => {
        if (!(directionToggle instanceof HTMLInputElement)) {
            return;
        }

        const reverse = directionToggle.checked;
        document.title = reverse ? `${language} to English Translator` : `English to ${language} Translator`;
        if (pageHeading !== null) {
            pageHeading.textContent = reverse ? `${language} to English` : `English to ${language}`;
        }
        if (intro !== null) {
            intro.textContent = reverse
                ? `Enter an ${language} word, phrase, or sentence. Unknown words remain unchanged.`
                : 'Enter an English word, phrase, or sentence. Unknown words remain unchanged.';
        }
        if (sourceLabel !== null) {
            sourceLabel.textContent = reverse ? `${language} text` : 'English text';
        }
        if (translateButton instanceof HTMLButtonElement) {
            translateButton.textContent = reverse ? 'Translate to English' : `Translate to ${language}`;
        }

        if (hideExistingResult) {
            if (resultSection instanceof HTMLElement) {
                resultSection.hidden = true;
            }
            [downloadTranslation, downloadUntranslated].forEach((link) => {
                if (link instanceof HTMLAnchorElement && link.parentElement !== null) {
                    link.parentElement.hidden = true;
                }
            });
        }
    };

    if (directionToggle instanceof HTMLInputElement) {
        directionToggle.addEventListener('change', () => updateDirectionLabels(true));
        updateDirectionLabels();
    }

    const downloadUrls = [];
    const attachTextDownload = (link, contents) => {
        const textFile = new Blob([contents], { type: 'text/plain;charset=utf-8' });
        const textUrl = URL.createObjectURL(textFile);
        link.href = textUrl;
        downloadUrls.push(textUrl);
    };

    const translatedTextArea = document.getElementById('translated-text');
    if (translatedTextArea instanceof HTMLTextAreaElement
        && downloadTranslation instanceof HTMLAnchorElement
        && translatedTextArea.value !== '') {
        attachTextDownload(downloadTranslation, translatedTextArea.value);
    }

    if (downloadUntranslated instanceof HTMLAnchorElement) {
        try {
            const untranslatedWords = JSON.parse(downloadUntranslated.dataset.words || '[]');
            if (Array.isArray(untranslatedWords) && untranslatedWords.length > 0) {
                const plainWords = untranslatedWords
                    .map((word) => String(word).trim())
                    .filter((word) => word !== '' && !word.includes('\r') && !word.includes('\n'));
                if (plainWords.length > 0) {
                    attachTextDownload(downloadUntranslated, `${plainWords.join('\r\n')}\r\n`);
                }
            }
        } catch (error) {
            downloadUntranslated.removeAttribute('href');
        }
    }

    if (downloadUrls.length > 0) {
        window.addEventListener('pagehide', () => {
            downloadUrls.forEach((url) => URL.revokeObjectURL(url));
        }, { once: true });
    }
})();
