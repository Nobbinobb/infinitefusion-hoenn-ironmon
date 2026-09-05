window.ironmonTrackerUi = {
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
