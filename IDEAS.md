# Stik-er-in — remaining work

Only unfinished ideas and follow-up work are listed here. This is a backlog, not an instruction to implement these features now.

Last updated: 8 November 2026.

## Hosting and syncing (later — deployment still to be decided)

- [ ] Host the Home Screen web app on the existing Raspberry Pi with its own HTTPS URL.
- [ ] Configure syncing and remote access when working from the home computer with Pi access.
- [ ] Before inviting friends, separate each client's library and diagnostics. The current local browser host uses one library on the host computer; Home Screen installation does not create an offline library per iPhone.
- [ ] Decide which data stays on each device and which data is hosted or synchronized.

## Personal phone ↔ PC editing and sync (later)

Goal: start from the phone, generate a personal temporary link or QR code, open the editor on a PC, add/edit stickers there, and transfer the results back to the phone. Preserve editable layers, text, GIF sources and custom fonts. This is separate from sharing public packs with friends: personal sync may include private packs without publishing them.

### Preferred direction to investigate: temporary “Edit on PC” sessions

- [ ] Add **Profile → Device sync → Start PC editing** on the phone.
- [ ] Generate a short-lived, single-use pairing link/QR code. Show the requesting browser on the phone and require approval before transferring artwork.
- [ ] Let the user select which packs/stickers the session can access; do not expose the whole library by default.
- [ ] Run editing locally in the PC browser. Send saved changes back to the phone and wait for acknowledgement before considering them synced.
- [ ] Show connection, transfer progress, pending edits and last successful sync. Allow the user to end/revoke a session from the phone.
- [ ] Expire unused pairing links and idle sessions. Ending a session revokes access and initiates cleanup of its temporary workspace; already downloaded/exported copies cannot be recalled or guaranteed erased from another device.
- [ ] If the phone disconnects, retain pending PC edits with a clear recovery choice: reconnect or download an editable backup. Never silently discard unsent work when a timer expires.

The temporary environment can be a browser workspace; it does **not** require launching a container/server for every user. The phone can remain the main library. This avoids centrally storing everyone's library, but it does not automatically eliminate all networking infrastructure.

### Connection and hosting options

| Option | What it does | Infrastructure | Main trade-off |
| --- | --- | --- | --- |
| **A. Same-network temporary session** | Phone and PC exchange selected artwork on the same home network. | A temporary local endpoint on the phone, or a browser editor with a local pairing/connection mechanism. | No public backend is inherently required, but local reachability, browser security and mobile lifecycle restrictions must be solved. |
| **B. Temporary internet session** | Phone-approved PC editing from different networks, with direct transfers where possible. | Static HTTPS editor hosting, a small signaling/pairing service, STUN and an authenticated TURN relay fallback. | No permanent central media library is required, but relay traffic can consume bandwidth/cost money. Phone must reconnect to receive saved edits. |
| **C. Persistent cloud sync** | Both devices keep local libraries and exchange changes through durable storage, even when they are online at different times. | Hosted API, database, private object storage, device authentication and a sync engine. | Most convenient, but needs ongoing hosting, storage budgets, privacy controls and recovery. |
| **D. Home-hosted or hybrid sync** | Run the backend on the existing NUC/Pi; optionally place media in private cloud storage. | Home server, isolated server network, HTTPS/tunnel, database, backups; optional external object storage. | Can reuse existing hardware, but home-hosted services still depend on home power, internet and maintenance. |

### How to set up A: same-network editing

1. Move the required web editor logic and temporary storage into the browser; bundle the editor assets for local use if the phone serves the page.
2. Implement an authenticated, narrowly scoped temporary endpoint in the native app, or another local transport/pairing mechanism. Bind only while a session is active.
3. Show the local URL and pairing QR/code on the phone. Connect the PC to the same network and approve it on the phone.
4. Resolve HTTPS/secure-context requirements for the chosen browser APIs. A raw local HTTP address is not a complete secure-browser solution; do not ask users to bypass certificate warnings.
5. Check firewall rules, guest Wi-Fi/client isolation, network changes and Android/iOS foreground/background behaviour. Stop accepting connections when the session ends.

### How to set up B: temporary internet editing

1. Publish the client-side editor as static files over HTTPS. Keep each browser workspace isolated.
2. Deploy a small pairing/signaling service that exchanges connection information, expires sessions and validates phone approval. No permanent phone credential belongs in the URL.
3. Configure WebRTC data channels with STUN and an authenticated TURN fallback. Use short-lived relay credentials, transfer/session limits and abuse controls; never operate an open relay.
4. Try a direct encrypted connection first. If the networks prevent it, relay encrypted traffic through TURN. The relay need not retain sticker files, but it still needs capacity and a budget.
5. Transfer versioned artwork, verify integrity, acknowledge saves and implement reconnect/resume. Validate received media, composition sizes and fonts on both ends.
6. Close the connection, revoke the session and clean up temporary data at completion. Give the user an explicit way to retain or export pending edits if reconnecting fails.

### How to set up C: persistent private sync

1. Establish an authenticated private identity and per-device sessions. Decide how recovery works if the original phone is lost; a username/local profile GUID is not proof of ownership.
2. Deploy the existing C# API, extended for personal sync, on managed/container hosting or a rented server. Add a database for identities, devices, revisions and change history.
3. Use private object storage for artwork, animations, layers and fonts, with authorized short-lived transfers. Do not make a media bucket public.
4. Add stable sync IDs, version checks, offline queues, retry/resume and deletion records. Transfer changed assets rather than replacing an entire library after every edit.
5. Keep conflicting edits as separate recoverable versions. Add a deleted-item recovery period so an offline device cannot resurrect removed stickers.
6. Start with sync on app open/save and a **Sync now** button. Add background work later, without promising immediate delivery while a phone app is suspended.
7. Configure quotas, rate limits, monitoring, backup/restore, device revocation, retention and remote-data deletion. Choose hosting plans after measuring storage and transfer needs; no provider or price is selected yet.

