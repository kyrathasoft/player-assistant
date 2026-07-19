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

    const orcishTextArea = document.getElementById('orcish');
    const downloadOrcish = document.getElementById('download-orcish');
    if (orcishTextArea instanceof HTMLTextAreaElement
        && downloadOrcish instanceof HTMLAnchorElement
        && orcishTextArea.value !== '') {
        const translationFile = new Blob([orcishTextArea.value], { type: 'text/plain;charset=utf-8' });
        const translationUrl = URL.createObjectURL(translationFile);
        downloadOrcish.href = translationUrl;
        window.addEventListener('pagehide', () => URL.revokeObjectURL(translationUrl), { once: true });
    }
})();
