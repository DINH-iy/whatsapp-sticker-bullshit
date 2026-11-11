import { test, expect, openLibrary, importImage, waitReady, editImage, renameEdited, canvasBounds, assertNoOverflow } from '../fixtures.mjs';
import { png, movingGif, hash } from '../media.mjs';

test('fit transparent artwork, undo/redo, save a copy, replace and reopen without changing the template', async ({ page, app }) => {
    await openLibrary(page, app);
    const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
    const original = (await app.library()).Stickers[0], originalHash = hash(await app.bytes(original.FileName));
    let editor = await editImage(page, name);
    expect(await editor.locator('canvas[aria-label]').evaluate(canvasBounds)).toMatchObject({ artworkWidth: 128, artworkHeight: 64 });
    await assertNoOverflow(page, editor.locator('.live-history-row'));
    await assertNoOverflow(page, editor.locator('.editor-header-actions'));
    await editor.getByRole('button', { name: 'Fit artwork to maximum', exact: true }).click();
    await expect.poll(() => editor.locator('canvas[aria-label]').evaluate(canvasBounds)).toMatchObject({ width: 512, height: 256, artworkWidth: 512, artworkHeight: 256 });
    await editor.getByRole('button', { name: 'Undo', exact: true }).click();
    await expect.poll(() => editor.locator('canvas[aria-label]').evaluate(canvasBounds)).toMatchObject({ artworkWidth: 128, artworkHeight: 64 });
    await editor.getByRole('button', { name: 'Redo', exact: true }).click();
    await expect.poll(() => editor.locator('canvas[aria-label]').evaluate(canvasBounds)).toMatchObject({ artworkWidth: 512, artworkHeight: 256 });
    await renameEdited(page, editor, 'Filled copy');
    await editor.getByRole('button', { name: 'Save a copy', exact: true }).click();
    await expect(editor).not.toBeVisible(); await waitReady(page, app, 'Filled copy');
    expect((await app.library()).Stickers).toHaveLength(2);
    expect(hash(await app.bytes(original.FileName))).toBe(originalHash);
    await page.reload(); editor = await editImage(page, 'Filled copy');
    expect(await editor.locator('canvas[aria-label]').evaluate(canvasBounds)).toMatchObject({ width: 512, height: 512, artworkWidth: 512, artworkHeight: 256 });
    await renameEdited(page, editor, 'Replaced copy');
    await editor.getByRole('button', { name: 'Replace current sticker', exact: true }).click();
    await expect(editor).not.toBeVisible();
    await expect.poll(async () => (await app.library()).Stickers.map(image => image.Name).sort()).toEqual(['Artwork', 'Replaced copy']);
    expect(hash(await app.bytes(original.FileName))).toBe(originalHash);
});

test('maximum fit uses every GIF frame and retains the complete moving animation after save', async ({ page, app }) => {
    await openLibrary(page, app);
    const name = await importImage(page, app, { name: 'Moving.gif', mimeType: 'image/gif', buffer: movingGif() }); await waitReady(page, app, name);
    const editor = await editImage(page, name);
    await editor.getByRole('button', { name: 'Fit artwork to maximum', exact: true }).click();
    await expect.poll(() => editor.locator('canvas[aria-label]').evaluate(canvasBounds)).toMatchObject({ width: 512, height: 146 });
    await editor.getByRole('button', { name: 'Replace current sticker', exact: true }).click();
    await expect(editor).not.toBeVisible({ timeout: 120_000 }); await waitReady(page, app, name);
    const image = (await app.library()).Stickers[0];
    expect(image.Animated).toBe(true); expect(image.DurationMs).toBe(160); expect(image.Bytes).toBeLessThanOrEqual(500 * 1024);
    const source = `data:image/webp;base64,${(await app.bytes(image.FileName)).toString('base64')}`;
    const output = await page.evaluate(async source => {
        const { openAnimation } = await import('/_content/StickerApp.Shared/js/media/animation/animation-reader.js');
        const reader = await openAnimation(source); let time = 0, left = 512, right = -1;
        try {
            for (const duration of reader.frameDurations) {
                const frame = reader.at(time), ctx = frame.getContext('2d'), data = ctx.getImageData(0, 0, 512, 512).data;
                for (let y = 0; y < 512; y++) for (let x = 0; x < 512; x++) if (data[(y * 512 + x) * 4 + 3] > 128) { left = Math.min(left, x); right = Math.max(right, x); }
                time += duration;
            }
            return { duration: reader.duration, frames: reader.frameCount, width: right - left + 1 };
        } finally { reader.dispose?.(); }
    }, source);
    expect(output).toEqual({ duration: 160, frames: 2, width: 512 });
});

