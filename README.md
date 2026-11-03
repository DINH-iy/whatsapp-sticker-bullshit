# Stickers

C# / Blazor Hybrid Android app with a browser development host.

Run the browser version: `.\dev.ps1 web`
Build Android: `.\dev.ps1 android`

Requires .NET 10. Android additionally requires the MAUI Android workload, SDK platform 36, and a compatible JDK.

Features: import JPEG/PNG/GIF/WebP, rename, replace, rotate and square-crop still images, trim GIFs by start/end time, remove images, manage packs, export compliant WebP packs through Android.

GIF trimming runs locally in a background worker. Animated WebP editing and GIF/video-to-WebP conversion are not implemented yet. The browser and Android libraries are stored separately. Data is local (JSON index and media files).

WhatsApp export still needs verification on a real phone. This is a local development app, without authentication or cloud sync.

Rebuild the bundled GIF worker only when changing its source: run npm ci --ignore-scripts, then npm run build in tools/gif. Normal app builds use the included bundle.

