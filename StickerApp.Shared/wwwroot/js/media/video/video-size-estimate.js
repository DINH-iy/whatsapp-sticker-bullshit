import { MAX_STICKER_BYTES } from './video-limits.js';
// Sampled compression is an estimate, never a promise about unsampled frames.
export function estimateSizes(samples, frameCount, duration) {
    if (!samples.length || frameCount < 2 || samples.some(s => !Number.isFinite(s.low) || !Number.isFinite(s.high) || s.low <= 0 || s.high <= 0)) throw new Error('Could not estimate this clip.');
    const mean = key => samples.reduce((sum, sample) => sum + sample[key] + 24, 0) / samples.length;
    const low = 64 + mean('low') * frameCount, high = 64 + mean('high') * frameCount;
    const stickerLow = Math.ceil(low * .7), stickerHigh = Math.max(stickerLow, Math.ceil(high * 1.35));
    const fraction = Math.max(0, Math.min(1, (MAX_STICKER_BYTES * .8 - low) / Math.max(1, high - low)));
    const quality = high * 1.15 <= MAX_STICKER_BYTES ? .8 : Math.max(.1, Math.floor((.1 + .7 * fraction) * 20) / 20);
    return { stickerLow, stickerHigh, quality, tooLarge: stickerLow > MAX_STICKER_BYTES,
        nearLimit: stickerHigh > MAX_STICKER_BYTES,
        suggestedSeconds: Math.max(.1, Math.floor(duration * Math.min(1, MAX_STICKER_BYTES / stickerLow * .8) * 10) / 10),
        sampleCount: samples.length };
}