for (const placement of ['top', 'bottom', 'both']) {
    test(`white meme captions ${placement}: undo/redo, edit, copy, replace and reopen`, async ({ page, app }) => {
        await openLibrary(page, app);
        const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
        const original = (await app.library()).Stickers[0], originalHash = hash(await app.bytes(original.FileName));
        const editor = await editImage(page, name);
        await editor.getByRole('button', { name: 'Layers', exact: true }).click();
        await editor.getByRole('button', { name: 'Add meme template', exact: true }).click();
        await page.getByRole('dialog', { name: 'Meme text position', exact: true }).getByRole('button', { name: placement === 'top' ? 'Above image' : placement === 'bottom' ? 'Below image' : 'Above and below', exact: true }).click();
        let layers = page.getByRole('dialog', { name: 'Layer editor', exact: true });
        const count = placement === 'both' ? 5 : 3;
        await expect(layers.getByText(`Layers (${count}) · top is front`, { exact: true })).toBeVisible();
        await layers.getByRole('button', { name: 'Undo', exact: true }).click();
        await expect(layers.getByText('Layers (1) · top is front', { exact: true })).toBeVisible();
        await layers.getByRole('button', { name: 'Redo', exact: true }).click();
        await expect(layers.getByText(`Layers (${count}) · top is front`, { exact: true })).toBeVisible();
        await layers.getByRole('textbox', { name: 'Text content', exact: true }).fill('My editable caption');
        await layers.getByRole('button', { name: 'Save a copy', exact: true }).click();
        await expect(layers).not.toBeVisible();
        await expect.poll(async () => (await app.library()).Stickers.length).toBe(2);
        let copy = (await app.library()).Stickers.find(image => image.Id !== original.Id);
        await waitReady(page, app, copy.Name);
        let document = await app.document(copy);
        expect(document.Layers).toHaveLength(count);
        const captions = document.Layers.filter(layer => layer.Text);
        expect(captions.map(layer => layer.Y).sort((a, b) => a - b)).toEqual(placement === 'both' ? [64, 448] : [placement === 'top' ? 64 : 448]);
        expect(captions.some(layer => layer.Text.Content === 'My editable caption')).toBe(true);
        expect(document.Layers.filter(layer => !layer.Text).slice(1).every(layer => layer.Width === 512 && layer.Height === 128)).toBe(true);
        expect(hash(await app.bytes(original.FileName))).toBe(originalHash);
        await page.reload();
        // Copies are added last and appear first in the default library order.
        await page.locator('.image-card').first().getByRole('button', { name: `Edit ${copy.Name}`, exact: true }).last().click();
        layers = page.getByRole('dialog', { name: 'Layer editor', exact: true });
        await expect(layers.getByRole('textbox', { name: 'Text content', exact: true })).toHaveValue('My editable caption');
        await layers.getByRole('textbox', { name: 'Text content', exact: true }).fill('Replaced caption');
        await layers.getByRole('button', { name: 'Replace current sticker', exact: true }).click();
        await expect(layers).not.toBeVisible();
        copy = (await app.library()).Stickers.find(image => image.Id === copy.Id);
        document = await app.document(copy);
        expect(document.Layers.some(layer => layer.Text?.Content === 'Replaced caption')).toBe(true);
        expect((await app.library()).Stickers).toHaveLength(2);
        expect(hash(await app.bytes(original.FileName))).toBe(originalHash);
    });
}

test('load an image meme template as editable layers and reopen it', async ({ page, app }) => {
    await openLibrary(page, app);
    await page.getByRole('button', { name: 'Add stickers', exact: true }).click();
    await page.getByRole('button', { name: /Layered image/ }).click();
    const layers = page.getByRole('dialog', { name: 'Layer editor', exact: true });
    await layers.locator('summary[aria-label="Add layer"]').click();
    await layers.getByLabel('Load meme template', { exact: true }).setInputFiles({ name: 'Custom-template.png', mimeType: 'image/png', buffer: png() });
    await expect(layers.getByText('Layers (1) · top is front', { exact: true })).toBeVisible();
    await layers.getByRole('button', { name: 'Save sticker', exact: true }).click();
    await expect(layers).not.toBeVisible();
    const saved = (await app.library()).Stickers[0], document = await app.document(saved);
    expect(document.Layers).toHaveLength(1); expect(document.Layers[0].Name).toBe('Custom-template');
    await page.reload();
    await page.locator('.image-card').first().getByRole('button', { name: `Edit ${saved.Name}`, exact: true }).last().click();
    await expect(page.getByRole('dialog', { name: 'Layer editor', exact: true }).getByText('Layers (1) · top is front', { exact: true })).toBeVisible();
});

test('transparent-only artwork rejects maximum fit and cancellation preserves its saved source', async ({ page, app }) => {
    await openLibrary(page, app);
    const name = await importImage(page, app, { name: 'Empty.png', buffer: png(512, 512, { x: 0, y: 0, width: 0, height: 0 }) });
    await waitReady(page, app, name);
    const before = (await app.library()).Stickers[0], fingerprint = hash(await app.bytes(before.FileName));
    const editor = await editImage(page, name);
    await editor.getByRole('button', { name: 'Fit artwork to maximum', exact: true }).click();
    await expect(editor.getByRole('alert')).toContainText(/transparent/i);
    await editor.getByRole('button', { name: 'Cancel', exact: true }).click();
    await expect(editor).not.toBeVisible();
    expect((await app.library()).Stickers[0].FileName).toBe(before.FileName);
    expect(hash(await app.bytes(before.FileName))).toBe(fingerprint);
});
