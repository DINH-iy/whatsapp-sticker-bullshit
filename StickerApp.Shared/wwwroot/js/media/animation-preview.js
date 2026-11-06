// Shared playback controls for standalone animations and compositions. Pointer
// scrubbing stays local to the WebView rather than waiting for a server roundtrip.
export function previewControls(root, { seek, speed, error }) {
    const owns = control => control.closest('.image-customizer') === (root.matches('.image-customizer') ? root : null);
    const input = event => {
        const control = event.target;
        if (!owns(control)) return;
        if (control.matches('[data-animation-seek]')) {
            const value = Number(control.value);
            if (Number.isFinite(value)) Promise.resolve(seek(value)).catch(error);
        }
        if (control.matches('[data-animation-speed]')) speed(Number(control.value));
    };
    root.addEventListener('input', input);
    return {
        update(seconds) {
            for (const field of root.querySelectorAll('[data-animation-seek]')) {
                if (!owns(field)) continue;
                field.value = String(seconds);
                field.setAttribute('aria-valuetext', `${seconds.toFixed(2)} seconds`);
                const marker = field.closest('.trim-timeline')?.querySelector('.timeline-playhead-line');
                if (marker) marker.style.left = `${Math.max(0, Math.min(100, seconds / Number(field.max) * 100))}%`;
            }
            for (const readout of root.querySelectorAll('[data-animation-time]'))
                if (owns(readout)) readout.textContent = `${seconds.toFixed(2)} s`;
        },
        dispose() { root.removeEventListener('input', input); }
    };
}
