import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
export const root = fileURLToPath(new URL('../../', import.meta.url));
export default function setup() {
    // Build once, before any servers start: rebuilding compressed static assets
    // while a host is running can invalidate that host's asset manifest.
    execFileSync('dotnet', ['build', 'StickerApp.Web/StickerApp.Web.csproj', '-p:NuGetAudit=false'], {
        cwd: root, stdio: 'inherit', timeout: 180_000,
        env: { ...process.env, DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE: 'true' }
    });
}
