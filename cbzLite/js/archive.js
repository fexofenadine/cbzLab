//archive reading on top of LiteZip, mirroring ArchiveService.cs: page counting, junk filtering,
//natural-sorted cover choice, and the cbz save
(function (root) {
    "use strict";

    const COMICINFO = "comicinfo.xml";

    function baseName(path) {
        return path.split("/").pop();
    }

    function extension(name) {
        const m = name.match(/\.[^.]*$/);
        return m ? m[0].toLowerCase() : "";
    }

    //compares digit runs as numbers so "2.jpg" sorts before "10.jpg"
    function naturalCompare(a, b) {
        const pa = a.split(/(\d+)/);
        const pb = b.split(/(\d+)/);
        const len = Math.min(pa.length, pb.length);
        for (let i = 0; i < len; i++) {
            const x = pa[i];
            const y = pb[i];
            let cmp;
            if (x.length > 0 && y.length > 0 && /\d/.test(x[0]) && /\d/.test(y[0])) {
                const nx = BigInt(x);
                const ny = BigInt(y);
                cmp = nx < ny ? -1 : nx > ny ? 1 : 0;
            } else {
                cmp = x < y ? -1 : x > y ? 1 : 0;
            }
            if (cmp !== 0)
                return cmp;
        }
        return pa.length - pb.length;
    }

    //os droppings that are never pages, even with an image extension
    function isJunkEntry(key) {
        const parts = key.replace(/\\/g, "/").split("/").filter((p) => p.length > 0);
        if (parts.length === 0)
            return false;
        if (parts.some((p) => p.toLowerCase() === "__macosx"))
            return true;
        const name = parts[parts.length - 1];
        const lower = name.toLowerCase();
        return name.startsWith("._") || lower === ".ds_store" || lower === "thumbs.db" || lower === "desktop.ini";
    }

    function isComicInfo(entry) {
        return baseName(entry.name).toLowerCase() === COMICINFO;
    }

    //everything an open needs: the raw xml, page count, and which entry to use as the cover
    async function read(blob, name, imageExtensions, coverSource) {
        const format = await LiteZip.sniff(blob, name);
        if (format === "cbr")
            throw new LiteArchive.CbrError();
        if (format !== "cbz")
            throw new Error("Not a comic archive this browser version can read.");

        const entries = await LiteZip.list(blob);
        let xmlEntry = null;
        const images = [];
        for (const entry of entries) {
            if (entry.isDirectory)
                continue;
            const fileName = baseName(entry.name);
            if (!xmlEntry && fileName.toLowerCase() === COMICINFO)
                xmlEntry = entry;
            else if (imageExtensions.includes(extension(fileName)) && !isJunkEntry(entry.name))
                images.push(entry);
        }

        images.sort((a, b) => naturalCompare(a.name, b.name));
        const cover = images.length === 0 ? null : (coverSource === "last" ? images[images.length - 1] : images[0]);
        const xml = xmlEntry ? await LiteZip.readEntry(blob, xmlEntry) : null;
        return { format, entries, xml, pageCount: images.length, coverEntry: cover };
    }

    async function readCover(blob, entry) {
        const bytes = await LiteZip.readEntry(blob, entry);
        return new Blob([bytes]);
    }

    //a new cbz: every entry but directories and ComicInfo.xml copied verbatim, the new xml last
    async function save(blob, entries, xmlBytes) {
        return LiteZip.rewrite(blob, entries, (e) => !isComicInfo(e), "ComicInfo.xml", xmlBytes);
    }

    class CbrError extends Error {
        constructor() {
            super("CBR (RAR) archives can't be read in a browser.");
            this.name = "CbrError";
        }
    }

    root.LiteArchive = { read, readCover, save, naturalCompare, isJunkEntry, CbrError };
})(typeof window !== "undefined" ? window : globalThis);
