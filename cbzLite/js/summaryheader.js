//a "Publisher - Year" line lifted off the top of a Summary, mirroring SummaryHeaderParser.cs
(function (root) {
    "use strict";

    //the separator can be a hyphen or either of the longer dashes, since all three turn up in the wild
    const HEADER_LINE = /^(?<publisher>\S.*?)\s*[-\u2013\u2014]\s*(?<year>\p{Nd}{4})$/u;

    //strict on purpose: the header must be the whole first line, end in a four digit year, and be
    //followed by a blank line or nothing. a false positive rewrites someone's summary, so a missed
    //header (which leaves the text alone) is much the cheaper mistake. returns null when there's none
    function tryParse(summary) {
        if (!summary || summary.trim().length === 0)
            return null;

        const lines = summary.replace(/\r\n/g, "\n").replace(/\r/g, "\n").trim().split("\n");
        const match = lines[0].trim().match(HEADER_LINE);
        if (!match)
            return null;

        //a second line of prose means this was a sentence that happened to end in a year
        if (lines.length > 1 && lines[1].trim().length > 0)
            return null;

        return {
            publisher: match.groups.publisher.trim(),
            year: match.groups.year,
            summary: lines.slice(1).join("\n").trim(),
        };
    }

    root.LiteSummaryHeader = { tryParse };
})(typeof window !== "undefined" ? window : globalThis);
