# Stik-er-in

C# / Blazor Hybrid app for Android and iPhone/iPad, with a browser development host.

build apk: dotnet publish StickerApp/StickerApp.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=apk
dotnet build StickerApp/StickerApp.csproj \
  -t:InstallAndroidDependencies \
  -f net10.0-android \
  -p:AndroidSdkDirectory="$HOME/Library/Android/sdk" \
  -p:JavaSdkDirectory="$HOME/Library/Java/maui-jdk" \
  -p:AcceptAndroidSDKLicenses=True
Run the browser version: `.\dev.ps1 web`

Open `http://localhost:5100` for local desktop use. In VS Code, install the recommended C# extension, select **Stik-er-in (localhost)** in **Run and Debug**, and press **F5** to build, launch, and open the app with debugging. **Ctrl+F5** launches without debugging. Stop an already-running terminal server with **Ctrl+C** before using the editor launcher so port 5100 is available.
Build Android: `./dev.sh android` (macOS/Linux) or `.\dev.ps1 android` (PowerShell). Build the iPhone simulator app with `./dev.sh ios`.

Requires .NET 10. Android additionally requires the MAUI Android workload, SDK platform 36, and a compatible JDK.

Features: import JPEG/PNG/GIF/WebP, rename, replace, crop, cut out, erase, rotate, resize, recolor, and adjust opacity on still images and GIF/animated WebP stickers; trim animations by start/end time, remove images, manage packs, export compliant WebP packs through Android or iOS.

GIF trimming runs locally in a background worker. Automatic background preparation converts still images and GIFs to 512 × 512 WebP locally, with transparent padding and compression. Animated WebP can be edited and re-encoded directly, including stickers imported from packs; video conversion is not implemented. Each installed mobile app has its own local library; the browser version uses the host computer’s library; use pack import/export to transfer packs between devices. Data is local (JSON index and media files).

WhatsApp export still needs verification on a real phone. This is a local development app, without authentication or direct cloud account integration.

Rebuild the bundled GIF worker only when changing its source: run npm ci --ignore-scripts, then npm run build in tools/gif. Normal app builds use the included bundle.

Settings: open **Settings** in the app navigation to choose Light, Dark, or the system theme. Choose Green, Blue, Purple, Rose, or Amber as the color theme. Appearance and color are remembered independently on the current device/browser and applied before the first paint when the app reopens.

**Settings → General → Library statistics & storage** shows image/animation and pack counts, actual file usage, originals, editable layers, unused files, and the library index. Each saved pack has a colored storage bar and separate image/source sizes. Shared images count once in the library total and once in each pack that uses them; pack bars compare with the largest pack. Totals exclude logs, downloaded exports, and old library folders.

The optional **Library storage limit** sets an app-managed budget from 1 MiB to 1 TiB. Saves that grow the final library beyond it are rejected; cleanup and smaller replacements remain allowed if already above the limit. Staged imports/conversion files can temporarily use more space. This is not an OS disk reservation, and it cannot guarantee free space on iOS, Android, or desktop. The shared library store enforces it for imports, edits, compositions, and pack imports. Settings are local to the host/device and survive changing the library folder.

When adding device images or GIFs, a review dialog offers **Keep original** for each file before importing. Folder selections use the same review; Pinterest previews have per-image checkboxes. The choices are saved before automatic preparation starts. **Always use my Settings defaults** skips future choices and applies your image/GIF defaults; turn it off in Settings → General → Keep original files to ask again. Still-image sources are off by default; animation sources are on by default to preserve the full original. Canceling the review saves nothing, and you can select the same files again. Pack archives contain their saved sticker images and use the configured defaults if further preparation is needed.

Each image editor also has **Keep the source image / GIF after saving** for trims, replacements, and edits. Turning it off keeps the smaller sticker after preparation succeeds and removes the source and superseded edits. The warning explains that the previous image/uncut animation cannot be recovered without your own saved copy, while prepared animated WebP stickers can still be trimmed and edited. Failed preparation keeps a recovery source. Editable layers keep their embedded source media independently.

