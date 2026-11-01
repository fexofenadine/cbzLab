//zip reading and re-writing with no dependencies
//page entries are never decompressed on save: their stored bytes are sliced straight out of the
//source file and only ComicInfo.xml is replaced, so a save can't alter a page and never holds a
//whole archive in memory
(function (root) {
    "use strict";

    const SIG_LOCAL = 0x04034b50;
    const SIG_CENTRAL = 0x02014b50;
    const SIG_EOCD = 0x06054b50;
    const SIG_ZIP64_EOCD = 0x06064b50;
    const SIG_ZIP64_LOCATOR = 0x07064b50;

    const FLAG_ENCRYPTED = 0x0001;
    const FLAG_DATA_DESCRIPTOR = 0x0008;
    const FLAG_UTF8 = 0x0800;

    const U16_MAX = 0xffff;
    const U32_MAX = 0xffffffff;

    const utf8 = new TextDecoder("utf-8");

    let crcTable = null;

    function crc32(bytes) {
        if (!crcTable) {
            crcTable = new Uint32Array(256);
            for (let n = 0; n < 256; n++) {
                let c = n;
                for (let k = 0; k < 8; k++)
                    c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
                crcTable[n] = c >>> 0;
            }
        }
        let crc = 0xffffffff;
        for (let i = 0; i < bytes.length; i++)
            crc = crcTable[(crc ^ bytes[i]) & 0xff] ^ (crc >>> 8);
        return (crc ^ 0xffffffff) >>> 0;
    }

    async function readRange(blob, start, end) {
        return new Uint8Array(await blob.slice(start, end).arrayBuffer());
    }

    function view(bytes) {
        return new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    }

    function u64(dv, offset) {
        return Number(dv.getBigUint64(offset, true));
    }

    //end of central directory, plus the zip64 record when any field overflowed
    async function readDirectoryLocation(blob) {
        const tailSize = Math.min(blob.size, 22 + 65535);
        const tail = await readRange(blob, blob.size - tailSize, blob.size);
        const dv = view(tail);

        let eocd = -1;
        for (let i = tail.length - 22; i >= 0; i--) {
            if (dv.getUint32(i, true) === SIG_EOCD) {
                eocd = i;
                break;
            }
        }
        if (eocd < 0)
            throw new Error("Not a zip archive (no central directory found).");

        let count = dv.getUint16(eocd + 10, true);
        let size = dv.getUint32(eocd + 12, true);
        let offset = dv.getUint32(eocd + 16, true);

        if (count === U16_MAX || size === U32_MAX || offset === U32_MAX) {
            const locatorAt = eocd - 20;
            if (locatorAt >= 0 && dv.getUint32(locatorAt, true) === SIG_ZIP64_LOCATOR) {
                const recordOffset = u64(dv, locatorAt + 8);
                const record = view(await readRange(blob, recordOffset, recordOffset + 56));
                if (record.getUint32(0, true) !== SIG_ZIP64_EOCD)
                    throw new Error("Damaged zip64 directory record.");
                count = u64(record, 32);
                size = u64(record, 40);
                offset = u64(record, 48);
            }
        }
        return { count, size, offset };
    }

    function readZip64Extra(extra, entry) {
        const dv = view(extra);
        let p = 0;
        while (p + 4 <= extra.length) {
            const id = dv.getUint16(p, true);
            const len = dv.getUint16(p + 2, true);
            if (id === 0x0001) {
                let q = p + 4;
                const end = q + len;
                if (entry.uncompressedSize === U32_MAX && q + 8 <= end) { entry.uncompressedSize = u64(dv, q); q += 8; }
                if (entry.compressedSize === U32_MAX && q + 8 <= end) { entry.compressedSize = u64(dv, q); q += 8; }
                if (entry.localOffset === U32_MAX && q + 8 <= end) { entry.localOffset = u64(dv, q); }
                return;
            }
            p += 4 + len;
        }
    }

    //central directory listing; names are decoded as utf-8 like .net's ZipFile, raw bytes kept for re-writing
    async function list(blob) {
        const loc = await readDirectoryLocation(blob);
        const dir = await readRange(blob, loc.offset, loc.offset + loc.size);
        const dv = view(dir);
        const entries = [];
        let p = 0;
        while (p + 46 <= dir.length && dv.getUint32(p, true) === SIG_CENTRAL) {
            const nameLen = dv.getUint16(p + 28, true);
            const extraLen = dv.getUint16(p + 30, true);
            const commentLen = dv.getUint16(p + 32, true);
            const rawName = dir.slice(p + 46, p + 46 + nameLen);
            const entry = {
                versionMadeBy: dv.getUint16(p + 4, true),
                versionNeeded: dv.getUint16(p + 6, true),
                flags: dv.getUint16(p + 8, true),
                method: dv.getUint16(p + 10, true),
                time: dv.getUint16(p + 12, true),
                date: dv.getUint16(p + 14, true),
                crc: dv.getUint32(p + 16, true),
                compressedSize: dv.getUint32(p + 20, true),
                uncompressedSize: dv.getUint32(p + 24, true),
                internalAttrs: dv.getUint16(p + 36, true),
                externalAttrs: dv.getUint32(p + 38, true),
                localOffset: dv.getUint32(p + 42, true),
                rawName,
                name: utf8.decode(rawName).replace(/\\/g, "/"),
            };
            readZip64Extra(dir.slice(p + 46 + nameLen, p + 46 + nameLen + extraLen), entry);
            entry.isDirectory = entry.name.endsWith("/");
            entries.push(entry);
            p += 46 + nameLen + extraLen + commentLen;
        }
        if (entries.length === 0 && loc.count > 0)
            throw new Error("The archive's central directory is damaged.");
        return entries;
    }

    //where the entry's stored (still compressed) bytes start - the local header's own name/extra
    //lengths can differ from the central directory's, so it's read rather than assumed
    async function dataStart(blob, entry) {
        const header = view(await readRange(blob, entry.localOffset, entry.localOffset + 30));
        if (header.getUint32(0, true) !== SIG_LOCAL)
            throw new Error(`Damaged local header for '${entry.name}'.`);
        return entry.localOffset + 30 + header.getUint16(26, true) + header.getUint16(28, true);
    }

    async function storedData(blob, entry) {
        const start = await dataStart(blob, entry);
        return blob.slice(start, start + entry.compressedSize);
    }

    async function readEntry(blob, entry) {
        if (entry.flags & FLAG_ENCRYPTED)
            throw new Error(`'${entry.name}' is encrypted.`);
        const data = await storedData(blob, entry);
        if (entry.method === 0)
            return new Uint8Array(await data.arrayBuffer());
        if (entry.method === 8) {
            const stream = data.stream().pipeThrough(new DecompressionStream("deflate-raw"));
            return new Uint8Array(await new Response(stream).arrayBuffer());
        }
        throw new Error(`'${entry.name}' uses a compression method (${entry.method}) this browser version can't read.`);
    }

    async function deflate(bytes) {
        const stream = new Blob([bytes]).stream().pipeThrough(new CompressionStream("deflate-raw"));
        return new Uint8Array(await new Response(stream).arrayBuffer());
    }

    function dosDateTime(d) {
        const year = Math.max(1980, d.getFullYear());
        return {
            date: ((year - 1980) << 9) | ((d.getMonth() + 1) << 5) | d.getDate(),
            time: (d.getHours() << 11) | (d.getMinutes() << 5) | (d.getSeconds() >> 1),
        };
    }

    function zip64Extra(values) {
        const extra = new Uint8Array(4 + values.length * 8);
        const dv = view(extra);
        dv.setUint16(0, 0x0001, true);
        dv.setUint16(2, values.length * 8, true);
        values.forEach((v, i) => dv.setBigUint64(4 + i * 8, BigInt(v), true));
        return extra;
    }

    //source entries, minus directories and keepEntry rejects, copied through verbatim, plus one new
    //deflated entry appended at the end - the same entry set the desktop app's cbz save produces
    async function rewrite(blob, entries, keepEntry, newName, newBytes) {
        const parts = [];
        const central = [];
        let offset = 0;
        const now = dosDateTime(new Date());
        const encoder = new TextEncoder();

        function addEntry(meta, data) {
            const needs64 = meta.compressedSize >= U32_MAX || meta.uncompressedSize >= U32_MAX;
            const localExtra = needs64 ? zip64Extra([meta.uncompressedSize, meta.compressedSize]) : new Uint8Array(0);
            const header = new Uint8Array(30);
            const dv = view(header);
            dv.setUint32(0, SIG_LOCAL, true);
            dv.setUint16(4, needs64 ? Math.max(45, meta.versionNeeded) : meta.versionNeeded, true);
            dv.setUint16(6, meta.flags, true);
            dv.setUint16(8, meta.method, true);
            dv.setUint16(10, meta.time, true);
            dv.setUint16(12, meta.date, true);
            dv.setUint32(14, meta.crc, true);
            dv.setUint32(18, needs64 ? U32_MAX : meta.compressedSize, true);
            dv.setUint32(22, needs64 ? U32_MAX : meta.uncompressedSize, true);
            dv.setUint16(26, meta.rawName.length, true);
            dv.setUint16(28, localExtra.length, true);
            parts.push(header, meta.rawName, localExtra, data);
            central.push({ meta, offset });
            offset += 30 + meta.rawName.length + localExtra.length + meta.compressedSize;
        }

        for (const entry of entries) {
            if (entry.isDirectory || !keepEntry(entry))
                continue;
            const name = entry.rawName.map((b) => (b === 0x5c ? 0x2f : b));
            addEntry({
                ...entry,
                rawName: name,
                //sizes and crc now live in the local header, so the trailing descriptor is dropped
                flags: entry.flags & ~FLAG_DATA_DESCRIPTOR,
            }, await storedData(blob, entry));
        }

        const compressed = await deflate(newBytes);
        addEntry({
            versionMadeBy: 20,
            versionNeeded: 20,
            flags: FLAG_UTF8,
            method: 8,
            time: now.time,
            date: now.date,
            crc: crc32(newBytes),
            compressedSize: compressed.length,
            uncompressedSize: newBytes.length,
            internalAttrs: 0,
            externalAttrs: 0,
            rawName: encoder.encode(newName),
        }, compressed);

        const dirStart = offset;
        for (const { meta, offset: at } of central) {
            const sizes64 = meta.compressedSize >= U32_MAX || meta.uncompressedSize >= U32_MAX;
            const offset64 = at >= U32_MAX;
            const extraValues = [];
            if (sizes64)
                extraValues.push(meta.uncompressedSize, meta.compressedSize);
            if (offset64)
                extraValues.push(at);
            const extra = extraValues.length ? zip64Extra(extraValues) : new Uint8Array(0);

            const record = new Uint8Array(46);
            const dv = view(record);
            dv.setUint32(0, SIG_CENTRAL, true);
            dv.setUint16(4, meta.versionMadeBy, true);
            dv.setUint16(6, extra.length ? Math.max(45, meta.versionNeeded) : meta.versionNeeded, true);
            dv.setUint16(8, meta.flags, true);
            dv.setUint16(10, meta.method, true);
            dv.setUint16(12, meta.time, true);
            dv.setUint16(14, meta.date, true);
            dv.setUint32(16, meta.crc, true);
            dv.setUint32(20, sizes64 ? U32_MAX : meta.compressedSize, true);
            dv.setUint32(24, sizes64 ? U32_MAX : meta.uncompressedSize, true);
            dv.setUint16(28, meta.rawName.length, true);
            dv.setUint16(30, extra.length, true);
            dv.setUint16(36, meta.internalAttrs, true);
            dv.setUint32(38, meta.externalAttrs, true);
            dv.setUint32(42, offset64 ? U32_MAX : at, true);
            parts.push(record, meta.rawName, extra);
            offset += 46 + meta.rawName.length + extra.length;
        }
        const dirSize = offset - dirStart;
        const count = central.length;

        if (count >= U16_MAX || dirStart >= U32_MAX || dirSize >= U32_MAX) {
            const record = new Uint8Array(56);
            const dv = view(record);
            dv.setUint32(0, SIG_ZIP64_EOCD, true);
            dv.setBigUint64(4, 44n, true);
            dv.setUint16(12, 45, true);
            dv.setUint16(14, 45, true);
            dv.setBigUint64(24, BigInt(count), true);
            dv.setBigUint64(32, BigInt(count), true);
            dv.setBigUint64(40, BigInt(dirSize), true);
            dv.setBigUint64(48, BigInt(dirStart), true);

            const locator = new Uint8Array(20);
            const lv = view(locator);
            lv.setUint32(0, SIG_ZIP64_LOCATOR, true);
            lv.setBigUint64(8, BigInt(offset), true);
            lv.setUint32(16, 1, true);
            parts.push(record, locator);
        }

        const eocd = new Uint8Array(22);
        const ev = view(eocd);
        ev.setUint32(0, SIG_EOCD, true);
        ev.setUint16(8, Math.min(count, U16_MAX), true);
        ev.setUint16(10, Math.min(count, U16_MAX), true);
        ev.setUint32(12, Math.min(dirSize, U32_MAX), true);
        ev.setUint32(16, Math.min(dirStart, U32_MAX), true);
        parts.push(eocd);

        return new Blob(parts, { type: "application/vnd.comicbook+zip" });
    }

    //"PK" zip, "Rar!" rar - magic bytes first, the extension only as a fallback
    async function sniff(blob, name) {
        const magic = await readRange(blob, 0, 4);
        if (magic.length >= 2 && magic[0] === 0x50 && magic[1] === 0x4b)
            return "cbz";
        if (magic.length >= 4 && magic[0] === 0x52 && magic[1] === 0x61 && magic[2] === 0x72 && magic[3] === 0x21)
            return "cbr";
        const ext = (name.match(/\.[^.]*$/) || [""])[0].toLowerCase();
        if (ext === ".cbz" || ext === ".zip")
            return "cbz";
        if (ext === ".cbr" || ext === ".rar")
            return "cbr";
        return "unknown";
    }

    const api = { list, readEntry, rewrite, sniff, crc32 };
    root.LiteZip = api;
    if (typeof module !== "undefined")
        module.exports = api;
})(typeof window !== "undefined" ? window : globalThis);
