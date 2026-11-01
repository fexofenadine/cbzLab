//one open archive, mirroring ComicFileViewModel.cs: saved vs current values, dirty state, subtitle
(function (root) {
    "use strict";

    class ComicFile {
        //source is a File (or Blob); handle is a FileSystemFileHandle when the browser gave us one,
        //which is what makes an in-place save possible
        //path is the name plus any folders it was opened from, "/"-separated
        constructor({ source, handle, name, path, format, entries, rawXml, values, pageCount, coverEntry }) {
            this.id = ComicFile.nextId++;
            this.source = source;
            this.handle = handle || null;
            this.name = name;
            this.path = path || name;
            this.format = format;
            this.entries = entries;
            this.rawXml = rawXml;
            this.saved = new Map(values);
            this.current = new Map(values);
            this.detectedPageCount = pageCount;
            this.coverEntry = coverEntry;
            this.coverUrl = null;
            this.isDirty = false;
            this.subtitle = "";
            this.updateSubtitle();
        }

        getValue(tag) {
            return this.current.get(tag) || "";
        }

        //empty values are stored as removals
        setValue(tag, value) {
            if (!value)
                this.current.delete(tag);
            else
                this.current.set(tag, value);
            this.afterValueChanged(tag);
        }

        revertField(tag) {
            if (this.saved.has(tag))
                this.current.set(tag, this.saved.get(tag));
            else
                this.current.delete(tag);
            this.afterValueChanged(tag);
        }

        //writes both saved and current, for fills that shouldn't count as an edit
        seedValue(tag, value) {
            if (!value) {
                this.saved.delete(tag);
                this.current.delete(tag);
            } else {
                this.saved.set(tag, value);
                this.current.set(tag, value);
            }
            this.afterValueChanged(tag);
        }

        //wholesale replacement, as for a pasted ComicInfo.xml; left unsaved so it shows as an edit
        replaceCurrentValues(values) {
            this.current = new Map(values);
            this.recomputeDirty();
            this.updateSubtitle();
        }

        //used by Revert: a fresh read of the archive becomes both the baseline and the current values
        reloadFrom({ source, entries, rawXml, values, pageCount, coverEntry }) {
            this.source = source;
            this.entries = entries;
            this.rawXml = rawXml;
            this.saved = new Map(values);
            this.current = new Map(values);
            this.detectedPageCount = pageCount;
            this.coverEntry = coverEntry;
            this.recomputeDirty();
            this.updateSubtitle();
        }

        //drops every unsaved edit, back to what was last opened or saved
        revertAll() {
            this.current = new Map(this.saved);
            this.recomputeDirty();
            this.updateSubtitle();
        }

        afterValueChanged(tag) {
            this.recomputeDirty();
            if (tag === "Series" || tag === "Number" || tag === "Volume")
                this.updateSubtitle();
        }

        markSaved(newRawXml, { source, entries, name } = {}) {
            this.rawXml = newRawXml;
            this.saved = new Map(this.current);
            if (source)
                this.source = source;
            if (entries)
                this.entries = entries;
            if (name) {
                this.path = this.path.slice(0, this.path.lastIndexOf("/") + 1) + name;
                this.name = name;
            }
            this.recomputeDirty();
        }

        //union of saved+current keys, cleared fields as empty strings so their elements are removed
        buildWriteValues() {
            const result = [];
            const seen = new Set();
            for (const key of [...this.saved.keys(), ...this.current.keys()]) {
                if (seen.has(key))
                    continue;
                seen.add(key);
                result.push([key, this.current.get(key) || ""]);
            }
            return result;
        }

        recomputeDirty() {
            const keys = new Set([...this.saved.keys(), ...this.current.keys()]);
            this.isDirty = [...keys].some((k) => (this.saved.get(k) || "") !== (this.current.get(k) || ""));
        }

        //Series+Number -> "Batman #1" (Number beats Volume); Series+Volume -> "Batman - Vol.2"
        updateSubtitle() {
            const series = this.getValue("Series").trim();
            const number = this.getValue("Number").trim();
            const volume = this.getValue("Volume").trim();
            let text;
            if (series && number)
                text = `${series} #${number}`;
            else if (series && volume)
                text = `${series} - Vol.${volume}`;
            else if (series)
                text = series;
            else if (number)
                text = `#${number}`;
            else if (volume)
                text = `Vol.${volume}`;
            else
                text = "no metadata";
            this.subtitle = text;
        }

        get hasMetadata() {
            return this.subtitle !== "no metadata";
        }
    }
    ComicFile.nextId = 1;

    root.LiteModel = { ComicFile };
})(typeof window !== "undefined" ? window : globalThis);
