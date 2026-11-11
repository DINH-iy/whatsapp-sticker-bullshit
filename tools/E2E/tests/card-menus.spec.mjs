import { test, expect, openLibrary, importImage, waitReady, assertNoOverflow } from '../fixtures.mjs';
import { png } from '../media.mjs';
import { writeFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import path from 'node:path';

async function compactMenu(menu) {
    await expect(menu).toBeVisible();
    await expect(menu).toHaveCSS('opacity', '1');
    const metrics = await menu.evaluate(el => {
        const bounds = el.getBoundingClientRect();
        const rows = [...el.querySelectorAll('.toolbar-menu-option')];
        return {
            width: bounds.width,
            bottomPadding: bounds.bottom - rows.at(-1).getBoundingClientRect().bottom,
            rows: rows.map(row => {
                const icon = row.querySelector('.app-icon').getBoundingClientRect();
                const text = row.querySelector('span').getBoundingClientRect();
                return { height: row.getBoundingClientRect().height, offset: Math.abs(icon.y + icon.height / 2 - text.y - text.height / 2) };
            })
        };
    });
    expect(metrics.width).toBeLessThanOrEqual(240);
    expect(metrics.bottomPadding).toBeLessThanOrEqual(8);
    for (const row of metrics.rows) { expect(row.height).toBeGreaterThanOrEqual(44); expect(row.height).toBeLessThanOrEqual(46); expect(row.offset).toBeLessThan(1.5); }
}

for (const list of [false, true]) {
    test(`${list ? 'list' : 'tile'} cards use options menus for library, pack stickers and packs`, async ({ page, app }) => {
        await openLibrary(page, app);
        const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
        const library = await app.library(), packId = randomUUID();
        library.Packs = [{ Id: packId, Name: 'Menu pack', Publisher: 'Fixture', StickerIds: [library.Stickers[0].Id] }];
        await writeFile(path.join(app.data, 'library.json'), JSON.stringify(library));
        await page.reload();
        if (list) { await page.getByRole('switch', { name: 'List view', exact: true }).click(); await expect(page.getByRole('switch', { name: 'List view', exact: true })).toHaveAttribute('aria-checked', 'true'); }
        for (const url of [app.url + '/library', `${app.url}/packs/${packId}`]) {
            await page.goto(url);
            if (list) await expect(page.locator('.image-grid')).toHaveClass(/library-list-view/);
            const card = page.locator('.image-card').filter({ has: page.getByRole('button', { name: `Sticker options for ${name}`, exact: true }) }).first();
            await expect(card.getByRole('button', { name: `Remove ${name}`, exact: true })).not.toBeVisible();
            const options = card.getByRole('button', { name: `Sticker options for ${name}`, exact: true });
            await options.click();
            const menu = card.locator('.toolbar-dropdown');
            await compactMenu(menu); await assertNoOverflow(page, menu);
            // Opening the menu must not activate the image or editor.
            await expect(page.getByRole('dialog')).toHaveCount(0);
            await menu.getByRole('button', { name: `Details for ${name}`, exact: true }).click();
            const details = page.getByRole('dialog', { name: 'Sticker details', exact: true });
            await expect(details).toBeVisible();
            await details.getByRole('button', { name: 'Close sticker details', exact: true }).click();
            await options.click();
            await menu.getByRole('button', { name: `Add ${name} to pack`, exact: true }).click();
            await page.getByRole('button', { name: 'Close pack picker', exact: true }).click();
            await options.click();
            await menu.getByRole('button', { name: `Remove ${name}`, exact: true }).click();
            const deletion = page.getByRole('dialog', { name: 'Delete sticker?', exact: true });
            await expect(deletion).toBeVisible();
            await deletion.getByRole('button', { name: 'Cancel', exact: true }).click();
            await expect(deletion).not.toBeVisible();
            await assertNoOverflow(page, card);
            if (list) {
                const metadata = await card.locator('.sticker-row-details').boundingBox(), action = await options.boundingBox();
                expect(action.x).toBeGreaterThan(metadata.x + metadata.width - 1);
                expect(action.y).toBeLessThan(metadata.y + metadata.height);
            }
            await page.screenshot({ path: `test-results/${list ? 'list' : 'tile'}-sticker-menu-${page.viewportSize().width}.png` });
        }
        await page.goto(app.url + '/');
        if (list) { await page.getByRole('switch', { name: 'List view', exact: true }).click(); await expect(page.getByRole('switch', { name: 'List view', exact: true })).toHaveAttribute('aria-checked', 'true'); }
        const pack = page.locator('.pack-card');
        const options = pack.getByRole('button', { name: 'Pack options for Menu pack', exact: true });
        await options.click();
        const menu = pack.locator('.toolbar-dropdown');
        await compactMenu(menu); await assertNoOverflow(page, menu);
        await page.screenshot({ path: `test-results/${list ? 'list' : 'tile'}-pack-menu-${page.viewportSize().width}.png` });
        await menu.getByRole('button', { name: 'Edit details for Menu pack', exact: true }).click();
        const editing = page.getByRole('dialog', { name: /Edit pack/ });
        await expect(editing).toBeVisible();
        await editing.getByRole('button', { name: 'Cancel', exact: true }).click();
        await options.click();
        await menu.getByRole('button', { name: 'Share or export', exact: true }).click();
        await expect(page.getByRole('dialog', { name: 'Share pack', exact: true })).toBeVisible();
        await page.getByRole('button', { name: 'Close sharing', exact: true }).click();
        await options.click();
        await menu.getByRole('button', { name: 'Delete pack Menu pack', exact: true }).click();
        const deletion = page.getByRole('dialog', { name: 'Delete pack?', exact: true });
        await deletion.getByRole('button', { name: 'Cancel', exact: true }).click();
        expect((await app.library()).Stickers).toHaveLength(1);
        expect((await app.library()).Packs).toHaveLength(1);
    });
}
