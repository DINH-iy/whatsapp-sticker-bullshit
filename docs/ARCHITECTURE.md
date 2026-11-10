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

The library screen keeps selection state and activation rules in `Home.Selection.cs`. Select mode overlays a full-card checkbox label and makes editor contents inert, including unfinished stickers. Bulk deletion captures a fixed confirmation list, pauses preparation selection, cancels the selected active/queued jobs and removes their library/pack references together.

Razor namespaces match their folders. Shared `_Imports.razor` makes the component groups available to screens and layouts. `Routes.razor` and `Layout` remain the common routing/shell entry points.

`Services` is grouped into `Abstractions`, `Diagnostics`, `Infrastructure`, `Localization`, `Media`, `Packs`, and `Pinterest`. These keep the `StickerApp.Shared.Services` namespace; each host supplies its platform-specific implementations through dependency injection.

## Browser assets

- `wwwroot/css`: shared layout, theme styles, and subtle effects in `motion.css`.
- `wwwroot/js/ui`: dialogs, menus, navigation, appearance, diagnostics and foreground lifecycle.
- `wwwroot/js/imports`: KLIPY, Imgur and Discord import helpers and generated ZIP parsing.
- `wwwroot/js/packs` and `wwwroot/js/friends`: cover generation, sharing, friends API and invite bootstrapping.
- `wwwroot/js/media`: image customization, GIF editing, preparation workers, encoder bundles, WebAssembly and vendor licenses. Related worker files remain together so module-relative imports work in both browser and native WebView hosts.
- `Media`: readable source for bundled GIF and WebP modules. Edit these sources, then run `npm run build --prefix tools/MediaBuild` from the repository root. Install the tool dependencies with `npm ci --ignore-scripts --prefix tools/MediaBuild` when needed. The generated assets are included in the repository; ordinary .NET builds do not require Node.

Both `StickerApp.Web/Components/App.razor` and `StickerApp/wwwroot/index.html` reference the shared styles and startup theme script. Dynamic imports use `_content/StickerApp.Shared/js/...`. Update both hosts when moving shared assets.

## Local checks

Build the browser host with `dotnet build StickerApp.Web/StickerApp.Web.csproj`. Run the relevant feature/storage suites and `node tools/check.mjs` after structural changes. See [the tools guide](../tools/README.md) for suite ownership and browser fixtures. Native Android/iOS compilation and actual phone testing are separate checks; build native apps when requested.

Hosting, synchronization, the friends dashboard and other future features are tracked separately in [IDEAS.md](IDEAS.md).

## Touch feedback

Native hosts register `ITapFeedback` with `NativeTapFeedback`; the browser does not. `TapFeedbackHost` attaches a delegated button-click listener only when that native service exists. `TapFeedbackSettings` appears under General settings; its controls are disabled in the browser with an explanation that native apps are required. Preferences store the enabled flag and intensity on the device (default enabled, 25%). Both the toggle and slider bypass automatic click feedback; enabling or releasing the slider plays one preview.

iOS uses a light impact at bounded intensity. Android uses an 8 ms low-amplitude pulse, respecting the system haptic setting; motors without amplitude control fall back to the system key tap. Tap requests are throttled. No vibration occurs on typing, dragging, hover, or background work.

## Layer compositions

`Components/Editor/Layers/LayerEditorDialog` owns the dialog and save flow. `Pages/Library/Home.Layers.cs` opens it from Add stickers and reopens saved compositions from a sticker’s edit action. `wwwroot/js/media/layers/layer-editor.js` owns the 512 × 512 canvas, pointer dragging, image normalization, layer stack, tint, and bounded undo/redo history; `wwwroot/css/layers.css` contains its responsive layout. Gestures draw locally without round trips on every pointer move.

