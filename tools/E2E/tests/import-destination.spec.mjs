import { test, expect, openLibrary, importImage, waitReady } from '../fixtures.mjs';
import { png, noisyGif } from '../media.mjs';
import { writeFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import path from 'node:path';

test('device imports use the open pack and offer a destination only in My Library', async ({ page, app }) => {
    const packId = randomUUID();
    await writeFile(path.join(app.data, 'library.json'), JSON.stringify({ Stickers: [], Packs: [{ Id: packId, Name: 'Import destination', Publisher: 'Fixture', StickerIds: [] }], Folders: [] }));
    await page.goto(`${app.url}/packs/${packId}`);
    for (const [name, route] of [['GIF search', 'gifs'], ['Import from Pinterest', 'pinterest'], ['Import from Imgur', 'imgur'], ['Import from Discord', 'discord']]) {
        await expect(page.getByRole('link', { name, exact: true, includeHidden: true })).toHaveAttribute('href', `${route}?pack=${packId}`);
    }
    await page.getByRole('button', { name: 'Add stickers', exact: true }).click();
    await page.getByLabel('Choose image files', { exact: true }).filter({ visible: true }).setInputFiles({ name: 'In pack.png', mimeType: 'image/png', buffer: png() });
    const dialog = page.getByRole('dialog', { name: 'Add images & GIFs', exact: true });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('combobox', { name: 'Destination', exact: true })).toHaveCount(0);
    await dialog.getByRole('button', { name: 'Add 1 files', exact: true }).click();
    await expect.poll(async () => (await app.library()).Packs[0].StickerIds.length).toBe(1);
    await waitReady(page, app, 'In pack');
    await openLibrary(page, app);
    await page.getByRole('button', { name: 'Add stickers', exact: true }).click();
    await page.getByLabel('Choose image files', { exact: true }).filter({ visible: true }).setInputFiles({ name: 'Chosen pack.png', mimeType: 'image/png', buffer: png() });
    await dialog.getByRole('combobox', { name: 'Destination', exact: true }).selectOption(packId);
    await dialog.getByRole('button', { name: 'Add 1 files', exact: true }).click();
    await expect.poll(async () => (await app.library()).Packs[0].StickerIds.length).toBe(2);
});

test('only the processing list row grows to contain its buttons', async ({ page, app }, testInfo) => {
    await openLibrary(page, app);
    await importImage(page, app, { name: 'Ready.png', buffer: png() });
    await waitReady(page, app, 'Ready');
    await page.getByRole('switch', { name: 'List view', exact: true }).click();
    await expect(page.locator('.image-grid')).toHaveClass(/library-list-view/);
    const ready = page.locator('.image-card').filter({ has: page.locator('strong', { hasText: 'Ready' }) });
    const height = (await ready.boundingBox()).height;
    await importImage(page, app, { name: 'Processing.gif', mimeType: 'image/gif', buffer: noisyGif() });
    const row = page.locator('.image-card-processing');
    await expect(row).toHaveCount(1);
    await expect(row.getByRole('button', { name: 'Stop processing', exact: true })).toBeVisible();
    const bounds = await row.evaluate(element => {
        const rect = element.getBoundingClientRect();
        return { height: rect.height, contained: [...element.querySelectorAll('.processing-actions button')].every(button => {
            const b = button.getBoundingClientRect();
            return b.top >= rect.top && b.bottom <= rect.bottom && b.left >= rect.left && b.right <= rect.right;
        }) };
    });
    expect(bounds.contained).toBe(true);
    expect(bounds.height).toBeGreaterThan(height);
    expect((await ready.boundingBox()).height).toBeCloseTo(height, 0);
    await testInfo.attach('processing-list-row', { body: await page.screenshot(), contentType: 'image/png' });
    await row.getByRole('button', { name: 'Stop processing', exact: true }).click();
    await expect(page.locator('.image-card-processing')).toHaveCount(0);
});

test('GIF search imports straight into the open pack without a pack picker', async ({ page, app }) => {
    const packId = randomUUID();
    await writeFile(path.join(app.data, 'library.json'), JSON.stringify({ Stickers: [], Packs: [{ Id: packId, Name: 'GIF destination', Publisher: 'Fixture', StickerIds: [] }], Folders: [] }));
    await page.addInitScript(() => localStorage.setItem('stickers.klipyApiKey', 'fixture-key'));
    const source = 'https://static.klipy.com/destination.gif';
    await page.route('https://api.klipy.com/**', route => route.fulfill({ json: {
        result: true, data: { data: [{ slug: 'destination', title: 'Destination GIF', file: { hd: { gif: { url: source, size: 42 } } } }], has_next: false }
    } }));
    await page.route(source, route => route.fulfill({ contentType: 'image/gif', body: Buffer.from('R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7', 'base64') }));
    await page.goto(`${app.url}/packs/${packId}`);
    for (const [name, route] of [['GIF search', 'gifs'], ['Import from Pinterest', 'pinterest'], ['Import from Imgur', 'imgur'], ['Import from Discord', 'discord']]) {
        await expect(page.getByRole('link', { name, exact: true, includeHidden: true })).toHaveAttribute('href', `${route}?pack=${packId}`);
    }
    await page.getByRole('button', { name: 'Add stickers', exact: true }).click();
    await page.getByRole('button', { name: /^Find a GIF/ }).click();
    for (const count of [1, 2]) {
        if (count === 2) await page.goto(`${app.url}/gifs?pack=${packId}`);
        await page.getByRole('textbox', { name: 'Search KLIPY GIFs', exact: true }).fill('cat');
        await page.getByRole('button', { name: 'Search', exact: true }).click();
        await page.getByRole('button', { name: 'Import Destination GIF', exact: true }).click();
        await page.getByRole('button', { name: 'Download GIF', exact: true }).click();
        await expect.poll(async () => (await app.library()).Packs[0].StickerIds.length).toBe(count);
        const library = await app.library();
        expect(new Set(library.Packs[0].StickerIds)).toEqual(new Set(library.Stickers.map(image => image.Id)));
        await expect(page.getByRole('dialog', { name: 'Add imported GIF to a pack?', exact: true })).toHaveCount(0);
    }
});
