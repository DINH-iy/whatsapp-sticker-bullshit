# Development tools

Run these commands from the repository root. Checks use generated fixtures and temporary libraries.

| Folder | Purpose | Command |
| --- | --- | --- |
| `EditorChecks/node/` | Video timing/budgets, workers, cancellation, deduplication and editor preferences | `node tools/check.mjs` |
| `EditorChecks/browser/` | Actual browser rendering, media conversion and worker lifecycle checks | See [browser checks](EditorChecks/README.md) |
| `FeatureChecks/` | Shared UI, import, lifecycle, diagnostics and library regressions | `dotnet run --project tools/FeatureChecks` |
| `FeatureChecks/node/` | Local drafts and reset-progress browser helpers | `node tools/check.mjs` |
| `StorageChecks/` | Library persistence, atomic imports, sources, packs and backups | `dotnet run --project tools/StorageChecks` |
| `FriendsChecks/` | Sharing server repositories and collections | `dotnet run --project tools/FriendsChecks` |
| `DiscordChecks/` | Discord import parser fixtures | `node tools/check.mjs` |
| `MediaBuild/` | Rebuild GIF/WebP/WASM and ZIP-reader bundles from readable sources | `npm run build --prefix tools/MediaBuild` |

`node tools/check.mjs` runs all Node checks in separate processes and stops on the first failure. Install the media-tool dependencies first; the Discord suite uses `fflate` to generate ZIP fixtures. The other Node checks have no external dependencies. An individual `.mjs` file can also be run directly with Node.

The FeatureChecks C# cases are grouped into `Imports/`, `Infrastructure/`, and `Library/`; `Program.cs` is the suite entry point. These projects use the SDK's recursive source inclusion, so moving a check does not require editing the project file.

Install media-tool dependencies with `npm ci --ignore-scripts --prefix tools/MediaBuild`. The generated runtime files and third-party licenses remain in `StickerApp.Shared/wwwroot/js/media/codecs/` and `StickerApp.Shared/wwwroot/js/imports/vendor/`. Do not edit generated bundles directly.
