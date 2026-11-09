const STICKER_LIMIT = 500 * 1024, GIF_LIMIT = 20 * 1024 * 1024;
// Sampled compression is an estimate, never a promise about unsampled frames.
export function estimateSizes(samples, frameCount, gifBytes, duration) {
    if (!samples.length || frameCount < 2 || samples.some(s => !Number.isFinite(s.low) || !Number.isFinite(s.high) || s.low <= 0 || s.high <= 0)) throw new Error('Could not estimate this clip.');
    const mean = key => samples.reduce((sum, sample) => sum + sample[key] + 24, 0) / samples.length;
    const stickerLow = Math.ceil(64 + mean('low') * frameCount * .7);
    const stickerHigh = Math.ceil(64 + mean('high') * frameCount * 1.35);
    const gifLow = Math.ceil(gifBytes / samples.length * frameCount * .7);
    const gifHigh = Math.ceil(gifBytes / samples.length * frameCount * 1.35);
    const tooLarge = stickerLow > STICKER_LIMIT || gifLow > GIF_LIMIT;
    const ratio = Math.min(STICKER_LIMIT / stickerLow, GIF_LIMIT / Math.max(1, gifLow));
    return { stickerLow, stickerHigh, gifLow, gifHigh, tooLarge,
        nearLimit: stickerHigh > STICKER_LIMIT || gifHigh > GIF_LIMIT,
        suggestedSeconds: Math.max(.1, Math.floor(duration * Math.min(1, ratio * .8) * 10) / 10),
        sampleCount: samples.length };
}
