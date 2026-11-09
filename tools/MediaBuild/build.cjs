const path = require('node:path');
const fs = require('node:fs');
const esbuild = require('esbuild');
async function buildMedia() {
    const media = path.join(__dirname, '../../StickerApp.Shared/Media/Codecs');
    const output = path.join(__dirname, '../../StickerApp.Shared/wwwroot/js/media/codecs');
    fs.mkdirSync(path.join(output,'licenses'), { recursive: true });
    // Keep app-owned modules readable and shared at runtime instead of copying them
    // into the vendor codec bundle. Source imports still point to real files.
    const runtimeModules = {
        name: 'runtime-media-modules',
        setup(build) {
            build.onResolve({ filter: /^\.\.\/\.\.\/wwwroot\/js\/media\// }, args => {
                const resolved = path.resolve(path.dirname(args.importer), args.path);
                if (!fs.existsSync(resolved)) throw new Error(`Missing media module: ${resolved}`);
                return { path: '../' + path.relative(path.dirname(output), resolved).split(path.sep).join('/'), external: true };
            });
        }
    };
    const options = {
        nodePaths: [path.join(__dirname, 'node_modules')],
        bundle: true, format: 'esm', platform: 'browser', target: 'es2020',
        minify: true, legalComments: 'eof'
    };
    await esbuild.build({
        ...options, entryPoints: [path.join(media, 'webp-encoder.js')],
        outfile: path.join(output, 'webp-encoder.js')
    });
    fs.copyFileSync(path.join(__dirname, 'node_modules/@jsquash/webp/LICENSE'),
        path.join(output, 'licenses/webp-encoder-LICENSE.txt'));
    fs.copyFileSync(path.join(__dirname, 'node_modules/@jsquash/webp/codec/enc/webp_enc.wasm'),
        path.join(output, 'webp_enc.wasm'));
    await esbuild.build({
        ...options, entryPoints: [path.join(media, 'gif-engine.js')],
        outfile: path.join(output, 'gif-engine.js'), plugins: [runtimeModules]
    });
    await esbuild.build({
        ...options, entryPoints: [path.join(media, 'gif-worker.js')],
        outfile: path.join(output, 'gif-worker.js'), external: ['./gif-engine.js']
    });
    await esbuild.build({
        ...options, entryPoints: [path.join(media, 'video-gif-encoder.js')],
        outfile: path.join(output, 'video-gif-encoder.js')
    });

    const importsOutput = path.join(__dirname, '../../StickerApp.Shared/wwwroot/js/imports/vendor');
    fs.mkdirSync(importsOutput, { recursive: true });
    await esbuild.build({
        ...options, entryPoints: [path.join(media, '../Imports/zip-reader.js')],
        outfile: path.join(importsOutput, 'zip-reader.js')
    });
    fs.copyFileSync(path.join(__dirname, 'node_modules/fflate/LICENSE'), path.join(importsOutput, 'fflate-LICENSE.txt'));

}
buildMedia().catch(error => { console.error(error); process.exitCode = 1; });
