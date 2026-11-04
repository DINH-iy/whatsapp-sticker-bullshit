# Stickers

C# / Blazor Hybrid Android app with a browser development host.

Run the browser version: `.\dev.ps1 web`
Build Android: `.\dev.ps1 android`

Requires .NET 10. Android additionally requires the MAUI Android workload, SDK platform 36, and a compatible JDK.

Features: import JPEG/PNG/GIF/WebP, rename, replace, rotate and square-crop still images, trim GIFs by start/end time, remove images, manage packs, export compliant WebP packs through Android.

GIF trimming runs locally in a background worker. Animated WebP editing and GIF/video-to-WebP conversion are not implemented yet. The browser and Android libraries are stored separately. Data is local (JSON index and media files).

WhatsApp export still needs verification on a real phone. This is a local development app, without authentication or direct cloud account integration.

Rebuild the bundled GIF worker only when changing its source: run npm ci --ignore-scripts, then npm run build in tools/gif. Normal app builds use the included bundle.


Settings: open **Settings** in the app navigation to choose Light, Dark, or the system theme. Choose Green, Blue, Purple, Rose, or Amber as the color theme. Appearance and color are remembered independently on the current device/browser and applied before the first paint when the app reopens.

Sticker storage: use **Browse…** to navigate folders in a popup (and optionally create an empty folder), or type a full path in Settings. Either method fills the same field; **Save folder** applies the choice. In the browser host, the popup browses folders on the host computer. The app copies the JSON index and all media before saving the new location; the old library stays intact as a backup. The folder preference is kept in `storage-settings.json` inside the original data directory. If a custom folder is unavailable at startup, reconnect it before launching the app.

For the browser development host, you can use an empty subfolder inside a locally synced OneDrive or Google Drive folder on the host computer. Keep it available offline and use one running app at a time; your cloud client handles syncing and the app does not resolve sync conflicts. Android accepts filesystem paths the app can access; Android document-provider/cloud folders and direct OneDrive/Google Drive sign-in are not implemented.

## GIF search

Open **GIF search** in the navigation, or **Find GIFs** on the library screen. Add your own KLIPY API key in **Settings → GIF search** first. Create a key through the [KLIPY Partner Panel](https://partner.klipy.com/) and configure its allowed app origins/platforms there. A saved key is not validated until you request results. The app supports trending GIFs, keyword searches, loading more results, and copying GIF links.

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
- `StickerApp.Web/Program.cs` and `StickerApp/MauiProgram.cs` wire up the browser host and Android app respectively.

App source uses four-space indentation and named methods. Screen markup and C# behavior live in adjacent files; `.editorconfig` records the formatting conventions. Bundled vendor files and the generated GIF worker are intentionally left as supplied.

## Location surprises and controls

**Settings → Easter eggs** contains an optional **Location surprises** toggle, off by default. When enabled, the device asks [country.is](https://country.is/) for the country of its public IP. Romania (`RO`) triggers a dismissible Count Stickula message and a brief bat animation once per session. The setting persists in local storage; only the country code and lookup time are cached in session storage for up to 30 minutes. No IP address or GPS location is saved by the app. A VPN can affect the country; failed lookups silently skip the surprise. The **Preview Romania surprise** button works anywhere without making a location request.

`Components/LocationSurprise.razor` and its code-behind render the surprise. `Components/SurpriseSettings.razor` and its code-behind handle the toggle and preview. `wwwroot/location-surprises.js` owns device-local preferences, session checks, and direct IP-country lookup. Add more country effects there and expose a corresponding named callback on the component.

Dark themes share neutral charcoal backgrounds across all five accents. Primary buttons and highlights carry the selected accent. Shared controls use a 44px height and 8px corner radius; preview canvases, checkboxes, and sliders retain dimensions appropriate to their purpose. Compact library actions use local SVG icons from `Components/Icon.razor`, with accessible labels and hover titles.