The total storage overview offers **Bar** and **Donut** views with matching colors and a shared legend. The remembered preference survives reloads and library relocation. Both views count each file once and show remaining library budget when a limit is enabled. Individual pack bars remain inside the collapsed **Pack details** section. Stat cards use small local SVG icons and a subtle total-size accent without animated effects. The MiB/MB selector converts display units and limit input values without changing the saved byte budget.

**Find duplicates & unused files** hashes current files plus any retained originals and editable compositions. Only exact matches with matching sources are offered; visually similar encodings and different originals remain separate. Review previews and choose which copy/name to keep. Removing reviewed duplicates merges pack references, favorites, and tags, without repeating an image within a pack. Separate reviewed actions remove retained originals from ready stickers or app-generated unreferenced media. Existing originals are not bulk-deleted by changing a preference. Cleanup rechecks references under the library lock, and originals needed by unfinished preparation or editable layers are protected. No cleanup runs automatically on opening statistics.

Run the isolated persistence checks with `.\.tools\dotnet\dotnet.exe run --project tools/StorageChecks/StorageChecks.csproj` on this Windows checkout, or `dotnet run --project tools/StorageChecks/StorageChecks.csproj` with a matching SDK. They use temporary test libraries rather than personal media.

Sticker storage: use **Browse…** to navigate folders in a popup (and optionally create an empty folder), or type a full path in Settings. Either method fills the same field; **Save folder** applies the choice. In the browser host, the popup browses folders on the host computer. The app copies the JSON index and all media before saving the new location; the old library stays intact as a backup. The folder preference is kept in `storage-settings.json` inside the original data directory. If a custom folder is unavailable at startup, reconnect it before launching the app.

For the browser development host, you can use an empty subfolder inside a locally synced OneDrive or Google Drive folder on the host computer. Keep it available offline and use one running app at a time; your cloud client handles syncing and the app does not resolve sync conflicts. Android accepts filesystem paths the app can access; Android document-provider/cloud folders and direct OneDrive/Google Drive sign-in are not implemented.

## Layer editor

In a pack or All images, open **Add stickers → Layered image**. Add up to ten device or library images, drag them into place, resize with corner/edge handles, rotate with the round arrow handle, and adjust proportions, opacity, order, flip, or color tint. Quick actions sit below the canvas, with tools wrapping into rows on phones. Adjustment panels take their own space below the tools; only Save and Cancel stay pinned. Animation timelines sit directly beneath the canvas. Longer explanations are under expandable help. Save creates a transparent 512 × 512 sticker; its pencil button reopens the editable composition. GIF and animated WebP layers retain their animation, with a trim timeline beneath the selected layer. **Edit image** opens the same crop, trace cutout, eraser, magic wand, size, rotation, opacity, and color controls as ordinary sticker editing. Apply to layer commits the artwork change, which composition Undo/Redo can restore. Drag the grip beside each layer to reorder it. Animated compositions save as WhatsApp-ready animated WebP; still-only compositions stay visually still and receive identical frames during WhatsApp export.

Editable sources stay in the local library as self-contained `.layers.json` files, separate from the automatically prepared sticker. Source images can be removed without damaging saved layers. Pack exports contain the flattened image only; transferring editable compositions remains on the ideas list.

## GIF search

