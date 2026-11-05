// Native Android Back and the app's Back button share overlay-first navigation.
window.stickerNavigation = {
    back(native = false) {
        const dialogs = [...document.querySelectorAll('dialog[open]')];
        const dialog = dialogs[dialogs.length - 1];
        if (dialog) {
            dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
            return true;
        }
        const cancel = document.querySelector('.modal-backdrop button');
        if (cancel) { cancel.click(); return true; }
        const dismiss = document.querySelector('.toolbar-menu-dismiss, .navigation-dismiss');
        if (dismiss) { dismiss.click(); return true; }
        const atHome = location.pathname === '/' || location.pathname === '/index.html';
        if (native && atHome) return false;
        if (history.length > 1) history.back();
        else if (!atHome) location.assign('/');
        else return false;
        return true;
    }
};
