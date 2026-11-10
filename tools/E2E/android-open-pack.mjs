import { spawnSync } from 'node:child_process';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import assert from 'node:assert/strict';
import { packArchive } from './media.mjs';

// Opt-in real-device check. It never clears app data, reinstalls the app, or
// deletes existing packs. Fixture packs remain identifiable by their E2E title.
const serial = process.env.ANDROID_SERIAL;
if (!serial) throw new Error('Set ANDROID_SERIAL to an Android 10+ test device/emulator. No device was changed.');
const adb = process.env.ADB || 'adb', packageName = process.env.E2E_ANDROID_PACKAGE || 'com.stickerstudio.app';
const output = fileURLToPath(new URL('./test-results/android/', import.meta.url));
assert.match(packageName, /^[a-zA-Z0-9_.]+$/, 'Use a valid Android package name');
await mkdir(output, { recursive: true });
function command(args, input) {
    const result = spawnSync(adb, ['-s', serial, ...args], { input, encoding: input ? undefined : 'utf8', timeout: 30_000, maxBuffer: 8 * 1024 * 1024 });
    if (result.error || result.status !== 0) throw new Error(`${args.join(' ')}: ${result.error?.message || result.stderr}`);
    return input ? result.stdout : result.stdout.trim();
}
function resolve(uri, mime) {
    const result = command(['shell', 'cmd', 'package', 'query-activities', '--brief', '-a', 'android.intent.action.VIEW', '-c', 'android.intent.category.BROWSABLE', '-d', uri, '-t', mime]);
    const components = result.match(/[\w.]+\/[\w.$]+/g) || [];
    const component = components.find(component => component.startsWith(packageName + '/') && component.endsWith('MainActivity'));
    assert.ok(component, `Android does not offer ${packageName} for ${mime} ${uri}: ${result}`);
    return component;
}
function xmlNodes(xml) {
    return [...xml.matchAll(/<node\b([^>]+)>/g)].map(match => Object.fromEntries([...match[1].matchAll(/([\w-]+)="([^"]*)"/g)].map(item => [item[1], item[2].replaceAll('&amp;', '&').replaceAll('&quot;', '"')])));
}
function dump() {
    command(['shell', 'uiautomator', 'dump', '/sdcard/stikerin-e2e.xml']);
    return command(['shell', 'cat', '/sdcard/stikerin-e2e.xml']);
}
async function visible(text, timeout = 30_000) {
    const end = Date.now() + timeout;
    while (Date.now() < end) {
        const xml = dump(); await writeFile(path.join(output, 'latest-ui.xml'), xml);
        const node = xmlNodes(xml).find(node => node.text === text || node['content-desc'] === text);
        if (node) return node;
        await new Promise(resolve => setTimeout(resolve, 500));
    }
    throw new Error(`Android UI did not show “${text}”. Set the test app's language to English; see latest-ui.xml.`);
}
function tap(node) {
    const bounds = node.bounds?.match(/\[(\d+),(\d+)\]\[(\d+),(\d+)\]/)?.slice(1).map(Number);
    assert.ok(bounds, `No bounds for ${JSON.stringify(node)}`);
    command(['shell', 'input', 'tap', String(Math.round((bounds[0] + bounds[2]) / 2)), String(Math.round((bounds[1] + bounds[3]) / 2))]);
}
async function screenshot(name) {
    const result = spawnSync(adb, ['-s', serial, 'exec-out', 'screencap', '-p'], { timeout: 15_000, maxBuffer: 16 * 1024 * 1024 });
    if (!result.error && result.status === 0) await writeFile(path.join(output, name + '.png'), result.stdout);
}
assert.equal(command(['get-state']), 'device');
assert.ok(Number(command(['shell', 'getprop', 'ro.build.version.sdk'])) >= 29, 'MediaStore Downloads fixture provisioning requires Android 10+');
command(['shell', 'pm', 'path', packageName]);
for (const extension of ['wassticker', 'wasticker', 'wastickers']) {
    for (const mime of ['application/octet-stream', 'application/zip', 'application/x-wastickers'])
        resolve(`content://stikerin.e2e/Download/fixture.${extension}`, mime);
}
console.log('PASS Android resolver advertises the app for all three pack extensions and common provider MIME types');
const webp = await readFile(new URL('./fixtures/artwork.webp', import.meta.url));
try {
    for (const mode of ['cold', 'warm']) {
        const title = `E2E ${mode} pack ${Date.now()}`;
        const inserted = command(['shell', 'content', 'insert', '--uri', 'content://media/external_primary/downloads', '--bind', `_display_name:s:${mode}.wassticker`, '--bind', 'mime_type:s:application/octet-stream']);
        const uri = inserted.match(/content:\/\/[^\s]+/)?.[0];
        assert.ok(uri, `Could not create a MediaStore fixture: ${inserted}`);
        try {
            command(['shell', 'content', 'write', '--uri', uri], packArchive(webp, title));
            const component = resolve(uri, 'application/octet-stream');
            if (mode === 'cold') command(['shell', 'am', 'force-stop', packageName]);
            const started = command(['shell', 'am', 'start', '-W', '-a', 'android.intent.action.VIEW', '-d', uri, '-t', 'application/octet-stream', '-f', '0x00000001', '-n', component]);
            assert.doesNotMatch(started, /Error:|Exception/);
            // Explicit intent targets the already verified resolver candidate,
            // avoiding a device-dependent app-chooser dialog.
            tap(await visible('Import pack'));
            await visible(title, 60_000); await screenshot(mode + '-imported');
            command(['shell', 'am', 'force-stop', packageName]);
            command(['shell', 'am', 'start', '-W', '-n', component]);
            // Import must persist across process death; the pack is accessible
            // from the home screen even if navigation isn't restored.
            await visible('My Packs'); await visible(title);
            console.log(`PASS ${mode} VIEW intent imports a granted MediaStore URI and retains the pack after restart`);
            if (mode === 'cold') {
                // Exercise the Android system picker too: resolver registration
                // alone cannot prove a custom extension is selectable there.
                const pickerTitle = title + ' picker';
                command(['shell', 'content', 'write', '--uri', uri], packArchive(webp, pickerTitle));
                tap(await visible('Import pack'));
                let file;
                try { file = await visible('cold.wassticker', 8000); }
                catch {
                    tap(await visible('Show roots'));
                    tap(await visible('Downloads'));
                    file = await visible('cold.wassticker');
                }
                assert.notEqual(file.enabled, 'false', '.wassticker is disabled in the Android file picker');
                tap(file);
                await visible(pickerTitle, 60_000); await screenshot('system-picker-imported');
                console.log('PASS Android system picker selects a generic-binary .wassticker and imports it through the app');
            }
        } finally {
            command(['shell', 'content', 'delete', '--uri', uri]);
        }
    }
} catch (error) {
    await screenshot('failed'); throw error;
} finally {
    command(['shell', 'rm', '-f', '/sdcard/stikerin-e2e.xml']);
}
