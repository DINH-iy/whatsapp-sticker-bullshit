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

- `wwwroot/css`: shared layout, theme styles, and subtle effects in `motion.css`.
- `wwwroot/js/ui`: dialogs, menus, navigation, appearance, diagnostics, cover/sharing helpers, and external-provider clients.
- `wwwroot/js/media`: image customization, GIF editing, preparation workers, encoder bundles, WebAssembly and vendor licenses. Related worker files remain together so module-relative imports work in both browser and native WebView hosts.
- `Media`: readable source for bundled GIF and WebP modules. Edit these sources, then run `npm run build --prefix tools/gif` from the repository root. Install the tool dependencies with `npm ci --ignore-scripts --prefix tools/gif` when needed. The generated assets are included in the repository; ordinary .NET builds do not require Node.

Both `StickerApp.Web/Components/App.razor` and `StickerApp/wwwroot/index.html` reference the shared styles and startup theme script. Dynamic imports use `_content/StickerApp.Shared/js/...`. Update both hosts when moving shared assets.

## Local checks

Build the browser host with `dotnet build StickerApp.Web/StickerApp.Web.csproj`, then manually check the affected screens. No test suite is required. Build Android APKs and iOS apps only when requested.

Hosting, synchronization, the friends dashboard and other future features are tracked separately in [IDEAS.md](../IDEAS.md).

## Touch feedback

Native hosts register `ITapFeedback` with `NativeTapFeedback`; the browser does not. `TapFeedbackHost` attaches a delegated button-click listener only when that native service exists. `TapFeedbackSettings` appears under General settings; its controls are disabled in the browser with an explanation that native apps are required. Preferences store the enabled flag and intensity on the device (default enabled, 25%). Both the toggle and slider bypass automatic click feedback; enabling or releasing the slider plays one preview.

iOS uses a light impact at bounded intensity. Android uses an 8 ms low-amplitude pulse, respecting the system haptic setting; motors without amplitude control fall back to the system key tap. Tap requests are throttled. No vibration occurs on typing, dragging, hover, or background work.

## Layer compositions

`Components/Editor/Layers/LayerEditorDialog` owns the dialog and save flow. `Pages/Library/Home.Layers.cs` opens it from Add stickers and reopens saved compositions from a sticker’s edit action. `wwwroot/js/media/layer-editor.js` owns the 512 × 512 canvas, pointer dragging, image normalization, layer stack, tint, and bounded undo/redo history; `wwwroot/css/layers.css` contains its responsive layout. Gestures draw locally without round trips on every pointer move.

Core composition models and persistence live under `StickerApp.Core/Compositions`. The library index stores only `CompositionFileName`; the self-contained source is a separate `.layers.json` media file with normalized PNG layers. Saving publishes a new PNG and source together in one library mutation. Source stickers may be removed without breaking the composition. Resaving removes replaced files after committing. Automatic WebP preparation retains the composition reference. Copying the storage folder includes source files; current pack archive exports include only flattened images. Limits are 10 layers, 8 MB of source data, and 1024 pixels per source image side. GIF layers are still snapshots.

The selected layer exposes corner and edge resize handles plus a rotation handle. Pointer transforms use the initial rotated coordinate space, preserving the opposite anchor and optional proportions. Handles have fixed 44 px touch targets and keyboard arrow support. Selection graphics are excluded from exports. Layer rows and the selected-layer inspector show cached 128 px image previews; source images remain out of UI state updates.

The layer editor footer keeps common actions visible while the canvas, layer stack, and precise settings scroll. Native editing and file persistence need no internet; the connected browser host still needs a working connection to its server (local Wi-Fi is sufficient for a local host). Source-save copy distinguishes native device storage from browser host storage.
