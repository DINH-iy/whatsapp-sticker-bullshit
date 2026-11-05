// Runs before the stylesheet so saved appearance is applied before the first paint.
(() => {
    const appearanceKey = 'stickers.theme';
    const colorKey = 'stickers.color';
    const systemTheme = window.matchMedia('(prefers-color-scheme: dark)');
    const appearances = ['system', 'light', 'dark'];
    const colors = ['green', 'blue', 'purple', 'rose', 'amber'];

    function readPreference(key, allowedValues, fallback) {
        try {
            const saved = localStorage.getItem(key);
            return allowedValues.includes(saved) ? saved : fallback;
        } catch {
            return fallback;
        }
    }

    let appearance = readPreference(appearanceKey, appearances, 'system');
    let color = readPreference(colorKey, colors, 'green');

    function applyPreferences() {
        const resolvedAppearance = systemTheme.matches ? 'dark' : 'light';
        document.documentElement.dataset.theme = appearance === 'system' ? resolvedAppearance : appearance;
        document.documentElement.dataset.color = color;
    }

    function savePreference(key, value, allowedValues) {
        if (!allowedValues.includes(value)) return false;

        try {
            localStorage.setItem(key, value);
            return true;
        } catch {
            return false;
        }
    }

    window.stickerTheme = {
        get: () => appearance,
        getColor: () => color,
        set(value) {
            if (!savePreference(appearanceKey, value, appearances)) return false;
            appearance = value;
            applyPreferences();
            return true;
        },
        setColor(value) {
            if (!savePreference(colorKey, value, colors)) return false;
            color = value;
            applyPreferences();
            return true;
        }
    };

    systemTheme.addEventListener('change', applyPreferences);
    window.addEventListener('storage', event => {
        if (event.key !== appearanceKey && event.key !== colorKey && event.key !== null) return;
        appearance = readPreference(appearanceKey, appearances, 'system');
        color = readPreference(colorKey, colors, 'green');
        applyPreferences();
    });

    applyPreferences();
})();
