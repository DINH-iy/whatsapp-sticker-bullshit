import { test, expect, openLibrary, importImage, waitReady, assertNoOverflow } from '../fixtures.mjs';
import { png, packArchive } from '../media.mjs';

test('folders create, bulk move, filter, rename, persist and unfile without deleting artwork', async ({ page, app }) => {
    await openLibrary(page, app);
    const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
    const original = (await app.library()).Stickers[0];
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
    await page.getByLabel('Folder name', { exact: true }).fill('Meme templates');
    await page.getByLabel('Folder name', { exact: true }).press('Tab');
    await page.getByRole('button', { name: 'Rename folder', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Folder', exact: true }).locator('option:checked')).toHaveText('Meme templates');
    await page.reload();
    await page.getByRole('combobox', { name: 'Folder', exact: true }).selectOption(folder.Id);
    await expect(page.locator('.image-card')).toHaveCount(1);
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
    await page.getByRole('button', { name: 'List view', exact: true }).click();
    await expect(page.locator('.image-grid')).toHaveClass(/library-list-view/);
    await assertNoOverflow(page, page.locator('.browse-toolbar'));
    await assertNoOverflow(page, page.locator('.image-card'));
    await page.reload();
    await expect(page.getByRole('button', { name: 'List view', exact: true })).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('.image-grid')).toHaveClass(/library-list-view/);
    await page.goto(app.url + '/');
    await expect(page.getByRole('heading', { name: 'My Packs', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Tile view', exact: true })).toHaveAttribute('aria-pressed', 'true');
    await page.getByRole('button', { name: /New pack Start a sticker collection/ }).click();
    const create = page.getByRole('dialog', { name: 'New pack', exact: true });
    await create.getByLabel('Pack name', { exact: true }).fill('List fixture pack');
    await create.getByRole('button', { name: 'Create pack', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'List fixture pack', exact: true })).toBeVisible();
    const pack = (await app.library()).Packs[0];
    await page.getByRole('button', { name: 'List view', exact: true }).click();
    await expect(page.locator('.image-grid')).toHaveClass(/library-list-view/);
    await page.goto(app.url + '/');
    await page.getByRole('button', { name: 'List view', exact: true }).click();
    await expect(page.locator('.pack-grid')).toHaveClass(/library-list-view/);
    await assertNoOverflow(page, page.locator('.pack-card'));
    await page.reload();
    await expect(page.locator('.pack-grid')).toHaveClass(/library-list-view/);
    await page.getByRole('button', { name: 'Tile view', exact: true }).click();
    await expect(page.locator('.pack-grid')).not.toHaveClass(/library-list-view/);
    await page.goto(`${app.url}/packs/${pack.Id}`);
    await expect(page.locator('.image-grid')).toHaveClass(/library-list-view/);
    await page.getByRole('button', { name: 'Tile view', exact: true }).click();
    await expect(page.locator('.image-grid')).not.toHaveClass(/library-list-view/);
});

for (const extension of ['wassticker', 'wasticker', 'wastickers', 'zip']) {
    test(`import a generic-binary .${extension} pack through the actual file picker`, async ({ page, app }) => {
        await openLibrary(page, app);
        const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
        const image = (await app.library()).Stickers[0];
        const buffer = packArchive(await app.bytes(image.FileName));
        // Exercise both entry points that previously restricted selectable files.
        if (extension === 'wassticker' || extension === 'zip') {
            await page.getByRole('button', { name: 'Add stickers', exact: true }).click();
        } else await page.goto(app.url + '/');
        const picker = page.getByLabel('Import pack file', { exact: true });
        await expect(picker).toHaveAttribute('accept', '*/*');
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
    await page.getByLabel('Import pack file', { exact: true }).setInputFiles({ name: 'Broken.wassticker', mimeType: 'application/octet-stream', buffer: Buffer.from('not a ZIP') });
    await expect(page.getByRole('alert')).toBeVisible();
    expect(await app.library()).toEqual(before);
    const buffer = packArchive(await app.bytes(before.Stickers[0].FileName), 'Recovered import');
    await page.getByLabel('Import pack file', { exact: true }).setInputFiles({ name: 'Valid.wassticker', mimeType: 'application/octet-stream', buffer });
    await expect(page.getByRole('heading', { name: 'Recovered import', exact: true })).toBeVisible();
    expect((await app.library()).Packs).toHaveLength(1);
});
