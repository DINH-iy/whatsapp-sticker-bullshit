import { test, expect, openLibrary, importImage, waitReady, assertNoOverflow } from '../fixtures.mjs';
import { png } from '../media.mjs';
import { writeFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import path from 'node:path';

test('GIF limit and quality persist with visible performance warnings', async ({ page, app }) => {
    await openLibrary(page, app);
    await page.goto(app.url + '/settings');
    const editing = page.getByRole('button', { name: 'Imports, quality & editing', exact: true });
    await editing.click();
    const limit = page.getByRole('spinbutton', { name: 'GIF import limit (MB)', exact: true });
    await expect(limit).toBeEnabled();
    await expect(limit).toHaveValue('20.97152');
    await expect(page.getByText(/Higher limits allow larger GIF files/)).toBeVisible();
    await limit.fill('50'); await limit.press('Tab');
    await page.getByRole('combobox', { name: 'Sticker quality', exact: true }).selectOption('0.55');
    await page.reload(); await editing.click();
    await expect(limit).toHaveValue('50');
    await expect(page.getByRole('combobox', { name: 'Sticker quality', exact: true })).toHaveValue('0.55');
    expect(await page.evaluate(() => JSON.parse(localStorage.getItem('stickers.mediaSettings')))).toEqual({ gifMiB: 50_000_000 / 1_048_576, quality: .55 });
    await assertNoOverflow(page, page.locator('.settings-card').filter({ has: limit }));
    await page.getByRole('button', { name: 'Library statistics & storage', exact: true }).click();
    await page.getByRole('button', { name: /Display units/ }).click();
    const units = page.getByRole('combobox', { name: 'Storage units', exact: true });
    await expect(units).toHaveValue('decimal');
    await units.selectOption('binary');
    const binaryLimit = page.getByRole('spinbutton', { name: 'GIF import limit (MiB)', exact: true });
    await expect(binaryLimit).toHaveValue('47.68371582');
    expect(await page.evaluate(() => JSON.parse(localStorage.getItem('stickers.mediaSettings')).gifMiB * 1_048_576)).toBe(50_000_000);
    await binaryLimit.fill('32'); await binaryLimit.press('Tab');
    await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('stickers.mediaSettings')).gifMiB)).toBe(32);
    await units.selectOption('decimal');
    await expect(limit).toHaveValue('33.554432');
    await page.reload(); await editing.click();
    await expect(limit).toHaveValue('33.554432');
});

test('virtualized thumbnails remain associated with their stickers while scrolling and switching views', async ({ page, app }) => {
    await openLibrary(page, app);
    const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
    const library = await app.library(), source = library.Stickers[0];
    library.Stickers = Array.from({ length: 90 }, (_, index) => ({ ...source, Id: randomUUID(), Name: `Scroll ${index}` }));
    await writeFile(path.join(app.data, 'library.json'), JSON.stringify(library));
    await page.reload();
    await expect(page.locator('.sticker-grid-row').first()).toBeVisible();
    for (const offset of [1400, 3200, 0]) {
        await page.evaluate(y => window.scrollTo(0, y), offset);
        await expect.poll(async () => page.locator('.image-card:visible img[data-thumbnail-state="ready"]').count()).toBeGreaterThan(0);
    }
    await page.getByRole('switch', { name: 'List view', exact: true }).click();
    await expect(page.locator('.image-grid')).toHaveClass(/library-list-view/);
    await expect(page.locator('.view-toggle-track')).toHaveCount(0);
    await assertNoOverflow(page, page.locator('.browse-toolbar'));
    const left = await page.locator('.browse-actions').boundingBox(), right = await page.locator('.browse-view-actions').boundingBox();
    expect(right.x).toBeGreaterThan(left.x);
    await page.screenshot({ path: `test-results/compact-list-${page.viewportSize().width}.png`, fullPage: false });
});
