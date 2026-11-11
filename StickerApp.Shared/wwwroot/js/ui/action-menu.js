export function watch(trigger, menu) {
    const place = () => {
        const viewport = window.visualViewport;
        const left = viewport?.offsetLeft ?? 0, top = viewport?.offsetTop ?? 0;
        const width = viewport?.width ?? innerWidth, height = viewport?.height ?? innerHeight;
        const bounds = trigger.getBoundingClientRect();
        const style = getComputedStyle(menu);
        const preferredWidth = Number.parseFloat(style.getPropertyValue('--action-menu-width')) || 340;
        const alignEnd = style.getPropertyValue('--action-menu-align').trim() === 'end';
        const menuWidth = Math.max(1, Math.min(preferredWidth, width - 32));
        const below = top + height - bounds.bottom - 24, above = bounds.top - top - 24;
        const upwards = below < 180 && above > below;
        menu.style.position = 'fixed';
        menu.style.width = `${menuWidth}px`;
        menu.style.left = `${Math.max(left + 16, Math.min(alignEnd ? bounds.right - menuWidth : bounds.left, left + width - menuWidth - 16))}px`;
        menu.style.right = 'auto';
        menu.style.bottom = 'auto';
        menu.style.maxHeight = `${Math.max(1, Math.min(height - 32, upwards ? above : below))}px`;
        const menuHeight = menu.getBoundingClientRect().height;
        const desiredTop = upwards ? bounds.top - menuHeight - 8 : bounds.bottom + 8;
        menu.style.top = `${Math.max(top + 16, Math.min(desiredTop, top + height - menuHeight - 16))}px`;
    };
    place();
    window.addEventListener('resize', place); window.addEventListener('scroll', place, true);
    window.visualViewport?.addEventListener('resize', place); window.visualViewport?.addEventListener('scroll', place);
    return { dispose() {
        window.removeEventListener('resize', place); window.removeEventListener('scroll', place, true);
        window.visualViewport?.removeEventListener('resize', place); window.visualViewport?.removeEventListener('scroll', place);
    } };
}
