//field schema and tab assignment, mirroring SchemaService.cs; reads the same schema.json the
//desktop app ships, inlined into the page at build time
(function (root) {
    "use strict";

    const TAB_ORDER = ["Basic Info", "Publication", "Creators", "Story", "Extras"];

    //explicit tag to tab assignment; anything not listed lands in Extras
    const TAB_MAP = {
        Title: "Basic Info", Series: "Basic Info", Number: "Basic Info", Count: "Basic Info",
        Volume: "Basic Info", AlternateSeries: "Basic Info", AlternateNumber: "Basic Info",
        AlternateCount: "Basic Info", Summary: "Basic Info", Notes: "Basic Info",
        Publisher: "Publication", Imprint: "Publication", Genre: "Publication", Format: "Publication",
        LanguageISO: "Publication", Year: "Publication", Month: "Publication", Day: "Publication",
        PageCount: "Publication", Web: "Publication",
        Writer: "Creators", Penciller: "Creators", Inker: "Creators", Colorist: "Creators",
        Letterer: "Creators", CoverArtist: "Creators", Editor: "Creators",
        Characters: "Story", Teams: "Story", Locations: "Story", StoryArc: "Story",
        SeriesGroup: "Story", MainCharacterOrTeam: "Story", BlackAndWhite: "Story",
        Manga: "Story", ScanInformation: "Story",
        AgeRating: "Extras", CommunityRating: "Extras", Review: "Extras",
    };

    //browser-side equivalent of %APPDATA%\cbzLab\schema_extra.json
    const EXTRA_KEY = "cbzlite.schemaExtra";

    class Schema {
        constructor(doc) {
            this.fields = [];
            this.byTag = new Map();
            this.constraints = doc.constraints || {};
            for (const section of doc.sections || []) {
                for (const f of section.fields || []) {
                    if (!this.byTag.has(f.tag))
                        this.add({ ...f, isExtra: false });
                }
            }
            for (const f of this.loadExtras()) {
                if (!this.byTag.has(f.tag))
                    this.add({ ...f, isExtra: true });
            }
        }

        add(field) {
            field.widget = field.widget || "entry";
            field.tooltip = field.tooltip || "";
            this.fields.push(field);
            this.byTag.set(field.tag, field);
        }

        tabFor(field) {
            return field.isExtra ? "Extras" : (TAB_MAP[field.tag] || "Extras");
        }

        imageExtensions() {
            return this.constraints.image_extensions || [];
        }

        //registers a non-schema tag found in an opened file as an editable Extras field
        registerExtraTag(tag) {
            if (this.byTag.has(tag))
                return false;
            this.add({
                tag,
                label: tag,
                widget: "entry",
                tooltip: `Unofficial field '${tag}' discovered in an opened file.`,
                isExtra: true,
            });
            this.saveExtras();
            return true;
        }

        loadExtras() {
            try {
                const raw = localStorage.getItem(EXTRA_KEY);
                const list = raw ? JSON.parse(raw) : [];
                return Array.isArray(list) ? list.filter((f) => f && typeof f.tag === "string") : [];
            } catch {
                return [];
            }
        }

        saveExtras() {
            try {
                const extras = this.fields.filter((f) => f.isExtra)
                    .map(({ tag, label, widget, tooltip }) => ({ tag, label, widget, tooltip }));
                localStorage.setItem(EXTRA_KEY, JSON.stringify(extras));
            } catch {
                //storage blocked or full - extras just won't survive a reload
            }
        }
    }

    root.LiteSchema = { Schema, TAB_ORDER };
})(typeof window !== "undefined" ? window : globalThis);
