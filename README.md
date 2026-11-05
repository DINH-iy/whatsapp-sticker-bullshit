# Stick it

C# / Blazor Hybrid app for Android and iPhone/iPad, with a browser development host.

Run the browser version: `.\dev.ps1 web`
Build Android: `./dev.sh android` (macOS/Linux) or `.\dev.ps1 android` (PowerShell). Build the iPhone simulator app with `./dev.sh ios`.

Requires .NET 10. Android additionally requires the MAUI Android workload, SDK platform 36, and a compatible JDK.

Features: import JPEG/PNG/GIF/WebP, rename, replace, rotate and square-crop still images, trim GIFs by start/end time, remove images, manage packs, export compliant WebP packs through Android or iOS.

GIF trimming runs locally in a background worker. Automatic background preparation converts still images and GIFs to 512 × 512 WebP locally, with transparent padding and compression. Animated WebP re-encoding requires the original GIF; video conversion is not implemented. Each browser or installed mobile app has its own local library; use pack import/export to transfer packs between devices. Data is local (JSON index and media files).

WhatsApp export still needs verification on a real phone. This is a local development app, without authentication or direct cloud account integration.

Rebuild the bundled GIF worker only when changing its source: run npm ci --ignore-scripts, then npm run build in tools/gif. Normal app builds use the included bundle.

Settings: open **Settings** in the app navigation to choose Light, Dark, or the system theme. Choose Green, Blue, Purple, Rose, or Amber as the color theme. Appearance and color are remembered independently on the current device/browser and applied before the first paint when the app reopens.

Sticker storage: use **Browse…** to navigate folders in a popup (and optionally create an empty folder), or type a full path in Settings. Either method fills the same field; **Save folder** applies the choice. In the browser host, the popup browses folders on the host computer. The app copies the JSON index and all media before saving the new location; the old library stays intact as a backup. The folder preference is kept in `storage-settings.json` inside the original data directory. If a custom folder is unavailable at startup, reconnect it before launching the app.

For the browser development host, you can use an empty subfolder inside a locally synced OneDrive or Google Drive folder on the host computer. Keep it available offline and use one running app at a time; your cloud client handles syncing and the app does not resolve sync conflicts. Android accepts filesystem paths the app can access; Android document-provider/cloud folders and direct OneDrive/Google Drive sign-in are not implemented.

## GIF search

