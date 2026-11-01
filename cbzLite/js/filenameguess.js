//best-effort Series/Number/Volume/Year/Count from a file's path, mirroring FilenameGuessService.cs.
//the regexes spell out .NET's unicode \d and \b, which plain javascript regexes keep to ascii
(function (root) {
    "use strict";

    const D = "\\p{Nd}";
    //.NET's \w
    const W = "[\\p{L}\\p{Mn}\\p{Nd}\\p{Pc}]";
    const START = `(?<!${W})`;
    const END = `(?!${W})`;

    const YEAR = new RegExp(`[\\(\\[](19|20)${D}{2}[\\)\\]]`, "u");
    const COUNT = new RegExp(`\\(\\s*of\\s*(${D}+)\\s*\\)`, "iu");
    const BRACKETED = /\([^()]*\)|\[[^[\]]*\]|\{[^{}]*\}/gu;
    const VOLUME = new RegExp(`${START}(?:Vol(?:ume)?)\\.?\\s*#?(${D}+)${END}`, "iu");
    const HASH = new RegExp(`#(${D}+)`, "u");
    const BARE_NUMBER = new RegExp(`${START}${D}{1,4}${END}`, "gu");
    const ALL_DIGITS = new RegExp(`^${D}+$`, "u");

    //leftovers made entirely of these ("issue_01.cbz" -> "issue") fall back to the folder name
    const GENERIC_TOKENS = new Set([
        "issue", "issues", "chapter", "chap", "ch", "part", "pt", "page", "pg",
        "vol", "volume", "no", "num", "number", "comic", "comics", "book",
        "file", "untitled", "unknown", "unnamed",
    ].map((t) => t.toUpperCase()));

    //path uses "/" between folders; a file opened on its own has no folder to fall back on
    function fromPath(path) {
        const parts = path.split("/");
        const fileName = parts[parts.length - 1];
        const folderName = parts.length > 1 ? parts[parts.length - 2] : "";
        const dot = fileName.lastIndexOf(".");
        let working = normalizeSeparators(dot >= 0 ? fileName.slice(0, dot) : fileName);

        //year: four digits in parentheses or square brackets, e.g. "(1940)"
        let year = null;
        let m = working.match(YEAR);
        if (m) {
            year = m[0].replace(/^[([]+|[)\]]+$/g, "");
            working = remove(working, m);
        }

        //miniseries length, "Watchmen 01 (of 12)" - taken before the issue number, since its
        //number would otherwise be the last numeric token and win
        let count = null;
        m = working.match(COUNT);
        if (m) {
            count = normalizeNumber(m[1]);
            working = remove(working, m);
        }

        //every other bracketed group is release noise, never part of the series name
        working = working.replace(BRACKETED, " ");

        //explicit "Vol"/"Volume" prefix only - a bare "V2" is too ambiguous (e.g. "V for Vendetta")
        let volume = null;
        m = working.match(VOLUME);
        if (m) {
            volume = normalizeNumber(m[1]);
            working = remove(working, m);
        }

        //"#123" first, else the last standalone numeric token
        let number = null;
        m = working.match(HASH);
        if (m) {
            number = normalizeNumber(m[1]);
            working = remove(working, m);
        } else {
            const all = [...working.matchAll(BARE_NUMBER)];
            if (all.length > 0) {
                const last = all[all.length - 1];
                number = normalizeNumber(last[0]);
                working = remove(working, last);
            }
        }

        let series = cleanSeries(working);
        if (!isUsableSeries(series)) {
            const folderCandidate = cleanSeries(normalizeSeparators(folderName));
            series = isUsableSeries(folderCandidate) ? folderCandidate : null;
        }
        return { series, number, volume, year, count };
    }

    function remove(text, match) {
        return text.slice(0, match.index) + text.slice(match.index + match[0].length);
    }

    function normalizeSeparators(raw) {
        return raw.replace(/[._]+/g, " ");
    }

    function cleanSeries(raw) {
        //empty brackets left behind by a stripped token
        let s = raw.replace(/\(\s*\)|\[\s*\]/gu, "");
        s = s.replace(/\s{2,}/gu, " ");
        return s.replace(/^[ \-_.]+|[ \-_.]+$/g, "");
    }

    //strips leading zeros used for filename sort order ("001" -> "1"); "000" -> "0"
    function normalizeNumber(raw) {
        const trimmed = raw.replace(/^0+/, "");
        return trimmed.length === 0 ? "0" : trimmed;
    }

    function isUsableSeries(s) {
        if (!s || s.trim().length === 0 || s.length < 2 || ALL_DIGITS.test(s))
            return false;
        const words = s.split(" ").filter((w) => w.length > 0);
        return !words.every((w) => GENERIC_TOKENS.has(w.toUpperCase()));
    }

    root.LiteGuess = { fromPath };
})(typeof window !== "undefined" ? window : globalThis);
