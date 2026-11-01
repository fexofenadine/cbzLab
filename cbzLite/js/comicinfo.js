//ComicInfo.xml parse/build, mirroring the desktop app's ComicInfoXml.cs: complex elements such as
//<Pages> survive untouched, new elements go in schema order, output is re-indented the way .net's
//XmlWriter does it
(function (root) {
    "use strict";

    const XSD = "http://www.w3.org/2001/XMLSchema";
    const XSI = "http://www.w3.org/2001/XMLSchema-instance";
    const PARSER_ERROR_NS = [
        "http://www.w3.org/1999/xhtml",
        "http://www.mozilla.org/newlayout/xml/parsererror.xml",
    ];

    //the ComicInfo v2.0 xs:sequence - a strict reader can reject out-of-order elements
    const SCHEMA_ORDER = [
        "Title", "Series", "Number", "Count", "Volume", "AlternateSeries", "AlternateNumber", "AlternateCount",
        "Summary", "Notes", "Year", "Month", "Day", "Writer", "Penciller", "Inker", "Colorist", "Letterer",
        "CoverArtist", "Editor", "Publisher", "Imprint", "Genre", "Web", "PageCount", "LanguageISO", "Format",
        "BlackAndWhite", "Manga", "Characters", "Teams", "Locations", "ScanInformation", "StoryArc", "SeriesGroup",
        "AgeRating", "Pages", "CommunityRating", "MainCharacterOrTeam", "Review",
    ];

    //matches the windows desktop build's XmlWriter, which uses Environment.NewLine
    const NEWLINE = "\r\n";

    //bom first, then the declared encoding, then utf-8 - same order an XmlReader detects it in
    function decode(bytes) {
        if (bytes.length >= 3 && bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf)
            return new TextDecoder("utf-8").decode(bytes.subarray(3));
        if (bytes.length >= 2 && bytes[0] === 0xff && bytes[1] === 0xfe)
            return new TextDecoder("utf-16le").decode(bytes.subarray(2));
        if (bytes.length >= 2 && bytes[0] === 0xfe && bytes[1] === 0xff)
            return new TextDecoder("utf-16be").decode(bytes.subarray(2));
        const head = new TextDecoder("latin1").decode(bytes.subarray(0, 200));
        const declared = head.match(/^<\?xml[^>]*encoding\s*=\s*["']([^"']+)["']/i);
        if (declared) {
            try {
                return new TextDecoder(declared[1]).decode(bytes);
            } catch {
                //unknown label, fall through to utf-8
            }
        }
        return new TextDecoder("utf-8").decode(bytes);
    }

    //null for anything the desktop app would refuse to load: malformed xml, or any dtd at all
    //(it loads with DtdProcessing.Prohibit, since archive contents are untrusted)
    function load(bytes) {
        const doc = new DOMParser().parseFromString(decode(bytes), "application/xml");
        for (const ns of PARSER_ERROR_NS) {
            if (doc.getElementsByTagNameNS(ns, "parsererror").length > 0)
                return null;
        }
        if (doc.doctype || !doc.documentElement)
            return null;
        return doc;
    }

    function newDocument() {
        const doc = document.implementation.createDocument(null, "ComicInfo", null);
        doc.documentElement.setAttribute("xmlns:xsd", XSD);
        doc.documentElement.setAttribute("xmlns:xsi", XSI);
        return doc;
    }

    function childElements(el) {
        return Array.from(el.children);
    }

    function isLeaf(el) {
        return el.children.length === 0;
    }

    //first occurrence wins, matching build(), which edits the first matching element
    function parse(bytes) {
        const values = new Map();
        if (!bytes || bytes.length === 0)
            return values;
        const doc = load(bytes);
        if (!doc)
            return values;
        for (const el of childElements(doc.documentElement)) {
            if (isLeaf(el) && !values.has(el.localName))
                values.set(el.localName, el.textContent);
        }
        return values;
    }

    function isUnreadable(bytes) {
        return !!bytes && bytes.length > 0 && !load(bytes);
    }

    //existing elements are never moved - only the new one is placed, before the first element that belongs after it
    function insertInSchemaOrder(rootEl, element) {
        const position = SCHEMA_ORDER.indexOf(element.localName);
        if (position >= 0) {
            const next = childElements(rootEl).find((e) => SCHEMA_ORDER.indexOf(e.localName) > position);
            if (next) {
                rootEl.insertBefore(element, next);
                return;
            }
        }
        rootEl.appendChild(element);
    }

    //values is an ordered list of [tag, value]; an empty value removes its element
    function build(originalBytes, values) {
        let doc = originalBytes && originalBytes.length > 0 ? load(originalBytes) : null;
        if (!doc)
            doc = newDocument();
        const rootEl = doc.documentElement;
        const ns = rootEl.namespaceURI;

        for (const [tag, value] of values) {
            const el = childElements(rootEl).find((e) => e.localName === tag && isLeaf(e));
            if (!value) {
                if (el)
                    el.remove();
            } else if (el) {
                el.textContent = value;
            } else {
                const created = ns ? doc.createElementNS(ns, tag) : doc.createElement(tag);
                created.textContent = value;
                insertInSchemaOrder(rootEl, created);
            }
        }
        return new TextEncoder().encode(serialise(doc));
    }

    function escapeText(s) {
        return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    }

    function escapeAttr(s) {
        return escapeText(s).replace(/"/g, "&quot;").replace(/\r/g, "&#xD;").replace(/\n/g, "&#xA;").replace(/\t/g, "&#x9;");
    }

    //whitespace-only text is dropped, as XDocument.Load does by default, so the output is freshly indented
    function significantChildren(node) {
        return Array.from(node.childNodes).filter((c) =>
            !(c.nodeType === Node.TEXT_NODE && c.nodeValue.trim() === ""));
    }

    function serialiseNode(node, depth, indent, out) {
        const pad = indent ? NEWLINE + "  ".repeat(depth) : "";
        switch (node.nodeType) {
            case Node.ELEMENT_NODE: {
                out.push(pad, "<", node.tagName);
                for (const attr of Array.from(node.attributes))
                    out.push(" ", attr.name, '="', escapeAttr(attr.value), '"');
                const children = significantChildren(node);
                if (children.length === 0) {
                    out.push(" />");
                    return;
                }
                out.push(">");
                //like XmlWriter, content containing text is written inline, not indented
                const mixed = children.some((c) => c.nodeType === Node.TEXT_NODE || c.nodeType === Node.CDATA_SECTION_NODE);
                for (const child of children)
                    serialiseNode(child, depth + 1, !mixed, out);
                if (!mixed)
                    out.push(NEWLINE + "  ".repeat(depth));
                out.push("</", node.tagName, ">");
                return;
            }
            case Node.TEXT_NODE:
                out.push(escapeText(node.nodeValue));
                return;
            case Node.CDATA_SECTION_NODE:
                out.push("<![CDATA[", node.nodeValue, "]]>");
                return;
            case Node.COMMENT_NODE:
                out.push(pad, "<!--", node.nodeValue, "-->");
                return;
            case Node.PROCESSING_INSTRUCTION_NODE:
                out.push(pad, "<?", node.target, " ", node.data, "?>");
                return;
        }
    }

    function serialise(doc) {
        const out = ['<?xml version="1.0" encoding="utf-8"?>'];
        for (const child of Array.from(doc.childNodes))
            serialiseNode(child, 0, true, out);
        return out.join("");
    }

    function toDisplayString(originalBytes, values) {
        return new TextDecoder("utf-8").decode(build(originalBytes, values));
    }

    root.LiteComicInfo = { parse, build, isUnreadable, toDisplayString };
})(typeof window !== "undefined" ? window : globalThis);
