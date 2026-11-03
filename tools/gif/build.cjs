const path = require('node:path');
require('esbuild').buildSync({
    entryPoints: [path.join(__dirname, '../../StickerApp.Shared/Media/gif-worker.js')],
    outfile: path.join(__dirname, '../../StickerApp.Shared/wwwroot/gif-worker.js'),
    nodePaths: [path.join(__dirname, 'node_modules')],
    bundle: true, format: 'esm', platform: 'browser', target: 'es2020', minify: true,
    legalComments: 'eof'
});
