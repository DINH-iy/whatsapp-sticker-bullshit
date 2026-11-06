const path = require('node:path');
const fs = require('node:fs');
const esbuild = require('esbuild');
const media = path.join(__dirname, '../../StickerApp.Shared/Media');
const output = path.join(__dirname, '../../StickerApp.Shared/wwwroot/js/media');
fs.mkdirSync(output, { recursive: true });
const options = {
    nodePaths: [path.join(__dirname, 'node_modules')],
    bundle: true, format: 'esm', platform: 'browser', target: 'es2020',
    minify: true, legalComments: 'eof'
};
esbuild.buildSync({
    ...options, entryPoints: [path.join(media, 'webp-encoder.js')],
    outfile: path.join(output, 'webp-encoder.js')
});
fs.copyFileSync(path.join(__dirname, 'node_modules/@jsquash/webp/LICENSE'),
    path.join(output, 'webp-encoder-LICENSE.txt'));
fs.copyFileSync(path.join(__dirname, 'node_modules/@jsquash/webp/codec/enc/webp_enc.wasm'),
    path.join(output, 'webp_enc.wasm'));
esbuild.buildSync({
    ...options, entryPoints: [path.join(media, 'gif-engine.js')],
    outfile: path.join(output, 'gif-engine.js'), external: ['./sticker-converter.js', './gif-engine.js']
});
esbuild.buildSync({
    ...options, entryPoints: [path.join(media, 'gif-worker.js')],
    outfile: path.join(output, 'gif-worker.js'), external: ['./gif-engine.js']
});
