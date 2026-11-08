# Host pack links on a Raspberry Pi

This runs only the pack sharing service. It does not expose your sticker library or the browser development host. The app explicitly uploads the selected pack when you choose **Create share link**.

Use a Raspberry Pi running a 64-bit Linux OS with Docker Engine and the Docker Compose plugin. Build on the Pi so Docker selects its native ARM64 images. The share server targets .NET 10; an older Pi running a 32-bit OS needs a deployment targeting `linux-arm` instead of this ARM64 setup.

## Domain and network

Choose a subdomain such as `stickers.example.com`. Point its DNS record to your home connection's public IP. Forward TCP ports 80 and 443 on your router to the Pi. Caddy handles HTTPS certificates for that hostname.

If your connection is behind carrier-grade NAT or you cannot forward those ports, a public hostname needs a tunnel or another reachable server. A domain by itself does not make the Pi reachable. You can also use a private VPN for sharing between its members, with an HTTPS hostname reachable by those devices.

## Start the service

1. Copy or clone this repository onto the Pi.
2. In `deploy/pi`, copy `.env.example` to `.env`.
3. Set `PACK_DOMAIN` to your hostname, without `https://`.
4. Generate a token with `openssl rand -hex 32` and put it in `PACK_PUBLISH_TOKEN`. Keep it private; it allows uploads and link deletion.
5. Run `docker compose up -d --build` from `deploy/pi`.
6. Visit `https://YOUR_HOSTNAME/health`. It should return `{"status":"ok"}`.
7. In the app, open **Settings → Pack sharing**. Enter `https://YOUR_HOSTNAME` and the same publishing token. Save the settings.
8. Select a pack, click **Share or export pack**, then **Create share link**.

The token lives in the app's device-local storage, not in exported packs. Published links can be downloaded by anyone who has the link. Links do not expire automatically. File exports remain usable independently of the server.

Docker's `pack-data` volume keeps uploaded archives across container restarts. Back it up if the links matter. The default storage limit is 2 GiB; configure `PACK_STORAGE_LIMIT_BYTES` to change it. Packs are limited to 100 MB, and at most two uploads are processed at once. Leave spare disk space for temporary uploads and operating-system files.

## What the link does

A link looks like `https://YOUR_HOSTNAME/p/SHA256_HASH`. It opens a small download page. The recipient saves the `.stickerpack.zip` file and uses **Import pack** in the app. This is not a direct WhatsApp installation link.

The hash is the SHA-256 of the complete ZIP file. Exporting unchanged pack contents gives the same file and link. Changing the pack name, image order, tags, or image bytes gives a new hash and link. Old links keep serving the old pack until removed; they do not update automatically.

The fingerprint detects changed bytes. It is neither encryption nor a signature proving who created the pack.

## Remove a shared link

Use the authenticated API from a terminal. Set `PACK_SERVER_URL`, `PACK_PUBLISH_TOKEN`, and `PACK_HASH` to your values, then run:

```sh
curl --fail --request DELETE \
  --header "Authorization: Bearer ${PACK_PUBLISH_TOKEN}" \
  "${PACK_SERVER_URL}/api/packs/${PACK_HASH}"
```

Only the server copy is removed. Recipients may still have files they already downloaded. Re-uploading identical contents recreates the same link.

## Local development

A public domain is not required to check the feature locally. Supply a development-only token of at least 32 characters as `Sharing__PublishToken`, then run:

```sh
dotnet run --project StickerApp.ShareServer --urls http://localhost:5142
```

Configure `http://localhost:5142` and that token in the app. HTTP is accepted only for localhost; remotely hosted servers need HTTPS. A localhost link only works on the same computer.

## Source and references

The service is in `StickerApp.ShareServer`. `PackRepository.cs` validates and stores ZIP files; `PackLandingPage.cs` renders the download page; `Program.cs` defines the authenticated upload/delete API and public download routes. ZIP validation and the manifest format live in `StickerApp.Core/Packs/PackArchive.cs` and are shared with the app.

- [.NET deployment on Raspberry Pi and ARM boards](https://learn.microsoft.com/en-us/dotnet/iot/deployment)
- [Docker Engine installation](https://docs.docker.com/engine/install/)
- [Caddy automatic HTTPS](https://caddyserver.com/docs/automatic-https)

## Enable the invited-friends overview

1. Generate a **separate** code with `openssl rand -hex 32` and set `FRIENDS_INVITE_CODE` in `deploy/pi/.env`. Leave it empty to keep the friends API disabled. Keep `PACK_PUBLISH_TOKEN` private; friends do not need it.
2. Rebuild with `docker compose up -d --build`. No deployment is performed automatically by editing these files.
3. Check `https://YOUR_HOSTNAME/api/friends/status`: enabled should be true and visibility should be invited-friends-only.
4. Give invited friends the HTTPS server root URL and the invitation code. They open **Friends’ packs → Join your friends’ group** in their own app.
5. Choose Public in Pack options, then **Sync my public packs**. Other invited members can preview and import it. Making it private removes the server copy before committing the local visibility change. Sync again after edits.

Membership authentication protects the overview and every pack/preview download. Friends archives are stored under `/data/friends` in the existing volume; they do not become anonymous sharing links. `FRIENDS_STORAGE_LIMIT_BYTES` controls their separate quota (default 2 GiB). Back up the whole volume to preserve membership identities and archives. Use **Leave group and remove my shared packs** before resetting local app data if remote removal is wanted. Changing the invitation code blocks future joins with the old code but does not revoke current members; the admin management UI is not implemented yet.
