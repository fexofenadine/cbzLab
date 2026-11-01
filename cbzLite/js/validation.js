//type validation driven by schema.json's constraints block, mirroring ValidationService.cs;
//empty values are always valid
(function (root) {
    "use strict";

    const LONG_MIN = -9223372036854775808n;
    const LONG_MAX = 9223372036854775807n;
    const INT_MAX = 2147483647;

    //long.TryParse with NumberStyles.Integer after Trim(): optional sign, ascii digits, must fit in a long
    function parseLong(text) {
        if (!/^[+-]?[0-9]+$/.test(text))
            return null;
        const n = BigInt(text);
        return n < LONG_MIN || n > LONG_MAX ? null : n;
    }

    //double.TryParse with NumberStyles.Float and the invariant culture: no thousands separators or hex,
    //but the .NET infinity/nan words are accepted
    function parseDouble(text) {
        if (/^[+-]?([0-9]+\.?[0-9]*|\.[0-9]+)([eE][+-]?[0-9]+)?$/.test(text))
            return parseFloat(text);
        const word = text.toLowerCase();
        if (word === "infinity" || word === "+infinity" || word === "∞" || word === "+∞")
            return Infinity;
        if (word === "-infinity" || word === "-∞")
            return -Infinity;
        if (word === "nan" || word === "+nan" || word === "-nan")
            return NaN;
        return null;
    }

    //double.ToString() as .NET prints it: shortest round-trip digits, E notation outside 1e-5..1e15
    function formatDouble(x) {
        if (Number.isNaN(x))
            return "NaN";
        if (!Number.isFinite(x))
            return x > 0 ? "∞" : "-∞";
        if (x === 0)
            return Object.is(x, -0) ? "-0" : "0";
        const [mantissa, exp] = x.toExponential().split("e");
        const e = parseInt(exp, 10);
        if (e >= 15 || e < -5)
            return `${mantissa}E${e < 0 ? "-" : "+"}${String(Math.abs(e)).padStart(2, "0")}`;
        return String(x);
    }

    //returns {problem, suggestion} or null, for live feedback and save-time checks alike
    function checkField(schema, tag, value) {
        if (!value || value.trim().length === 0)
            return null;
        const c = schema.constraints;
        const intFields = c.int_fields || [];
        const floatFields = c.float_fields || [];

        if (intFields.includes(tag)) {
            const hint = (c.int_hints || {})[tag] || "a whole number";
            const number = parseLong(value.trim());
            if (number === null)
                return { problem: `'${value}' is not a whole number.`, suggestion: `Enter ${hint}.` };
            const r = (c.int_ranges || {})[tag];
            if (Array.isArray(r) && r.length === 2 && (number < BigInt(r[0]) || number > BigInt(r[1]))) {
                const allowed = r[1] >= INT_MAX ? `${r[0]} or more` : `a value from ${r[0]} to ${r[1]}`;
                return { problem: `${number} is outside the allowed range.`, suggestion: `Enter ${allowed}.` };
            }
            return null;
        }

        if (floatFields.includes(tag)) {
            const parsed = parseDouble(value.trim());
            if (parsed === null)
                return { problem: `'${value}' is not a number.`, suggestion: "Enter a decimal number, e.g. 4.5." };
            const range = (c.float_ranges || {})[tag];
            if (Array.isArray(range) && range.length === 2 && (parsed < range[0] || parsed > range[1])) {
                return {
                    problem: `${formatDouble(parsed)} is outside the allowed range.`,
                    suggestion: `Enter a value between ${formatDouble(range[0])} and ${formatDouble(range[1])}.`,
                };
            }
            return null;
        }

        return null;
    }

    //every problem in one file's values, int fields then float fields, in schema constraint order
    function validate(schema, fileName, values) {
        const c = schema.constraints;
        const errors = [];
        for (const tag of [...(c.int_fields || []), ...(c.float_fields || [])]) {
            if (!values.has(tag))
                continue;
            const check = checkField(schema, tag, values.get(tag));
            if (check) {
                const field = schema.byTag.get(tag);
                errors.push({ fileName, tag, label: field ? field.label : tag, ...check });
            }
        }
        return errors;
    }

    root.LiteValidation = { checkField, validate, formatDouble };
})(typeof window !== "undefined" ? window : globalThis);
