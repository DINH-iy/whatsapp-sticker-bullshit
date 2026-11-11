import { test, expect } from '../fixtures.mjs';

for (const mode of ['metadata', 'headers', 'unknown', 'oversized']) {
    test(`KLIPY reviews ${mode} size before downloading the full GIF`, async ({ page, app }) => {
        await page.addInitScript(() => localStorage.setItem('stickers.klipyApiKey', 'fixture-key'));
        let heads = 0, downloads = 0;
        const source = 'https://static.klipy.com/size-fixture.gif';
        await page.route('https://api.klipy.com/**', route => route.fulfill({ json: {
            result: true, data: { data: [{ slug: 'size-fixture', title: 'Sized GIF', file: { hd: { gif: { url: source, ...(mode === 'metadata' ? { size: 1_000_000 } : mode === 'oversized' ? { size: 100_000_000 } : {}) } } } }], has_next: false }
        } }));
        await page.route(source, route => {
            if (route.request().method() === 'HEAD') {
                heads++;
                return route.fulfill({ status: 200, headers: { 'access-control-allow-origin': '*', ...(mode === 'headers' ? { 'content-length': '2000000' } : {}) }, body: '' });
            }
            downloads++;
            return route.fulfill({ contentType: 'image/gif', headers: { 'access-control-allow-origin': '*' }, body: Buffer.from('R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7', 'base64') });
        });
        await page.goto(app.url + '/gifs');
        await page.getByRole('textbox', { name: 'Search KLIPY GIFs', exact: true }).fill('cat');
        await page.getByRole('button', { name: 'Search', exact: true }).click();
        await page.getByRole('button', { name: 'Import Sized GIF', exact: true }).click();
        const dialog = page.getByRole('dialog', { name: 'Download this GIF?', exact: true });
        await expect(dialog).toBeVisible();
        if (mode === 'unknown') await expect(dialog).toContainText('File size unavailable');
        else await expect(dialog).toContainText(mode === 'metadata' ? /Full GIF size: 1[,.]0 MB/ : mode === 'headers' ? /Full GIF size: 2[,.]0 MB/ : /Full GIF size: 100[,.]0 MB/);
        await expect(dialog).toContainText('Your import limit:');
        expect(downloads).toBe(0);
        expect(heads).toBe(mode === 'metadata' || mode === 'oversized' ? 0 : 1);
        if (mode === 'oversized') {
            await expect(dialog.getByRole('button', { name: 'Download GIF', exact: true })).toBeDisabled();
            await expect(dialog).toContainText('exceeds your import limit');
            await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
        } else if (mode === 'unknown') {
            await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
        } else {
            await dialog.getByRole('button', { name: 'Download GIF', exact: true }).click();
            await expect.poll(() => downloads).toBe(1);
            await expect.poll(async () => (await app.library()).Stickers.length).toBe(1);
        }
        if (mode === 'unknown' || mode === 'oversized') {
            expect(downloads).toBe(0);
            expect((await app.library()).Stickers).toHaveLength(0);
        }
    });
}
