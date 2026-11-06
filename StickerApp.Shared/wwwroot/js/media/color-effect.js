// A hue rotation mixes with the original RGB values, retaining texture and
// the exact alpha channel. Shared by live previews and every exported frame.
export function applyColorEffect(context, { hue = 0, colorStrength = 1, tint = null } = {}) {
    const strength = Math.max(0, Math.min(1, colorStrength));
    if (tint) { // Existing projects retain their legacy color until hue is adjusted.
        context.globalCompositeOperation = 'source-in'; context.fillStyle = tint;
        context.fillRect(0, 0, context.canvas.width, context.canvas.height);
        context.globalCompositeOperation = 'source-over';
        return;
    }
    if (!hue || !strength) return;
    const angle = hue * Math.PI / 180, c = Math.cos(angle), s = Math.sin(angle);
    const matrix = [
        .213 + c * .787 - s * .213, .715 - c * .715 - s * .715, .072 - c * .072 + s * .928,
        .213 - c * .213 + s * .143, .715 + c * .285 + s * .140, .072 - c * .072 - s * .283,
        .213 - c * .213 - s * .787, .715 - c * .715 + s * .715, .072 + c * .928 + s * .072
    ];
    const pixels = context.getImageData(0, 0, context.canvas.width, context.canvas.height), data = pixels.data;
    const clamp = value => Math.max(0, Math.min(255, value));
    for (let i = 0; i < data.length; i += 4) {
        if (!data[i + 3]) continue;
        const r = data[i], g = data[i + 1], b = data[i + 2];
        data[i] = r + (clamp(r * matrix[0] + g * matrix[1] + b * matrix[2]) - r) * strength;
        data[i + 1] = g + (clamp(r * matrix[3] + g * matrix[4] + b * matrix[5]) - g) * strength;
        data[i + 2] = b + (clamp(r * matrix[6] + g * matrix[7] + b * matrix[8]) - b) * strength;
    }
    context.putImageData(pixels, 0, 0);
}
