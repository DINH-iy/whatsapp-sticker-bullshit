export async function transform(source, rotation, square) {
    const image = new Image();
    image.src = source;
    await image.decode();
    const quarterTurn = rotation % 180 !== 0;
    const width = quarterTurn ? image.naturalHeight : image.naturalWidth;
    const height = quarterTurn ? image.naturalWidth : image.naturalHeight;
    if (width * height > 32_000_000) throw new Error('Image too large to edit.');
    const rotated = document.createElement('canvas');
    rotated.width = width;
    rotated.height = height;
    const context = rotated.getContext('2d');
    context.translate(width / 2, height / 2);
    context.rotate(rotation * Math.PI / 180);
    context.drawImage(image, -image.naturalWidth / 2, -image.naturalHeight / 2);
    if (!square) return rotated.toDataURL('image/png');
    const size = Math.min(width, height);
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = size;
    canvas.getContext('2d').drawImage(rotated, (width - size) / 2, (height - size) / 2, size, size, 0, 0, size, size);
    return canvas.toDataURL('image/png');
}
