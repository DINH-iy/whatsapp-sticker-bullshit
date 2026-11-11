import { test, expect, openLibrary, importImage, waitReady } from '../fixtures.mjs';
import { png } from '../media.mjs';
import { writeFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import path from 'node:path';

async function sort(page, label, value) {
    await page.getByRole('button', { name: label, exact: true }).click();
    await page.getByRole('combobox', { name: label, exact: true }).selectOption(value);
    await page.getByRole('button', { name: 'Done', exact: true }).click();
}
async function filter(page, label, fields) {
    await page.getByRole('button', { name: label, exact: true }).click();
    for (const [name, value] of fields) await page.getByRole('combobox', { name, exact: true }).selectOption(value);
    await page.getByRole('button', { name: 'Done', exact: true }).click();
}
async function saved(page, scope, key, value) {
    await expect.poll(() => page.evaluate(([scope, key]) => JSON.parse(localStorage.getItem('stickers.browse.' + scope) || '{}')[key], [scope, key])).toBe(value);
}
async function reopen(page, url) { await page.goto(url); }

test('sort and filters survive reopening independently for packs, library and pack stickers; reset persists', async ({ page, app }) => {
    await openLibrary(page, app);
    const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
    const library = await app.library(), packId = randomUUID(), folderId = randomUUID();
    library.Folders = [{ Id: folderId, Name: 'Remembered folder' }];
    library.Stickers[0].FolderId = folderId;
    library.Packs = [{ Id: packId, Name: 'Remembered pack', Publisher: 'Fixture', StickerIds: [library.Stickers[0].Id] }];
    await writeFile(path.join(app.data, 'library.json'), JSON.stringify(library));
    await page.reload();
    await sort(page, 'Sort images', 'oldest');
    await filter(page, 'Filter images', [['Filter image type', 'still'], ['Filter image format', 'webp']]);
    await page.getByRole('combobox', { name: 'Folder', exact: true }).selectOption(folderId);
    await saved(page, 'library', 'folder', folderId);
    await reopen(page, app.url + '/library');
    await expect(page.getByRole('combobox', { name: 'Folder', exact: true })).toHaveValue(folderId);
    await page.getByRole('button', { name: 'Sort images', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Sort images', exact: true })).toHaveValue('oldest');
    await page.getByRole('button', { name: 'Done', exact: true }).click();
    await page.getByRole('button', { name: 'Filter images', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Filter image type', exact: true })).toHaveValue('still');
    await expect(page.getByRole('combobox', { name: 'Filter image format', exact: true })).toHaveValue('webp');
    await page.getByRole('button', { name: 'Done', exact: true }).click();

    await page.goto(app.url + '/');
    await sort(page, 'Sort packs', 'newest');
    await filter(page, 'Filter packs', [['Visibility', 'private'], ['Filter pack type', 'still']]);
    await saved(page, 'packs', 'visibility', 'private');
    await reopen(page, app.url + '/');
    await page.getByRole('button', { name: 'Sort packs', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Sort packs', exact: true })).toHaveValue('newest');
    await page.getByRole('button', { name: 'Done', exact: true }).click();
    await page.getByRole('button', { name: 'Filter packs', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Visibility', exact: true })).toHaveValue('private');
    await expect(page.getByRole('combobox', { name: 'Filter pack type', exact: true })).toHaveValue('still');
    await page.getByRole('button', { name: 'Done', exact: true }).click();

    await page.goto(`${app.url}/packs/${packId}`);
    await sort(page, 'Sort images', 'newest');
    await filter(page, 'Filter images', [['Favorites', 'no']]);
    await saved(page, 'pack-stickers', 'favorites', 'no');
    await reopen(page, `${app.url}/packs/${packId}`);
    await page.getByRole('button', { name: 'Sort images', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Sort images', exact: true })).toHaveValue('newest');
    await page.getByRole('button', { name: 'Done', exact: true }).click();
    await page.getByRole('button', { name: 'Filter images', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Favorites', exact: true })).toHaveValue('no');
    await expect(page.getByRole('combobox', { name: 'Filter image type', exact: true })).toHaveValue('all');
    await page.getByRole('button', { name: 'Done', exact: true }).click();

    await page.goto(app.url + '/library');
    await page.getByRole('button', { name: 'Filter images', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Filter image type', exact: true })).toHaveValue('still');
    await page.getByRole('button', { name: 'Reset filters', exact: true }).click();
    await saved(page, 'library', 'type', 'all');
    await page.getByRole('button', { name: 'Done', exact: true }).click();
    await reopen(page, app.url + '/library');
    await expect(page.getByRole('combobox', { name: 'Folder', exact: true })).toHaveValue('all');
    await page.getByRole('button', { name: 'Filter images', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Filter image type', exact: true })).toHaveValue('all');
    await page.getByRole('button', { name: 'Done', exact: true }).click();
    // An old folder filter must not leave a reopened library looking empty.
    await page.evaluate(folder => localStorage.setItem('stickers.browse.library', JSON.stringify({ folder, sort: 'obsolete-sort' })), randomUUID());
    await reopen(page, app.url + '/library');
    await expect(page.getByRole('combobox', { name: 'Folder', exact: true })).toHaveValue('all');
    await saved(page, 'library', 'folder', 'all');
    await page.getByRole('button', { name: 'Sort images', exact: true }).click();
    await expect(page.getByRole('combobox', { name: 'Sort images', exact: true })).toHaveValue('default');
});
