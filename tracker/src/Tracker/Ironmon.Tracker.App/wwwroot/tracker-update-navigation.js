(() => {
    const shellSelector = ".tracker-shell";
    const events = ["pointerdown", "wheel", "keydown"];
    let stopRestore;

    function pathFor(element, root) {
        const path = [];
        while (element && element !== root && path.length < 24) {
            const parent = element.parentElement;
            if (!parent) return null;
            path.unshift(Array.prototype.indexOf.call(parent.children, element));
            element = parent;
        }
        return element === root ? path : null;
    }

    function resolve(root, path) {
        if (!Array.isArray(path) || path.length > 24) return null;
        let element = root;
        for (const index of path) {
            if (!Number.isInteger(index) || index < 0 || index > 10000) return null;
            element = element?.children[index];
        }
        return element;
    }

    function coordinate(value) {
        return Number.isFinite(value) && value >= 0 && value <= 1000000 ? value : 0;
    }

    window.ironmonUpdateNavigation = {
        capture() {
            const root = document.querySelector(shellSelector);
            const items = root ? [...root.querySelectorAll("*")]
                .filter(element => element.scrollTop > 0 || element.scrollLeft > 0)
                .slice(0, 8)
                .map(element => ({ path: pathFor(element, root), x: element.scrollLeft, y: element.scrollTop })) : [];
            return { y: window.scrollY, items };
        },
        restore(saved) {
            stopRestore?.();
            if (!saved || !Array.isArray(saved.items) || saved.items.length > 8) return;
            let frame;
            const apply = () => {
                frame = undefined;
                const root = document.querySelector(shellSelector);
                if (!root) return;
                for (const item of saved.items) {
                    if (!item || typeof item !== "object") continue;
                    const element = resolve(root, item.path);
                    if (element) element.scrollTo(coordinate(item.x), coordinate(item.y));
                }
                window.scrollTo(0, coordinate(saved.y));
            };
            const schedule = () => {
                if (frame === undefined) frame = requestAnimationFrame(apply);
            };
            const observer = new MutationObserver(schedule);
            const stop = () => {
                observer.disconnect();
                clearTimeout(timeout);
                if (frame !== undefined) cancelAnimationFrame(frame);
                events.forEach(event => document.removeEventListener(event, stop, true));
                stopRestore = undefined;
            };
            const timeout = setTimeout(stop, 120000);
            stopRestore = stop;
            events.forEach(event => document.addEventListener(event, stop, { capture: true, passive: true }));
            observer.observe(document.body, { childList: true, subtree: true });
            schedule();
        }
    };
})();
