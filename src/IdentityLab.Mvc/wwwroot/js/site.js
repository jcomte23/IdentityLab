// IdentityLab: small progressive enhancements shared by every page.
// Behaviors are opted into with data-* attributes, so views don't need inline scripts.
(() => {
    'use strict';

    // Temporarily replace a button's text to give feedback ("Copied!").
    const flashLabel = (button, text) => {
        button.dataset.originalLabel ??= button.textContent;
        button.textContent = text;
        clearTimeout(button._flashTimer);
        button._flashTimer = setTimeout(() => {
            button.textContent = button.dataset.originalLabel;
        }, 2000);
    };

    const textOf = (selector) => document.querySelector(selector)?.innerText.trim() ?? '';

    // <form data-confirm="Are you sure?">: ask before submitting.
    document.addEventListener('submit', (event) => {
        const message = event.target instanceof HTMLFormElement ? event.target.dataset.confirm : undefined;
        if (message && !window.confirm(message)) {
            event.preventDefault();
        }
    });

    document.addEventListener('click', async (event) => {
        // <button data-copy-target="#id">: copy the text of an element to the clipboard.
        const copyButton = event.target.closest('[data-copy-target]');
        if (copyButton) {
            try {
                await navigator.clipboard.writeText(textOf(copyButton.dataset.copyTarget));
                flashLabel(copyButton, 'Copied!');
            } catch {
                flashLabel(copyButton, 'Select and copy manually');
            }
            return;
        }

        // <button data-download-target="#id" data-download-name="file.txt">: save the text of an element as a file.
        const downloadButton = event.target.closest('[data-download-target]');
        if (downloadButton) {
            const blob = new Blob([textOf(downloadButton.dataset.downloadTarget) + '\n'], { type: 'text/plain' });
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = downloadButton.dataset.downloadName || 'download.txt';
            document.body.append(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(url);
        }
    });
})();
