# Code map

## Projects

| Project | Responsibility |
| --- | --- |
| `StickerApp.Core` | Library files, media inspection, pack rules/archives, Pinterest import client, WhatsApp export rules, and local logs. No UI dependency. |
| `StickerApp.Shared` | Razor screens, reusable components, shared services, browser scripts and styles. Used by both hosts. |
| `StickerApp.Web` | Browser/connected Home Screen host, browser-specific services, manifest and offline/install shell. |
| `StickerApp` | MAUI Android/iOS host, native services, platform integration and resources. |
| `StickerApp.ShareExtension` | iOS share extension. |
| `StickerApp.ShareServer` | Optional pack-sharing API. |

Core files are grouped under `Library`, `Media`, `Packs`, `Pinterest`, `WhatsApp`, and `Diagnostics`. They retain the `StickerApp.Core` namespace so platform consumers share the same API.

## Shared UI

`Pages` contains routed screens grouped into `Library`, `Packs`, `Gifs`, `Pinterest`, and `Settings`. Moving a file does not change its `@page` URL. Each screen's markup and code-behind stay together. The library screen separates state/lifecycle and pack actions (`Home.razor.cs`), device/file imports (`Home.Importing.cs`), and image/GIF editing (`Home.Editing.cs`).

`Components` contains these groups:

- `Common`: icons, buttons/tiles, history controls, and floating action menus.
- `Packs`: pack creation, cover selection, sharing, and adding images to packs.
- `Editor`: sticker details and image customization.
- `Gifs`: embedded GIF search dialog.
- `Settings`: settings cards and storage-folder picker.
- `Infrastructure`: app-level hosts for navigation, appearance, diagnostics, preparation and incoming Pinterest shares.
- `Surprises`: optional visual effects.

Razor namespaces match their folders. Shared `_Imports.razor` makes the component groups available to screens and layouts. `Routes.razor` and `Layout` remain the common routing/shell entry points.

`Services` is grouped into `Abstractions`, `Diagnostics`, `Media`, `Packs`, and `Pinterest`. These keep the `StickerApp.Shared.Services` namespace; each host supplies its platform-specific implementations through dependency injection.

## Browser assets

- `wwwroot/css`: shared layout and theme styles.
- `wwwroot/js/ui`: dialogs, menus, navigation, appearance, diagnostics, cover/sharing helpers, and external-provider clients.
- `wwwroot/js/media`: image customization, GIF editing, preparation workers, encoder bundles, WebAssembly and vendor licenses. Related worker files remain together so module-relative imports work in both browser and native WebView hosts.
- `Media`: readable source for bundled GIF and WebP modules. Edit these sources, then run `npm run build --prefix tools/gif` from the repository root. Install the tool dependencies with `npm ci --ignore-scripts --prefix tools/gif` when needed. The generated assets are included in the repository; ordinary .NET builds do not require Node.

Both `StickerApp.Web/Components/App.razor` and `StickerApp/wwwroot/index.html` reference the shared styles and startup theme script. Dynamic imports use `_content/StickerApp.Shared/js/...`. Update both hosts when moving shared assets.

## Local checks

Build the browser host with `dotnet build StickerApp.Web/StickerApp.Web.csproj`, then manually check the affected screens. No test suite is required. Build Android APKs and iOS apps only when requested.

Hosting, synchronization, the friends dashboard and other future features are tracked separately in [IDEAS.md](../IDEAS.md).
