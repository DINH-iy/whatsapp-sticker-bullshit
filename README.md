# Stik-er-in

A local sticker library and editor with image/GIF editing, layers, direct video-to-WebP conversion, and WhatsApp pack export. The shared Blazor UI runs in a MAUI native app and a browser development host.

## Project layout

| Folder | Purpose |
| --- | --- |
| `StickerApp/` | MAUI host, native integrations, platform resources |
| `StickerApp.Core/` | Library persistence, media inspection, import/export and pack rules |
| `StickerApp.Shared/` | Shared screens, components, services, localization and browser assets |
| `StickerApp.Web/` | Browser development host |
| `StickerApp.ShareExtension/` | iOS share extension |
| `StickerApp.ShareServer/` | Optional pack-sharing service |
| `tools/` | Checks, browser fixtures and media bundle build tools |
| `docs/` | Architecture, Android setup, detailed project notes and future work |
| `deploy/` | Sharing-server deployment configuration |

## Development

Run commands from the repository root. The .NET SDK version is pinned in [global.json](global.json).

```sh
./dev.sh web
```

PowerShell: `./dev.ps1 web`. Native build prerequisites, signing and phone installation are documented in [Android builds](docs/ANDROID_BUILD.md). A successful build checks compilation; running on a phone is a separate validation step.

The browser host stores its development library in `StickerApp.Web/.data/`. Native hosts use their app storage. Build outputs, SDKs, local data and dependency caches are ignored by Git.

## Checks and media bundles

```sh
node tools/check.mjs
dotnet run --project tools/FeatureChecks
dotnet run --project tools/StorageChecks
dotnet run --project tools/FriendsChecks
dotnet build StickerApp.Web/StickerApp.Web.csproj
```

For end-to-end UI coverage and Android file-opening smoke tests, see [the E2E suite](tools/E2E/README.md).

See [the tools guide](tools/README.md) for individual suites and [browser checks](tools/EditorChecks/README.md) for real media conversion, rendering and worker cancellation checks.
The Node suite's ZIP fixtures use the media-tool dependencies installed below.

Readable codec sources live in `StickerApp.Shared/Media/`. Rebuild the checked-in browser bundles only when changing those sources:

```sh
npm ci --ignore-scripts --prefix tools/MediaBuild
npm run build --prefix tools/MediaBuild
```

Ordinary .NET builds use the included bundles and do not require Node dependencies.

## Documentation

- [Architecture and code map](docs/ARCHITECTURE.md)
- [Android setup and builds](docs/ANDROID_BUILD.md)
- [Detailed project guide and development notes](docs/PROJECT_GUIDE.md)
- [KLIPY import notes](docs/KLIPY-IMPORT.md)
- [Localization](StickerApp.Shared/Localization/README.md)
- [Future work](docs/IDEAS.md)
- [Sharing-server deployment](deploy/pi/README.md)
