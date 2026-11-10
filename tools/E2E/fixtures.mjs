import { test as base, expect } from '@playwright/test';
import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { createWriteStream } from 'node:fs';
import { mkdir, readFile, writeFile, rm } from 'node:fs/promises';
import { once } from 'node:events';
import path from 'node:path';
import { root } from './setup.mjs';

async function freePort() {
    const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
    const port = server.address().port; await new Promise(resolve => server.close(resolve)); return port;
}
export const test = base.extend({
    app: async ({ page }, use, testInfo) => {
        const folder = testInfo.outputPath('host'), data = path.join(folder, '.data');
        await mkdir(data, { recursive: true }); await mkdir(path.join(folder, 'wwwroot'), { recursive: true });
        await writeFile(path.join(data, 'profile.json'), JSON.stringify({ SetupComplete: true, Username: 'E2E', FirstName: 'Fixture' }));
        const port = await freePort(), url = `http://127.0.0.1:${port}`;
        const logPath = testInfo.outputPath('host.log'), log = createWriteStream(logPath);
        const server = spawn('dotnet', [path.join(root, 'StickerApp.Web/bin/Debug/net10.0/StickerApp.Web.dll'), '--contentRoot', folder, '--urls', url], {
            cwd: root, env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', Logging__LogLevel__Default: 'Warning' }, stdio: ['ignore', 'pipe', 'pipe']
        });
        server.stdout.pipe(log); server.stderr.pipe(log);
        const closed = new Promise(resolve => server.once('close', resolve));
        let startupError; server.on('error', error => { startupError = error; });
        const pageErrors = []; page.on('pageerror', error => pageErrors.push(error.message));
        await page.addInitScript(() => {
            localStorage.setItem('stickers.language', 'en');
            localStorage.setItem('stickers.app-guide-seen', '1');
        });
        // No external imports or accounts are involved in these tests.
        await page.route('https://**/*', route => route.abort());
        const app = {
            url, folder, data,
            async library() {
                try { return JSON.parse(await readFile(path.join(data, 'library.json'), 'utf8')); }
                catch (error) { if (error.code === 'ENOENT') return { Stickers: [], Packs: [], Folders: [] }; throw error; }
            },
            bytes: name => readFile(path.join(data, 'media', name)),
            async document(sticker) { return JSON.parse(await this.bytes(sticker.CompositionFileName)); }
        };
        try {
            await expect.poll(async () => {
                if (startupError) throw startupError;
                if (server.exitCode !== null || server.signalCode !== null) throw new Error(`Test host exited (${server.exitCode}); see ${logPath}`);
                try { return (await fetch(url)).status; } catch { return 0; }
            }, { timeout: 30_000, message: 'isolated web host becomes ready' }).toBe(200);
            await use(app);
            expect(pageErrors, 'uncaught browser errors').toEqual([]);
        } finally {
            if (testInfo.status !== testInfo.expectedStatus && !page.isClosed()) {
                await testInfo.attach('live-failure-ui', { body: await page.screenshot(), contentType: 'image/png' });
            }
            if (server.pid && server.exitCode === null && server.signalCode === null) {
                server.kill('SIGTERM');
                const timer = setTimeout(() => server.kill('SIGKILL'), 5000);
                await closed; clearTimeout(timer);
            }
            await new Promise(resolve => log.end(resolve));
            await testInfo.attach('host.log', { path: logPath, contentType: 'text/plain' });
            if (testInfo.status === testInfo.expectedStatus) await rm(folder, { recursive: true, force: true });
        }
    }
});
export { expect };

export async function openLibrary(page, app) {
    await page.goto(app.url + '/library');
    await expect(page.getByRole('heading', { name: 'My Library', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Add stickers', exact: true })).toBeEnabled();
}
export async function importImage(page, app, { name = 'Artwork.png', mimeType = 'image/png', buffer }) {
    await page.getByRole('button', { name: 'Add stickers', exact: true }).click();
    const label = mimeType === 'image/gif' ? 'Choose GIF files' : 'Choose image files';
    await page.getByLabel(label, { exact: true }).setInputFiles({ name, mimeType, buffer });
    const originals = page.getByRole('dialog', { name: 'Add images & GIFs', exact: true });
    await expect(originals).toBeVisible();
    await originals.getByRole('button', { name: 'Add 1 files', exact: true }).click();
    await expect(originals).not.toBeVisible();
    const dismiss = page.getByRole('button', { name: 'Close Add stickers menu', exact: true });
    if (await dismiss.isVisible()) await dismiss.click({ position: { x: 8, y: 8 } });
    const imageName = name.replace(/\.[^.]+$/, '');
    await expect.poll(async () => (await app.library()).Stickers.some(image => image.Name === imageName), { timeout: 60_000 }).toBe(true);
    await expect(page.locator('.image-card').filter({ has: page.locator('strong', { hasText: imageName }) }).first()).toBeVisible();
    return imageName;
}
export async function waitReady(page, app, name) {
    await expect.poll(async () => {
        const image = (await app.library()).Stickers.find(image => image.Name === name);
        return image?.MimeType === 'image/webp' && image.Width === 512 && image.Height === 512;
    }, { timeout: 120_000, message: `${name} finishes actual WebP preparation` }).toBe(true);
    await expect(page.locator('.sticker-processing-overlay')).toHaveCount(0, { timeout: 120_000 });
}
export async function editImage(page, name) {
    await page.locator('.image-card').filter({ has: page.getByText(name, { exact: true }) }).first()
        .getByRole('button', { name: `Edit ${name}`, exact: true }).last().click();
    const dialog = page.getByRole('dialog', { name: /Edit (image|GIF|animated image)/ });
    await expect(dialog.getByRole('button', { name: 'Fit artwork to maximum', exact: true })).toBeEnabled();
    return dialog;
}
export async function renameEdited(page, dialog, name) {
    await dialog.getByRole('button', { name: 'Sticker options', exact: true }).click();
    const options = page.getByRole('dialog', { name: 'Sticker options', exact: true });
    await options.getByLabel('Name', { exact: true }).fill(name);
    await options.getByRole('button', { name: 'Close Sticker options', exact: true }).click();
}
export function canvasBounds(canvas) {
    const { width, height } = canvas, pixels = canvas.getContext('2d').getImageData(0, 0, width, height).data;
    let left = width, top = height, right = -1, bottom = -1;
    for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) if (pixels[(y * width + x) * 4 + 3] > 128) {
        left = Math.min(left, x); top = Math.min(top, y); right = Math.max(right, x); bottom = Math.max(bottom, y);
    }
    return { width, height, artworkWidth: Math.max(0, right - left + 1), artworkHeight: Math.max(0, bottom - top + 1) };
}
export async function assertNoOverflow(page, scope) {
    const clipped = await scope.locator('button:visible').evaluateAll(buttons => buttons.filter(button => {
        const rect = button.getBoundingClientRect();
        return rect.left < -1 || rect.right > innerWidth + 1;
    }).map(button => button.getAttribute('aria-label') || button.textContent));
    expect(clipped, 'visible controls stay within the viewport').toEqual([]);
}
