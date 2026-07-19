(() => {
    'use strict';

    const textArea = document.getElementById('english');
    const wordCount = document.getElementById('english-word-count');
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

    const downloadUrls = [];
    const attachTextDownload = (link, contents) => {
        const textFile = new Blob([contents], { type: 'text/plain;charset=utf-8' });
        const textUrl = URL.createObjectURL(textFile);
        link.href = textUrl;
        downloadUrls.push(textUrl);
    };

    const orcishTextArea = document.getElementById('orcish');
    const downloadOrcish = document.getElementById('download-orcish');
    if (orcishTextArea instanceof HTMLTextAreaElement
        && downloadOrcish instanceof HTMLAnchorElement
        && orcishTextArea.value !== '') {
        attachTextDownload(downloadOrcish, orcishTextArea.value);
    }

    const downloadUntranslated = document.getElementById('download-untranslated');
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
