import { mkdir, writeFile, readFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { test, expect, openLibrary, importImage, waitReady, editImage } from '../fixtures.mjs';
import { movingGif } from '../media.mjs';

async function seedLibrary(app, count) {
    const bytes = await readFile(new URL('../fixtures/artwork.webp', import.meta.url));
    const media = path.join(app.data, 'media'); await mkdir(media, { recursive: true });
    const stickers = [];
    for (let index = 0; index < count; index++) {
        const id = randomUUID(), file = id + '.webp'; await writeFile(path.join(media, file), bytes);
        stickers.push({ Id: id, Name: `Sticker ${String(index).padStart(3, '0')}`, FileName: file, MimeType: 'image/webp', Width: 512, Height: 512, Bytes: bytes.length, Animated: false });
    }
    await writeFile(path.join(app.data, 'library.json'), JSON.stringify({ SchemaVersion: 1, Stickers: stickers, Packs: [], Folders: [], Fonts: [] }));
}

test('large libraries virtualize cards and cache static thumbnails without loading originals offscreen', async ({ page, app }) => {
    await seedLibrary(app, 180); await openLibrary(page, app);
    await expect(page.locator('.image-card').first()).toBeVisible();
    await expect.poll(() => page.locator('img[data-thumbnail-state="ready"]').count()).toBeGreaterThan(0);
    expect(await page.locator('.image-card').count()).toBeLessThan(80);
    expect(await page.locator('img[data-thumbnail-state="ready"]').count()).toBeLessThan(60);
    expect(await page.locator('.image-card').filter({ hasText: 'Sticker 000' }).count()).toBe(0);
    const first = page.locator('.image-card').first().locator('img');
    await expect(first).toHaveAttribute('data-thumbnail-state', 'ready');
    expect(await first.evaluate(image => image.naturalWidth)).toBe(192);
    await expect(first).toHaveAttribute('data-thumbnail-cache', 'miss');
    await page.reload();
    const cached = page.locator('.image-card').first().locator('img');
    await expect(cached).toHaveAttribute('data-thumbnail-state', 'ready');
    await expect(cached).toHaveAttribute('data-thumbnail-cache', 'hit');
    // Scroll to an item that wasn't initially rendered, then filter it to prove
    // virtualization, filtering and full-resolution editing work together.
    await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
    await expect(page.locator('.image-card').filter({ hasText: 'Sticker 000' })).toBeVisible();
    await page.getByRole('switch', { name: 'List view', exact: true }).click();
    await page.evaluate(() => window.scrollTo(0, 0));
    await expect(page.locator('.image-card').first()).toBeVisible();
    expect(await page.locator('.image-card').count()).toBeLessThan(40);
    await page.getByRole('button', { name: 'Select images', exact: true }).click();
    await page.getByRole('button', { name: 'Select all', exact: true }).click();
    await expect(page.getByText('180 selected', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Done selecting', exact: true }).click();
    await page.getByRole('searchbox', { name: 'Search images', exact: true }).fill('Sticker 000');
    await expect(page.locator('.image-card')).toHaveCount(1);
    const editor = await editImage(page, 'Sticker 000');
    expect(await editor.locator('canvas[aria-label]').evaluate(canvas => [canvas.width, canvas.height])).toEqual([512, 512]);
});

test('animation previews redraw at frame changes, pause cleanly, and preserve layered exports', async ({ page, app }) => {
    await page.addInitScript(() => {
        window.e2eDraws = { image: 0, layer: 0 };
        const draw = CanvasRenderingContext2D.prototype.drawImage;
        CanvasRenderingContext2D.prototype.drawImage = function (...args) {
            const label = this.canvas.getAttribute?.('aria-label') || '';
            if (label.startsWith('Image editing canvas')) window.e2eDraws.image++;
            if (label.startsWith('Layer canvas')) window.e2eDraws.layer++;
            return draw.apply(this, args);
        };
    });
    await openLibrary(page, app);
    await importImage(page, app, { name: 'Slow.gif', mimeType: 'image/gif', buffer: movingGif(500) }); await waitReady(page, app, 'Slow');
    const editor = await editImage(page, 'Slow');
    await editor.getByRole('button', { name: 'Preview animation', exact: true }).click();
    const before = await page.evaluate(() => window.e2eDraws.image);
    // A defined measurement window, rather than waiting for UI synchronization.
    await page.waitForTimeout(1250);
    const draws = await page.evaluate(() => window.e2eDraws.image) - before;
    expect(draws).toBeGreaterThanOrEqual(2); expect(draws).toBeLessThanOrEqual(4);
    await editor.getByRole('button', { name: 'Stop preview', exact: true }).click();
    await expect(editor.getByRole('button', { name: 'Preview animation', exact: true })).toBeEnabled();
    const stopped = await page.evaluate(() => window.e2eDraws.image);
    await page.waitForTimeout(600); expect(await page.evaluate(() => window.e2eDraws.image)).toBe(stopped);
    await editor.getByRole('button', { name: 'Layers', exact: true }).click();
    await editor.getByRole('button', { name: 'Meme text position', exact: true }).click();
        await editor.getByRole('button', { name: 'Above and below', exact: true }).click();
    const layers = page.getByRole('dialog', { name: 'Layer editor', exact: true });
    await expect(layers.getByText('Layers (5) · top is front', { exact: true })).toBeVisible();
    const initial = await page.evaluate(() => window.e2eDraws.layer);
    await page.waitForTimeout(1250);
    const layerDraws = await page.evaluate(() => window.e2eDraws.layer) - initial;
    expect(layerDraws).toBeGreaterThanOrEqual(10); expect(layerDraws).toBeLessThanOrEqual(20);
    await layers.getByRole('button', { name: 'Pause animation', exact: true }).click();
    await expect(layers.getByRole('button', { name: 'Play animation', exact: true })).toBeEnabled();
    const paused = await page.evaluate(() => window.e2eDraws.layer);
    await page.waitForTimeout(600); expect(await page.evaluate(() => window.e2eDraws.layer)).toBe(paused);
    await layers.getByRole('textbox', { name: 'Text content', exact: true }).fill('Still editable');
    await layers.getByRole('button', { name: 'Save a copy', exact: true }).click();
    await expect(layers).not.toBeVisible();
    const copy = (await app.library()).Stickers.find(image => image.CompositionFileName);
    expect(copy.DurationMs).toBe(1000); expect((await app.document(copy)).Layers.some(layer => layer.Text?.Content === 'Still editable')).toBe(true);
});

test('identical successful GIF conversions are reused and changed sources are encoded separately', async ({ page, app }) => {
    await page.addInitScript(() => {
        window.e2eGifWorkers = 0;
        const NativeWorker = window.Worker;
        window.Worker = class extends NativeWorker {
            constructor(url, options) { super(url, options); if (String(url).includes('/gif-worker.js')) window.e2eGifWorkers++; }
        };
    });
    await openLibrary(page, app);
    const buffer = movingGif();
    await importImage(page, app, { name: 'First.gif', mimeType: 'image/gif', buffer }); await waitReady(page, app, 'First');
    const workers = await page.evaluate(() => window.e2eGifWorkers); expect(workers).toBe(1);
    await importImage(page, app, { name: 'Same-source.gif', mimeType: 'image/gif', buffer }); await waitReady(page, app, 'Same-source');
    expect(await page.evaluate(() => window.e2eGifWorkers)).toBe(workers);
    const images = (await app.library()).Stickers;
    expect(await app.bytes(images[0].FileName)).toEqual(await app.bytes(images[1].FileName));
    await importImage(page, app, { name: 'Changed-source.gif', mimeType: 'image/gif', buffer: movingGif(120) }); await waitReady(page, app, 'Changed-source');
    expect(await page.evaluate(() => window.e2eGifWorkers)).toBe(workers + 1);
    expect((await app.library()).Stickers.find(image => image.Name === 'Changed-source').DurationMs).toBe(240);
});

test('browsing and full-source editing work when persistent thumbnail storage is unavailable', async ({ page, app }) => {
    await page.addInitScript(() => { Object.defineProperty(window, 'indexedDB', { value: undefined, configurable: true }); });
    await seedLibrary(app, 1); await openLibrary(page, app);
    const thumbnail = page.locator('.image-card').first().locator('img');
    await expect(thumbnail).toHaveAttribute('data-thumbnail-state', 'ready');
    await expect(thumbnail).toHaveAttribute('data-thumbnail-cache', 'miss');
    await page.reload();
    await expect(thumbnail).toHaveAttribute('data-thumbnail-state', 'ready');
    await expect(thumbnail).toHaveAttribute('data-thumbnail-cache', 'miss');
    const editor = await editImage(page, 'Sticker 000');
    expect(await editor.locator('canvas[aria-label]').evaluate(canvas => [canvas.width, canvas.height])).toEqual([512, 512]);
});
