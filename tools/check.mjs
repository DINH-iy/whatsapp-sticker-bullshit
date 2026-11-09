import { existsSync, readdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = fileURLToPath(new URL('../', import.meta.url));
if (!existsSync(path.join(root, 'tools/MediaBuild/node_modules/fflate'))) {
    console.error('Install ZIP fixture dependencies: npm ci --ignore-scripts --prefix tools/MediaBuild');
    process.exit(1);
}
const suites = ['tools/EditorChecks/node', 'tools/FeatureChecks/node', 'tools/DiscordChecks'];
let passed = 0;
for (const suite of suites) {
    for (const file of readdirSync(path.join(root, suite)).filter(file => file.endsWith('-checks.mjs')).sort()) {
        const relative = path.join(suite, file);
        console.log(`\n${relative}`);
        const result = spawnSync(process.execPath, [path.join(root, relative)], { cwd: root, stdio: 'inherit' });
        if (result.error) { console.error(result.error.message); process.exit(1); }
        if (result.status !== 0) process.exit(result.status ?? 1);
        passed++;
    }
}
console.log(`\n${passed} Node check suites passed.`);
