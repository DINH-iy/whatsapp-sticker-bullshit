// Keep local video conversion bounded on phones. Imported GIF files have their own limit.
export const MAX_VIDEO_BYTES = 20 * 1024 * 1024;
export const MAX_CLIP_SECONDS = 5;
export const MAX_VIDEO_FRAMES = 100;
export const MAX_STICKER_BYTES = 500 * 1024;
export const MIN_FRAME_MS = 8;
