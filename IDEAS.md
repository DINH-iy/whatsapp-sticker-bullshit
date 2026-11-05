# Stick it — ideas for later

These are a backlog, not instructions to implement now. None of the features below are implemented by creating this list.

## Hosting and syncing (later, at home)

- [ ] Host the Home Screen web app on the existing Raspberry Pi with its own HTTPS URL.
- [ ] Configure syncing and remote access when working from the home computer with Pi access.
- [ ] Before inviting friends, separate each client's library and diagnostics. The current local browser host uses one library on the host computer; Home Screen installation does not create an offline library per iPhone.
- [ ] Decide which data stays on each device and which data is hosted or synchronized.

## Friends' pack overview

- [ ] A dashboard showing packs shared by friends who use the app.
- [ ] A private/public setting on each pack. Public packs appear in the overview; private packs do not.
- [ ] Define whether "public" means visible to this friends-only app or visible to anyone with a link.

## Layer editor

- [x] Put multiple stickers/images on top of one another to create a new custom sticker.
- [x] Move, resize, rotate, and reorder individual layers.
- [x] Adjust opacity and optional color tint per layer; flip, duplicate, remove, undo and redo.
- [x] Preserve an editable composition locally and export a flattened sticker with transparency.
- [ ] Animated layer compositions: the current editor intentionally captures GIFs as a still frame.
- [ ] Transfer editable layers in pack backups; current pack exports contain the flattened sticker only.

## Owner/admin application

- [ ] A separate admin panel for the owner to inspect clients and manage the app.
- [ ] No account creation for friends: ask for a name once on first use and remember it.
- [ ] Associate clients with a persistent GUID, chosen name, app/browser and OS versions, and other useful device information.
- [ ] Decide which location information is useful and how it is requested/disclosed; do not collect location now.
- [ ] Allow client logs and relevant diagnostics to be uploaded to the admin service, with a clear upload policy.
- [ ] Add/manage clients or users and provide reset/recovery actions.
- [ ] Manage API keys and integrations centrally, keeping secrets on the server where possible.
- [ ] Define access control for the admin panel separately from the friendly first-name popup.

## Pack sharing settings

- [ ] Review whether the existing pack-sharing server configuration is useful for friends.
- [ ] Remove or replace that settings section if it is redundant once the shared dashboard exists.
- Current behavior: this config powers **Create share link** by uploading a selected pack to a configured server. File export works without it. No settings are removed as part of this backlog request.

## Discord integration — investigate first

- [ ] Find out whether Discord offers a supported way to access a user's favorited GIFs.
- [ ] If feasible, make favorites easy to browse and import into Stick it.
- [ ] Check available permissions/APIs before choosing an approach; do not assume favorites are exposed.

## Instagram integration — investigate first

- [ ] Paste an Instagram link to import an image or video and create a sticker.
- [ ] Investigate supported media access, login requirements, permissions, and applicable provider limits.
- [ ] Add a video-to-sticker workflow if a supported import path is feasible.
