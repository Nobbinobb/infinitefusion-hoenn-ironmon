(() => {
    const states = new WeakMap();

    function apply(state) {
        state.canvas.style.transform = `translate3d(${state.x}px, ${state.y}px, 0) scale(${state.scale})`;
    }

    function setScale(state, requestedScale, focalX, focalY) {
        const nextScale = Math.min(state.maximumScale, Math.max(state.minimumScale, requestedScale));
        if (Math.abs(nextScale - state.scale) < 0.001)
            return;

        const ratio = nextScale / state.scale;
        state.x = focalX - ((focalX - state.x) * ratio);
        state.y = focalY - ((focalY - state.y) * ratio);
        state.scale = nextScale;
        apply(state);
    }

    function center(state) {
        const width = state.viewport.clientWidth;
        const height = state.viewport.clientHeight;
        state.scale = Math.min(1, Math.max(0.7, (width - 32) / 524));
        state.x = (width / 2) - (state.currentCenterX * state.scale);
        state.y = (height / 2) - (state.currentCenterY * state.scale);
        apply(state);
    }

    function createState(viewport, canvas) {
        const state = {
            viewport,
            canvas,
            canvasWidth: 0,
            currentCenterX: 0,
            currentCenterY: 0,
            minimumScale: 0.45,
            maximumScale: 2,
            x: 0,
            y: 0,
            scale: 1,
            dragging: false,
            pointerId: null,
            lastX: 0,
            lastY: 0,
            frame: 0
        };
        state.pointerDown = event => {
            if (event.button !== 0 || event.target.closest("button"))
                return;

            state.dragging = true;
            state.pointerId = event.pointerId;
            state.lastX = event.clientX;
            state.lastY = event.clientY;
            viewport.classList.add("dragging");
            viewport.setPointerCapture(event.pointerId);
        };
        state.pointerMove = event => {
            if (!state.dragging || state.pointerId !== event.pointerId)
                return;

            state.x += event.clientX - state.lastX;
            state.y += event.clientY - state.lastY;
            state.lastX = event.clientX;
            state.lastY = event.clientY;
            if (state.frame !== 0)
                return;

            state.frame = requestAnimationFrame(() => {
                state.frame = 0;
                apply(state);
            });
        };
        state.pointerUp = event => {
            if (!state.dragging || state.pointerId !== event.pointerId)
                return;

            state.dragging = false;
            state.pointerId = null;
            viewport.classList.remove("dragging");
            if (viewport.hasPointerCapture(event.pointerId))
                viewport.releasePointerCapture(event.pointerId);
        };
        state.wheel = event => {
            event.preventDefault();
            const bounds = viewport.getBoundingClientRect();
            const focalX = event.clientX - bounds.left;
            const focalY = event.clientY - bounds.top;
            setScale(state, event.deltaY < 0 ? state.scale * 1.16 : state.scale / 1.16, focalX, focalY);
        };
        viewport.addEventListener("pointerdown", state.pointerDown);
        viewport.addEventListener("pointermove", state.pointerMove);
        viewport.addEventListener("pointerup", state.pointerUp);
        viewport.addEventListener("pointercancel", state.pointerUp);
        viewport.addEventListener("wheel", state.wheel, { passive: false });
        states.set(viewport, state);
        return state;
    }

    window.ironmonEvolutionGraph = {
        update(viewport, canvas, canvasWidth, currentCenterX, currentCenterY, minimumScale, maximumScale, reset) {
            const state = states.get(viewport) ?? createState(viewport, canvas);
            const previousWidth = state.canvasWidth;
            state.canvas = canvas;
            state.canvasWidth = canvasWidth;
            state.currentCenterX = currentCenterX;
            state.currentCenterY = currentCenterY;
            state.minimumScale = minimumScale;
            state.maximumScale = maximumScale;
            if (reset) {
                requestAnimationFrame(() => requestAnimationFrame(() => center(state)));
            } else if (previousWidth !== 0 && previousWidth !== canvasWidth) {
                state.x -= ((canvasWidth - previousWidth) / 2) * state.scale;
                apply(state);
            }
        },
        zoom(viewport, factor) {
            const state = states.get(viewport);
            if (!state)
                return;

            setScale(state, state.scale * factor, viewport.clientWidth / 2, viewport.clientHeight / 2);
        },
        reset(viewport) {
            const state = states.get(viewport);
            if (state)
                center(state);
        },
        dispose(viewport) {
            const state = states.get(viewport);
            if (!state)
                return;

            state.viewport.removeEventListener("pointerdown", state.pointerDown);
            state.viewport.removeEventListener("pointermove", state.pointerMove);
            state.viewport.removeEventListener("pointerup", state.pointerUp);
            state.viewport.removeEventListener("pointercancel", state.pointerUp);
            state.viewport.removeEventListener("wheel", state.wheel);
            if (state.frame !== 0)
                cancelAnimationFrame(state.frame);
            states.delete(viewport);
        }
    };
})();
