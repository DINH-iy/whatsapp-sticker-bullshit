# Direct KLIPY import investigation

Reviewed 6 November 2026 against https://docs.klipy.com/integration-requirements and https://docs.klipy.com/.

The existing GIF search already calls KLIPY directly from the device. `klipy-client.js` receives original GIF URLs in `item.file.hd.gif.url` / `md.gif.url`, preserves them, and currently offers a link to the original. No page extraction server is needed to discover the file.

The implemented interaction is a per-result Import button: fetch the selected original GIF directly from the device, validate the GIF and existing import size limits, and pass it through the app's normal original-retention, library import, editing, and WhatsApp preparation flow. Failed or cancelled downloads leave no partial library item. Browsers use bounded JavaScript streams and surface CORS/network failures. Native hosts use `KlipyGifDownloadClient` on the device, with redirects disabled. Both preserve provider URLs and invoke the existing share report after a successful import.

KLIPY's current standard integration requirements say media must load from original provider URLs, prohibit retained copies without written approval, and require prior approval for partner servers, proxying, caching, and custom integrations. Saving an editable GIF and exporting derived stickers therefore needs written approval for that use. A separate service, like the Pinterest extractor, would add a proxy requirement without solving the storage/use requirement.

Recommended next step: request KLIPY guidance at developers@klipy.com for user-triggered local download, editing, storage and WhatsApp sticker export; clarify whether browser-hosted library storage is covered and which share/report events are required. Do not send credentials or sample private library data. No email has been sent. One-button import now uses direct device downloads, as requested; no proxy service was added. Operators still need to arrange the appropriate provider permission for their deployment. Browser and native transport checks use generated fixtures, not API credentials or private library data.

After a successful import the GIF remains in All images and the shared AddToPackDialog offers optional existing/new pack membership. Keep in library and closing the popup retain the imported item without adding it to a pack. Result actions are Import and Open in browser.
