# Browser module layout

| Folder | Responsibility |
| --- | --- |
| `ui/` | Shared controls, themes, navigation, localization, diagnostics and app lifecycle |
| `media/` | Video/GIF conversion, image/layer editors, effects, playback and codecs |
| `imports/klipy/` | KLIPY search and bounded media download sessions |
| `imports/imgur/` | Local Imgur importer preferences |
| `imports/discord/` | Discord export parsing and file input controller |
| `imports/vendor/` | Generated ZIP-reader bundle and license |
| `packs/` | Pack cover generation and pack sharing helpers |
| `friends/` | Friends API client and invite bootstrap script |

The browser and native hosts both load modules under `_content/StickerApp.Shared/js/`. Update imports in Razor, C#, browser fixtures and both hosts when moving an asset. Module-relative worker, WASM and font URLs must still resolve from the new folder.

Readable codec/ZIP sources live under `StickerApp.Shared/Media/`. Rebuild generated files with `npm run build --prefix tools/MediaBuild` from the repository root. See [media ownership](media/README.md) and [development checks](../../../tools/README.md).
