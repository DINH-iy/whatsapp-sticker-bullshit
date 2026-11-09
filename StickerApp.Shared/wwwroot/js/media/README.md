# Media modules

| Folder | Purpose |
| --- | --- |
| `animation/` | Frame decoding, playback/seek controls and GIF editor sessions |
| `image/` | Image editing and transform/resize handles |
| `layers/` | Layer composition, reordering, animated layers and text rendering |
| `effects/` | Color, cutouts, rectangular borders and object outlines |
| `conversion/` | Sticker preparation, conversion and its worker |
| `video/` | Video loading/review, direct WebP encoding, batch conversion and bounded frame workers |
| `shared/` | Zoom/pan, magnifier and rotation grip shared by editors |
| `codecs/` | Generated codec bundles, codec worker and WebP WASM |
| `codecs/licenses/` | Third-party codec licenses |

Edit codec sources in `StickerApp.Shared/Media/Codecs/`, then run
`node tools/MediaBuild/build.cjs` from the repository root. Do not hand-edit generated
codec bundles. The build resolves app-owned imports to the modules above.

Razor/C# interop imports reference these folders directly. Workers and fonts use
paths relative to their owning module, so update those paths when moving files.
The browser fixtures in `tools/EditorChecks/` exercise both workers and main-thread
fallbacks, as well as image/GIF/layer editing, video capture and custom fonts.
