window.generatorDialog = {
    open(dialog) {
        const previous = document.activeElement;
        dialog.addEventListener('cancel', event => event.preventDefault());
        dialog.showModal();
        const observer = new MutationObserver(() => {
            if (dialog.isConnected) return;
            observer.disconnect();
            if (previous?.isConnected && !document.querySelector('dialog[open]'))
                previous.focus({ preventScroll: true });
        });
        observer.observe(document.body, { childList: true, subtree: true });
    },
    close(dialog) {
        dialog.close();
    }
};
