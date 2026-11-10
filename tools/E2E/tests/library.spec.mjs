import { test, expect, openLibrary, importImage, waitReady, assertNoOverflow, editImage } from '../fixtures.mjs';
import { png, packArchive, movingGif } from '../media.mjs';

test('folders create, bulk move, filter, rename, persist and unfile without deleting artwork', async ({ page, app }) => {
    await openLibrary(page, app);
    const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
    const original = (await app.library()).Stickers[0];
    await page.getByRole('button', { name: 'Folder options', exact: true }).click();
    await page.getByLabel('Folder name', { exact: true }).fill('Templates');
    await page.getByLabel('Folder name', { exact: true }).press('Tab');
    await page.getByRole('button', { name: 'New folder', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Folder', exact: true })).not.toHaveValue('all');
    const folder = (await app.library()).Folders[0];
    await expect(page.locator('.image-card')).toHaveCount(0);
    await page.getByRole('combobox', { name: 'Folder', exact: true }).selectOption('all');
    await page.getByRole('button', { name: 'Select images', exact: true }).click();
    await page.getByRole('checkbox', { name: `Select ${name}`, exact: true }).check();
    await page.getByRole('button', { name: 'More selection actions', exact: true }).click();
    await page.getByRole('combobox', { name: 'Move to folder', exact: true }).selectOption(folder.Id);
    await page.getByRole('button', { name: 'Move to folder', exact: true }).click();
    await expect.poll(async () => (await app.library()).Stickers[0].FolderId).toBe(folder.Id);
    await page.getByRole('button', { name: 'Close More selection actions menu', exact: true }).click({ position: { x: 8, y: 8 } });
    await page.getByRole('button', { name: 'Done selecting', exact: true }).click();
    await page.getByRole('combobox', { name: 'Folder', exact: true }).selectOption('none');
    await expect(page.locator('.image-card')).toHaveCount(0);
    await page.getByRole('combobox', { name: 'Folder', exact: true }).selectOption(folder.Id);
    await expect(page.locator('.image-card')).toHaveCount(1);
    await page.getByRole('button', { name: 'Folder options', exact: true }).click();
    await page.getByLabel('Folder name', { exact: true }).fill('Meme templates');
    await page.getByLabel('Folder name', { exact: true }).press('Tab');
    await page.getByRole('button', { name: 'Rename folder', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Folder', exact: true }).locator('option:checked')).toHaveText('Meme templates');
    await page.reload();
    await page.getByRole('combobox', { name: 'Folder', exact: true }).selectOption(folder.Id);
    await expect(page.locator('.image-card')).toHaveCount(1);
    await page.getByRole('button', { name: 'Folder options', exact: true }).click();
    await page.getByRole('button', { name: 'Remove folder', exact: true }).click();
    await expect.poll(async () => (await app.library()).Folders).toEqual([]);
    const saved = (await app.library()).Stickers[0];
    expect(saved.Id).toBe(original.Id); expect(saved.FolderId).toBeNull();
    expect((await app.bytes(saved.FileName)).length).toBeGreaterThan(0);
    await page.getByRole('combobox', { name: 'Folder', exact: true }).selectOption('none');
    await expect(page.locator('.image-card')).toHaveCount(1);
});

test('library and pack lists preserve their independent preferences and keep actions reachable', async ({ page, app }) => {
    await openLibrary(page, app);
    const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
    await page.getByRole('switch', { name: 'List view', exact: true }).click();
    await expect(page.locator('.image-grid')).toHaveClass(/library-list-view/);
    await assertNoOverflow(page, page.locator('.browse-toolbar'));
    await assertNoOverflow(page, page.locator('.image-card'));
    await page.screenshot({ path: 'test-results/library-list-' + (page.viewportSize().width < 650 ? 'mobile' : 'desktop') + '.png', fullPage: true });
    await page.reload();
    await expect(page.getByRole('switch', { name: 'List view', exact: true })).toHaveAttribute('aria-checked', 'true');
    await expect(page.locator('.image-grid')).toHaveClass(/library-list-view/);
    await page.goto(app.url + '/');
    await expect(page.getByRole('heading', { name: 'My Packs', exact: true })).toBeVisible();
    await expect(page.getByRole('switch', { name: 'List view', exact: true })).toHaveAttribute('aria-checked', 'false');
    await page.getByRole('button', { name: /New pack Start a sticker collection/ }).click();
    const create = page.getByRole('dialog', { name: 'New pack', exact: true });
    await create.getByLabel('Pack name', { exact: true }).fill('List fixture pack');
    await create.getByRole('button', { name: 'Create pack', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'List fixture pack', exact: true })).toBeVisible();
    const pack = (await app.library()).Packs[0];
    await expect(page.getByRole('switch', { name: 'List view', exact: true })).toHaveAttribute('aria-checked', 'true');
    await expect(page.locator('.image-grid')).toHaveClass(/library-list-view/);
    await page.goto(app.url + '/');
    await page.getByRole('switch', { name: 'List view', exact: true }).click();
    await expect(page.locator('.pack-grid')).toHaveClass(/library-list-view/);
    await assertNoOverflow(page, page.locator('.pack-card'));
    await page.screenshot({ path: 'test-results/pack-list-' + (page.viewportSize().width < 650 ? 'mobile' : 'desktop') + '.png', fullPage: true });
    await page.reload();
    await expect(page.locator('.pack-grid')).toHaveClass(/library-list-view/);
    await page.getByRole('switch', { name: 'List view', exact: true }).click();
    await expect(page.locator('.pack-grid')).not.toHaveClass(/library-list-view/);
    await page.goto(`${app.url}/packs/${pack.Id}`);
    await expect(page.locator('.image-grid')).toHaveClass(/library-list-view/);
    await page.getByRole('switch', { name: 'List view', exact: true }).click();
    await expect(page.locator('.image-grid')).not.toHaveClass(/library-list-view/);
});

for (const extension of ['wasstickers', 'wastickers']) {
    test(`import a generic-binary .${extension} pack through the actual file picker`, async ({ page, app }) => {
        await openLibrary(page, app);
        const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
        const image = (await app.library()).Stickers[0];
        const buffer = packArchive(await app.bytes(image.FileName));
        // Exercise both entry points that previously restricted selectable files.
        if (extension === 'wasstickers') {
            await page.getByRole('button', { name: 'Add stickers', exact: true }).click();
        } else await page.goto(app.url + '/');
        const picker = page.getByLabel('Import pack file', { exact: true });
        await expect(picker).toHaveAttribute('accept', '.wastickers,.wasstickers');
        await picker.setInputFiles({ name: `Fixture.${extension}`, mimeType: 'application/octet-stream', buffer });
        await expect(page.getByRole('heading', { name: 'Imported fixture', exact: true })).toBeVisible();
        await expect(page.locator('.image-card')).toHaveCount(1);
        const pack = (await app.library()).Packs[0];
        expect(pack.Name).toBe('Imported fixture'); expect(pack.StickerIds).toHaveLength(1);
        await page.reload();
        await expect(page.getByRole('heading', { name: 'Imported fixture', exact: true })).toBeVisible();
        await expect(page.locator('.image-card')).toHaveCount(1);
    });
}

test('invalid pack reports an error, leaves the library intact and allows a subsequent valid import', async ({ page, app }) => {
    await openLibrary(page, app);
    const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
    const before = await app.library();
    await page.goto(app.url + '/');
    await page.getByLabel('Import pack file', { exact: true }).setInputFiles({ name: 'Broken.wastickers', mimeType: 'application/octet-stream', buffer: Buffer.from('not a ZIP') });
    await expect(page.getByRole('alert')).toBeVisible();
    expect(await app.library()).toEqual(before);
    const buffer = packArchive(await app.bytes(before.Stickers[0].FileName), 'Recovered import');
    await page.getByLabel('Import pack file', { exact: true }).setInputFiles({ name: 'Valid.wastickers', mimeType: 'application/octet-stream', buffer });
    await expect(page.getByRole('heading', { name: 'Recovered import', exact: true })).toBeVisible();
    expect((await app.library()).Packs).toHaveLength(1);
});

for (const extension of ['png', 'gif', 'zip']) {
 test('pack picker rejects .' + extension + ' without changing the library', async ({ page, app }) => {
    await openLibrary(page, app);
    await page.goto(app.url + '/');
    await page.getByLabel('Import pack file', { exact: true }).setInputFiles({ name: 'wrong.' + extension, mimeType: 'application/octet-stream', buffer: png() });
    await expect(page.getByRole('alert')).toContainText('Choose a .wastickers pack file.');
    expect((await app.library()).Stickers).toHaveLength(0);
    expect((await app.library()).Packs).toHaveLength(0);
 });
}

for (const blend of [false, true]) {
test('imported identical-frame sticker is still in the editor, blend=' + blend, async ({ page, app }) => {
    await openLibrary(page, app);
    const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
    const original = (await app.library()).Stickers[0];
    const wrapped = await page.evaluate(async ({ base64, blend }) => {
        const { animation } = await import('/_content/StickerApp.Shared/js/media/conversion/sticker-converter.js');
        const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
        const result = animation([{ bytes, duration: 500 }, { bytes, duration: 500 }], 512, 512);
        if (blend) {
            const view = new DataView(result.buffer); let seen = 0;
            for (let offset = 12; offset + 8 <= result.length;) {
                const size = view.getUint32(offset + 4, true);
                if (String.fromCharCode(...result.slice(offset, offset + 4)) === 'ANMF' && ++seen === 2) result[offset + 23] = 0;
                offset += 8 + size + (size & 1);
            }
        }
        return Array.from(result);
    }, { base64: (await app.bytes(original.FileName)).toString('base64'), blend });
    await page.goto(app.url + '/');
    await page.getByLabel('Import pack file', { exact: true }).setInputFiles({ name: 'Static.wastickers', mimeType: 'application/octet-stream', buffer: packArchive(Buffer.from(wrapped), 'Still wrapper') });
    await expect(page.getByRole('heading', { name: 'Still wrapper', exact: true })).toBeVisible();
    const saved = (await app.library()).Stickers.find(image => image.Id !== original.Id);
    expect(saved.Animated).toBe(false); expect(saved.DurationMs).toBe(0);
    await expect(page.getByRole('button', { name: 'New folder', exact: true })).toHaveCount(0);
    await page.locator('.image-preview').click();
    await expect(page.getByRole('heading', { name: 'Edit image', exact: true })).toBeVisible();
    await expect(page.locator('.animation-timeline')).toHaveCount(0);
});

}

test('imported moving sticker keeps animation controls', async ({ page, app }) => {
    await openLibrary(page, app);
    await importImage(page, app, { name: 'Moving.gif', mimeType: 'image/gif', buffer: movingGif() });
    await waitReady(page, app, 'Moving');
    const original = (await app.library()).Stickers[0];
    await page.goto(app.url + '/');
    await page.getByLabel('Import pack file', { exact: true }).setInputFiles({ name: 'Moving.wastickers', mimeType: 'application/octet-stream', buffer: packArchive(await app.bytes(original.FileName), 'Moving pack') });
    await expect(page.getByRole('heading', { name: 'Moving pack', exact: true })).toBeVisible();
    const saved = (await app.library()).Stickers.find(image => image.Id !== original.Id);
    expect(saved.Animated).toBe(true); expect(saved.DurationMs).toBe(160);
    await page.locator('.image-preview').click();
    await expect(page.getByRole('heading', { name: 'Edit animated image', exact: true })).toBeVisible();
});

test('processing batch keeps completed entries until the whole batch finishes', async ({ page, app }) => {
    await openLibrary(page, app);
    await page.evaluate(async () => { const jobs = await import('/_content/StickerApp.Shared/js/ui/foreground.js'); window.testBatch = jobs.startJobBatch(['First sticker', 'Second sticker']); });
    const menu = page.locator('.processing-menu'); await expect(menu).toBeVisible();
    await menu.locator('summary').click();
    await expect(menu).toContainText('First sticker'); await expect(menu).toContainText('Second sticker');
    await page.evaluate(() => window.testBatch.job(0)());
    await expect(menu).toBeVisible(); await expect(menu).toContainText('First sticker'); await expect(menu).toContainText('Second sticker');
    await page.evaluate(() => window.testBatch.finish()); await expect(menu).toHaveCount(0);
});

test('adding text focuses the caption and RGBA controls stay centered', async ({ page, app }) => {
    await openLibrary(page, app);
    const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
    const editor = await editImage(page, name);
    await editor.getByRole('button', { name: 'Text', exact: true }).click();
    await editor.getByRole('button', { name: 'Add text', exact: true }).click();
    await page.getByRole('button', { name: 'Continue to layers', exact: true }).click();
    const layers = page.getByRole('dialog', { name: 'Layer editor', exact: true });
    const caption = layers.getByRole('textbox', { name: 'Text content', exact: true });
    await expect(caption).toBeFocused(); await expect(caption).toBeInViewport();
    await layers.getByRole('button', { name: 'Color & gradient', exact: true }).click();
    await layers.getByRole('button', { name: 'Text color', exact: true }).click();
    const picker = page.getByRole('dialog', { name: 'Text color', exact: true });
    await expect(picker.getByLabel('Highlight alpha percent', { exact: true })).toBeVisible();
    await assertNoOverflow(page, picker.locator('.theme-rgba-fields'));
    await page.screenshot({ path: 'test-results/color-picker-' + (page.viewportSize().width < 650 ? 'mobile' : 'desktop') + '.png' });
});

test('NSFW pack keeps its cover, name and actions on one line', async ({ page, app }) => {
    await openLibrary(page, app);
    const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
    const image = (await app.library()).Stickers[0];
    await page.goto(app.url + '/');
    await page.getByLabel('Import pack file', { exact: true }).setInputFiles({ name: 'Test.wastickers', mimeType: 'application/octet-stream', buffer: packArchive(await app.bytes(image.FileName), 'NSFW pack') });
    await page.getByRole('button', { name: 'Pack options', exact: true }).click();
    await page.getByRole('button', { name: /Mark pack as NSFW/ }).click();
    const header = page.locator('.library-page-header');
    await expect(header.locator('.spoiler-eye')).toBeVisible();
    const title = await header.locator('.page-title').boundingBox(), actions = await header.locator('.header-actions').boundingBox();
    expect(actions.y).toBeLessThan(title.y + title.height); expect(actions.x).toBeGreaterThan(title.x);
    await assertNoOverflow(page, header);
    await page.screenshot({ path: 'test-results/nsfw-header-' + (page.viewportSize().width < 650 ? 'mobile' : 'desktop') + '.png' });
});
