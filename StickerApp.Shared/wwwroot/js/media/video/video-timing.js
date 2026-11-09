// Read MP4/MOV sample timing without decoding the clip or loading its media payload.
export async function readVideoTiming(file) {
    const u32 = (v, p) => v.getUint32(p);
    const kind = (v, p) => String.fromCharCode(...new Uint8Array(v.buffer, v.byteOffset + p, 4));
    function boxes(view, start, end) {
        const result = [];
        for (let p = start; p + 8 <= end;) {
            let length = u32(view, p), header = 8;
            if (length === 1) { if (p + 16 > end) break; length = Number(view.getBigUint64(p + 8)); header = 16; }
            if (length === 0) length = end - p;
            if (length < header || p + length > end) break;
            result.push({ kind: kind(view, p + 4), start: p + header, end: p + length }); p += length;
        }
        return result;
    }
    for (let offset = 0; offset + 8 <= file.size;) {
        const header = new DataView(await file.slice(offset, offset + 16).arrayBuffer());
        let length = u32(header, 0), head = 8;
        if (length === 1) { if (header.byteLength < 16) return null; length = Number(header.getBigUint64(8)); head = 16; }
        if (!length) length = file.size - offset;
        if (!Number.isSafeInteger(length) || length < head || offset + length > file.size) return null;
        if (kind(header, 4) === 'moov' && length < 8 * 1024 * 1024) {
            const view = new DataView(await file.slice(offset + head, offset + length).arrayBuffer());
            const children = box => boxes(view, box.start, box.end);
            for (const track of boxes(view, 0, view.byteLength).filter(b => b.kind === 'trak')) {
                const mdia = children(track).find(b => b.kind === 'mdia'); if (!mdia) continue;
                const fields = children(mdia), handler = fields.find(b => b.kind === 'hdlr'), mdhd = fields.find(b => b.kind === 'mdhd');
                if (!handler || handler.start + 12 > handler.end || kind(view, handler.start + 8) !== 'vide' || !mdhd) continue;
                const scaleOffset = mdhd.start + (view.getUint8(mdhd.start) === 1 ? 20 : 12);
                if (scaleOffset + 4 > mdhd.end) continue;
                const scale = u32(view, scaleOffset), minf = fields.find(b => b.kind === 'minf'); if (!scale || !minf) continue;
                const table = children(minf).find(b => b.kind === 'stbl'); if (!table) continue;
                const stts = children(table).find(b => b.kind === 'stts'); if (!stts || stts.start + 8 > stts.end) continue;
                const count = u32(view, stts.start + 4), runs = [];
                if (count > 100000 || stts.start + 8 + count * 8 > stts.end) continue;
                let total = 0, samples = 0;
                for (let i = 0; i < count; i++) {
                    const p = stts.start + 8 + i * 8, frames = u32(view, p), ticks = u32(view, p + 4);
                    if (!frames || !ticks) return null;
                    runs.push({ frames, seconds: ticks / scale }); samples += frames; total += frames * ticks / scale;
                }
                if (samples && total) return { fps: samples / total, runs };
            }
            return null;
        }
        offset += length;
    }
    return null;
}
export function planFrames(start, end, fps, timing, width, height) {
    if (!Number.isFinite(start) || !Number.isFinite(end) || start < 0 || end - start < .1 || end - start > 10.001) throw new Error('Choose a clip between 0.1 and 10 seconds.');
    const frames = [];
    if (fps === 0) {
        if (!timing) throw new Error('Native frame rate is unavailable for this video. Choose a frame rate.');
        let time = 0;
        for (const run of timing.runs) {
            // Jump to the selected range rather than walking through a long video.
            const first = Math.max(0, Math.floor((start - time) / run.seconds));
            for (let i = first; i < run.frames && time + i * run.seconds < end; i++) {
                const at = time + i * run.seconds, until = Math.min(end, at + run.seconds);
                if (until <= start) continue;
                if (run.seconds < .0199) throw new Error('This source exceeds 50 fps. GIF playback cannot retain that rate here. Choose 30 fps.');
                frames.push({ time: Math.max(start, at), end: until });
                if (frames.length > 1000) throw new Error('Too many frames. Choose a shorter clip or fewer frames per second.');
            }
            time += run.frames * run.seconds; if (time >= end) break;
        }
    } else {
        if (![8, 12, 20, 30].includes(fps)) throw new Error('Choose a supported frame rate.');
        const count = Math.max(2, Math.ceil((end - start) * fps));
        for (let i = 0; i < count; i++) frames.push({ time: start + (end - start) * i / count, end: start + (end - start) * (i + 1) / count });
    }
    // Partial edge frames must not round to 0/10 ms and become slow GIF frames.
    for (let i = 0; i < frames.length && frames.length > 1;) {
        if (frames[i].end - frames[i].time >= .0199) { ++i; continue; }
        const short = frames.splice(i, 1)[0];
        if (i > 0) frames[i - 1].end = short.end;
        else frames[0].time = short.time;
    }
    if (frames.length < 2) throw new Error('Choose a clip with at least two frames.');
    if (width * height * frames.length > 80_000_000) throw new Error('This clip has too many pixels to edit safely. Choose a shorter clip or fewer frames per second.');
    return frames;
}
