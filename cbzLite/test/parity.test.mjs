//runs the shared parity fixtures against cbzLite's ports; cbzLab.Tests runs the same files
//against the desktop originals. run with: node --test cbzLite/test/
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { runInThisContext } from "node:vm";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const lite = path.join(here, "..");
const read = (p) => readFileSync(p, "utf-8");

for (const name of ["schema.js", "validation.js", "datefield.js", "filenameguess.js", "summaryheader.js"])
    runInThisContext(read(path.join(lite, "js", name)), { filename: name });

const { LiteSchema, LiteValidation, LiteDate, LiteGuess, LiteSummaryHeader } = globalThis;
const schema = new LiteSchema.Schema(JSON.parse(read(path.join(lite, "..", "cbzLab", "Assets", "schema.json"))));
const fixture = (name) => JSON.parse(read(path.join(here, "parity", name)));

test("validation matches the shared fixtures", () => {
    for (const c of fixture("validation.json").cases) {
        const check = LiteValidation.checkField(schema, c.tag, c.value);
        const label = `${c.tag}='${c.value}'`;
        assert.equal(check ? check.problem : null, c.problem, label);
        if (check)
            assert.equal(check.suggestion, c.suggestion, label);
    }
});

test("date parsing matches the shared fixtures", () => {
    const doc = fixture("dates.json");
    for (const c of doc.parse)
        assert.deepEqual(LiteDate.parse(c.input, doc.culture), c.expected, `'${c.input}'`);
});

test("date display matches the shared fixtures", () => {
    const doc = fixture("dates.json");
    for (const c of doc.format)
        assert.equal(LiteDate.formatForDisplay(c.year, c.month, c.day, doc.culture), c.expected, JSON.stringify(c));
});

test("filename guessing matches the shared fixtures", () => {
    for (const c of fixture("tools.json").guess) {
        const { series, number, volume, year, count } = LiteGuess.fromPath(c.path);
        assert.deepEqual({ series, number, volume, year, count },
            { series: c.series, number: c.number, volume: c.volume, year: c.year, count: c.count }, c.path);
    }
});

test("summary header parsing matches the shared fixtures", () => {
    for (const c of fixture("tools.json").summaryHeader) {
        const header = LiteSummaryHeader.tryParse(c.summary);
        const label = JSON.stringify(c.summary);
        if (c.publisher === null) {
            assert.equal(header, null, label);
            continue;
        }
        assert.deepEqual(header, { publisher: c.publisher, year: c.year, summary: c.rest }, label);
    }
});
