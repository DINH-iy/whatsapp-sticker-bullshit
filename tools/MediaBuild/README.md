# Media bundle build

This is build tooling, not application runtime code. From the repository root:

```sh
npm ci --ignore-scripts --prefix tools/MediaBuild
npm run build --prefix tools/MediaBuild
```

| Readable source | Generated runtime output |
| --- | --- |
| `StickerApp.Shared/Media/Codecs/` | `StickerApp.Shared/wwwroot/js/media/codecs/` |
| `StickerApp.Shared/Media/Imports/` | `StickerApp.Shared/wwwroot/js/imports/vendor/` |

`build.cjs` bundles codec dependencies and copies the WebP encoder WASM and third-party licenses. Application-owned media modules remain shared runtime imports. Generated files are checked in so ordinary .NET builds do not need Node tooling.

The legacy video-to-GIF encoder is retained for conversion comparison fixtures. Production video import encodes directly to WebP.