Core composition models and persistence live under `StickerApp.Core/Compositions`. The library index stores only `CompositionFileName`; the self-contained source is a separate `.layers.json` media file with normalized PNG previews and GIF or edited animated WebP sources. The serialized `GifSource` name remains for existing project compatibility. Saving publishes a PNG (still compositions) or animated WebP and source together in one library mutation. Source stickers may be removed without breaking the composition. Resaving removes replaced files after committing. Automatic WebP preparation retains the composition reference. Copying the storage folder includes source files; current pack archive exports include only flattened images. Limits are 10 layers, 24 MB of encoded source data, and 1024 pixels per source image side. GIF layers use incremental disposal-aware decoding for canvas playback, with per-layer trim ranges. Animated composites are rendered at up to 10 fps in the GIF worker, capped at 10 seconds and 500 KB with adaptive WebP quality. Every animation has at least two frames; still-only compositions receive identical frames through the existing WhatsApp export path. `layer-gifs.js` handles preview readers and timeline thumbnails, and `layer-reorder.js` handles touch/pointer grips and keyboard reordering.

The selected layer exposes corner and edge resize handles plus a rotation handle. Pointer transforms use the initial rotated coordinate space, preserving the opposite anchor and optional proportions. Handles have fixed 44 px touch targets and keyboard arrow support. Selection graphics are excluded from exports. Layer rows and the selected-layer inspector show cached 128 px image previews; source images remain out of UI state updates.

The layer editor footer keeps common actions visible while the canvas, layer stack, and precise settings scroll. Native editing and file persistence need no internet; the connected browser host still needs a working connection to its server (local Wi-Fi is sufficient for a local host). Source-save copy distinguishes native device storage from browser host storage.

`Components/Library/AddStickerChoices` provides the shared Image/GIF/Layered image options in both the header menu and pack add tile. Image/GIF pickers have separate accept filters. `ActionMenu` accepts custom tile trigger content. The layer toolbar and adjustment panels take normal layout space below the canvas; only Save/Cancel are pinned. On phones, the toolbar wraps into rows and rotation uses a compact numeric slider instead of the large dial.

## Shared image and animation editing

`EditorAdjustments` and `EditorAdjustmentValues` provide size, rotation, opacity, and tint controls for both editors. `ImageCustomizer` adds crop, trace, eraser, wand, flip, and raster undo/redo. The layer editor opens that same component for selected artwork and commits it through `replaceSource`; source-keyed image/animation caches preserve artwork through composition undo/redo.

`image-customizer.js` records spatial operations and reference-frame alpha masks. `raster-edits.js` replays them across all animation frames for preview and export. Trace/eraser edits use fixed masks; wand edits flood-fill the reference color independently on each frame. `animation-reader.js` uses the GIF engine or decodes/composites WebP ANMF chunks, including blend/disposal behavior, without requiring WebCodecs. Animation edits preserve retained frame durations; layer exports preserve artwork dimensions, while standalone sticker edits fit to 512 × 512. `sticker-converter.js` supports both canvas sizes when encoding/muxing. `gif-editor.js` creates isolated cancellable sessions for each visible editor, including the main-thread fallback. Background preparation uses a separate shared session.

`tools/EditorChecks/browser/editor-checks.html` exercises the real browser modules with generated fixtures: frame cutouts, adjustable opacity, undo/redo, GIF/WebP export and reopening, layer source round trips, and session cancellation. Serve it from a temporary local host that also serves the shared assets; see the checks README. Persistence checks in `tools/StorageChecks` cover animated WebP composition sources and current-sticker selection over retained backups.

`AnimationTimeline` supplies the shared thumbnail strip, range handles, and compact time readout for ordinary animations and layers. `ImageCustomizer` accepts `Timeline` and `Options` fragments. Its preview region remains separate from the vertically scrolling tool area, so controls cannot cover the canvas or timeline. `HelpDisclosure` keeps optional explanations under native keyboard-accessible details/summary controls, also used by GIF search and Pinterest importing. Labels, loading/error states, and warnings for discarding originals remain visible at their relevant actions.

`animation-preview.js` delegates preview speed and frame seeking for both canvas controllers. `AnimationTimeline` renders the shared scrubber, time readout, and speed selector. Input and playhead updates remain in JavaScript; seek pauses playback, while preview speed is separate from persisted frame timing. Layer metadata reads timeline thumbnails from the source-keyed animation cache before excluding media source data from interop.

`EditorToolDock` places active tool settings beside the canvas in both raster/animation and layer editors. Tool settings use a consistent panel beside the fixed checkerboard; selection tools have no empty panel. Apply/Cancel float at the preview’s bottom right. `ConfirmToolCancel` guards cancellation and repeated activation of the same tool, so repeated taps cannot reset the rollback checkpoint. Range controls use a vertical grip; contextual Apply/Cancel actions commit or restore an editor checkpoint including pixels, animation recipes, and undo/redo state. The rail reserves space beside the artwork rather than covering it.

