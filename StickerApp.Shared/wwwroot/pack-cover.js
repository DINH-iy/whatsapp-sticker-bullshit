// Resize locally before sending the cover to .NET; originals never enter the library.
export async function prepare(input) {
    const file = input.files?.[0];
    if (!file) return null;
    if (file.size > 10 * 1024 * 1024) throw new Error('Choose a cover smaller than 10 MB.');
    const url = URL.createObjectURL(file);
    try {
        const image = new Image();
        image.src = url;
        await image.decode();
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = 256;
        const context = canvas.getContext('2d');
        context.fillStyle = '#ffffff';
        context.fillRect(0, 0, 256, 256);
        const side = Math.min(image.naturalWidth, image.naturalHeight);
        context.drawImage(image, (image.naturalWidth - side) / 2, (image.naturalHeight - side) / 2,
            side, side, 0, 0, 256, 256);
        const data = canvas.toDataURL('image/jpeg', 0.75).split(',')[1];
        if (data.length > 87000) throw new Error('This cover is too detailed. Choose a simpler image.');
        return data;
    } finally {
        input.value = '';
        URL.revokeObjectURL(url);
    }
}

export async function trayIcon(source) {
    const image = new Image();
    image.src = source;
    await image.decode();
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 96;
    const scale = 96 / Math.max(image.naturalWidth, image.naturalHeight);
    const width = image.naturalWidth * scale, height = image.naturalHeight * scale;
    canvas.getContext('2d').drawImage(image, (96 - width) / 2, (96 - height) / 2, width, height);
    return canvas.toDataURL('image/png').split(',')[1];
}
