import { test, expect, openLibrary, importImage, waitReady } from '../fixtures.mjs';
import { noisyGif, movingGif } from '../media.mjs';

async function watchProgress(page) {
    await page.evaluate(() => {
        window.e2eProgress = [];
        window.e2eProgressObserver = new MutationObserver(() => {
            const meter = document.querySelector('.sticker-processing-overlay progress');
            if (!meter) return;
            const detail = meter.parentElement.textContent;
            const percent = Number(meter.getAttribute('value'));
            const key = percent + ':' + detail;
            if (window.e2eProgress.at(-1)?.key !== key) window.e2eProgress.push({ key, percent, detail });
        });
        window.e2eProgressObserver.observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true });
    });
}
for (const fallback of [false, true]) {
    test(`oversized GIF compresses without trimming and publishes real progress (${fallback ? 'main-thread fallback' : 'worker'})`, async ({ page, app }, testInfo) => {
        if (fallback) await page.addInitScript(() => { Object.defineProperty(window, 'OffscreenCanvas', { value: undefined, configurable: true }); });
        await openLibrary(page, app); await watchProgress(page);
        const name = await importImage(page, app, { name: 'Noisy.gif', mimeType: 'image/gif', buffer: noisyGif() });
        await waitReady(page, app, name);
        const image = (await app.library()).Stickers[0];
        expect(image.Animated).toBe(true); expect(image.DurationMs).toBe(960);
        expect(image.Bytes).toBeLessThanOrEqual(500 * 1024);
        expect(image.MinimumFrameMs).toBe(80);
        expect((await app.bytes(image.OriginalFileName)).subarray(0, 6).toString()).toMatch(/^GIF8[79]a$/);
        const samples = await page.evaluate(() => { window.e2eProgressObserver.disconnect(); return window.e2eProgress; });
        await testInfo.attach('gif-progress', { body: Buffer.from(JSON.stringify({ image, samples }, null, 2)), contentType: 'application/json' });
        expect(samples.some(sample => sample.percent > 0 && sample.percent < 100), 'intermediate progress reaches the rendered library card').toBe(true);
        expect(samples.every(sample => sample.percent >= 0 && sample.percent <= 100)).toBe(true);
        expect(samples.some(sample => /frames.*Attempt [5-8] \/ 8/.test(sample.detail)), 'detail-reduction retries reach the rendered UI').toBe(true);
        await page.reload();
        await expect(page.locator('.image-card')).toHaveCount(1);
        await expect(page.locator('.sticker-processing-overlay')).toHaveCount(0);
        expect((await app.library()).Stickers[0].DurationMs).toBe(960);
    });
}

test('stopping GIF preparation keeps its source and can resume to completion', async ({ page, app }) => {
    await openLibrary(page, app);
    await importImage(page, app, { name: 'Stopped.gif', mimeType: 'image/gif', buffer: noisyGif() });
    const stop = page.getByRole('button', { name: 'Stop processing', exact: true });
    await expect(stop).toBeVisible(); await stop.click();
    await expect(page.getByRole('button', { name: 'Resume processing', exact: true })).toBeVisible();
    const stopped = (await app.library()).Stickers[0];
    expect(stopped.MimeType).toBe('image/gif'); expect((await app.bytes(stopped.FileName)).length).toBeGreaterThan(0);
    await page.getByRole('button', { name: 'Resume processing', exact: true }).click();
    await waitReady(page, app, 'Stopped');
    expect((await app.library()).Stickers[0].DurationMs).toBe(960);
});

test('animated meme copy retains the full animation and editable source on reopen', async ({ page, app }) => {
    await openLibrary(page, app);
    await importImage(page, app, { name: 'Animated-template.gif', mimeType: 'image/gif', buffer: movingGif() });
    await waitReady(page, app, 'Animated-template');
    const original = (await app.library()).Stickers[0];
    await page.locator('.image-card').first().getByRole('button', { name: 'Edit Animated-template', exact: true }).last().click();
    const editor = page.getByRole('dialog', { name: /Edit (GIF|animated image)/ });
    await editor.getByRole('button', { name: 'Layers', exact: true }).click();
    await editor.getByRole('combobox', { name: 'Meme text position', exact: true }).selectOption('both');
    await editor.getByRole('button', { name: 'Add white meme template', exact: true }).click();
    const layers = page.getByRole('dialog', { name: 'Layer editor', exact: true });
    await expect(layers.getByText('Layers (5) · top is front', { exact: true })).toBeVisible();
    await layers.getByRole('button', { name: 'Save a copy', exact: true }).click();
    await expect(layers).not.toBeVisible({ timeout: 120_000 });
    const saved = (await app.library()).Stickers.find(image => image.Id !== original.Id), document = await app.document(saved);
    expect(saved.Animated).toBe(true); expect(saved.DurationMs).toBe(160); expect(saved.Bytes).toBeLessThanOrEqual(500 * 1024);
    expect(document.Layers[0].GifSource).toMatch(/^data:image\/webp;base64,/);
    await page.reload();
    await page.locator('.image-card').first().getByRole('button', { name: 'Edit Animated-template', exact: true }).last().click();
    await expect(page.getByRole('dialog', { name: 'Layer editor', exact: true }).getByText('Layers (5) · top is front', { exact: true })).toBeVisible();
});