KLIPY search results expose a one-button Import action. A `createImportSession` JS object owns each browser download and hands a bounded Blob to .NET through `IJSStreamReference`; native hosts use `KlipyGifDownloadClient` with redirects/cookies disabled. Both enforce HTTPS provider hosts and 10 MB, validate GIF data, and reuse `LibraryStore.ImportAsync`, retention defaults, atomic pack membership, and automatic preparation. The search dialog passes the current pack ID and refreshes its parent after import. No credentials or provider result cache are stored in the library.

`image-resize.js` supplies corner/edge handles using the layer handle styles. Each drag redraws from one retained reference bitmap; Accept writes one resize recipe operation, while Cancel restores the tool checkpoint. Proportions can be unlocked. Preview seeking is disabled during a pending resize to keep the reference image stable.

The raster editor uses a fixed 512-unit square presentation frame independent of the artwork bitmap bounds. Sources initially fit within 512 without upscaling; the combined Transform tool moves, resizes, and rotates artwork within that fixed space. `image-transform.js` retains the source bitmap during gestures, provides pointer/keyboard handles, and emits a single 512 × 512 transform recipe operation. Raster replay and animation export preserve the artwork’s offset, size, and angle on every frame. `sticker-converter.fit` only scales down into the final 512 canvas. Contextual settings use one consistent dock and normal app buttons; selection tools have actions without an empty settings card. `color-effect.js` shares detail-preserving hue rotation/strength between live canvas, recipe replay, and composition encoding; legacy `Tint` remains backwards compatible. `dialog.js` locks the document while any nested dialog is open and restores its prior scroll position after the last closes.

### Video import

`VideoImportDialog` owns video selection, preview, frame-rate/clip review and single-sticker imports. `wwwroot/js/media/video` encodes frames directly into animated WebP using a bounded worker pool. Native timing is supported for compatible MP4/MOV files; fixed frame-rate choices remain available. Video input is capped at 20 MB, clips at 5 seconds/100 frames, and final stickers at 500 KB. The worker preference is a total concurrency budget, and foreground loss terminates active workers.

`Components/Pinterest/PinterestAnimationBatchDialog` reviews all staged videos before starting mixed video/GIF conversion. `video-batch.js` allows at most two decoding lanes and shares the total worker allowance across them. All animations must convert and pass validation before the atomic library commit. Retained GIF originals are copied separately; downloaded MP4s remain temporary. Pack covers use `StillPackCover` and `js/packs/pack-cover-still.js` to render a static first frame.

The rotation grip uses the same SVG stroke style as application icons. Its offset is fixed in artwork coordinates and continuously clamped to the workspace; it never chooses a new side while rotating.

### Editable text and custom fonts

`CompositionLayer.Text` is optional metadata on the existing version-1 composition. A cached normalized PNG remains the layer's rendering source, so still and animated exports use the same pixel data without adding font dependencies to GIF workers. `text-layers.js` loads the bundled Anton font with FontFace, wraps text with grapheme-aware fallback, and rasterizes Unicode text, stroke, gradient, decorations, shadow, and background to a bounded canvas. Layer transforms, ordering, duplicate, opacity, history, and save/reopen reuse the normal layer controller. Raster cutout tools are hidden on editable text.

`TextLayerEditor` debounces text changes; the parent flushes pending drafts before saving, selecting, duplicating, or adding layers. Font source bytes are excluded from UI state notifications. Font metadata loads separately; only the newly selected font is streamed through interop. GIF preview/trim controls target the selected animated layer, or the first animated layer while text is selected. The preview stays fixed while the inspector/action controls scroll.

`LibraryStore.Fonts` validates supported font headers and byte bounds, stages a font file before the atomic library-index commit, rolls it back on budget failure, and deduplicates by SHA-256. Browser font decoding validates actual font usability before storage. Font metadata is part of StickerLibrary; font files live in media and therefore follow library relocation. Storage statistics report a separate font total and segment, individual file sizes, and protect referenced fonts from cleanup. Custom font source data embedded in compositions counts against the existing 24 MB composition limit; uploaded registry files count against the library storage cap. Exported sticker packs still contain flattened artwork rather than editable compositions.