Open **GIF search** in the navigation, or **Add stickers → Find a GIF** in a pack or the library. The latter opens a popup so you can keep your place. Add your own KLIPY API key in **Settings → GIF search** first. Create a key through the [KLIPY Partner Panel](https://partner.klipy.com/) and configure its allowed app origins/platforms there. A saved key is not validated until you request results. The app supports keyword searches, loading more results, opening GIFs in the browser, and one-button GIF import. It does not load trending GIFs automatically. The search field retains KLIPY’s required provider placeholder; the optional Powered by KLIPY heading is omitted.

The key stays in local storage on the current browser/device. It is not encrypted and is not placed in the sticker library or sent to the Blazor host. Requests go directly from the device to KLIPY. Search API responses are not retained. A GIF is saved in the local library only when you choose Import. Results retain the provider's order and media URLs; the API's medium content filter is used. Copying a link sends a share event to KLIPY without a customer identifier.

KLIPY search results stay separate from the local sticker library until you choose Import. That button downloads the original directly on the device, validates the GIF and 10 MB limit, and imports it using the existing GIF retention defaults. Opening search from a pack imports into that pack. A completion message links to your images; the same result is marked Imported to prevent accidental repeated clicks. Closing or cancelling during download leaves the library unchanged; an atomic save already in progress completes. Native apps download through a bounded HTTP client on the device; browsers use direct JavaScript streaming and report CORS/network failures without a server proxy. Standard KLIPY integration rules require approval for retaining media and custom delivery; app operators should arrange the appropriate agreement for their use. See [KLIPY's integration requirements](https://docs.klipy.com/integration-requirements) and [API documentation](https://docs.klipy.com/). Tenor's public API was retired in June 2026.

## Finding your way around the code

See [the code map](docs/ARCHITECTURE.md) for the feature folders and asset build pipeline.

- `StickerApp.Shared/Pages/Library/Home.razor` contains the library screen markup; `Home.razor.cs` holds screen state and pack actions, with importing in `Home.Importing.cs` and editing in `Home.Editing.cs`.
- `StickerApp.Shared/Pages/Settings/Settings.razor` and `Settings.razor.cs` contain appearance and storage settings.
- `StickerApp.Shared/Pages/Gifs/GifSearch.razor` and `GifSearch.razor.cs` contain the GIF browsing screen and its state.
- `StickerApp.Shared/Components/Settings/GifSearchSettings.razor` and its `.razor.cs` file handle API-key setup; `FolderPicker.razor` and its `.razor.cs` file handle folder browsing.
- `StickerApp.Shared/wwwroot/js/ui/klipy-client.js` handles direct KLIPY requests, provider responses, and the device-local key. It includes isolated, cancellable browser download sessions; native downloads use `KlipyGifDownloadClient`. Neither path uses a server proxy.
- `StickerApp.Shared/wwwroot/js/ui/theme.js` loads and saves appearance preferences. `themes.css` defines shared color tokens and palettes; `app.css` contains the screen styles.
- `StickerApp.Core/Library/LibraryStore.cs` handles file persistence and copying the library to a new save folder.
- `StickerApp.Web/Program.cs` and `StickerApp/MauiProgram.cs` wire up the browser host and native Android/iOS app respectively.

App source uses four-space indentation and named methods. Screen markup and C# behavior live in adjacent files; `.editorconfig` records the formatting conventions. Bundled vendor files and the generated GIF worker are intentionally left as supplied.

## Location surprises and controls

**Settings → Easter eggs** contains an optional **Location surprises** toggle, off by default. When enabled, the device asks [country.is](https://country.is/) for the country of its public IP. Romania (`RO`) triggers a dismissible Romania roast and a brief bat animation once per session. The setting persists in local storage; only the country code and lookup time are cached in session storage for up to 30 minutes. No IP address or GPS location is saved by the app. A VPN can affect the country; failed lookups silently skip the surprise. The **Preview Romania surprise** button works anywhere without making a location request.

`Components/Surprises/LocationSurprise.razor` and its code-behind render the surprise. `Components/Settings/SurpriseSettings.razor` and its code-behind handle the toggle and preview. `wwwroot/js/ui/location-surprises.js` owns device-local preferences, session checks, and direct IP-country lookup. Add more country effects there and expose a corresponding named callback on the component.

Dark themes share neutral charcoal backgrounds across all five accents. Primary buttons and highlights carry the selected accent. Shared controls use a 44px height and 8px corner radius; preview canvases, checkboxes, and sliders retain dimensions appropriate to their purpose. Compact library actions use local SVG icons from `Components/Common/Icon.razor`, with accessible labels and hover titles.

## Quickly saving GIFs and adding images to packs

Every GIF search result has an **Import** button and an **Open in browser** link. Import saves the original to All images, then opens a pack picker: choose an existing pack, create one, or use Keep in library. Closing the picker leaves the imported GIF in the library. The browser link opens the provider's original URL in a new tab.

Once an image is in the local library, its folder-plus **Add to pack** action opens a picker of all packs. Choose an existing pack, or enter a name and use **Create & add**. Creating a pack and adding the image happen in a single atomic write. Already-added images and full packs are disabled in the picker. This works for imported GIFs and still images; WhatsApp export still requires compliant WebP files.

The picker lives in `Components/Packs/AddToPackDialog.razor` and its code-behind. Both folder browsing and pack picking use `wwwroot/js/ui/dialog.js` to open an accessible native dialog with browser focus management.

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

When viewing a pack, the first grid cell is always **Add sticker**, even when the pack is empty or a search has no matches. Click it for Image from device, GIF from device, or Layered image; files are imported directly into that pack in the same index write. Full packs disable the tile, and invalid files report an error without leaving unused images in the library. **Add stickers → Image from device / GIF from device** in the all-images view imports into the library. Each file picker filters the matching formats. **Import pack** creates a separate pack from an exported archive.

## Pack overview and covers

**Packs** opens the overview at `/`. Its first tile creates a new pack; each remaining card opens `/packs/{id}`. **All images** opens the separate library at `/library`. Pack names and publishers appear on their cards and pack headings. The pencil button on a pack card, or **Pack options → Edit pack details**, edits its name and publisher together with explicit Save/Cancel controls. Both fields accept 1–128 characters; publishers must fit on a single line. Publisher changes update the pack version and carry through editable ZIP backups, `.wastickers` author metadata, and native WhatsApp exports. New-pack, quick-add, and Pinterest create-pack flows all offer a publisher field.

Both browsing screens have **Search** and compact **Sort** / **Filters** buttons that open popups. Choose the order or filters, then use Done or Escape to close; active options are highlighted on their buttons. Packs search by name/publisher and filter by still, animated, mixed, or empty contents, sticker count, and total stored size. Sort by name, publisher, addition order, sticker count, current image-file bytes, or total stored bytes. All images and individual pack views search names/tags, filter by still/animated/editable-layer type, current file format, and stored-size ranges, and sort by name, type, current file bytes, or total stored bytes. Prepared GIFs remain under **GIFs / animations** even after converting to WebP. All-images views also offer recent/oldest addition order; individual pack views default to their saved sticker order. Filtering and sorting only affect the view and never rewrite pack order or exports. Dimensions remain available in sticker details rather than filtering controls. Empty filtered pack views show a compact clear-filters/new-pack row instead of a large standalone creation tile.

Cards show current dimensions/type and file/stored sizes. Stored size includes each image's current file, retained original, and editable composition, with each file counted once per image/pack. A shared image appears in each pack that contains it; the overall library statistics deduplicate shared files. Browsing measurements exclude library-index metadata, unrelated unused files, logs, and exports. Sizes and filter thresholds follow the MiB/MB preference. Reset filters clears search and filters while keeping the selected sort.

The storage card keeps chosen quick counters, the bar/donut, and its legend visible. **Customize stats** opens Library details and exposes drag handles, pin/hide buttons, and move-earlier/later buttons. Drag cards between Library details and Quick overview to choose them; drag within Quick overview to reorder. Mouse and touch dragging support edge scrolling. Preferences are saved in local storage on this browser/device; Refresh updates values without resetting the layout. Users can hide every quick stat or use Reset defaults to restore Images, Packs, Total used, and Unused files. All underlying metrics remain in Library details. Pack details, units, the storage limit, source defaults, and cleanup expand separately and are stacked vertically at every screen width. Removal buttons use a soft red background and border, and pack-cover/pencil actions have consistent widths and spacing.

Imported animated WebP files can specify a finite loop count, making them appear frozen after playing once. Library, pack-cover fallback, and editor previews now loop a display-only copy. The saved bytes, frame payloads, timing, and exported files are unchanged. Some pack stickers intentionally contain identical repeated frames and look still. Source GIFs are not included in `.wastickers` archives; their animated WebP stickers support the same trimming and cutout tools directly. Verify a supplied pack without touching your library by passing its path after `--` to the `tools/StorageChecks` command.

Use **Set cover** on a pack card or inside the pack. Covers are cropped to a square and resized on the device before saving. The original is not added as a sticker; the cover does not count toward the 30-sticker limit. Covers persist in the library, travel with exported pack ZIPs and share links, and supply the native WhatsApp tray icon. Without a custom cover, the overview shows the first sticker. Remove a custom cover inside the pack to return to that fallback.

`Components/Common/AddTile.razor` reuses the add-tile appearance for New pack and Add sticker. `PackCoverPicker` and `wwwroot/js/ui/pack-cover.js` handle device-side cover preparation. `PackCover.cs` checks the saved/imported cover format and size.

WhatsApp problems appear in red only for packs with validation errors. The full rules and usage FAQ live in **Settings → Help & FAQ**. Success messages use green, errors use red, and informational status uses blue, independently of the chosen accent and light/dark appearance.

The shared navigation has browser-history Back/Forward buttons. Pack, library, GIF search, and Settings routes use that same history. Visited links retain the selected theme colors. The pack toolbar groups image sources under **Add stickers**, with descriptions of each source. **Pack options** contains rename, cover, share/export, and delete actions. Menus float over the content and close when you choose an action, tap outside, or press Escape. The toolbar uses equal gaps and a proper WhatsApp brand silhouette from [Simple Icons (CC0)](https://github.com/simple-icons/simple-icons).

GIF editing is labeled **Edit GIF**. Timeline handles and start/end number fields use invariant decimal formatting, so a decimal duration such as 4.7 seconds is not silently replaced by the browser's midpoint under another locale. Start/end fields, timeline masks, selected duration, and saved trim use the same values. **Preview animation** plays the current cutouts and adjustments over the selected trim range; Reset trim restores the range. Loading shows only a progress indicator and Cancel; cancellation discards late results and leaves background preparation running.

Pack creation needs only a name; still and animated images can share a pack. Animation metadata is inferred from its contents. All pack tools sit in the heading, with Add to WhatsApp fixed at the bottom right. Validation lists only current problems, includes actual dimensions and size limits, and shows automatic preparation status through the sticker info button, with Edit / trim for images needing attention. Preparation preserves the first imported original, never crops or stretches the image, and never silently trims animation. Very detailed GIFs may need a shorter selection to fit 500 KB. The readable encoder wrapper and GIF worker source live in `StickerApp.Shared/Media`; rebuild their generated assets with `npm run build` in `tools/gif`. WebP encoding uses Apache-2.0 jSquash/libwebp, with its license included beside the bundled assets.

### Mixing still pictures and GIFs

Keep both in the same app pack. Library images, editing, and previews retain their original still/animated behavior. Automatic background preparation fits each image into 512 × 512 WebP as before. When exporting a `.wastickers` pack or handing off through Android or iOS, every sticker uses animated WebP. Still pictures are wrapped as two identical 500 ms frames without re-encoding the artwork; they remain visually still. Existing multi-frame animations retain their exact bytes. Older one-frame wrappers are repaired into two identical frames. The export copy is checked against the 500 KB, 10-second, and minimum 8 ms frame limits. Editable ZIP backups keep the library files unchanged.

WhatsApp's [sample validator](https://github.com/WhatsApp/stickers/blob/main/Android/app/src/main/java/com/example/samplestickerapp/StickerPackValidator.java) requires more than one frame for each sticker in an animated pack. An animation flag or a one-frame wrapper is insufficient. This change is verified through local export checks; actual WhatsApp acceptance still needs a phone. The native iOS exporter has not been updated or tested in this pass.

## Pinterest selection import

Open **Pinterest** in the navigation, or **Pinterest import** inside a pack to preselect that destination. In Pinterest, select several pins on a public board and share the selection as one link. Paste either the link or Pinterest's shared text, choose **Preview images**, select the images you want, and import into **All images**, an existing pack, or **Create new pack…**. For a new pack, enter its name and choose **Create pack & import**. The pack and its images are saved together after every download succeeds. Each pack holds at most 30 stickers; the preview shows remaining capacity. Imports retain the original image format. WhatsApp preparation starts automatically after import.

The app calls the separate [pinterest-link-extractor](https://github.com/DINH-iy/pinterest-link-extractor) service at `https://stickers.dinnyvanhuizen.com/api/pinterest/import`. No Pinterest login, client ID, secret, or backend settings are needed. The old OAuth settings and sharing-server routes have been removed; pack publishing remains independent. The Pi returns metadata and image URLs; the app downloads the confirmed images directly from `i.pinimg.com`. In the browser development host these requests and library writes run on the host computer.

Selection lookup and image import show a dimmed-screen progress overlay with cancellation. Once downloads start, it shows completed/total and remaining image counts. After two completed images, an approximate remaining download time is extrapolated from elapsed time; the final library save has its own stage and no download ETA. Selection lookup stays indeterminate because the service returns its metadata in one response. This uses the existing service API.

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

Open an image or animation’s Edit button for the shared touch canvas. Drag a rectangle and Apply crop, trace a shape and Keep traced area, erase with a brush, or tap a connected background with Magic wand. Rotation, opacity, color tint, flip, and size controls work on still images, GIFs, animated WebP, and selected layers. Trace and eraser cutouts apply the same spatial mask to every frame, without tracking moving subjects. Magic wand removes the selected connected background color separately on each frame, so moving artwork does not leave the previous frame’s background behind. Preview animation checks the result without saving. Animation edits preserve frame timing and transparency and save as animated WebP, within the existing 10-second/500-KB sticker limits. Reopening edits the current sticker rather than its retained original backup.

Undo/Redo retain the last eight raster actions. Reset restores the image as opened. Changes stay local until Save; Cancel leaves the library unchanged. Still cutouts use PNG before preparation. Sources fit a fixed 512 × 512 workspace without enlarging small images. The frame and timeline remain stationary during rotation and resizing; final sticker fitting only scales down. Editable layer source files remain limited to 1024 pixels per side. Editor controls share `EditorAdjustments` and `ImageCustomizer`. The image/animation editor reserves a fixed region for the canvas, timeline, and preview button; only the vertically wrapped tools, adjustments, and options scroll; the page behind an open editor is locked. Precise trim times and name/file options are expandable, and explanations stay under Editing help. The same compact timeline is reused for animated layers.

### iOS editor and pack export

The shared image customizer, transparent PNG cutouts, responsive card controls, and automatic preparation are also used in the iOS app. Native safe areas and visible-viewport dialog placement keep popups clear of the keyboard. iOS 15 retains support through HTML-dialog, card-layout, and main-thread GIF-canvas fallbacks; newer WebViews use background workers. iOS WhatsApp export runs the same animated WebP wrapper as Android, so mixed still/animated collections export as animated packs while still artwork stays visually still.

Build the simulator app with `dotnet build StickerApp/StickerApp.csproj -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64 -p:EnableCodeSigning=false`. The installed .NET iOS workload currently requires Xcode 27.1, while this machine has Xcode 27.2. Local simulator verification used the command-only `-p:ValidateXcodeVersion=false` override; the project keeps the default version validation. Use a matching supported Xcode/.NET workload and Apple signing/provisioning for a physical iPhone distribution build. WhatsApp handoff requires a physical iPhone with WhatsApp.

The iOS status-bar and home-indicator areas follow the resolved CSS background, including color palette and light/dark changes. A native appearance bridge updates the page/window background and a page controller selects readable status-bar icons without overriding the device’s system appearance. The last resolved colors are retained for startup. Dark and light appearances were checked manually in the iPhone simulator.

### Diagnostics and navigation

Settings → Developer → Diagnostics & error logs provides a recent-log viewer and Export log (.txt). Export uses the platform share sheet on phones and a download in the browser. Logs are local JSON lines with UTC timestamps, a per-run session, error reference IDs, operation names, and exception stacks. Imports, preparation, editing, pack cover changes, and exports record handled failures. Framework warnings/errors, uncaught JavaScript errors/rejections, and unhandled/unobserved managed exceptions are captured as well. Recent application startup and sticker operation messages help show what preceded an error. Four rotating files of up to 256 KB each bound storage to about 1 MB; logs remain in application storage across restarts, independent of the sticker library folder. Common credentials, request URLs, embedded image data, and the home directory are redacted; review a diagnostic report before sharing it. Logging failures do not block sticker actions. Native operating-system crashes or process kills can happen before an error can be recorded; this is a local diagnostic log, not a native crash-reporting service.

The pack heading shows its selected cover, or its first sticker as a fallback. Toolbar menus are clamped to the visible viewport even after wrapping to the left. Android Back and the in-app Back button dismiss an open sticker editor/details popup or menu first, keeping the current pack; a subsequent Back navigates normally.

Settings has two visible tabs: General contains appearance and help, while Developer contains diagnostics/export, GIF API configuration, pack-sharing integration, storage/cloud details, and optional surprises. The old five-click heading unlock has been removed. Links to diagnostics, GIF setup, and pack sharing automatically open the Developer tab.

### Home Screen browser app (local preparation)

`StickerApp.Web` includes a standalone web-app manifest, iPhone touch icon, safe-area styling, theme-color updates, and a service worker with a static offline reconnect page. Open Settings → General → Home Screen app for installation instructions. On iPhone, open the app URL in Safari, choose Share → Add to Home Screen, enable Open as Web App if offered, and Add. No Apple Developer membership, App Store submission, or periodic re-signing is needed for the Home Screen web app.

This is still an online Blazor Server app. The host computer must stay running and reachable. Its library and diagnostics live on the host, not in offline iPhone storage. The service worker never caches library pages, personal media, credentials, or logs. Service workers require HTTPS or localhost; plain HTTP LAN previews do not get the offline fallback. Direct Add to WhatsApp remains a native-app feature; the browser offers Export pack instead. The current browser host has one local library and is not ready for public multi-user deployment.

For local desktop use, keep `http://127.0.0.1:5178`. A real iPhone cannot reach the Mac using that loopback address: it needs the Mac’s LAN address, both devices on the same network, and a host binding reachable on that interface. For example, run `dotnet run --project StickerApp.Web --urls http://YOUR_MAC_LAN_IP:5178`, then open that address on the phone. Keep this development server on your trusted local network. Raspberry Pi hosting, HTTPS, client isolation, syncing, and the feature wishlist are deferred in [IDEAS.md](IDEAS.md); nothing was deployed to the Pi.

Animation timelines share a draggable timeline playhead and preview speeds of 0.1×, 0.25×, 0.5×, and 1× across image/GIF/WebP and layer editing. Scrubbing pauses on the chosen frame for cutout work; playback speed changes only the preview, preserving export timing. Layer cutout tools are directly available as Crop, Trace cutout, Eraser, and Magic wand.

Active settings use the same compact panel beside the fixed preview; Crop and Trace only show Apply/Cancel until a selection is drawn. Floating checkmark/cross Apply/Cancel buttons sit at the preview’s bottom right. Cancel and pressing an already active tool ask before discarding changes. Discard restores all changes made since selecting that tool; Apply keeps them. This applies to still images, animations, and layer adjustments.

Images, GIFs, animated WebP, and layer artwork share a Transform tool: drag the artwork to move it, use corner/edge handles to resize, and drag the arrow handle to rotate. Keep proportions is on by default; turn it off to stretch. The 512 × 512 workspace stays fixed. Preview gestures use retained source pixels; Apply records position, size, and angle together for every animation frame, and Cancel restores the prior artwork. Handles support arrow keys; Shift uses larger steps (and snaps pointer rotation to 15°).

Color now offers Hue and Strength instead of replacing the artwork with a flat fill. Hue preserves details and transparency; zero strength restores the original colors. These values are shared by still/animation previews, layer composition exports, and saved layer projects. Existing projects keep their legacy tint until a hue adjustment replaces it. Modal dialogs lock background scrolling, including nested confirmations, and restore the previous page position on close.

### Video clips

Use **Add stickers → Video from device**, choose a locally playable video (up to 100 MB), then select all or part of it on the timeline. Clips can be 0.1–10 seconds, at 8, 12, or 20 frames per second. Create GIF converts locally without sound, imports into the current pack or library, and opens the existing GIF editor. GIFs retain the source aspect ratio, fit within 512 pixels without enlargement, and must stay below the 10 MB image-import limit. Cancel stops conversion; failed conversions do not add a sticker. Format/codec playback support depends on the device; MP4 with H.264 is a useful fallback.

### Editable text and memes

Open an image or GIF and choose **Make layered** to use the current edited artwork as one layer, or **Add text** to open layers with a caption added. Both preserve applied edits and animation trims. **Add text** is also available inside the editor for an individual layer image. You can start from **Add stickers → Layered image → Add text** too. Text remains an editable layer after saving. New captions randomly start at the top as “TOP TEXT” or at the bottom as “BOTTOM TEXT.” Top, center, and bottom presets position each caption; drag the layer freely and use the shared canvas handles to resize or rotate it. Add another text layer for a second caption.

The bundled offline **Meme · Anton** font uses white text and a black outline by default. Other controls include solid/gradient colors and gradient angle, bold, italic, underline, strikethrough, uppercase, alignment, wrap width, line spacing, outline, shadow, background, opacity, and emoji. Emoji appearance depends on the device. Text can be at most 1,000 characters, with a raster up to 1024 pixels per side. GIF frames keep their animation behind text.

**Add your font** accepts TTF, OTF, WOFF, and WOFF2 files up to 4 MB each (40 custom fonts). Fonts are validated by the browser/WebView before import, deduplicated, and stored in the local library. Editable text embeds its chosen custom font so saved captions remain editable. **Settings → Library statistics & storage** shows a Custom fonts tile, a storage-chart segment, and each uploaded font's size. Embedded copies are included in Editable layers storage. Importing a font adds it to the font library even if you later cancel sticker edits.

Anton is distributed under the SIL Open Font License; its font file and license are bundled under `StickerApp.Shared/wwwroot/fonts`. Source: [Google Fonts / Anton](https://github.com/google/fonts/tree/main/ofl/anton).

### App-wide font preferences

**Settings → General → App font** changes the whole interface: system/sans/serif/monospace/Meme fonts or your uploaded font, text size (85–150%), bold text, letter spacing, and line spacing. Changes apply immediately and persist on that device. Reset font settings restores defaults. Uploaded fonts use the same font library as sticker text and appear in storage statistics. The currently selected custom interface font has a bounded device cache for startup; changing interface typography leaves sticker text and exports unchanged.
