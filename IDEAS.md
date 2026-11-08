# Stick it — remaining work

Only unfinished ideas and follow-up work are listed here. This is a backlog, not an instruction to implement these features now.

Last updated: 5 November 2026.

## Hosting and syncing (later, at home)

- [ ] Host the Home Screen web app on the existing Raspberry Pi with its own HTTPS URL.
- [ ] Configure syncing and remote access when working from the home computer with Pi access.
- [ ] Before inviting friends, separate each client's library and diagnostics. The current local browser host uses one library on the host computer; Home Screen installation does not create an offline library per iPhone.
- [ ] Decide which data stays on each device and which data is hosted or synchronized.

## Friends' pack overview

- [ ] A dashboard showing packs shared by friends who use the app.
- [ ] A private/public setting on each pack. Public packs appear in the overview; private packs do not. Local visibility metadata and editing are implemented; the shared overview and server enforcement remain future work.
- [ ] Define whether "public" means visible to this friends-only app or visible to anyone with a link.

## Editable layer backups

- [ ] Transfer editable layers in pack backups; current pack exports contain the flattened sticker only.

## Owner/admin application

- [ ] A separate admin panel for the owner to inspect clients and manage the app.
- [ ] No account creation for friends: ask for a name once on first use and remember it.
- [ ] Associate clients with a persistent GUID, chosen name, app/browser and OS versions, and other useful device information. Local profile ID and library stats are implemented; admin sync is not connected.
- [ ] Define sync permissions and an authenticated admin API: username/avatar and public packs for the shared overview; first name, private packs and device/storage stats stay outside the public payload. Do not use the local GUID as authentication.
- [ ] Decide which location information is useful and how it is requested/disclosed; do not collect location now.
- [ ] Allow client logs and relevant diagnostics to be uploaded to the admin service, with a clear upload policy.
- [ ] Add/manage clients or users and provide reset/recovery actions.
- [ ] Manage API keys and integrations centrally, keeping secrets on the server where possible.
- [ ] Define access control for the admin panel separately from the friendly first-name popup.

## Feature requests (later)

- [ ] An in-app section where friends can submit feature requests.
- [ ] Show submitted requests in the owner's admin panel, associated with the client's chosen name.
- [ ] Let the owner review, organize, and update request status (for example: new, planned, in progress, completed).
- [ ] Decide what request statuses or responses friends can see in the app.

## Pack sharing settings

- [ ] Review whether the existing pack-sharing server configuration is useful for friends.
- [ ] Remove or replace that settings section if it is redundant once the shared dashboard exists.

## Discord integration — investigate first

- [ ] Find out whether Discord offers a supported way to access a user's favorited GIFs.
- [ ] If feasible, make favorites easy to browse and import into Stick it.
- [ ] Check available permissions/APIs before choosing an approach; do not assume favorites are exposed.

## Instagram integration — investigate first

- [ ] Paste an Instagram link to import an image or video and create a sticker.
- [ ] Investigate supported media access, login requirements, permissions, and applicable provider limits.
- [ ] Add a video-to-sticker workflow if a supported import path is feasible.
