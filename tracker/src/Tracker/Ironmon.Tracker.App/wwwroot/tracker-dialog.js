(() => {
    const dialogs = new Map();
    let sequence = 0;
    window.ironmonDialog = {
        attach(root) {
            const dialog = root.querySelector('[role="dialog"]');
            if (!dialog) return 0;
            const previous = document.activeElement;
            const isolated = [];
            for (let node = root; node && node !== document.body; node = node.parentElement) {
                for (const sibling of node.parentElement.children) {
                    if (sibling !== node && !sibling.inert && !['SCRIPT', 'STYLE', 'LINK'].includes(sibling.tagName)) {
                        sibling.inert = true;
                        isolated.push(sibling);
                    }
                }
            }
            dialog.tabIndex = -1;
            const controls = () => [...dialog.querySelectorAll('button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), summary, [tabindex="0"]')].filter(e => e.getClientRects().length);
            const keydown = event => {
                if (event.key !== 'Tab') return;
                const items = controls();
                const first = items[0] || dialog;
                const last = items.at(-1) || dialog;
                if (event.shiftKey && (document.activeElement === first || document.activeElement === dialog)) {
                    event.preventDefault(); last.focus();
                } else if (!event.shiftKey && (document.activeElement === last || !items.length)) {
                    event.preventDefault(); first.focus();
                }
            };
            root.addEventListener('keydown', keydown);
            const observer = new MutationObserver(() => {
                if (dialog.isConnected && !root.closest('[inert]') && document.activeElement === document.body) {
                    (controls()[0] || dialog).focus({ preventScroll: true });
                }
            });
            observer.observe(dialog, { childList: true, subtree: true });
            (controls()[0] || dialog).focus({ preventScroll: true });
            const handle = ++sequence;
            dialogs.set(handle, { root, keydown, previous, isolated, observer });
            return handle;
        },
        detach(handle) {
            const state = dialogs.get(handle);
            if (!state) return;
            state.root.removeEventListener('keydown', state.keydown);
            state.observer.disconnect();
            state.isolated.forEach(e => e.inert = false);
            if (state.previous?.isConnected) state.previous.focus({ preventScroll: true });
            dialogs.delete(handle);
        }
    };
})();

window.ironmonSearch = {
    initialize(input) {
        input.addEventListener('keydown', event => {
            if (['ArrowDown', 'ArrowUp', 'Enter'].includes(event.key)) event.preventDefault();
        });
    },
    scrollActive(input) {
        const option = document.getElementById(input.getAttribute('aria-activedescendant'));
        option?.scrollIntoView({ block: 'nearest' });
    }
};
