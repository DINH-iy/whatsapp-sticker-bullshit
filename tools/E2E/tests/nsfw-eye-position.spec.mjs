import { test, expect, openLibrary, importImage, waitReady, assertNoOverflow } from '../fixtures.mjs';
import { png } from '../media.mjs';
import { writeFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import path from 'node:path';

for (const list of [false, true]) {
    test(`NSFW eye sits beside artwork in ${list ? 'list' : 'tile'} library and pack views`, async ({ page, app }) => {
        await openLibrary(page, app);
        const name = await importImage(page, app, { buffer: png() }); await waitReady(page, app, name);
        const library = await app.library(), packId = randomUUID();
        library.Stickers[0].Nsfw = true;
        library.Packs = [{ Id: packId, Name: 'Spoiler pack', Publisher: 'Fixture', Nsfw: true, StickerIds: [library.Stickers[0].Id] }];
        await writeFile(path.join(app.data, 'library.json'), JSON.stringify(library));
        for (const [url, selector] of [[app.url + '/library', '.image-card-content > .spoiler-preview'], [app.url + '/', '.pack-card > .spoiler-preview'], [`${app.url}/packs/${packId}`, '.image-card-content > .spoiler-preview']]) {
            await page.goto(url);
            const toggle = page.getByRole('switch', { name: 'List view', exact: true });
            await expect(toggle).toBeEnabled();
            if ((await toggle.getAttribute('aria-checked') === 'true') !== list) await toggle.click();
            const preview = page.locator(selector).first();
            const content = preview.locator('.spoiler-preview-content');
            const eye = preview.locator('.spoiler-eye');
            await expect(eye).toBeVisible();
            const art = await content.boundingBox(), control = await eye.boundingBox();
            expect(control.x).toBeGreaterThanOrEqual(art.x + art.width + 4);
            expect(Math.abs((control.y + control.height / 2) - (art.y + art.height / 2))).toBeLessThan(2);
            await assertNoOverflow(page, preview.locator('..'));
            await expect(preview).toHaveClass(/spoiler-hidden/);
            await eye.click();
            await expect(preview).not.toHaveClass(/spoiler-hidden/);
            await eye.click();
            await expect(preview).toHaveClass(/spoiler-hidden/);
            await expect(page.getByRole('dialog')).toHaveCount(0);
        }
        const heading = page.locator('.pack-heading-name h1');
        // Keep the eye beside the cover without crushing the name into fragments.
        expect(await heading.evaluate(el => el.getBoundingClientRect().width)).toBeGreaterThan(100);
        await page.screenshot({ path: `test-results/nsfw-eye-${list ? 'list' : 'tile'}-${page.viewportSize().width}.png` });
    });
}
