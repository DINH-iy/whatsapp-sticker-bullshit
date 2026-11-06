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

Core composition models and persistence live under `StickerApp.Core/Compositions`. The library index stores only `CompositionFileName`; the self-contained source is a separate `.layers.json` media file with normalized PNG previews and GIF or edited animated WebP sources. The serialized `GifSource` name remains for existing project compatibility. Saving publishes a PNG (still compositions) or animated WebP and source together in one library mutation. Source stickers may be removed without breaking the composition. Resaving removes replaced files after committing. Automatic WebP preparation retains the composition reference. Copying the storage folder includes source files; current pack archive exports include only flattened images. Limits are 10 layers, 24 MB of encoded source data, and 1024 pixels per source image side. GIF layers use incremental disposal-aware decoding for canvas playback, with per-layer trim ranges. Animated composites are rendered at up to 10 fps in the GIF worker, capped at 10 seconds and 500 KB with adaptive WebP quality. Every animation has at least two frames; still-only compositions receive identical frames through the existing WhatsApp export path. `layer-gifs.js` handles preview readers and timeline thumbnails, and `layer-reorder.js` handles touch/pointer grips and keyboard reordering.

The selected layer exposes corner and edge resize handles plus a rotation handle. Pointer transforms use the initial rotated coordinate space, preserving the opposite anchor and optional proportions. Handles have fixed 44 px touch targets and keyboard arrow support. Selection graphics are excluded from exports. Layer rows and the selected-layer inspector show cached 128 px image previews; source images remain out of UI state updates.

The layer editor footer keeps common actions visible while the canvas, layer stack, and precise settings scroll. Native editing and file persistence need no internet; the connected browser host still needs a working connection to its server (local Wi-Fi is sufficient for a local host). Source-save copy distinguishes native device storage from browser host storage.

`Components/Library/AddStickerChoices` provides the shared Image/GIF/Layered image options in both the header menu and pack add tile. Image/GIF pickers have separate accept filters. `ActionMenu` accepts custom tile trigger content. The layer toolbar and adjustment panels take normal layout space below the canvas; only Save/Cancel are pinned. On phones, the toolbar wraps into rows and rotation uses a compact numeric slider instead of the large dial.

## Shared image and animation editing

`EditorAdjustments` and `EditorAdjustmentValues` provide size, rotation, opacity, and tint controls for both editors. `ImageCustomizer` adds crop, trace, eraser, wand, flip, and raster undo/redo. The layer editor opens that same component for selected artwork and commits it through `replaceSource`; source-keyed image/animation caches preserve artwork through composition undo/redo.

`image-customizer.js` records spatial operations and reference-frame alpha masks. `raster-edits.js` replays them across all animation frames for preview and export. Trace/eraser edits use fixed masks; wand edits flood-fill the reference color independently on each frame. `animation-reader.js` uses the GIF engine or decodes/composites WebP ANMF chunks, including blend/disposal behavior, without requiring WebCodecs. Animation edits preserve retained frame durations; layer exports preserve artwork dimensions, while standalone sticker edits fit to 512 × 512. `sticker-converter.js` supports both canvas sizes when encoding/muxing. `gif-editor.js` creates isolated cancellable sessions for each visible editor, including the main-thread fallback. Background preparation uses a separate shared session.

`tools/EditorChecks/editor-checks.html` exercises the real browser modules with generated fixtures: frame cutouts, adjustable opacity, undo/redo, GIF/WebP export and reopening, layer source round trips, and session cancellation. Serve it from a temporary local host that also serves the shared assets; see the checks README. Persistence checks in `tools/StorageChecks` cover animated WebP composition sources and current-sticker selection over retained backups.

`AnimationTimeline` supplies the shared thumbnail strip, range handles, and compact time readout for ordinary animations and layers. `ImageCustomizer` accepts `Timeline` and `Options` fragments. Its preview region remains separate from the vertically scrolling tool area, so controls cannot cover the canvas or timeline. `HelpDisclosure` keeps optional explanations under native keyboard-accessible details/summary controls, also used by GIF search and Pinterest importing. Labels, loading/error states, and warnings for discarding originals remain visible at their relevant actions.

`animation-preview.js` delegates preview speed and frame seeking for both canvas controllers. `AnimationTimeline` renders the shared scrubber, time readout, and speed selector. Input and playhead updates remain in JavaScript; seek pauses playback, while preview speed is separate from persisted frame timing. Layer metadata reads timeline thumbnails from the source-keyed animation cache before excluding media source data from interop.

`EditorToolDock` places the active tool beside the checkerboard canvas in both raster/animation and layer editors. Range controls use a vertical grip; contextual Apply/Cancel actions commit or restore an editor checkpoint including pixels, animation recipes, and undo/redo state. The rail reserves space beside the artwork rather than covering it.
