import { test, expect, openLibrary } from '../fixtures.mjs';

test('rainbow loaders cycle colors and respect reduced motion', async ({ page, app }) => {
    await page.addInitScript(() => localStorage.setItem('stickers.color', 'rainbow'));
    await openLibrary(page, app);
    await page.evaluate(async () => {
        const jobs = await import('/_content/StickerApp.Shared/js/ui/foreground.js');
        window.loaderJob = jobs.startJob('Rainbow loading');
    });
    const menu = page.locator('.processing-menu');
    await menu.locator('summary').click();
    const spinner = menu.locator('.feedback-spinner').first();
    await expect(spinner).toBeVisible();
    const style = () => spinner.evaluate(el => {
        const css = getComputedStyle(el);
        return { animation: css.animationName, filter: css.filter, colors: [css.borderTopColor, css.borderRightColor, css.borderBottomColor, css.borderLeftColor] };
    });
    const initial = await style();
    expect(initial.animation).toContain('rainbow-loader-color');
    expect(new Set(initial.colors).size).toBe(4);
    await expect.poll(async () => (await style()).filter).not.toBe(initial.filter);
    await page.evaluate(() => {
        const ring = document.createElement('span'); ring.className = 'media-loading-ring';
        document.querySelector('.processing-menu-content').append(ring);
    });
    expect(await page.locator('.processing-menu .media-loading-ring').evaluate(el => getComputedStyle(el, '::before').animationName)).toContain('rainbow-loader-color');
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await expect.poll(async () => (await style()).animation).toBe('none');
    expect(await page.locator('.processing-menu .media-loading-ring').evaluate(el => getComputedStyle(el, '::before').animationName)).toBe('none');
    await page.evaluate(() => window.loaderJob());
});