`InterfaceFontSettings` reuses the font library for app typography. The startup `theme.js` applies bounded local preferences before rendering and loads the selected custom face from a single-entry IndexedDB cache (up to the font import limit). CSS font sizes and line heights use scale variables, with separate family, weight, and letter spacing preferences. Only UI typography changes; explicit canvas text metrics remain independent. Interface uploads refresh font storage statistics immediately. Library font files remain counted in library storage; browser preference/cache data, like other browser caches, is device-local and outside that library total.

Media JavaScript is grouped by purpose under `wwwroot/js/media/`: `animation`, `image`, `layers`, `effects`, `conversion`, `video`, `shared` and generated `codecs`. See that directory’s README for ownership and build instructions. Codec sources live in `StickerApp.Shared/Media/Codecs`; `tools/MediaBuild/build.cjs` rebuilds the bundled outputs and copies the WASM and license files.

## Library organization and file opening

`LibraryStore.Folders` persists flat library folders independently of WhatsApp packs. A sticker has one optional folder; removing a folder unfiles its contents. Bulk selection moves stickers between folders. Both the library/pack contents and pack overview offer a persistent tile/list preference through `LibraryViewToggle` and `library-view.js`.

The image editor's Fit artwork to maximum action scans alpha bounds across every animation frame, records one shared crop and proportional resize, and retains undo history. GIF/WebP export retries quality first and then lower detail resolution inside the required 512 canvas, preserving duration. Progress messages travel from the codec worker (or main-thread fallback) through the owning job/session to the library card, processing menu and save dialog. Percentages describe the current compression attempt and are paired with the attempt number.

Raster and layered editors offer Save a copy alongside replacement. White meme templates add separate background and editable caption layers above, below or both; artwork is fitted into the remaining canvas. Existing saved templates can be opened and copied; device image templates can be loaded as layers.

Android VIEW intents and iOS document types register the common `.wastickers`, `.wasticker` and `.wassticker` spellings. `PackFileInbox` stages granted file streams with a 100 MB limit and a bounded durable queue. `PackFileReceiver` imports staged archives only through the existing validated archive reader. Browser file pickers intentionally allow all MIME types because Android providers frequently label custom pack extensions as generic binary files; archive validation still runs on import.

## Media and library performance

`frame-clock.js` caches animation timing boundaries and schedules the next visual frame with a timer followed by `requestAnimationFrame`. Standalone and layered previews redraw when the source frame changes. Gestures, text changes, seeking, speed changes and trim updates still redraw immediately. Pause, backgrounding, export and disposal stop pending timers/animation callbacks. Multiple animated layers use the earliest next boundary across the composition.

Library navigation reads metadata without constructing original-media data URLs. `LibraryThumbnail` requests source bytes only near the viewport, with at most two concurrent thumbnail tasks. GIF posters decode the first composited frame; WebP posters decode only the first ANMF frame. Originals are read independently when opening an editor. Static 192px posters use file/version keys, an 8 MiB/128-entry memory cache and an optional 16 MiB/256-entry IndexedDB cache; denied/full storage falls back to uncached rendering. Explicit pack covers use a digest key. Replacement filenames invalidate old posters automatically. `StickerGrid` virtualizes rows above 60 images once the visible set is ready; variable-height preparation/error layouts retain ordinary rendering. Selection still targets the full filtered library, including unmounted cards.

`compression-search.js` probes up to three representative frames, selects a starting quality/detail pass, and then verifies the exact final byte budget. Failed estimates fall through to lower settings without dropping frames or changing duration. Probes reuse at most three normalized frames (12 MiB maximum) and 4 MiB of encoded samples. Short animations and working-source exports skip probing. Successful final encodes have a separate 4 MiB/eight-entry, five-minute memory cache keyed by a SHA-256 digest of the input and conversion settings; failures and working exports are not cached, and no input strings are retained in that result cache. Cancellation is checked during hashing, sampling and encoding. Progress includes sampling/encoding phase and encoder-call counts for diagnostics/tests.

These changes reduce unnecessary rendering, media reads and encoder calls. Physical-device power profiling is still required to establish battery savings; desktop/browser timings and counters are not energy measurements.
