const preferenceKey = 'stick-it.quick-statistics.v1';

export function load() {
    try {
        const value = JSON.parse(localStorage.getItem(preferenceKey));
        if (!value || !Array.isArray(value.selected) || value.selected.length > 32 ||
            value.selected.some(id => typeof id !== 'string')) return null;
        return { selected: value.selected };
    } catch { return null; }
}

export function save(selected) {
    try {
        localStorage.setItem(preferenceKey, JSON.stringify({ selected }));
        return true;
    } catch { return false; }
}

export function connect(root, dotnet) {
    let drag;
    let ghost;
    let frame;

    function targetAt(x, y) {
        const element = document.elementFromPoint(x, y);
        const zone = element?.closest('[data-stat-zone]');
        if (!zone || !root.contains(zone)) return null;
        const tile = element.closest('[data-stat-id]');
        let after = false;
        if (tile) {
            const rect = tile.getBoundingClientRect();
            const columns = getComputedStyle(zone).gridTemplateColumns.split(' ').length;
            after = columns > 1 ? x > rect.left + rect.width / 2 : y > rect.top + rect.height / 2;
        }
        return { zone, tile, after };
    }

    function highlight() {
        root.querySelectorAll('.stat-drop-target, .stat-zone-target').forEach(element => {
            element.classList.remove('stat-drop-target', 'stat-zone-target');
        });
        if (!drag?.active) return;
        const target = targetAt(drag.x, drag.y);
        (target?.tile ?? target?.zone)?.classList.add(target?.tile ? 'stat-drop-target' : 'stat-zone-target');
    }

    function tick() {
        if (!drag?.active) return;
        if (drag.y < 70) window.scrollBy(0, -12);
        else if (drag.y > innerHeight - 70) window.scrollBy(0, 12);
        highlight();
        frame = requestAnimationFrame(tick);
    }

    function reset() {
        cancelAnimationFrame(frame);
        ghost?.remove();
        ghost = null;
        root.classList.remove('stat-dragging');
        root.querySelectorAll('.stat-drop-target, .stat-zone-target, .stat-drag-source').forEach(element => {
            element.classList.remove('stat-drop-target', 'stat-zone-target', 'stat-drag-source');
        });
        if (drag) {
            try { drag.grip.releasePointerCapture(drag.pointerId); } catch { }
        }
        drag = null;
    }

    function down(event) {
        const grip = event.target.closest('[data-stat-grip]');
        if (!grip || grip.disabled || event.button !== 0 || root.getAttribute('aria-busy') === 'true') return;
        const tile = grip.closest('[data-stat-id]');
        if (!tile) return;
        drag = { grip, tile, id: tile.dataset.statId, pointerId: event.pointerId,
            startX: event.clientX, startY: event.clientY, x: event.clientX, y: event.clientY, active: false };
        grip.setPointerCapture(event.pointerId);
        grip.focus({ preventScroll: true });
        event.preventDefault();
    }

    function move(event) {
        if (!drag || event.pointerId !== drag.pointerId) return;
        drag.x = event.clientX;
        drag.y = event.clientY;
        if (!drag.active && Math.hypot(drag.x - drag.startX, drag.y - drag.startY) < 6) return;
        if (!drag.active) {
            drag.active = true;
            ghost = drag.tile.cloneNode(true);
            ghost.classList.add('stats-drag-ghost');
            ghost.setAttribute('aria-hidden', 'true');
            ghost.style.width = `${drag.tile.getBoundingClientRect().width}px`;
            ghost.querySelectorAll('button').forEach(button => button.disabled = true);
            document.body.append(ghost);
            drag.tile.classList.add('stat-drag-source');
            root.classList.add('stat-dragging');
            frame = requestAnimationFrame(tick);
        }
        ghost.style.left = `${Math.min(drag.x + 12, innerWidth - ghost.getBoundingClientRect().width - 8)}px`;
        ghost.style.top = `${drag.y + 12}px`;
        event.preventDefault();
        highlight();
    }

    function up(event) {
        if (!drag || event.pointerId !== drag.pointerId) return;
        const target = drag.active ? targetAt(event.clientX, event.clientY) : null;
        const id = drag.id;
        reset();
        if (target) {
            dotnet.invokeMethodAsync('DropQuickStat', id, target.tile?.dataset.statId ?? null,
                target.zone.dataset.statZone, target.after).catch(() => {});
        }
    }

    function key(event) { if (event.key === 'Escape') reset(); }
    root.addEventListener('pointerdown', down);
    root.addEventListener('pointermove', move, { passive: false });
    root.addEventListener('pointerup', up);
    root.addEventListener('pointercancel', reset);
    root.addEventListener('keydown', key);
    return { dispose() {
        reset();
        root.removeEventListener('pointerdown', down);
        root.removeEventListener('pointermove', move);
        root.removeEventListener('pointerup', up);
        root.removeEventListener('pointercancel', reset);
        root.removeEventListener('keydown', key);
    } };
}