Open **GIF search** in the navigation, or **Add stickers → Find a GIF** in a pack or the library. The latter opens a popup so you can keep your place. Add your own KLIPY API key in **Settings → GIF search** first. Create a key through the [KLIPY Partner Panel](https://partner.klipy.com/) and configure its allowed app origins/platforms there. A saved key is not validated until you request results. The app supports keyword searches, loading more results, and copying GIF links. It does not load trending GIFs automatically. The search field retains KLIPY’s required provider placeholder; the optional Powered by KLIPY heading is omitted.

The key stays in local storage on the current browser/device. It is not encrypted and is not placed in the sticker library or sent to the Blazor host. Requests go directly from the device to KLIPY. API responses and GIF files are not stored by the app. Results retain the provider's order and media URLs; the API's medium content filter is used. Copying a link sends a share event to KLIPY without a customer identifier.

KLIPY search results are separate from the local sticker library. Standard KLIPY integration rules restrict downloading, caching, and re-hosting media. This integration shares links; importing those GIFs for editing/export would require a separate agreement with KLIPY. See [KLIPY's integration requirements](https://docs.klipy.com/integration-requirements) and [API documentation](https://docs.klipy.com/). Tenor's public API was retired in June 2026.

## Finding your way around the code

- `StickerApp.Shared/Pages/Home.razor` contains the library screen markup; `Home.razor.cs` contains importing, pack actions, and image editing.
- `StickerApp.Shared/Pages/Settings.razor` and `Settings.razor.cs` contain appearance and storage settings.
- `StickerApp.Shared/Pages/GifSearch.razor` and `GifSearch.razor.cs` contain the GIF browsing screen and its state.
- `StickerApp.Shared/Components/GifSearchSettings.razor` and its `.razor.cs` file handle API-key setup; `FolderPicker.razor` and its `.razor.cs` file handle folder browsing.
- `StickerApp.Shared/wwwroot/klipy-client.js` handles direct KLIPY requests, provider responses, and the device-local key. It contains no server proxy or media download code.
- `StickerApp.Shared/wwwroot/theme.js` loads and saves appearance preferences. `themes.css` defines shared color tokens and palettes; `app.css` contains the screen styles.
- `StickerApp.Core/LibraryStore.cs` handles file persistence and copying the library to a new save folder.
- `StickerApp.Web/Program.cs` and `StickerApp/MauiProgram.cs` wire up the browser host and native Android/iOS app respectively.

App source uses four-space indentation and named methods. Screen markup and C# behavior live in adjacent files; `.editorconfig` records the formatting conventions. Bundled vendor files and the generated GIF worker are intentionally left as supplied.

## Location surprises and controls

**Settings → Easter eggs** contains an optional **Location surprises** toggle, off by default. When enabled, the device asks [country.is](https://country.is/) for the country of its public IP. Romania (`RO`) triggers a dismissible Romania roast and a brief bat animation once per session. The setting persists in local storage; only the country code and lookup time are cached in session storage for up to 30 minutes. No IP address or GPS location is saved by the app. A VPN can affect the country; failed lookups silently skip the surprise. The **Preview Romania surprise** button works anywhere without making a location request.

`Components/LocationSurprise.razor` and its code-behind render the surprise. `Components/SurpriseSettings.razor` and its code-behind handle the toggle and preview. `wwwroot/location-surprises.js` owns device-local preferences, session checks, and direct IP-country lookup. Add more country effects there and expose a corresponding named callback on the component.

Dark themes share neutral charcoal backgrounds across all five accents. Primary buttons and highlights carry the selected accent. Shared controls use a 44px height and 8px corner radius; preview canvases, checkboxes, and sliders retain dimensions appropriate to their purpose. Compact library actions use local SVG icons from `Components/Icon.razor`, with accessible labels and hover titles.

## Quickly saving GIFs and adding images to packs

Every GIF search result has a download-shaped **Open original GIF to save** link. It opens the provider's original URL in a new tab. This is not an automatic download: use the browser's Save Image option, then import the saved file through **Add stickers → Images or GIFs from device**. Cross-origin links cannot reliably force a file download, and the app does not fetch or re-host provider media.

Once an image is in the local library, its folder-plus **Add to pack** action opens a picker of all packs. Choose an existing pack, or enter a name and use **Create & add**. Creating a pack and adding the image happen in a single atomic write. Already-added images and full packs are disabled in the picker. This works for imported GIFs and still images; WhatsApp export still requires compliant WebP files.

The picker lives in `Components/AddToPackDialog.razor` and its code-behind. Both folder browsing and pack picking use `wwwroot/dialog.js` to open an accessible native dialog with browser focus management.

## Pack import, export, and sharing links

Select a pack and use the share icon to open **Share pack**. The default `.wastickers` export uses root-level WebP images, a 96 × 96 PNG cover, `title.txt`, and `author.txt`, matching the supplied sticker-maker sample. GIF/PNG/JPEG sources are prepared automatically; check the sticker info button for status. `.wastickers` import accepts the sample's leading slashes without extracting archive paths. Other apps may vary in their import behavior; physical-device interoperability still needs checking.

Choose **Editable backup (.zip)** to preserve image order, names, tags, cover, and current image bytes in this app's manifest format. Original pre-edit backups, settings, paths, and API tokens are excluded. Both formats can be published to the sharing server, which retains their proper download extension.

**Import pack** on the overview, or **Add stickers → Import a pack file** inside a pack/library, opens a shared pack archive. Select a .wastickers pack or ZIP backup to create a new pack with new image IDs. Existing images and packs are never overwritten. Import validates image formats and archive bounds; editable ZIP imports also verify the manifest and SHA-256 checksums before committing the images and pack together. Limits are 30 images, 10 MB per image, and 100 MB per archive/unpacked contents. The existing limit of 10 packs also applies to imports.

The editable backup is a normal ZIP with a versioned `manifest.json` and ordered `media/00.gif`, `media/01.webp`, etc. Each manifest image has a SHA-256 checksum. The whole ZIP's SHA-256 appears in the sharing dialog. Exports use fixed timestamps, so unchanged pack contents produce the same fingerprint. A hash identifies bytes; it does not contain the images, encrypt them, or authenticate their creator.

For links, configure your own server in **Settings → Pack sharing**, then use **Create share link** in the pack's sharing dialog. This uploads only the selected pack. Anyone with the generated link can open a download page, save the pack, and import it. Links are immutable snapshots; later edits produce a new link. File sharing works without a server.

`StickerApp.ShareServer` is a separate self-hosted service with authenticated uploads/deletion and public, hash-addressed download links. The server defaults to a 2 GiB archive quota and two concurrent uploads. [Raspberry Pi and domain setup instructions](deploy/pi/README.md) include Docker Compose, Caddy HTTPS, persistent volumes, token setup, and link removal. It can also run on any host with the .NET 10 ASP.NET runtime. The browser development host is not the public sharing server.

`PackArchive.cs` defines the portable format. `LibraryStore.cs` performs atomic library import/export. `PackSharingService.cs` prepares temporary archives and calls the host-specific file saver or device-side publishing client. `SharePackDialog` and `PackSharingSettings` provide the UI. Android restricts its file provider to the dedicated `sharing-root` cache directory.

## iPhone and iPad

The native project targets `net10.0-android` and `net10.0-ios`. Xcode supplies Apple's compiler and SDK; this is a MAUI project, so build the `.csproj` with .NET rather than looking for an `.xcodeproj`.

Use .NET SDK 10.0.402 and the pinned workload set:

```sh
dotnet workload install maui-ios maui-android --version 10.0.402.1
dotnet build StickerApp/StickerApp.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64
```

This workload requires **Xcode 27.1**. On this Mac, Xcode 27.2 is installed, and a normal build stops at the SDK compatibility check. Install Xcode 27.1 alongside it from Apple Developer Downloads, then select that copy for the build, for example `DEVELOPER_DIR=/Applications/Xcode_27.1.app/Contents/Developer dotnet build ...`. Do not permanently disable the version check. A one-time diagnostic build with `ValidateXcodeVersion=false` produced a signed simulator app with zero compiler warnings. It installed and launched in an iPhone 18 Pro simulator on iOS 27.2; the library, settings, and dark appearance rendered successfully. An unsigned ARM64 device build also completed; installing it still requires your Apple signing identity. That does not make Xcode 27.2 an officially supported toolchain.

Start an iPhone simulator in Xcode's Device Hub. Install the simulator `.app` from `StickerApp/bin/Debug/net10.0-ios/iossimulator-arm64/StickerApp.app` using Device Hub's Info → Apps → Add, or `xcrun simctl install booted <app-path>`. Launch **Stickers** on the simulator. Intel Macs should use `iossimulator-x64` instead.

For a real iPhone, add your Apple account/development team in Xcode, connect and trust the phone, and enable Developer Mode. Build for `ios-arm64` using an Apple Development signing identity and a development provisioning profile covering `com.stickerstudio.app` (or change ApplicationId to an identifier registered to your team). No team ID, certificate, or provisioning profile is committed here. The Mac currently has no valid code-signing identities. Simulator builds do not require an Apple team.

The shared UI, local library, appearance preferences, file import, editor, GIF search, and portable pack sharing are used on both mobile platforms. iOS has a scene delegate and generated icon/splash assets, safe-area spacing, photo/camera purpose strings, and a WhatsApp URL query declaration. `Platforms/iOS/IosWhatsAppExporter.cs` sends a compliant WebP pack via WhatsApp's documented expiring, device-local pasteboard payload and URL scheme. A real iPhone with WhatsApp is required to verify that final handoff; WhatsApp is not available in the simulator. Automatic preparation uses the same bundled WebAssembly encoder on iPhone and other platforms.

On iOS, keep the library inside the app sandbox. You can import pack ZIPs through the file picker and share exports to Files, AirDrop, or a cloud drive through the system share sheet. The live library is not automatically synced to iCloud, OneDrive, Google Drive, or another phone. Uninstalling the app removes local data unless you exported it first.

Android builds must select their target explicitly: `dotnet build StickerApp/StickerApp.csproj -f net10.0-android`. They also need the Android SDK and JDK, which are not currently installed on this Mac. `dev.ps1 android` now selects that target; `dev.ps1 ios` builds the Apple Silicon simulator target.

When viewing a pack, the first grid cell is always **Add sticker**, even when the pack is empty or a search has no matches. Click it to choose images or GIFs from the device; files are imported directly into that pack in the same index write. Full packs disable the tile, and invalid files report an error without leaving unused images in the library. **Add stickers → Images or GIFs from device** in the all-images view imports into the library. **Import pack** creates a separate pack from an exported archive.

## Pack overview and covers

**Packs** opens the overview at `/`. Its first tile creates a new pack; each remaining card opens `/packs/{id}`. **All images** opens the separate library at `/library`. Pack names appear as page headings. **Rename** opens a field with explicit Save/Cancel controls. The pack dropdown has been removed.

Use **Set cover** on a pack card or inside the pack. Covers are cropped to a square and resized on the device before saving. The original is not added as a sticker; the cover does not count toward the 30-sticker limit. Covers persist in the library, travel with exported pack ZIPs and share links, and supply the native WhatsApp tray icon. Without a custom cover, the overview shows the first sticker. Remove a custom cover inside the pack to return to that fallback.

`Components/AddTile.razor` reuses the add-tile appearance for New pack and Add sticker. `PackCoverPicker` and `wwwroot/pack-cover.js` handle device-side cover preparation. `PackCover.cs` checks the saved/imported cover format and size.

WhatsApp problems appear in red only for packs with validation errors. The full rules and usage FAQ live in **Settings → Help & FAQ**. Success messages use green, errors use red, and informational status uses blue, independently of the chosen accent and light/dark appearance.

The shared navigation has browser-history Back/Forward buttons. Pack, library, GIF search, and Settings routes use that same history. Visited links retain the selected theme colors. The pack toolbar groups image sources under **Add stickers**, with descriptions of each source. **Pack options** contains rename, cover, share/export, and delete actions. Menus float over the content and close when you choose an action, tap outside, or press Escape. The toolbar uses equal gaps and a proper WhatsApp brand silhouette from [Simple Icons (CC0)](https://github.com/simple-icons/simple-icons).

GIF editing is labeled **Edit GIF**. Timeline handles and start/end number fields use invariant decimal formatting, so a decimal duration such as 4.7 seconds is not silently replaced by the browser's midpoint under another locale. Start/end fields, timeline masks, selected duration, and saved trim use the same values. **Preview trim** regenerates the animation before saving; Reset trim restores the original range.

Pack creation needs only a name; still and animated images can share a pack. Animation metadata is inferred from its contents. All pack tools sit in the heading, with Add to WhatsApp fixed at the bottom right. Validation lists only current problems, includes actual dimensions and size limits, and shows automatic preparation status through the sticker info button, with Edit / trim for images needing attention. Preparation preserves the first imported original, never crops or stretches the image, and never silently trims animation. Very detailed GIFs may need a shorter selection to fit 500 KB. The readable encoder wrapper and GIF worker source live in `StickerApp.Shared/Media`; rebuild their generated assets with `npm run build` in `tools/gif`. WebP encoding uses Apache-2.0 jSquash/libwebp, with its license included beside the bundled assets.

### Mixing still pictures and GIFs

Keep both in the same app pack. Library images, editing, and previews retain their original still/animated behavior. Automatic background preparation fits each image into 512 × 512 WebP as before. When exporting a `.wastickers` pack or handing off through Android, every sticker uses animated WebP. Still pictures are wrapped as two identical 500 ms frames without re-encoding the artwork; they remain visually still. Existing multi-frame animations retain their exact bytes. Older one-frame wrappers are repaired into two identical frames. The export copy is checked against the 500 KB, 10-second, and minimum 8 ms frame limits. Editable ZIP backups keep the library files unchanged.

WhatsApp's [sample validator](https://github.com/WhatsApp/stickers/blob/main/Android/app/src/main/java/com/example/samplestickerapp/StickerPackValidator.java) requires more than one frame for each sticker in an animated pack. An animation flag or a one-frame wrapper is insufficient. This change is verified through local export checks; actual WhatsApp acceptance still needs a phone. The native iOS exporter has not been updated or tested in this pass.

## Pinterest selection import

Open **Pinterest** in the navigation, or **Pinterest import** inside a pack to preselect that destination. In Pinterest, select several pins on a public board and share the selection as one link. Paste either the link or Pinterest's shared text, choose **Preview images**, select the images you want, and import into **All images**, an existing pack, or **Create new pack…**. For a new pack, enter its name and choose **Create pack & import**. The pack and its images are saved together after every download succeeds. Each pack holds at most 30 stickers; the preview shows remaining capacity. Imports retain the original image format. WhatsApp preparation starts automatically after import.

The app calls the separate [pinterest-link-extractor](https://github.com/DINH-iy/pinterest-link-extractor) service at `https://stickers.dinnyvanhuizen.com/api/pinterest/import`. No Pinterest login, client ID, secret, or backend settings are needed. The old OAuth settings and sharing-server routes have been removed; pack publishing remains independent. The Pi returns metadata and image URLs; the app downloads the confirmed images directly from `i.pinimg.com`. In the browser development host these requests and library writes run on the host computer.

Only public `https://pin.it/...` and `https://www.pinterest.com/multi-pin-share/...` selection links are supported, with at most 100 images per selection. Whole boards, individual-pin links, private content, videos, and carousel expansion are not supported. Extraction depends on Pinterest's public page structure. A healthy `/health` response checks the service process; a real import also checks Chromium and Pinterest access.

The client rejects incomplete selections, repeated pin IDs, and image URLs outside the HTTPS Pinterest image CDN. Redirects are disabled. Metadata is bounded to 512 KiB and each image to 10 MiB, including responses without a Content-Length header. Preview requests time out after 90 seconds; the complete batch has a 10-minute deadline. Downloads go into a unique temporary directory. All selected images and pack membership commit together under the library lock. Failed/cancelled downloads and failed validation leave the library unchanged, and temporary downloads are removed. Cancellation is honored before the atomic commit; a commit already in progress finishes normally.

### iPhone share sheet

1. Share a Pinterest selection and choose **Stickers** (enable it under **More** if needed).
2. Tap **Save link for Stickers**.
3. Open the main **Stickers** app to preview the images, select them, and choose the destination.

The extension saves links in the shared App Group container; it does not download images. Apple does not provide a supported way for a Share Extension to launch its containing app. The main app checks on startup and when resumed. Saved links survive app restarts and remain queued until successfully imported or explicitly discarded. Up to 100 links can be queued, and **Check saved links** opens the next one. Android and the browser support pasted links; Android share-sheet receiving is not implemented.

The extension is a separate .NET iOS library, `StickerApp.ShareExtension`, embedded through an iOS-only project reference. Its principal controller is registered with Objective-C as `PinterestShareViewController`. Both targets declare the App Group in their entitlement files. Enable these identifiers and capabilities in Apple Developer and regenerate the development/distribution profiles:

| Target | Bundle identifier | App Group |
| --- | --- | --- |
| Main app | `com.stickerstudio.app` | `group.com.stickerstudio.app` |
| Share Extension | `com.stickerstudio.app.pinterestshare` | `group.com.stickerstudio.app` |

If changing bundle identifiers, keep the extension's identifier prefixed by the main app's identifier, update both entitlements, and update `PinterestShareLink.AppGroup`. Both targets must use the same Apple team; each device target needs its own matching provisioning profile. Use the matching Xcode version described above. Build the main project to build and embed the extension:

```sh
dotnet build StickerApp/StickerApp.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64
```

On an iPhone, verify that the built app contains `PlugIns/StickerApp.ShareExtension.appex`, that sharing works with Stickers closed and backgrounded, that saved links survive an app restart, and that preview, deselection, cancellation, and nearly full packs work. A simulator build or source compile does not verify device provisioning or Pinterest's iOS share payload.

### Local builds

```sh
dotnet build StickerApp.Web/StickerApp.Web.csproj
dotnet build StickerApp.ShareServer/StickerApp.ShareServer.csproj
```

### Image customization

Open a still image’s Edit button for a touch-friendly canvas. Drag a rectangle and Apply crop, trace a shape and Keep traced area to make the outside transparent, erase with a brush, or tap a background with Magic wand to remove a connected region of similar color. Color tolerance controls the wand. Rotate, flip, resize with locked proportions, or unlock proportions to stretch. Undo restores the last eight actions, and Reset restores the image as opened. Changes stay in the editor until Save; Cancel keeps the library unchanged. Cutouts use transparent PNG before automatic WebP preparation. Large sources are edited at up to 2048 pixels on the longest side; explicit resizing supports up to 4096 pixels per dimension. Animated images retain their GIF trimming tools and are never flattened by the still-image editor.