### How to set up D: NUC/Pi at home or hybrid

1. Check the available hardware, operating system, disk space, home upload speed and router/switch capabilities before choosing a deployment.
2. Place the server in a separate VLAN/subnet with firewall rules denying server-initiated access to personal computers/NAS. Permit administration only from trusted devices. A second Wi-Fi name/subnet without firewall isolation is insufficient.
3. Use an outbound tunnel and HTTPS; avoid opening the database to the internet. Router “DMZ host” forwarding is not equivalent to a protected server network.
4. Run the API/database with persistent storage, updates and off-device backups. Preserve existing Pinterest routes if adding sync to the same domain.
5. Optionally send large artwork uploads/downloads directly to private external object storage so they do not all pass through the home connection.
6. Apply invitation, request, storage and transfer limits. Network segmentation protects local devices but does not provide a separate internet connection or extra upstream bandwidth/DDoS protection.
7. If the app grows, migrate the backend/database outside the home network while keeping the same domain. A second ISP/mobile connection is an alternative for separate home connectivity, not a substitute for service security.

### Shared implementation requirements and open decisions

- [ ] Refactor the current Interactive Server web host: it runs application logic and uses a shared host-side library. It is not yet a static, browser-local editor or a safely isolated public multi-user service.
- [ ] Choose A/B for temporary sessions versus C/D for durable sync. Temporary sessions and permanent sync may eventually coexist as separate features.
- [ ] Define session permissions, expiry, reconnect retention, maximum transfer sizes and whether device approval lasts for one session or until revoked.
- [ ] Keep vibration, editor control placement and other device-specific settings local initially. Decide separately whether app fonts/theme preferences should sync.
- [ ] Test mobile sleep, screen locking, app termination, network switching, interrupted uploads and phone/PC edits to the same sticker. Do not promise uninterrupted background sessions on Android/iOS.
- [ ] Keep personal pairing, friends/group invitations and administrative credentials separate. Pairing a computer must not publish private packs or grant group/admin access.
- [ ] Add an in-app explanation of each available mode before asking users to enable it.

Reference material for later implementation:

- [WebRTC peer connections and signaling](https://webrtc.org/getting-started/peer-connections)
- [Why a TURN relay may be needed](https://webrtc.org/getting-started/turn-server)
- [Device authorization and expiring approval codes — RFC 8628](https://www.rfc-editor.org/info/rfc8628/) — a reference pattern, not a claim that personal pairing already implements OAuth.
- [Private R2 transfers with presigned URLs](https://developers.cloudflare.com/r2/api/s3/presigned-urls/)
- [Android background work scheduling](https://developer.android.com/develop/background-work/background-tasks/persistent/getting-started/define-work)
- [Apple background execution strategies](https://developer.apple.com/documentation/BackgroundTasks/choosing-background-strategies-for-your-app)
- [CISA network segmentation guidance](https://www.cisa.gov/sites/default/files/publications/layering-network-security-segmentation_infographic_508_0.pdf)

## Friends' pack overview — under maintenance

Current status: the invited-group prototype, invitation links, collaborative collections, credits and reactions are built and locally tested. The app now grays out this feature and shows an information guide; it does not offer joining, fetching or publishing group updates while hosting is deferred. Existing local working copies remain editable and have group badges/filtering. Maintenance does not delete existing server data.

- [ ] Deploy the invited-friends overview/API on the Pi and invite the first friends. Public currently means invited-group members only.
- [ ] Add automatic pack syncing; the current overview uses an explicit Sync my public packs action.

## Owner/admin application

- [ ] Build the owner/admin and request-review interface as a separate application, not a screen in the sticker app.
- [ ] Connect local profile identities and library stats to admin sync, including the chosen name and app/browser and OS versions.
- [ ] Define sync permissions and an authenticated admin API: username/avatar and public packs for the shared overview; first name, private packs and device/storage stats stay outside the public payload. Do not use the local GUID as authentication.
- [ ] Decide which location information is useful and how it is requested/disclosed; do not collect location now.
- [ ] Allow client logs and relevant diagnostics to be uploaded to the admin service, with a clear upload policy.
- [ ] Add/manage clients or users and provide reset/recovery actions.
- [ ] Manage API keys and integrations centrally, keeping secrets on the server where possible.
- [ ] Define access control for the admin panel separately from the friendly first-name popup.

## Feature requests — submission/review later

Local drafts, editing and explicit text-file export are implemented in the app. Drafts are device-local and are not submitted automatically.

- [ ] Connect requests to an authenticated backend once hosting is selected.
- [ ] Review requests and set Planned / In progress / Completed in the separate owner application.
- [ ] Define which status updates the sticker app can display, with clear consent before attaching profile/device details.

## Translation follow-up

English, Dutch, German and Romanian JSON catalogs, saved language selection, primary menus/settings/editor controls and the visual tutorial are implemented. Missing entries use English fallback; pack names, usernames and sticker content are not translated.

- [ ] Translate the remaining advanced help paragraphs, dynamic status/error messages and accessibility labels into Dutch, German and Romanian.
- [ ] Have native speakers review wording and test long labels on small phones.

## Pack sharing settings

- [ ] Review whether the existing pack-sharing server configuration is useful for friends.
- [ ] Remove or replace that settings section if it is redundant once the shared dashboard exists.

## Instagram integration — investigate first

- [ ] Paste an Instagram link to import an image or video and create a sticker.
- [ ] Investigate supported media access, login requirements, permissions, and applicable provider limits.
- [ ] Connect supported Instagram video imports to the existing video-to-sticker workflow, if feasible.
