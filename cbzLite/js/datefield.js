//one localised date string to and from ComicInfo's separate Year/Month/Day tags, mirroring
//DateFieldHelper.cs; partial dates (a bare year, or month and year) are kept partial
(function (root) {
    "use strict";

    const INT32_MIN = -2147483648;
    const INT32_MAX = 2147483647;

    const ENGLISH_MONTHS = ["January", "February", "March", "April", "May", "June", "July",
        "August", "September", "October", "November", "December"];
    const ENGLISH_DAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    function defaultLocale() {
        return (typeof navigator !== "undefined" && navigator.language) || "en-GB";
    }

    //int.TryParse: surrounding whitespace and a sign allowed, must fit an int
    function parseInt32(text) {
        if (!/^\s*[+-]?[0-9]+\s*$/.test(text))
            return null;
        const n = Number(text.trim());
        return n < INT32_MIN || n > INT32_MAX ? null : n;
    }

    function daysInMonth(y, m) {
        if (m === 2)
            return isLeap(y) ? 29 : 28;
        return [4, 6, 9, 11].includes(m) ? 30 : 31;
    }

    function isLeap(y) {
        return (y % 4 === 0 && y % 100 !== 0) || y % 400 === 0;
    }

    //what new DateTime(y, m, d) accepts
    function isValidDate(y, m, d) {
        return y >= 1 && y <= 9999 && m >= 1 && m <= 12 && d >= 1 && d <= daysInMonth(y, m);
    }

    function utcDate(y, m, d) {
        const date = new Date(Date.UTC(2000, 0, 1));
        date.setUTCFullYear(y, m - 1, d);
        return date;
    }

    //the locale's numeric short date - the browser's nearest equivalent of .NET's "d" format
    function shortDateFormat(locale) {
        return new Intl.DateTimeFormat(locale, {
            year: "numeric", month: "numeric", day: "numeric",
            timeZone: "UTC", calendar: "gregory", numberingSystem: "latn",
        });
    }

    //empty if Year is empty - a month/day with no year isn't a meaningful partial date
    function formatForDisplay(year, month, day, locale = defaultLocale()) {
        if (year.length === 0)
            return "";

        const y = parseInt32(year);
        const m = parseInt32(month);
        const d = parseInt32(day);
        if (y !== null && m !== null && d !== null && isValidDate(y, m, d))
            return shortDateFormat(locale).format(utcDate(y, m, d)).replace(/[‎‏]/g, "");

        if (y !== null && m !== null && m >= 1 && m <= 12)
            return `${String(m).padStart(2, "0")}/${year}`;

        return year;
    }

    //returns null (leave fields untouched) if the input isn't a full date, bare year, or month and year
    function parse(input, locale = defaultLocale()) {
        input = input.trim();
        if (input.length === 0)
            return ["", "", ""];

        //partial forms first, so a month and year is never completed to the 1st of the month
        if (/^\d{4}$/.test(input))
            return [input, "", ""];

        let m = input.match(/^(\d{1,2})[/\-.](\d{4})$/);
        if (m && monthNumber(m[1]))
            return [m[2], String(monthNumber(m[1])), ""];

        //iso-style year-first "2019-03" / "2019/03"
        m = input.match(/^(\d{4})[/\-.](\d{1,2})$/);
        if (m && monthNumber(m[2]))
            return [m[1], String(monthNumber(m[2])), ""];

        //"March 2019", "Mar 2019", "Sept. 2019" - a month name and a year with no day
        m = input.match(/^([^\d\s,]+)\.?,?\s+(\d{4})$/);
        if (m) {
            const named = monthFromName(m[1], locale);
            if (named)
                return [m[2], String(named), ""];
        }

        //a full date has to spell out a four-digit year, or every half-typed date would write a
        //wrong year before it was finished
        if (!/(?<!\d)\d{4}(?!\d)/.test(input))
            return null;

        const full = parseFullDate(input, locale);
        return full ? [String(full[0]), String(full[1]), String(full[2])] : null;
    }

    function monthNumber(text) {
        const n = parseInt(text, 10);
        return n >= 1 && n <= 12 ? n : null;
    }

    //a stand-in for DateTime.TryParse in the current culture: numeric dates in the locale's own
    //field order (or year first), and day/month-name/year in any order, with an optional weekday.
    //.NET's parser is looser still (times, era names, odd separators) - those aren't attempted
    function parseFullDate(input, locale) {
        if (/[:]/.test(input))
            return null;
        const tokens = input.split(/[\s,/\-.]+/).filter((t) => t.length > 0);
        const numbers = [];
        let month = null;
        let weekday = null;
        for (const token of tokens) {
            if (/^\d+$/.test(token)) {
                numbers.push(token);
                continue;
            }
            const named = monthFromName(token, locale);
            if (named && month === null) {
                month = named;
                continue;
            }
            const day = weekdayFromName(token, locale);
            if (day !== null && weekday === null) {
                weekday = day;
                continue;
            }
            return null;
        }

        let y, mo, d;
        if (month !== null) {
            if (numbers.length !== 2)
                return null;
            const yearIndex = numbers.findIndex((n) => n.length === 4);
            if (yearIndex < 0 || numbers[1 - yearIndex].length > 2)
                return null;
            y = parseInt(numbers[yearIndex], 10);
            mo = month;
            d = parseInt(numbers[1 - yearIndex], 10);
        } else {
            if (numbers.length !== 3)
                return null;
            let order = numericOrder(locale);
            if (numbers[0].length === 4)
                order = "ymd";
            else if (order === "ymd")
                order = "dmy";
            const parts = {};
            for (let i = 0; i < 3; i++)
                parts[order[i]] = numbers[i];
            if (parts.y.length !== 4 || parts.m.length > 2 || parts.d.length > 2)
                return null;
            y = parseInt(parts.y, 10);
            mo = parseInt(parts.m, 10);
            d = parseInt(parts.d, 10);
        }

        if (!isValidDate(y, mo, d))
            return null;
        //.NET rejects a weekday that doesn't match the date
        if (weekday !== null && utcDate(y, mo, d).getUTCDay() !== weekday)
            return null;
        return [y, mo, d];
    }

    //"dmy", "mdy" or "ymd", read off the locale's own short date
    function numericOrder(locale) {
        const order = shortDateFormat(locale).formatToParts(utcDate(2001, 2, 3))
            .map((p) => p.type).filter((t) => t === "day" || t === "month" || t === "year")
            .map((t) => t[0]).join("");
        return order === "mdy" || order === "ymd" ? order : "dmy";
    }

    function namesFor(locale, options, count, makeDate) {
        try {
            const format = new Intl.DateTimeFormat(locale, { ...options, timeZone: "UTC", calendar: "gregory" });
            return Array.from({ length: count }, (_, i) => format.format(makeDate(i)));
        } catch {
            return [];
        }
    }

    const nameCache = new Map();

    function monthNames(locale) {
        const key = "m:" + locale;
        if (!nameCache.has(key)) {
            const date = (i) => utcDate(2001, i + 1, 1);
            nameCache.set(key, [
                namesFor(locale, { month: "long" }, 12, date),
                namesFor(locale, { month: "short" }, 12, date),
            ]);
        }
        return nameCache.get(key);
    }

    function weekdayNames(locale) {
        const key = "d:" + locale;
        if (!nameCache.has(key)) {
            //7 jan 2001 was a sunday, so index i is getUTCDay() i
            const date = (i) => utcDate(2001, 1, 7 + i);
            nameCache.set(key, [
                namesFor(locale, { weekday: "long" }, 7, date),
                namesFor(locale, { weekday: "short" }, 7, date),
            ]);
        }
        return nameCache.get(key);
    }

    const same = (a, b) => a.toUpperCase() === b.toUpperCase();
    const stripDot = (s) => s.replace(/\.+$/, "");

    //the locale's names first, then english, so "March 2019" works on any locale
    function monthFromName(name, locale) {
        name = stripDot(name);
        const [long, short] = monthNames(locale);
        const tables = [[long, short], [ENGLISH_MONTHS, ENGLISH_MONTHS.map((n) => n.slice(0, 3))]];
        for (const [full, abbr] of tables) {
            for (let i = 0; i < 12; i++) {
                if ((full[i] && same(full[i], name)) || (abbr[i] && same(stripDot(abbr[i]), name)))
                    return i + 1;
            }
        }
        //"sept" is a common abbreviation not every table lists
        return same(name, "sept") ? 9 : null;
    }

    function weekdayFromName(name, locale) {
        name = stripDot(name);
        const [long, short] = weekdayNames(locale);
        const tables = [[long, short], [ENGLISH_DAYS, ENGLISH_DAYS.map((n) => n.slice(0, 3))]];
        for (const [full, abbr] of tables) {
            for (let i = 0; i < 7; i++) {
                if ((full[i] && same(full[i], name)) || (abbr[i] && same(stripDot(abbr[i]), name)))
                    return i;
            }
        }
        return null;
    }

    root.LiteDate = { formatForDisplay, parse };
})(typeof window !== "undefined" ? window : globalThis);
