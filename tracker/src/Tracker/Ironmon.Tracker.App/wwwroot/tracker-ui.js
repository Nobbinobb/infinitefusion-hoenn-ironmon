const ironmonSpriteFitObservers = new WeakMap();
const ironmonSpriteVisibleBounds = new WeakMap();
const ironmonSettingHelpCloseTimers = new WeakMap();

function ironmonSettingHelpFor(button) {
    return document.getElementById(button.getAttribute("popovertarget"));
}

function ironmonShowSettingHelp(button) {
    const tooltip = ironmonSettingHelpFor(button);
    if (!tooltip) return;
    clearTimeout(ironmonSettingHelpCloseTimers.get(tooltip));
    if (!tooltip.matches(":popover-open")) tooltip.showPopover();
    const anchor = button.getBoundingClientRect();
    const bounds = tooltip.getBoundingClientRect();
    const margin = 12;
    const gap = 6;
    const viewportWidth = document.documentElement.clientWidth;
    const viewportHeight = document.documentElement.clientHeight;
    const left = Math.max(margin, Math.min(anchor.left, viewportWidth - bounds.width - margin));
    const roomBelow = viewportHeight - anchor.bottom - margin;
    const roomAbove = anchor.top - margin;
    const top = roomBelow < bounds.height + gap && roomAbove > roomBelow
        ? anchor.top - bounds.height - gap
        : anchor.bottom + gap;
    tooltip.style.left = left + "px";
    tooltip.style.top = Math.max(margin, Math.min(top, viewportHeight - bounds.height - margin)) + "px";
}

function ironmonHideSettingHelpLater(tooltip) {
    if (!tooltip) return;
    clearTimeout(ironmonSettingHelpCloseTimers.get(tooltip));
    const timer = setTimeout(() => {
        const button = document.querySelector(`[popovertarget="${tooltip.id}"]`);
        if (tooltip.matches(":hover") || button?.matches(":hover") || document.activeElement === button) return;
        if (tooltip.matches(":popover-open")) tooltip.hidePopover();
    }, 120);
    ironmonSettingHelpCloseTimers.set(tooltip, timer);
}

document.addEventListener("pointerover", event => {
    const button = event.target.closest?.(".obsidian-help-button");
    if (button && !button.contains(event.relatedTarget)) ironmonShowSettingHelp(button);
    const tooltip = event.target.closest?.(".obsidian-help-tooltip");
    if (tooltip) clearTimeout(ironmonSettingHelpCloseTimers.get(tooltip));
});

document.addEventListener("pointerout", event => {
    const button = event.target.closest?.(".obsidian-help-button");
    if (button && !button.contains(event.relatedTarget)) ironmonHideSettingHelpLater(ironmonSettingHelpFor(button));
    const tooltip = event.target.closest?.(".obsidian-help-tooltip");
    if (tooltip && !tooltip.contains(event.relatedTarget)) ironmonHideSettingHelpLater(tooltip);
});

document.addEventListener("focusin", event => {
    const button = event.target.closest?.(".obsidian-help-button");
    if (button) ironmonShowSettingHelp(button);
});

document.addEventListener("focusout", event => {
    const button = event.target.closest?.(".obsidian-help-button");
    if (button) ironmonHideSettingHelpLater(ironmonSettingHelpFor(button));
});

function ironmonHideSettingHelp() {
    document.querySelectorAll(".obsidian-help-tooltip:popover-open").forEach(tooltip => tooltip.hidePopover());
}

document.addEventListener("scroll", event => {
    if (!event.target.closest?.(".obsidian-help-tooltip")) ironmonHideSettingHelp();
}, true);
window.addEventListener("resize", ironmonHideSettingHelp);

window.ironmonTrackerUi = {
    scrollMoveAccess(element) {
        const content = element?.closest(".research-page, .obsidian-lookup-content, .obsidian-archive-content");
        if (content) content.scrollTop = 0;
    },
    scrollLookup(element, key) {
        const content = element?.closest(".obsidian-lookup-content, .obsidian-archive-content");
        if (!content) return;
        if (!key) { content.scrollTop = 0; return; }
        const anchor = [...element.querySelectorAll("[data-lookup-anchor]")].find(node => node.dataset.lookupAnchor === key);
        if (anchor) content.scrollTop += anchor.getBoundingClientRect().top - content.getBoundingClientRect().top;
    },
    fitSprite(image) {
        if (!image.complete || !image.naturalWidth || image.src.startsWith("data:image/gif")) return;
        if (!ironmonSpriteFitObservers.has(image)) {
            const observer = new ResizeObserver(() => window.ironmonTrackerUi.fitSprite(image));
            ironmonSpriteFitObservers.set(image, observer);
            observer.observe(image.parentElement);
        }
        const box = image.parentElement;
        if (box.clientWidth <= 4 || box.clientHeight <= 4) return;
        const source = image.currentSrc || image.src;
        let bounds = ironmonSpriteVisibleBounds.get(image);
        if (!bounds || bounds.source !== source) {
            const canvas = document.createElement("canvas");
            canvas.width = image.naturalWidth;
            canvas.height = image.naturalHeight;
            const context = canvas.getContext("2d", { willReadFrequently: true });
            if (!context) return;
            context.drawImage(image, 0, 0);
            const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
            let left = canvas.width, top = canvas.height, right = -1, bottom = -1;
            for (let y = 0; y < canvas.height; y++) {
                for (let x = 0; x < canvas.width; x++) {
                    if (pixels[(y * canvas.width + x) * 4 + 3] > 0) {
                        left = Math.min(left, x);
                        top = Math.min(top, y);
                        right = Math.max(right, x);
                        bottom = Math.max(bottom, y);
                    }
                }
            }
            bounds = { source, left, top, width: right - left + 1, height: bottom - top + 1 };
            ironmonSpriteVisibleBounds.set(image, bounds);
        }
        const { left, top, width, height } = bounds;
        if (width <= 0 || height <= 0) return;
        const scale = Math.min((box.clientWidth - 4) / width, (box.clientHeight - 4) / height);
        if (scale <= 0) return;
        Object.assign(image.style, {
            position: "absolute", maxWidth: "none", transform: "none",
            width: image.naturalWidth * scale + "px", height: image.naturalHeight * scale + "px",
            left: ((box.clientWidth - width * scale) / 2 - left * scale) + "px",
            top: ((box.clientHeight - height * scale) / 2 - top * scale) + "px"
        });
    },
    shouldOpenPickerUp(element, preferredHeight) {
        if (!element) {
            return false;
        }

        const elementBounds = element.getBoundingClientRect();
        let visibleTop = 0;
        let visibleBottom = window.innerHeight;
        let ancestor = element.parentElement;

        while (ancestor && ancestor !== document.body) {
            const style = window.getComputedStyle(ancestor);
            if (/(auto|scroll|hidden|clip)/.test(style.overflowY)) {
                const ancestorBounds = ancestor.getBoundingClientRect();
                visibleTop = Math.max(visibleTop, ancestorBounds.top);
                visibleBottom = Math.min(visibleBottom, ancestorBounds.bottom);
                break;
            }
            ancestor = ancestor.parentElement;
        }

        const margin = 6;
        const roomAbove = Math.max(0, elementBounds.top - visibleTop - margin);
        const roomBelow = Math.max(0, visibleBottom - elementBounds.bottom - margin);
        const requiredRoom = Math.max(40, Number(preferredHeight) || 240);
        return roomBelow < requiredRoom && roomAbove > roomBelow;
    }
};
