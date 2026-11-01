//cbzLite ui: the browser counterpart of the desktop app's MainWindow + MainViewModel
(function () {
    "use strict";

    const VERSION = "0.5.1";
    const DESKTOP_URL = "https://github.com/fexofenadine/cbzLab/releases";
    const SITE_URL = "https://fexofenadine.github.io/cbzLab/lite/";
    const SETTINGS_KEY = "cbzlite.settings";
    const SUPPORTED = [".cbz", ".cbr", ".zip", ".rar"];

    //compiled-in solarized dark so a broken themes block can never leave the page unstyled
    const HARD_FALLBACK = {
        bg: "#002b36", bg2: "#073642", bg3: "#073642", sep: "#586e75",
        fg: "#839496", fg_bright: "#93a1a1", accent: "#268bd2",
        entry_bg: "#073642", entry_fg: "#839496", entry_sel: "#586e75",
        lbl_fg: "#93a1a1", section_fg: "#268bd2",
        status_bg: "#073642", status_fg: "#657b83", insert_cur: "#839496",
        disabled_bg: "#002b36", disabled_fg: "#586e75",
        scrollbar: "#073642", thumb: "#586e75",
        error_bg: "#3d1010", error_fg: "#dc322f", error_lbl: "#dc322f",
        search_bg: "#073642", search_fg: "#839496",
        tooltip_bg: "#073642", tooltip_fg: "#93a1a1",
        dirty_fg: "#b58900", mixed_fg: "#6c71c4",
        list_sel: "#073642", list_sel_fg: "#268bd2",
        list_bg: "#002b36", list_fg: "#839496", list_dirty: "#b58900",
        batch_bg: "#001f2a",
    };
    const FALLBACK_THEME = "Solarized Dark";

    //defaults match the desktop AppSettings
    const DEFAULT_SETTINGS = {
        showAllFieldsDefault: false,
        showExtraFieldsDefault: true,
        autoPageCount: true,
        autoSelectFirstOnOpen: true,
        coverSource: "first",
        editorFontSize: 14,
        editorFontFamily: "Segoe UI",
        activeTab: 0,
        sortMode: "name",
        sidebarWidth: 260,
        //"keystroke", "blur", or "off"; save-time validation runs regardless
        liveValidationMode: "keystroke",
        theme: FALLBACK_THEME,
        editorFieldsFillWidth: false,
        rememberLastTab: true,
        compactDensity: false,
        clearFilterOnOpen: false,
        gridColumns: ["Series", "Number", "Writer", "Publisher"],
        toolbarButtons: ["Open", "Save", "SaveAll", "Remove", "Revert", "CopyXml", "PasteXml", "AllFields", "Extras", "GridView"],
    };

    //the desktop's Settings font list, so a font chosen in either edition means the same thing
    const FONT_FAMILY_OPTIONS = ["Segoe UI", "Segoe UI Variable", "Consolas", "Cascadia Code", "Georgia", "Comic Sans MS"];

    const MIXED_SENTINEL = "(multiple values - edit to override all)";

    const $ = (id) => document.getElementById(id);

    const state = {
        schema: null,
        settings: { ...DEFAULT_SETTINGS },
        files: [],
        displayed: [],
        //in selection order, so selection[0] is the current file, like SelectedFiles.First()
        selection: [],
        anchor: null,
        themesDoc: null,
        currentTheme: FALLBACK_THEME,
        //the table view keeps its own selection and sort, like the desktop's DataGrid
        gridActive: false,
        gridSelection: [],
        gridAnchor: null,
        gridSort: null,
        gridWidths: {},
        activeTab: "Basic Info",
        search: "",
        filter: "",
        showAllFields: false,
        showExtraFields: true,
        status: "Ready",
        downloadNoticeShown: false,
    };

    //---------------------------------------------------------------- settings

    function loadSettings() {
        try {
            const raw = localStorage.getItem(SETTINGS_KEY);
            if (raw)
                Object.assign(state.settings, JSON.parse(raw));
        } catch {
            //blocked or corrupt storage - defaults are fine
        }
    }

    function saveSettings() {
        try {
            localStorage.setItem(SETTINGS_KEY, JSON.stringify(state.settings));
        } catch {
            //storage blocked - preferences just won't persist
        }
    }

    //---------------------------------------------------------------- data

    //inlined at build time; an unbuilt checkout served over http falls back to the desktop's own copies
    async function loadJson(id, url) {
        const text = $(id).textContent.trim();
        if (text)
            return JSON.parse(text);
        const response = await fetch(url);
        if (!response.ok)
            throw new Error(`${url}: ${response.status}`);
        return response.json();
    }

    //---------------------------------------------------------------- theme

    function themeNames() {
        const themes = (state.themesDoc && state.themesDoc.themes) || {};
        return Object.keys(themes).sort(ordinalIgnoreCase);
    }

    //an unknown name falls back to the bundled default, then solarized dark, like ThemeService.Apply
    function applyTheme(name) {
        const doc = state.themesDoc;
        const themes = (doc && doc.themes) || {};
        const resolved = themes[name] ? name : themes[doc && doc.default] ? doc.default : FALLBACK_THEME;
        const theme = themes[resolved] || {};
        const fallback = themes[FALLBACK_THEME] || HARD_FALLBACK;
        const style = document.documentElement.style;
        for (const key of Object.keys(HARD_FALLBACK)) {
            const value = validHex(theme[key]) || validHex(fallback[key]) || HARD_FALLBACK[key];
            style.setProperty("--th-" + key.replace(/_/g, "-"), value);
        }
        const bg = validHex(theme.bg) || HARD_FALLBACK.bg;
        document.documentElement.toggleAttribute("data-light", luminance(bg) > 0.5);
        state.currentTheme = resolved;
        buildThemeMenu();
    }

    function buildThemeMenu() {
        $("themeMenu").replaceChildren(...themeNames().map((name) =>
            el("button", {
                class: "menu-item check" + (name === state.currentTheme ? " on" : ""),
                type: "button",
                role: "menuitemradio",
                onclick: (e) => {
                    e.stopPropagation();
                    closeMenus();
                    applyTheme(name);
                    state.settings.theme = name;
                    saveSettings();
                },
            }, el("span", null, name), el("kbd"))));
    }

    //font, width and density preferences, the css side of MainViewModel's EditorFont*/FieldMargin
    function applyPreferences() {
        const s = state.settings;
        const style = document.documentElement.style;
        style.setProperty("--editor-font-size", `${s.editorFontSize}px`);
        style.setProperty("--editor-font", `"${s.editorFontFamily}", var(--font)`);
        style.setProperty("--fields-max-width", s.editorFieldsFillWidth ? "none" : "780px");
        style.setProperty("--field-gap", s.compactDensity ? "8px" : "14px");
        style.setProperty("--row-pad", s.compactDensity ? "3px" : "6px");
    }

    function validHex(v) {
        return typeof v === "string" && /^#([0-9a-f]{6}|[0-9a-f]{8})$/i.test(v.trim()) ? v.trim() : null;
    }

    function luminance(hex) {
        const h = hex.replace("#", "");
        const r = parseInt(h.slice(0, 2), 16);
        const g = parseInt(h.slice(2, 4), 16);
        const b = parseInt(h.slice(4, 6), 16);
        return (0.299 * r + 0.587 * g + 0.114 * b) / 255;
    }

    //---------------------------------------------------------------- fields

    //fields that share another field's row, like WireFieldGroups in MainViewModel.cs
    const ROW_GROUPS = { Number: ["Count", "Volume"], AlternateNumber: ["AlternateCount"] };
    //Year's row is one composite date that also edits Month and Day
    const DATE_PARTS = ["Year", "Month", "Day"];
    const COMPANION_TAGS = new Set([...Object.values(ROW_GROUPS).flat(), "Month", "Day"]);

    const current = () => state.selection[0] || null;
    const isBatch = () => state.selection.length > 1;

    //the shared value across the selection, or mixed when the selected files disagree
    function fieldState(tag) {
        const files = state.selection;
        if (files.length === 0)
            return { value: "", mixed: false };
        const first = files[0].getValue(tag);
        for (const f of files) {
            if (f.getValue(tag) !== first)
                return { value: "", mixed: true };
        }
        return { value: first, mixed: false };
    }

    //mirrors RebuildVisibleFields: tab, extras, show-all and search filters; search is off in batch mode
    function visibleFields() {
        if (state.selection.length === 0)
            return [];
        const search = isBatch() ? "" : state.search.trim().toLowerCase();
        return state.schema.fields.filter((field) => {
            if (COMPANION_TAGS.has(field.tag))
                return false;
            if (state.schema.tabFor(field) !== state.activeTab)
                return false;
            if (field.isExtra && !state.showExtraFields)
                return false;
            const { value, mixed } = fieldState(field.tag);
            if (!state.showAllFields && value.length === 0 && !mixed)
                return false;
            if (search && !field.label.toLowerCase().includes(search) && !value.toLowerCase().includes(search))
                return false;
            return true;
        });
    }

    //live as-you-type feedback; save-time validation is separate and always runs
    function liveCheck(tag, value) {
        if (state.settings.liveValidationMode === "off")
            return null;
        return LiteValidation.checkField(state.schema, tag, value);
    }

    function registerExtrasFrom(tags) {
        for (const tag of tags)
            state.schema.registerExtraTag(tag);
    }

    //---------------------------------------------------------------- rendering

    function el(tag, props, ...children) {
        const node = document.createElement(tag);
        if (props) {
            for (const [k, v] of Object.entries(props)) {
                if (k === "class")
                    node.className = v;
                else if (k === "dataset")
                    Object.assign(node.dataset, v);
                else if (k.startsWith("on"))
                    node.addEventListener(k.slice(2), v);
                else if (v !== undefined && v !== null && v !== false)
                    node[k] = v;
            }
        }
        for (const child of children) {
            if (child !== null && child !== undefined && child !== false)
                node.append(child);
        }
        return node;
    }

    function render() {
        $("workspace").hidden = state.gridActive;
        $("gridView").hidden = !state.gridActive;
        if (state.gridActive) {
            renderGrid();
        } else {
            renderFileList();
            renderEditor();
        }
        renderToolbar();
        renderChecks();
        renderStatus();
        renderDirtyBadge();
    }

    function refreshDisplayed() {
        const filter = state.filter.trim().toLowerCase();
        let list = state.files;
        if (filter)
            list = list.filter((f) => f.name.toLowerCase().includes(filter) || f.subtitle.toLowerCase().includes(filter));
        const byName = (a, b) => ordinalIgnoreCase(a.name, b.name);
        list = [...list];
        if (state.settings.sortMode === "series") {
            list.sort((a, b) => {
                const s = ordinalIgnoreCase(a.getValue("Series").trim(), b.getValue("Series").trim());
                if (s !== 0)
                    return s;
                const na = numberSortKey(a.getValue("Number"));
                const nb = numberSortKey(b.getValue("Number"));
                if (na !== nb)
                    return na < nb ? -1 : 1;
                return ordinalIgnoreCase(a.getValue("Number").trim(), b.getValue("Number").trim());
            });
        } else if (state.settings.sortMode === "modified") {
            list.sort((a, b) => (b.isDirty - a.isDirty) || byName(a, b));
        } else {
            list.sort(byName);
        }
        state.displayed = list;
        //a file filtered out of the list leaves the selection too, as it does in the desktop's ListBox
        const shown = new Set(list);
        if (state.selection.some((f) => !shown.has(f)))
            state.selection = state.selection.filter((f) => shown.has(f));
    }

    //StringComparer.OrdinalIgnoreCase, which is what the desktop sorts with - not a locale-aware compare
    function ordinalIgnoreCase(a, b) {
        const x = a.toUpperCase();
        const y = b.toUpperCase();
        return x < y ? -1 : x > y ? 1 : 0;
    }

    //numeric issue order (2 before 10, half-issues, "10a"); non-numeric sorts last
    function numberSortKey(number) {
        const m = number.trim().match(/^-?\d+(\.\d+)?/);
        return m ? parseFloat(m[0]) : Number.MAX_VALUE;
    }

    function renderFileList() {
        const list = $("fileList");
        list.replaceChildren(...state.displayed.map((file) => {
            const img = el("img", { alt: "", hidden: !file.coverUrl, src: file.coverUrl || undefined });
            return el("li", {
                class: "file-row" + (state.selection.includes(file) ? " selected" : ""),
                role: "option",
                //the full path: books from different subfolders can share a filename
                title: file.path,
                dataset: { id: String(file.id) },
                onclick: (e) => onRowClick(file, e),
                oncontextmenu: (e) => onFileRowMenu(file, e),
            },
                el("div", { class: "cover" }, img),
                el("div", { class: "file-row-text" },
                    el("div", { class: "file-row-name" },
                        el("span", null, file.name),
                        file.isDirty ? el("span", { class: "dirty-dot" }, "•") : null),
                    el("div", { class: "file-row-subtitle" + (file.hasMetadata ? "" : " empty") }, file.subtitle)),
                el("button", {
                    class: "rowclose",
                    type: "button",
                    title: "Close this file",
                    onclick: (e) => {
                        e.stopPropagation();
                        removeWithConfirm([file]);
                    },
                }, "✕"));
        }));
    }

    //updates one row in place - re-rendering the whole list on every keystroke would be wasteful
    function refreshFileRow(file) {
        const row = $("fileList").querySelector(`[data-id="${file.id}"]`);
        if (!row)
            return;
        const name = row.querySelector(".file-row-name");
        const dot = name.querySelector(".dirty-dot");
        if (file.isDirty && !dot)
            name.append(el("span", { class: "dirty-dot" }, "•"));
        else if (!file.isDirty && dot)
            dot.remove();
        const sub = row.querySelector(".file-row-subtitle");
        sub.textContent = file.subtitle;
        sub.classList.toggle("empty", !file.hasMetadata);
        const img = row.querySelector("img");
        if (file.coverUrl && img.src !== file.coverUrl) {
            img.src = file.coverUrl;
            img.hidden = false;
        }
    }

    //single-file mode only - batch mode shows the batch panel in the sidebar instead
    function renderHeader() {
        const file = isBatch() ? null : current();
        $("editorHeader").hidden = !file;
        if (!file)
            return;
        $("headerName").textContent = file.name;
        $("headerSubtitle").textContent = file.subtitle;
        $("headerSubtitle").classList.toggle("empty", !file.hasMetadata);
        const img = $("headerCover");
        img.hidden = !file.coverUrl;
        if (file.coverUrl)
            img.src = file.coverUrl;
    }

    function renderBatchPanel() {
        const batch = isBatch();
        $("batchPanel").hidden = !batch;
        if (!batch)
            return;
        $("batchHeader").textContent = `Batch scope - ${state.selection.length} files`;
        $("batchFiles").replaceChildren(...state.selection.map((f) => el("li", { title: f.name }, f.name)));
    }

    function renderEditor() {
        const file = current();
        renderHeader();
        renderBatchPanel();
        $("emptyState").hidden = !!file;
        $("tabs").hidden = !file;
        $("fieldScroll").hidden = !file;
        $("searchBox").disabled = isBatch();
        closePopups();

        $("tabs").replaceChildren(...LiteSchema.TAB_ORDER.map((tab) =>
            el("button", {
                class: "tab" + (tab === state.activeTab ? " selected" : ""),
                type: "button",
                role: "tab",
                onclick: () => setTab(tab),
            }, tab)));

        rows.clear();
        $("fieldList").replaceChildren(...visibleFields().map(buildFieldRow));
    }

    //tag -> function that re-reads that field's state into its row, for updates without a re-render
    const rows = new Map();

    function refreshRow(tag) {
        const refresh = rows.get(tag);
        if (refresh)
            refresh();
    }

    //dispatches composite rows first, like FieldTemplateSelector: date, then numeric group, then widget type
    function buildFieldRow(field) {
        if (field.tag === "Year")
            return buildDateRow(field);
        if (ROW_GROUPS[field.tag])
            return buildNumericGroup(field);
        if (field.widget === "combo")
            return buildComboRow(field);
        if (field.widget === "text")
            return buildTextRow(field);
        return buildEntryRow(field);
    }

    function fieldLabel(field, id) {
        return el("label", { class: "field-label", htmlFor: id }, field.label);
    }

    function errorText() {
        return el("div", { class: "field-error", hidden: true });
    }

    function showError(input, error, check) {
        input.classList.toggle("invalid", !!check);
        error.hidden = !check;
        error.textContent = check ? `${check.problem} ${check.suggestion}` : "";
    }

    //a text input or textarea that writes every keystroke through to the selection
    function wireTextInput(field, input, error) {
        input.addEventListener("input", () => {
            input.placeholder = "";
            applyValues([[field.tag, input.value]]);
            if (error && state.settings.liveValidationMode !== "blur")
                showError(input, error, liveCheck(field.tag, input.value));
        });
        if (error) {
            input.addEventListener("blur", () => {
                if (state.settings.liveValidationMode === "blur")
                    showError(input, error, liveCheck(field.tag, input.value));
            });
        }
        attachFieldMenu(input, [field.tag]);
        const refresh = () => {
            const { value, mixed } = fieldState(field.tag);
            input.value = value;
            input.placeholder = mixed ? MIXED_SENTINEL : "";
            //the blended placeholder isn't a real value, so there's nothing to validate
            if (error)
                showError(input, error, mixed ? null : liveCheck(field.tag, value));
        };
        rows.set(field.tag, refresh);
        refresh();
    }

    function buildEntryRow(field) {
        const id = "f-" + field.tag;
        const input = el("input", { class: "textbox", id, type: "text", autocomplete: "off" });
        const error = errorText();
        wireTextInput(field, input, error);
        //batch mode always offers the picker; the desktop's single-file recent values aren't kept here
        const picker = isBatch() ? pickerButton(field) : null;
        return el("div", { class: "field-row", title: field.tooltip },
            fieldLabel(field, id), el("div", { class: "field-input-line" }, input, picker), error);
    }

    //multi-line fields have no free-text format rules, so no error line
    function buildTextRow(field) {
        const id = "f-" + field.tag;
        const input = el("textarea", { class: "textbox", id, spellcheck: true });
        wireTextInput(field, input, null);
        return el("div", { class: "field-row", title: field.tooltip },
            el("div", { class: "field-label-line" }, fieldLabel(field, id), isBatch() ? pickerButton(field) : null),
            input);
    }

    //batch mode swaps the combo for a button and picker, since a plain combo can't show "N files"
    function buildComboRow(field) {
        const id = "f-" + field.tag;
        const options = field.options || [];
        if (isBatch()) {
            const button = el("button", { class: "combobox batch-combo", id, type: "button" });
            button.addEventListener("click", (e) => {
                e.stopPropagation();
                openPicker(field, button);
            });
            attachFieldMenu(button, [field.tag]);
            const refresh = () => {
                const { value, mixed } = fieldState(field.tag);
                button.textContent = mixed ? MIXED_SENTINEL : (value.length === 0 ? "(not set)" : value);
            };
            rows.set(field.tag, refresh);
            refresh();
            return el("div", { class: "field-row", title: field.tooltip }, fieldLabel(field, id), button);
        }
        const select = el("select", { class: "combobox", id },
            el("option", { value: "", hidden: true, disabled: true }),
            ...options.map((o) => el("option", { value: o }, o)));
        select.addEventListener("change", () => applyValues([[field.tag, select.value]]));
        attachFieldMenu(select, [field.tag]);
        const refresh = () => {
            const value = fieldState(field.tag).value;
            select.value = options.includes(value) ? value : "";
        };
        rows.set(field.tag, refresh);
        refresh();
        return el("div", { class: "field-row", title: field.tooltip }, fieldLabel(field, id), select);
    }

    //free text rather than a calendar: LiteDate reads full dates, a bare year, or month and year
    function buildDateRow(field) {
        const id = "f-Year";
        const input = el("input", { class: "textbox", id, type: "text", autocomplete: "off" });
        const error = errorText();
        const refresh = () => {
            const parts = DATE_PARTS.map(fieldState);
            const mixed = parts.some((p) => p.mixed);
            input.value = mixed ? "" : LiteDate.formatForDisplay(parts[0].value, parts[1].value, parts[2].value);
            input.placeholder = mixed ? MIXED_SENTINEL : "";
            showError(input, error, parts[0].mixed ? null : liveCheck("Year", parts[0].value));
        };
        input.addEventListener("input", () => {
            input.placeholder = "";
            //half-typed or unreadable dates leave the fields alone until they parse
            const parsed = LiteDate.parse(input.value);
            if (!parsed)
                return;
            applyValues(DATE_PARTS.map((tag, i) => [tag, parsed[i]]));
            if (state.settings.liveValidationMode !== "blur")
                showError(input, error, liveCheck("Year", parsed[0]));
        });
        //reformatted once you leave the box, not mid-typing
        input.addEventListener("change", refresh);
        input.addEventListener("blur", () => {
            if (state.settings.liveValidationMode === "blur")
                showError(input, error, liveCheck("Year", fieldState("Year").value));
        });
        attachFieldMenu(input, DATE_PARTS);
        for (const tag of DATE_PARTS)
            rows.set(tag, refresh);
        refresh();
        return el("div", { class: "field-row", title: field.tooltip },
            fieldLabel(field, id), el("div", { class: "field-input-line" }, input), error);
    }

    //Number/Count/Volume or AlternateNumber/AlternateCount side by side, each its own field
    function buildNumericGroup(primary) {
        const members = [primary, ...ROW_GROUPS[primary.tag].map((t) => state.schema.byTag.get(t)).filter(Boolean)];
        return el("div", { class: "numeric-group" }, ...members.map((field) => {
            const id = "f-" + field.tag;
            const input = el("input", { class: "textbox", id, type: "text", autocomplete: "off" });
            const error = errorText();
            wireTextInput(field, input, error);
            return el("div", { class: "numeric-item", title: field.tooltip }, fieldLabel(field, id), input, error);
        }));
    }

    function pickerButton(field) {
        const button = el("button", { class: "picker-button", type: "button", title: "Choose a value" }, "▾");
        button.addEventListener("click", (e) => {
            e.stopPropagation();
            openPicker(field, button);
        });
        return button;
    }

    function renderChecks() {
        const checks = { showAllFields: state.showAllFields, showExtraFields: state.showExtraFields, gridView: state.gridActive };
        for (const node of document.querySelectorAll("[data-check]"))
            node.classList.toggle("on", !!checks[node.dataset.check]);
    }

    function updateStatus() {
        const dirty = state.files.filter((f) => f.isDirty).length;
        renderDirtyBadge();
        const files = state.files.length === 1 ? "1 file open" : `${state.files.length} files open`;
        setStatus(dirty === 0 ? files : `${files} · ${dirty} unsaved`);
    }

    //save all's unsaved-count badge, recounted on every render so a save that sets its own status still clears it
    function renderDirtyBadge() {
        const dirty = state.files.filter((f) => f.isDirty).length;
        for (const badge of document.querySelectorAll("[data-badge]")) {
            badge.hidden = dirty === 0;
            badge.textContent = String(dirty);
        }
    }

    function setStatus(text) {
        state.status = text;
        renderStatus();
    }

    function renderStatus() {
        $("statusText").textContent = state.status;
    }

    //---------------------------------------------------------------- editing

    //applies to every selected file at once - "edit to override all" in batch mode
    function applyValues(pairs) {
        const files = state.selection;
        if (files.length === 0)
            return;
        let flipped = false;
        for (const file of files) {
            const wasDirty = file.isDirty;
            for (const [tag, value] of pairs)
                file.setValue(tag, value);
            flipped ||= wasDirty !== file.isDirty;
            refreshFileRow(file);
        }
        if (pairs.some(([tag]) => tag === "Series" || tag === "Number" || tag === "Volume"))
            renderHeader();
        updateStatus();
        //modified-first reacts live to dirty state; the other sorts deliberately don't re-sort mid-edit
        if (flipped && state.settings.sortMode === "modified") {
            refreshDisplayed();
            renderFileList();
        }
    }

    //single-file mode only - "revert to saved" has no one clean meaning across a batch selection.
    //Year's row reverts Month and Day with it, or they'd keep unsaved values
    function revertFields(tags) {
        const file = current();
        if (!file || isBatch())
            return;
        const wasDirty = file.isDirty;
        for (const tag of tags)
            file.revertField(tag);
        for (const tag of tags)
            refreshRow(tag);
        refreshFileRow(file);
        renderHeader();
        updateStatus();
        if (wasDirty !== file.isDirty && state.settings.sortMode === "modified") {
            refreshDisplayed();
            renderFileList();
        }
    }

    function setSelection(files, anchor) {
        state.selection = files;
        if (anchor !== undefined)
            state.anchor = anchor;
        //search is off in batch mode - cleared so no stale filter lingers
        if (isBatch() && state.search) {
            state.search = "";
            $("searchBox").value = "";
        }
        renderFileList();
        renderEditor();
        if (state.files.length > 0)
            updateStatus();
    }

    function select(file) {
        setSelection(file ? [file] : [], file);
    }

    function onRowClick(file, e) {
        const next = clickSelection(state.displayed, state.selection, state.anchor, file, e);
        setSelection(next.selection, next.anchor);
        $("fileList").focus({ preventScroll: true });
    }

    function onFileListKey(e) {
        if (e.key === "Delete" && !e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey) {
            e.preventDefault();
            removeWithConfirm([...state.selection]);
            return;
        }
        const next = keySelection(state.displayed, state.selection, state.anchor, e);
        if (!next)
            return;
        e.preventDefault();
        setSelection(next.selection, next.anchor);
        if (next.target) {
            const row = $("fileList").querySelector(`[data-id="${next.target.id}"]`);
            if (row)
                row.scrollIntoView({ block: "nearest" });
        }
    }

    function setTab(tab) {
        state.activeTab = tab;
        state.settings.activeTab = LiteSchema.TAB_ORDER.indexOf(tab);
        saveSettings();
        renderEditor();
    }

    //---------------------------------------------------------------- value picker

    //detected values, most common first; combo fields also list every schema option not already
    //present, so a field unset on every selected file still offers something to pick
    function pickerValues(field) {
        const counts = new Map();
        for (const f of state.selection) {
            const v = f.getValue(field.tag);
            counts.set(v, (counts.get(v) || 0) + 1);
        }
        const detected = [...counts].map(([value, count]) => ({ value, count }))
            .sort((a, b) => (b.count - a.count) || ordinalIgnoreCase(a.value, b.value));
        if (field.widget === "combo") {
            for (const o of field.options || []) {
                if (!counts.has(o))
                    detected.push({ value: o, count: 0 });
            }
        }
        return detected;
    }

    function pickerLabel({ value, count }) {
        const shown = value.length === 0 ? "(blank)" : value;
        return count === 0 ? shown : `${shown}  -  ${count} file${count === 1 ? "" : "s"}`;
    }

    function openPicker(field, anchor) {
        const popup = $("pickerPopup");
        if (!popup.hidden && popup.dataset.tag === field.tag) {
            closePopups();
            return;
        }
        closePopups();
        popup.dataset.tag = field.tag;
        popup.replaceChildren(...pickerValues(field).map((item) =>
            el("button", {
                class: "popup-item",
                type: "button",
                onclick: (e) => {
                    e.stopPropagation();
                    closePopups();
                    //a pick always applies, even when the text matches, since it also resolves a mixed field
                    applyValues([[field.tag, item.value]]);
                    refreshRow(field.tag);
                },
            }, pickerLabel(item))));
        showPopupNear(popup, anchor.getBoundingClientRect());
        const first = popup.querySelector("button");
        if (first)
            first.focus();
    }

    function showPopupNear(popup, rect) {
        popup.hidden = false;
        const width = popup.offsetWidth;
        const height = popup.offsetHeight;
        let left = Math.min(rect.left, window.innerWidth - width - 8);
        let top = rect.bottom + 2;
        if (top + height > window.innerHeight - 8)
            top = Math.max(8, rect.top - height - 2);
        popup.style.left = `${Math.max(8, left)}px`;
        popup.style.top = `${top}px`;
    }

    function closePopups() {
        for (const id of ["pickerPopup", "contextMenu"]) {
            const popup = $(id);
            if (popup && !popup.hidden) {
                popup.hidden = true;
                popup.dataset.tag = "";
            }
        }
    }

    //---------------------------------------------------------------- field context menu

    //Cut/Copy/Paste rebuilt by hand, like the desktop's AttachRevertContextMenu, since replacing the
    //browser's own menu would otherwise lose them; shift+right-click still gets the browser's menu
    function attachFieldMenu(input, tags) {
        input.addEventListener("contextmenu", (e) => {
            if (e.shiftKey)
                return;
            e.preventDefault();
            openFieldMenu(input, tags, e.clientX, e.clientY);
        });
    }

    function openFieldMenu(input, tags, x, y) {
        closePopups();
        const menu = $("contextMenu");
        const isText = input.tagName === "INPUT" || input.tagName === "TEXTAREA";
        const hasSelection = isText && input.selectionStart !== input.selectionEnd;
        const item = (label, enabled, run) => el("button", {
            class: "popup-item",
            type: "button",
            disabled: !enabled,
            onclick: (e) => {
                e.stopPropagation();
                closePopups();
                run();
            },
        }, label);
        const items = [];
        if (isText) {
            items.push(
                item("Cut", hasSelection, () => {
                    input.focus();
                    document.execCommand("cut");
                }),
                item("Copy", hasSelection, () => {
                    input.focus();
                    document.execCommand("copy");
                }),
                item("Paste", !!(navigator.clipboard && navigator.clipboard.readText), () => pasteInto(input)),
                el("div", { class: "menu-sep" }));
        }
        items.push(item("Revert to Saved", !isBatch() && !!current(), () => revertFields(tags)));
        menu.replaceChildren(...items);
        showPopupNear(menu, { left: x, right: x, top: y, bottom: y });
    }

    //the clipboard is read through the async api, since execCommand("paste") is blocked for pages;
    //the browser may ask first
    async function pasteInto(input) {
        let text;
        try {
            text = await navigator.clipboard.readText();
        } catch {
            setStatus("This browser didn't allow pasting from the menu - use Ctrl+V instead");
            return;
        }
        input.focus();
        input.setRangeText(text, input.selectionStart, input.selectionEnd, "end");
        input.dispatchEvent(new Event("input", { bubbles: true }));
    }

    //---------------------------------------------------------------- opening

    function isSupported(name) {
        const m = name.toLowerCase().match(/\.[^.]*$/);
        return !!m && SUPPORTED.includes(m[0]);
    }

    async function onOpen() {
        if (window.showOpenFilePicker) {
            let handles;
            try {
                handles = await window.showOpenFilePicker({
                    multiple: true,
                    types: [{ description: "Comic archives", accept: { "application/octet-stream": SUPPORTED } }],
                });
            } catch (e) {
                if (e.name !== "AbortError")
                    showMessage("Couldn't open the file picker", e.message);
                return;
            }
            await openSources(await Promise.all(handles.map(async (h) => ({ file: await h.getFile(), handle: h }))));
            return;
        }
        $("fileInput").value = "";
        $("fileInput").click();
    }

    async function isAlreadyOpen(source) {
        for (const f of state.files) {
            if (source.handle && f.handle && await source.handle.isSameEntry(f.handle))
                return true;
            if (!source.handle && !f.handle && f.name === source.file.name
                && f.source.size === source.file.size && f.source.lastModified === source.file.lastModified)
                return true;
        }
        return false;
    }

    async function openSources(sources) {
        const pending = [];
        for (const s of sources) {
            if (!(await isAlreadyOpen(s)))
                pending.push(s);
        }
        if (pending.length === 0)
            return;

        //cleared first, so a book about to be opened can't be hidden by an old filter
        if (state.settings.clearFilterOnOpen && state.filter) {
            state.filter = "";
            $("fileFilter").value = "";
        }

        const loaded = [];
        const failures = [];
        const cbr = [];
        const unreadable = [];
        for (const { file, handle, path } of pending) {
            try {
                const result = await LiteArchive.read(file, file.name, state.schema.imageExtensions(), state.settings.coverSource);
                const values = LiteComicInfo.parse(result.xml);
                if (LiteComicInfo.isUnreadable(result.xml))
                    unreadable.push(file.name);
                const comic = new LiteModel.ComicFile({
                    source: file,
                    handle,
                    name: file.name,
                    path: path || file.name,
                    format: result.format,
                    entries: result.entries,
                    rawXml: result.xml,
                    values,
                    pageCount: result.pageCount,
                    coverEntry: result.coverEntry,
                });
                //seeded into the baseline, so filling an empty Page Count never marks a file unsaved by itself
                if (state.settings.autoPageCount && result.pageCount > 0 && comic.getValue("PageCount").length === 0)
                    comic.seedValue("PageCount", String(result.pageCount));
                loaded.push(comic);
            } catch (e) {
                if (e instanceof LiteArchive.CbrError)
                    cbr.push(file.name);
                else
                    failures.push(`${file.name}: ${e.message}`);
            }
        }

        for (const comic of loaded)
            registerExtrasFrom(comic.current.keys());
        state.files.push(...loaded);
        refreshDisplayed();
        if (loaded.length > 0 && state.settings.autoSelectFirstOnOpen && state.displayed.includes(loaded[0])) {
            state.selection = [loaded[0]];
            state.anchor = loaded[0];
        }
        render();
        if (loaded.length > 0)
            updateStatus();
        loadCovers(loaded);

        if (failures.length > 0)
            await showMessage("Some files could not be opened", failures.join("\n"));
        if (cbr.length > 0) {
            await showMessage("CBR files need the desktop app",
                "These are RAR archives, which browsers have no way to read:\n\n" + cbr.join("\n")
                + "\n\nThe cbzLab desktop app opens and saves CBR files, and can convert them to CBZ.",
                { link: true });
        }
        if (unreadable.length > 0) {
            await showMessage("Unreadable ComicInfo.xml",
                "The ComicInfo.xml in these files isn't valid XML, so no metadata could be read from it:\n\n"
                + unreadable.sort().join("\n")
                + "\n\nSaving one of them replaces that ComicInfo.xml with a new one holding only the fields "
                + "filled in here. Leave it unsaved if you want to repair the original by hand.");
        }
    }

    //one at a time, so a big batch never holds every cover's raw bytes at once
    async function loadCovers(files) {
        for (const file of files) {
            if (!file.coverEntry || !state.files.includes(file))
                continue;
            try {
                const blob = await LiteArchive.readCover(file.source, file.coverEntry);
                file.coverUrl = URL.createObjectURL(blob);
                refreshFileRow(file);
                if (file === current())
                    renderHeader();
            } catch {
                //no cover is fine - the placeholder slot stays
            }
        }
    }

    //---------------------------------------------------------------- saving

    //batch mode saves every dirty selected file; single-file mode saves the current one
    async function onSave() {
        if (state.selection.length === 0)
            return;
        if (isBatch()) {
            const targets = state.selection.filter((f) => f.isDirty);
            if (targets.length === 0) {
                setStatus("No changes to save in the selection");
                return;
            }
            await saveFiles(targets);
        } else {
            await saveFiles([current()]);
        }
    }

    async function onSaveAll() {
        const targets = state.files.filter((f) => f.isDirty);
        if (targets.length === 0) {
            setStatus("Nothing to save");
            return;
        }
        await saveFiles(targets);
    }

    //shared save pipeline, like SaveFilesAsync: validate, then write each file in place where the
    //browser allows it and as a download where it doesn't
    async function saveFiles(files) {
        const errors = files.flatMap((f) => LiteValidation.validate(state.schema, f.name, f.current));
        if (errors.length > 0 && !(await showValidation(errors)))
            return false;

        //asked up front, while the click (or the validation dialog's) still counts as a user gesture
        const inPlace = new Map();
        for (const f of files)
            inPlace.set(f, await canWriteInPlace(f));

        const failures = [];
        const saved = [];
        const downloaded = [];
        for (const file of files) {
            try {
                const xml = LiteComicInfo.build(file.rawXml, file.buildWriteValues());
                const blob = await LiteArchive.save(file.source, file.entries, xml);
                if (inPlace.get(file)) {
                    await writeToHandle(file.handle, blob);
                    const fresh = await file.handle.getFile();
                    file.markSaved(xml, { source: fresh, entries: await LiteZip.list(fresh) });
                    saved.push(file);
                } else {
                    download(blob, file.name);
                    file.markSaved(xml);
                    downloaded.push(file);
                }
            } catch (e) {
                failures.push(`${file.name}: ${e.message}`);
            }
        }
        afterSave();

        if (failures.length > 0) {
            await showMessage("Some files could not be saved", failures.join("\n\n"));
            return false;
        }
        setStatus(saveSummary(saved, downloaded));
        if (downloaded.length > 0)
            await explainDownloadOnce();
        return true;
    }

    function saveSummary(saved, downloaded) {
        const count = (n) => (n === 1 ? "1 file" : `${n} files`);
        if (downloaded.length === 0)
            return `Saved ${count(saved.length)}`;
        const got = downloaded.length === 1 ? `Downloaded ${downloaded[0].name}` : `Downloaded ${count(downloaded.length)}`;
        const kept = downloaded.length === 1 ? "the original file is unchanged" : "the originals are unchanged";
        return saved.length === 0
            ? `${got} - ${kept}`
            : `Saved ${count(saved.length)}, ${got.charAt(0).toLowerCase()}${got.slice(1)} - ${kept}`;
    }

    //single file only - doesn't apply to a batch selection
    async function onSaveAs() {
        const file = current();
        if (!file || isBatch()) {
            setStatus("Save As works on a single selected file");
            return;
        }
        const suggested = file.name.replace(/\.[^.]*$/, "") + ".cbz";

        if (window.showSaveFilePicker) {
            let handle;
            try {
                handle = await window.showSaveFilePicker({
                    suggestedName: suggested,
                    types: [{ description: "Comic ZIP archive", accept: { "application/vnd.comicbook+zip": [".cbz"] } }],
                });
            } catch (e) {
                if (e.name !== "AbortError")
                    await showMessage("Save failed", e.message);
                return;
            }
            const errors = LiteValidation.validate(state.schema, file.name, file.current);
            if (errors.length > 0 && !(await showValidation(errors)))
                return;
            try {
                const xml = LiteComicInfo.build(file.rawXml, file.buildWriteValues());
                const blob = await LiteArchive.save(file.source, file.entries, xml);
                await writeToHandle(handle, blob);
                const fresh = await handle.getFile();
                file.handle = handle;
                file.markSaved(xml, { source: fresh, entries: await LiteZip.list(fresh), name: handle.name });
                file.format = "cbz";
                afterSave();
                setStatus(`Saved as ${handle.name}`);
            } catch (e) {
                await showMessage("Save failed", e.message);
            }
            return;
        }

        const name = await promptSaveAsName(suggested);
        if (!name)
            return;
        const errors = LiteValidation.validate(state.schema, file.name, file.current);
        if (errors.length > 0 && !(await showValidation(errors)))
            return;
        try {
            const xml = LiteComicInfo.build(file.rawXml, file.buildWriteValues());
            const blob = await LiteArchive.save(file.source, file.entries, xml);
            download(blob, name);
            file.markSaved(xml, { name });
            file.format = "cbz";
            afterSave();
            setStatus(`Downloaded ${name} - the original file is unchanged`);
            await explainDownloadOnce();
        } catch (e) {
            await showMessage("Save failed", e.message);
        }
    }

    function afterSave() {
        refreshDisplayed();
        render();
    }

    async function canWriteInPlace(file) {
        if (!file.handle || !file.handle.createWritable)
            return false;
        try {
            const opts = { mode: "readwrite" };
            if ((await file.handle.queryPermission(opts)) === "granted")
                return true;
            return (await file.handle.requestPermission(opts)) === "granted";
        } catch {
            return false;
        }
    }

    //the browser writes to a swap file and only replaces the original on close, so the source
    //blob's slices stay readable for the whole write
    async function writeToHandle(handle, blob) {
        const writable = await handle.createWritable();
        try {
            await writable.write(blob);
            await writable.close();
        } catch (e) {
            await writable.abort().catch(() => {});
            throw e;
        }
    }

    function download(blob, name) {
        const url = URL.createObjectURL(blob);
        const a = el("a", { href: url, download: name });
        document.body.append(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 60000);
    }

    async function explainDownloadOnce() {
        if (state.downloadNoticeShown)
            return;
        state.downloadNoticeShown = true;
        await showMessage("Saved as a download",
            "This browser can't write back to the file you opened, so your edited archive was downloaded "
            + "as a new copy and the original is untouched. Some browsers rename the copy if a file with "
            + "that name already exists in your downloads folder.\n\n"
            + "Chrome and Edge can save in place here. The cbzLab desktop app always saves in place, "
            + "and works on whole folders at once.",
            { link: true });
    }

    function promptSaveAsName(suggested) {
        const dialog = $("saveAsDialog");
        const input = $("saveAsName");
        input.value = suggested;
        return new Promise((resolve) => {
            dialog.addEventListener("close", () => {
                let name = dialog.returnValue === "ok" ? input.value.trim() : "";
                if (name && !/\.(cbz|zip)$/i.test(name))
                    name += ".cbz";
                resolve(name);
            }, { once: true });
            dialog.returnValue = "";
            dialog.showModal();
            input.select();
        });
    }

    //---------------------------------------------------------------- dialogs

    function showMessage(title, message, { link = false } = {}) {
        const dialog = $("messageDialog");
        dialog.querySelector(".dialog-title").textContent = title;
        const body = dialog.querySelector(".dialog-body");
        body.replaceChildren(message);
        if (link) {
            body.append("\n\n", el("a", { href: DESKTOP_URL, target: "_blank", rel: "noopener" }, "Get the cbzLab desktop app"));
        }
        return new Promise((resolve) => {
            dialog.addEventListener("close", () => resolve(), { once: true });
            dialog.showModal();
        });
    }

    //resolves true for Save Anyway, false for Fix (back to the editor) or Esc
    function showValidation(errors) {
        const dialog = $("validationDialog");
        dialog.querySelector(".validation-list").replaceChildren(...errors.map((err) =>
            el("div", { class: "validation-entry" },
                el("div", { class: "validation-where" }, `${err.fileName} - ${err.label}`),
                el("div", { class: "validation-problem" }, err.problem),
                el("div", { class: "validation-fix" }, "Fix: " + err.suggestion))));
        return new Promise((resolve) => {
            dialog.addEventListener("close", () => resolve(dialog.returnValue === "anyway"), { once: true });
            dialog.returnValue = "";
            dialog.showModal();
        });
    }

    //---------------------------------------------------------------- toolbar

    //the desktop's ToolbarCatalog in its order; group drives separator placement. lite is true, or
    //"desktop" for buttons that can't work in a browser
    const TIPS = {
        OpenFolder: "Open every archive in a folder, including subfolders",
        Remove: "Remove the selected file(s) from the list (not from disk)",
        Revert: "Discard edits on the current file and reload from disk",
        CopyXml: "Copy the current file's ComicInfo.xml to the clipboard",
        PasteXml: "Replace the current file's metadata from clipboard XML",
        GuessFromFilename: "Fill empty Series/Number/Volume/Year fields from the filename and folder",
        ExtractSummaryHeader: "Move a \"Publisher - Year\" line from the top of the summary into those fields",
    };

    const TOOLBAR_CATALOG = [
        { id: "Open", label: "Open…", group: 0, lite: true, action: "open", tip: "Open archives" },
        { id: "OpenFolder", label: "Open Folder…", group: 0, lite: true, action: "openFolder", tip: TIPS.OpenFolder },
        { id: "Save", label: "Save", group: 0, lite: true, action: "save", tip: "Save the selected file(s) in place" },
        { id: "SaveAll", label: "Save All", group: 0, lite: true, action: "saveAll", tip: "Save every file with unsaved changes" },
        { id: "Remove", label: "Remove", group: 1, lite: true, action: "remove", tip: TIPS.Remove },
        { id: "Revert", label: "Revert", group: 1, lite: true, action: "revert", tip: TIPS.Revert },
        { id: "CopyXml", label: "Copy XML", group: 2, lite: true, action: "copyXml", tip: TIPS.CopyXml },
        { id: "PasteXml", label: "Paste XML", group: 2, lite: true, action: "pasteXml", tip: TIPS.PasteXml },
        { id: "GuessFromFilename", label: "Guess from Filename", group: 3, lite: true, action: "guessFromFilename", tip: TIPS.GuessFromFilename },
        { id: "ExtractSummaryHeader", label: "Extract Publisher/Year from Summary", group: 3, lite: true, action: "extractSummaryHeader", tip: TIPS.ExtractSummaryHeader },
        { id: "Combine", label: "Combine Archives…", group: 3, lite: "desktop" },
        { id: "TrimFooters", label: "Trim Branding Footers…", group: 3, lite: "desktop" },
        { id: "SearchComicVine", label: "Search ComicVine…", group: 4, lite: "desktop" },
        { id: "AllFields", label: "All Fields", group: 5, lite: true, action: "toggleShowAll", check: "showAllFields", tip: "Show all fields, including empty ones" },
        { id: "Extras", label: "Extras", group: 5, lite: true, action: "toggleShowExtra", check: "showExtraFields", tip: "Show unofficial/extra fields" },
        { id: "GridView", label: "Grid View", group: 6, lite: true, action: "toggleGrid", check: "gridView", tip: "Switch between the editor and a table view of your library" },
    ];

    //unknown ids are skipped; desktop-only ones show dimmed and explain themselves
    function renderToolbar() {
        const panel = $("toolbarPanel");
        const ids = state.settings.toolbarButtons.join("|");
        if (panel.dataset.ids === ids)
            return;
        panel.dataset.ids = ids;
        const nodes = [];
        let lastGroup = null;
        for (const id of state.settings.toolbarButtons) {
            const def = TOOLBAR_CATALOG.find((d) => d.id === id);
            if (!def)
                continue;
            if (lastGroup !== null && lastGroup !== def.group)
                nodes.push(el("div", { class: "toolbar-sep" }));
            nodes.push(toolbarButton(def));
            lastGroup = def.group;
        }
        panel.replaceChildren(...nodes);
        renderDirtyBadge();
    }

    function toolbarButton(def) {
        if (def.lite === "desktop") {
            return el("button", {
                class: "toolflat desktop-only",
                type: "button",
                title: "Desktop app only - click to see why",
                dataset: { desktop: def.id },
            }, { Combine: "Combine…", TrimFooters: "Trim Footers…" }[def.id] || def.label);
        }
        const props = { class: "toolflat" + (def.check ? " toggle" : ""), type: "button", title: def.tip, dataset: { action: def.action } };
        if (def.check)
            props.dataset.check = def.check;
        if (def.id === "SaveAll") {
            return el("button", props, el("span", { class: "toolflat-content" },
                "Save All", el("span", { class: "dirty-badge", hidden: true, dataset: { badge: "" } })));
        }
        //the desktop's shorter toolbar captions
        const caption = { CopyXml: "Copy XML", PasteXml: "Paste XML", ExtractSummaryHeader: "Extract Pub/Year" }[def.id] || def.label;
        return el("button", props, caption);
    }

    //---------------------------------------------------------------- selection helpers

    //plain click selects one, ctrl toggles, shift extends from the anchor, like the desktop's ListBox
    function clickSelection(list, selection, anchor, file, e) {
        if (e.shiftKey && anchor && list.includes(anchor)) {
            const a = list.indexOf(anchor);
            const b = list.indexOf(file);
            const range = list.slice(Math.min(a, b), Math.max(a, b) + 1);
            //the anchor stays first, so it remains the current file
            return { selection: [anchor, ...range.filter((f) => f !== anchor)], anchor };
        }
        if (e.ctrlKey || e.metaKey) {
            const has = selection.includes(file);
            return { selection: has ? selection.filter((f) => f !== file) : [...selection, file], anchor: file };
        }
        return { selection: [file], anchor: file };
    }

    //arrows, home/end (shift extends) and ctrl+a; null for keys it doesn't handle
    function keySelection(list, selection, anchor, e) {
        if (list.length === 0)
            return null;
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "a")
            return { selection: [...list], anchor: list[0], target: null };
        const moves = { ArrowUp: -1, ArrowDown: 1, Home: -Infinity, End: Infinity };
        if (!(e.key in moves))
            return null;
        const focus = selection.length > 0 ? selection[selection.length - 1] : null;
        const from = focus ? list.indexOf(focus) : -1;
        const step = moves[e.key];
        const to = Number.isFinite(step)
            ? Math.max(0, Math.min(list.length - 1, from + step))
            : (step < 0 ? 0 : list.length - 1);
        const target = list[to];
        if (e.shiftKey && anchor && list.includes(anchor)) {
            const a = list.indexOf(anchor);
            const range = list.slice(Math.min(a, to), Math.max(a, to) + 1);
            return { selection: a <= to ? range : range.reverse(), anchor, target };
        }
        return { selection: [target], anchor: target, target };
    }

    //---------------------------------------------------------------- grid view

    //most useful-at-a-glance fields first; unlisted fields fall back to schema order
    const GRID_COLUMN_PRIORITY = [
        "Series", "Number", "Title", "Volume",
        "Writer", "Penciller", "Inker", "Colorist", "Letterer", "CoverArtist", "Editor",
        "Publisher", "Imprint",
        "Year", "Month", "Day",
        "Genre", "Characters", "Teams", "Locations", "StoryArc", "SeriesGroup", "MainCharacterOrTeam",
    ];

    //dirty indicator, Size and Modified are fixed; the rest come from settings.gridColumns
    function gridColumnDefs() {
        const fixed = [
            { key: "dirty", label: "", sortable: false },
            { key: "size", label: "Size", text: (f) => formatSize(f.source.size), sort: (f) => f.source.size },
            { key: "modified", label: "Modified", text: (f) => formatModified(f.source.lastModified), sort: (f) => f.source.lastModified },
        ];
        return fixed.concat(state.settings.gridColumns.map((tag) => {
            const field = state.schema.byTag.get(tag);
            return { key: "f:" + tag, label: field ? field.label : tag, text: (f) => f.getValue(tag), sort: (f) => f.getValue(tag) };
        }));
    }

    //bytes/KB/MB with at most one decimal, like the desktop's "0.#" format
    function formatSize(bytes) {
        if (bytes < 1024)
            return `${bytes} B`;
        const [value, unit] = bytes < 1024 * 1024 ? [bytes / 1024, "KB"] : [bytes / (1024 * 1024), "MB"];
        const text = String(Number(value.toFixed(1)));
        return `${text.replace(".", decimalSeparator())} ${unit}`;
    }

    function decimalSeparator() {
        const part = new Intl.NumberFormat().formatToParts(1.5).find((p) => p.type === "decimal");
        return part ? part.value : ".";
    }

    //the short date plus short time, the browser's take on .NET's "g" format
    function formatModified(ms) {
        if (!ms)
            return "";
        const d = new Date(ms);
        const date = new Intl.DateTimeFormat(undefined, { year: "numeric", month: "2-digit", day: "2-digit" }).format(d);
        const time = new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit" }).format(d);
        return `${date} ${time}`;
    }

    //the grid shows the same filtered list as the sidebar, in its own sort order
    function gridRows() {
        const rows = [...state.displayed];
        const sort = state.gridSort;
        const def = sort && gridColumnDefs().find((c) => c.key === sort.key);
        if (def && def.sort) {
            rows.sort((a, b) => {
                const x = def.sort(a);
                const y = def.sort(b);
                const c = typeof x === "number" ? x - y : ordinalIgnoreCase(x, y);
                return sort.dir * c;
            });
        }
        return rows;
    }

    function renderGrid() {
        const defs = gridColumnDefs();
        const rows = gridRows();
        state.gridSelection = state.gridSelection.filter((f) => rows.includes(f));

        $("gridHead").replaceChildren(el("tr", null, ...defs.map((def) => {
            const sorted = state.gridSort && state.gridSort.key === def.key;
            const th = el("th", {
                class: (def.sortable === false ? "" : "sortable") + (def.key === "dirty" ? " dirty-col" : ""),
                title: def.sortable === false ? "" : "Sort by " + def.label,
                onclick: () => {
                    if (def.sortable === false)
                        return;
                    const dir = state.gridSort && state.gridSort.key === def.key ? -state.gridSort.dir : 1;
                    state.gridSort = { key: def.key, dir };
                    renderGrid();
                },
            }, el("span", null, def.label), sorted ? el("span", { class: "sort-arrow" }, state.gridSort.dir > 0 ? "▲" : "▼") : null);
            const width = state.gridWidths[def.key];
            if (width)
                th.style.width = `${width}px`;
            if (def.key !== "dirty")
                th.append(resizeHandle(th, def.key));
            return th;
        })));

        $("gridBody").replaceChildren(...rows.map((file) => el("tr", {
            class: state.gridSelection.includes(file) ? "selected" : "",
            dataset: { id: String(file.id) },
            onclick: (e) => {
                const next = clickSelection(rows, state.gridSelection, state.gridAnchor, file, e);
                state.gridSelection = next.selection;
                state.gridAnchor = next.anchor;
                renderGridSelection();
            },
            ondblclick: () => editFromGrid([file]),
            oncontextmenu: (e) => {
                e.preventDefault();
                //right-clicking outside the selection replaces it first
                if (!state.gridSelection.includes(file)) {
                    state.gridSelection = [file];
                    state.gridAnchor = file;
                    renderGridSelection();
                }
                openGridMenu(e.clientX, e.clientY);
            },
        }, ...defs.map((def) => def.key === "dirty"
            ? el("td", { class: "dirty-col" }, file.isDirty ? el("span", { class: "dirty-dot" }, "•") : null)
            : el("td", { title: def.text(file) }, def.text(file))))));
        $("gridView").classList.toggle("empty", rows.length === 0);
    }

    function renderGridSelection() {
        for (const tr of $("gridBody").children) {
            const file = state.files.find((f) => String(f.id) === tr.dataset.id);
            tr.classList.toggle("selected", state.gridSelection.includes(file));
        }
    }

    function resizeHandle(th, key) {
        const handle = el("span", { class: "col-resize", onclick: (e) => e.stopPropagation() });
        handle.addEventListener("pointerdown", (e) => {
            e.preventDefault();
            e.stopPropagation();
            handle.setPointerCapture(e.pointerId);
            const startX = e.clientX;
            const startW = th.getBoundingClientRect().width;
            const move = (ev) => {
                const w = Math.max(40, Math.round(startW + ev.clientX - startX));
                state.gridWidths[key] = w;
                th.style.width = `${w}px`;
            };
            handle.addEventListener("pointermove", move);
            handle.addEventListener("pointerup", () => handle.removeEventListener("pointermove", move), { once: true });
        });
        return handle;
    }

    function openGridMenu(x, y) {
        closePopups();
        const count = state.gridSelection.length;
        const item = (label, enabled, run) => el("button", {
            class: "popup-item",
            type: "button",
            disabled: !enabled,
            onclick: (e) => {
                e.stopPropagation();
                closePopups();
                run();
            },
        }, label);
        $("contextMenu").replaceChildren(
            item(count > 1 ? `Edit ${count} Books in Batch Editor` : "Edit This Book", count > 0, () => editFromGrid(state.gridSelection)),
            el("div", { class: "menu-sep" }),
            item("Choose Columns…", true, onChooseColumns));
        showPopupNear($("contextMenu"), { left: x, right: x, top: y, bottom: y });
    }

    function onGridKey(e) {
        const rows = gridRows();
        if (e.key === "Enter" && state.gridSelection.length > 0) {
            e.preventDefault();
            editFromGrid(state.gridSelection);
            return;
        }
        const next = keySelection(rows, state.gridSelection, state.gridAnchor, e);
        if (!next)
            return;
        e.preventDefault();
        state.gridSelection = next.selection;
        state.gridAnchor = next.anchor;
        renderGridSelection();
        if (next.target) {
            const row = $("gridBody").querySelector(`[data-id="${next.target.id}"]`);
            if (row)
                row.scrollIntoView({ block: "nearest" });
        }
    }

    //back to the editor with exactly these files selected - one file, or a batch
    function editFromGrid(files) {
        if (files.length === 0)
            return;
        state.gridActive = false;
        render();
        setSelection([...files], files[0]);
    }

    //the grid starts out showing the editor's selection, so switching views doesn't lose your place
    function onToggleGrid() {
        state.gridActive = !state.gridActive;
        if (state.gridActive) {
            state.gridSelection = [...state.selection];
            state.gridAnchor = state.anchor;
        }
        closePopups();
        render();
        if (state.gridActive)
            $("gridScroll").focus({ preventScroll: true });
    }

    //---------------------------------------------------------------- column, toolbar and settings dialogs

    function showDialog(dialog) {
        return new Promise((resolve) => {
            dialog.addEventListener("close", () => resolve(dialog.returnValue), { once: true });
            dialog.returnValue = "";
            dialog.showModal();
        });
    }

    function showConfirm(title, message, confirmText) {
        const dialog = $("confirmDialog");
        dialog.querySelector(".dialog-title").textContent = title;
        dialog.querySelector(".dialog-body").textContent = message;
        dialog.querySelector('button[value="ok"]').textContent = confirmText;
        return showDialog(dialog).then((v) => v === "ok");
    }

    function checkRow(label, checked) {
        const box = el("input", { type: "checkbox", checked });
        return { box, row: el("label", { class: "check" }, box, label) };
    }

    async function onChooseColumns() {
        const priority = (tag) => {
            const i = GRID_COLUMN_PRIORITY.indexOf(tag);
            return i >= 0 ? i : Number.MAX_SAFE_INTEGER;
        };
        //stable sort, so unlisted fields keep schema order
        const fields = [...state.schema.fields].sort((a, b) => priority(a.tag) - priority(b.tag));
        const checks = fields.map((f) => ({ tag: f.tag, ...checkRow(f.label, state.settings.gridColumns.includes(f.tag)) }));
        $("columnsList").replaceChildren(...checks.map((c) => c.row));
        if ((await showDialog($("columnsDialog"))) !== "ok")
            return;
        state.settings.gridColumns = checks.filter((c) => c.box.checked).map((c) => c.tag);
        saveSettings();
        if (state.gridActive)
            renderGrid();
    }

    async function onCustomizeToolbar() {
        const current = state.settings.toolbarButtons;
        //keep the current order, then append whatever isn't on the toolbar
        const rowsFor = (ids, enabled) => {
            const order = ids.filter((id) => TOOLBAR_CATALOG.some((d) => d.id === id));
            for (const def of TOOLBAR_CATALOG) {
                if (!order.includes(def.id))
                    order.push(def.id);
            }
            return order.map((id) => ({ def: TOOLBAR_CATALOG.find((d) => d.id === id), enabled: enabled(id) }));
        };
        let rows = rowsFor(current, (id) => current.includes(id));

        const draw = () => {
            $("toolbarList").replaceChildren(...rows.map((row, i) => {
                const box = el("input", { type: "checkbox", checked: row.enabled });
                box.addEventListener("change", () => row.enabled = box.checked);
                const note = row.def.lite === "desktop" ? "desktop app only" : null;
                const move = (delta) => {
                    const j = i + delta;
                    [rows[i], rows[j]] = [rows[j], rows[i]];
                    draw();
                };
                return el("div", { class: "toolbar-row" },
                    el("label", { class: "check" }, box, row.def.label, note ? el("span", { class: "row-note" }, ` (${note})`) : null),
                    el("button", { class: "button small", type: "button", disabled: i === 0, title: "Move up", onclick: () => move(-1) }, "▲"),
                    el("button", { class: "button small", type: "button", disabled: i === rows.length - 1, title: "Move down", onclick: () => move(1) }, "▼"));
            }));
        };
        $("toolbarReset").onclick = () => {
            rows = rowsFor(DEFAULT_SETTINGS.toolbarButtons, (id) => DEFAULT_SETTINGS.toolbarButtons.includes(id));
            draw();
        };
        draw();
        if ((await showDialog($("toolbarDialog"))) !== "ok")
            return;
        state.settings.toolbarButtons = rows.filter((r) => r.enabled).map((r) => r.def.id);
        saveSettings();
        renderToolbar();
        renderChecks();
    }

    async function onSettings() {
        const s = state.settings;
        const theme = $("setTheme");
        theme.replaceChildren(...themeNames().map((n) => el("option", { value: n }, n)));
        theme.value = themeNames().includes(s.theme) ? s.theme : state.currentTheme;
        $("setFontSize").value = s.editorFontSize;
        $("setFontFamily").replaceChildren(...FONT_FAMILY_OPTIONS.map((n) => el("option", { value: n }, n)));
        $("setFontFamily").value = FONT_FAMILY_OPTIONS.includes(s.editorFontFamily) ? s.editorFontFamily : FONT_FAMILY_OPTIONS[0];
        $("setCoverSource").value = s.coverSource === "last" ? "last" : "first";
        $("setFillWidth").checked = s.editorFieldsFillWidth;
        $("setRememberTab").checked = s.rememberLastTab;
        $("setCompact").checked = s.compactDensity;
        $("setShowAll").checked = s.showAllFieldsDefault;
        $("setShowExtra").checked = s.showExtraFieldsDefault;
        $("setAutoPage").checked = s.autoPageCount;
        $("setAutoSelect").checked = s.autoSelectFirstOnOpen;
        $("setClearFilter").checked = s.clearFilterOnOpen;
        $("setLiveValidation").value = ["blur", "off"].includes(s.liveValidationMode) ? s.liveValidationMode : "keystroke";

        let reset = false;
        $("settingsReset").onclick = async () => {
            //resets immediately on confirmation; Save's own field-by-field writeback never runs
            const confirmed = await showConfirm("Reset to defaults",
                "This resets every preference - theme, fonts, toggles, grid columns and toolbar - back to default. "
                + "Your open files and remembered extra fields aren't touched. This can't be undone.", "Reset");
            if (!confirmed)
                return;
            state.settings = structuredClone(DEFAULT_SETTINGS);
            saveSettings();
            reset = true;
            $("settingsDialog").close("reset");
        };

        const result = await showDialog($("settingsDialog"));
        if (result === "ok") {
            const size = Number($("setFontSize").value);
            Object.assign(s, {
                theme: theme.value,
                editorFontSize: Number.isFinite(size) ? Math.min(28, Math.max(10, Math.round(size))) : s.editorFontSize,
                editorFontFamily: $("setFontFamily").value,
                coverSource: $("setCoverSource").value,
                editorFieldsFillWidth: $("setFillWidth").checked,
                rememberLastTab: $("setRememberTab").checked,
                compactDensity: $("setCompact").checked,
                showAllFieldsDefault: $("setShowAll").checked,
                showExtraFieldsDefault: $("setShowExtra").checked,
                autoPageCount: $("setAutoPage").checked,
                autoSelectFirstOnOpen: $("setAutoSelect").checked,
                clearFilterOnOpen: $("setClearFilter").checked,
                liveValidationMode: $("setLiveValidation").value,
            });
            saveSettings();
        } else if (!reset) {
            return;
        }
        applyTheme(state.settings.theme);
        applyPreferences();
        $("sortCombo").value = state.settings.sortMode;
        document.documentElement.style.setProperty("--sidebar-width", `${state.settings.sidebarWidth}px`);
        render();
    }

    //---------------------------------------------------------------- open folder

    //asks before opening more than this many at once
    const LARGE_IMPORT_CONFIRM_THRESHOLD = 100;

    //chrome and edge hand back a folder handle, asked for read-write once so every book in it can
    //later be saved in place; other browsers fall back to a folder upload input, read-only
    async function onOpenFolder() {
        if (!window.showDirectoryPicker) {
            $("folderInput").value = "";
            $("folderInput").click();
            return;
        }
        let dir;
        try {
            dir = await window.showDirectoryPicker({ mode: "readwrite" });
        } catch (e) {
            if (e.name !== "AbortError")
                setStatus(`Folder picker failed: ${e.message}`);
            return;
        }
        setStatus(`Scanning ${dir.name}...`);
        const found = [];
        await scanFolder(dir, dir.name, found);
        await openFound(dir.name, found);
    }

    //an unreadable branch is skipped rather than aborting the whole scan, like FindArchivesUnder
    async function scanFolder(dir, path, found) {
        try {
            for await (const entry of dir.values()) {
                if (entry.kind === "directory")
                    await scanFolder(entry, `${path}/${entry.name}`, found);
                else if (isSupported(entry.name))
                    found.push({ handle: entry, path: `${path}/${entry.name}` });
            }
        } catch {
            //skip this branch, keep scanning the rest
        }
    }

    async function onFolderInput(e) {
        const files = [...e.target.files].filter((f) => isSupported(f.name));
        const rootName = e.target.files.length > 0 ? e.target.files[0].webkitRelativePath.split("/")[0] : "";
        await openFound(rootName, files.map((file) => ({ file, path: file.webkitRelativePath || file.name })));
    }

    async function openFound(rootName, found) {
        if (found.length === 0) {
            await showMessage("Open Folder", `No cbz, cbr, zip or rar archives were found under:\n${rootName}`);
            setStatus("Ready");
            return;
        }
        //opening a few thousand files is supported, but it is still worth being asked first
        if (found.length >= LARGE_IMPORT_CONFIRM_THRESHOLD
            && !(await showConfirm("Open folder", `Found ${found.length} archives under:\n${rootName}\n\nOpen all of them?`, "Open All"))) {
            setStatus("Ready");
            return;
        }
        found.sort((a, b) => LiteArchive.naturalCompare(a.path, b.path));
        const sources = [];
        for (const f of found) {
            try {
                sources.push({ file: f.file || await f.handle.getFile(), handle: f.handle || null, path: f.path });
            } catch {
                //a file that vanished between the scan and now is just skipped
            }
        }
        await openSources(sources);
    }

    //---------------------------------------------------------------- file lifecycle

    //reloads from disk when the browser can see the file's current state; a file opened without a
    //handle is a snapshot, so its saved values are the truest "on disk" there is
    async function onRevert() {
        const file = current();
        if (!file)
            return;
        if (file.isDirty && !(await showConfirm("Revert file", `Discard all edits on ${file.name} and reload it from disk?`, "Revert")))
            return;
        try {
            if (file.handle) {
                const fresh = await file.handle.getFile();
                const result = await LiteArchive.read(fresh, fresh.name, state.schema.imageExtensions(), state.settings.coverSource);
                const values = LiteComicInfo.parse(result.xml);
                registerExtrasFrom(values.keys());
                file.reloadFrom({
                    source: fresh, entries: result.entries, rawXml: result.xml, values,
                    pageCount: result.pageCount, coverEntry: result.coverEntry,
                });
                if (file.coverUrl)
                    URL.revokeObjectURL(file.coverUrl);
                file.coverUrl = null;
                loadCovers([file]);
            } else {
                file.revertAll();
            }
            afterSave();
            setStatus(`Reverted ${file.name}`);
        } catch (e) {
            await showMessage("Revert failed", e.message);
        }
    }

    const MAX_RECENTLY_CLOSED = 10;
    const recentlyClosed = [];

    //shared by Remove, Close and Close All - confirms first if any target has unsaved changes
    async function removeWithConfirm(files) {
        if (files.length === 0)
            return;
        const dirty = files.filter((f) => f.isDirty).map((f) => f.name);
        if (dirty.length > 0
            && !(await showConfirm("Unsaved changes", "These files have unsaved changes that will be lost:\n\n" + dirty.join("\n"), "Remove Anyway")))
            return;
        for (const file of files) {
            //the browser can't reopen by path, so the file itself (or its handle) is kept instead
            recentlyClosed.unshift({ file: file.source, handle: file.handle, path: file.path });
            if (file.coverUrl)
                URL.revokeObjectURL(file.coverUrl);
        }
        recentlyClosed.splice(MAX_RECENTLY_CLOSED);
        const gone = new Set(files);
        state.files = state.files.filter((f) => !gone.has(f));
        state.selection = state.selection.filter((f) => !gone.has(f));
        state.gridSelection = state.gridSelection.filter((f) => !gone.has(f));
        refreshDisplayed();
        render();
        updateStatus();
    }

    async function onReopenLastClosed() {
        while (recentlyClosed.length > 0) {
            const entry = recentlyClosed.shift();
            try {
                const file = entry.handle ? await entry.handle.getFile() : entry.file;
                await openSources([{ file, handle: entry.handle, path: entry.path }]);
                return;
            } catch {
                //moved or deleted since - try the next one
            }
        }
        setStatus("No recently closed file to reopen.");
    }

    //---------------------------------------------------------------- tools

    //never overwrites a field that already has a value
    function onGuessFromFilename() {
        if (state.selection.length === 0)
            return;
        let filled = 0;
        for (const file of state.selection) {
            const guess = LiteGuess.fromPath(file.path);
            for (const [tag, value] of [["Series", guess.series], ["Number", guess.number], ["Volume", guess.volume], ["Year", guess.year], ["Count", guess.count]]) {
                if (!value || value.trim().length === 0 || file.getValue(tag).length > 0)
                    continue;
                file.setValue(tag, value);
                filled++;
            }
        }
        afterToolEdit();
        setStatus(filled === 0 ? "No new values guessed from filename" : `Filled ${filled} field${filled === 1 ? "" : "s"} from filename`);
    }

    function onAutoPageCount() {
        const file = current();
        if (!file)
            return;
        file.setValue("PageCount", String(file.detectedPageCount));
        afterToolEdit();
        setStatus(`Page count set to ${file.detectedPageCount}`);
    }

    //scoped to the selection, or every open file when nothing is selected - same rule as Find & Replace
    async function onExtractSummaryHeader() {
        const targets = state.selection.length > 0 ? state.selection : state.files;
        if (targets.length === 0) {
            await showMessage("Extract Publisher/Year from Summary", "Open an archive first.");
            return;
        }
        let changed = 0;
        for (const file of targets) {
            const header = LiteSummaryHeader.tryParse(file.getValue("Summary"));
            if (!header)
                continue;
            //only empty fields get filled, so existing metadata always wins
            if (file.getValue("Publisher").trim().length === 0)
                file.setValue("Publisher", header.publisher);
            if (file.getValue("Year").trim().length === 0)
                file.setValue("Year", header.year);
            file.setValue("Summary", header.summary);
            changed++;
        }
        afterToolEdit();
        if (changed === 0) {
            await showMessage("Extract Publisher/Year from Summary",
                `No "Publisher - Year" header was found in ${targets.length} file(s).\n\n`
                + "This looks for a line like \"DC Comics - 1995\" that stands alone at the top of "
                + "the summary, followed by a blank line.");
            return;
        }
        setStatus(changed === 1
            ? "Extracted a publisher/year header from 1 file - not yet saved"
            : `Extracted publisher/year headers from ${changed} files - not yet saved`);
    }

    async function onCopyXml() {
        const file = current();
        if (!file)
            return;
        const text = LiteComicInfo.toDisplayString(file.rawXml, file.buildWriteValues());
        if (await writeClipboard(text))
            setStatus("ComicInfo.xml copied to clipboard");
        else
            await showMessage("Copy ComicInfo.xml", "This browser didn't allow copying to the clipboard.");
    }

    async function writeClipboard(text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            //older or stricter browsers: copy through a hidden textarea instead
            const area = el("textarea", { value: text, class: "offscreen" });
            document.body.append(area);
            area.select();
            const ok = document.execCommand("copy");
            area.remove();
            return ok;
        }
    }

    async function onPasteXml() {
        const file = current();
        if (!file)
            return;
        let text = null;
        try {
            text = await navigator.clipboard.readText();
        } catch {
            //not allowed here - ask for it to be pasted by hand
            $("pasteBox").value = "";
            if ((await showDialog($("pasteDialog"))) !== "ok")
                return;
            text = $("pasteBox").value;
        }
        if (!text) {
            await showMessage("Paste XML", "The clipboard does not contain any text.");
            return;
        }
        const values = LiteComicInfo.parse(new TextEncoder().encode(text));
        if (values.size === 0) {
            await showMessage("Paste XML", "The clipboard text is not valid ComicInfo XML.");
            return;
        }
        registerExtrasFrom(values.keys());
        file.replaceCurrentValues(values);
        afterToolEdit();
        setStatus(`Metadata replaced from clipboard (${values.size} fields)`);
    }

    async function onFindReplace() {
        if (state.files.length === 0)
            return;
        const scopeIsSelection = state.selection.length > 0;
        const scope = scopeIsSelection ? [...state.selection] : [...state.files];
        $("frScope").textContent = scopeIsSelection
            ? `Applies to ${scope.length} selected file(s).`
            : `Applies to all ${scope.length} open file(s) (nothing selected).`;
        const fields = state.schema.fields;
        $("frField").replaceChildren(...fields.map((f, i) => el("option", { value: String(i) }, f.label)));
        $("frStatus").textContent = "";
        const dialog = $("findReplaceDialog");

        let changed = 0;
        $("frReplaceButton").onclick = async () => {
            const find = $("frFind").value;
            if (find.length === 0) {
                $("frStatus").textContent = "Enter something to find.";
                return;
            }
            const field = fields[Number($("frField").value)];
            const replace = $("frReplace").value;
            //ordinal matching, case-folded unless asked not to; the replacement is literal text
            const pattern = new RegExp(find.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"), $("frCase").checked ? "g" : "gi");
            const toChange = scope.filter((f) => {
                pattern.lastIndex = 0;
                return pattern.test(f.getValue(field.tag));
            });
            if (toChange.length === 0) {
                $("frStatus").textContent = "No matches found.";
                return;
            }
            if (!(await showConfirm("Replace",
                `Replace "${find}" with "${replace}" in the ${field.label} field of ${toChange.length} file(s)?`, "Replace")))
                return;
            for (const file of toChange)
                file.setValue(field.tag, file.getValue(field.tag).replace(pattern, () => replace));
            changed = toChange.length;
            dialog.close("ok");
        };
        //Enter means Replace, the desktop dialog's default button
        const onEnter = (e) => {
            if (e.key === "Enter") {
                e.preventDefault();
                $("frReplaceButton").click();
            }
        };
        $("frFind").onkeydown = onEnter;
        $("frReplace").onkeydown = onEnter;
        await showDialog(dialog);
        if (changed === 0)
            return;
        afterToolEdit();
        setStatus(`Replaced text in ${changed} file(s)`);
    }

    async function onValidateAll() {
        if (state.files.length === 0)
            return;
        const errors = state.files.flatMap((f) => LiteValidation.validate(state.schema, f.name, f.current));
        if (errors.length === 0) {
            await showMessage("Validate All Open Files", `No problems found across ${state.files.length} open file(s).`);
            return;
        }
        const summary = errors.map((err) => `${err.fileName} - ${err.label}\n${err.problem}\nFix: ${err.suggestion}`).join("\n\n");
        await showMessage("Validate All Open Files", `${errors.length} problem(s) across ${state.files.length} open file(s):\n\n${summary}`);
    }

    //Number and Page Count start unchecked - copying them across a batch of different issues
    //would be a predictable footgun
    const COPY_DEFAULT_OFF = new Set(["Number", "PageCount"]);

    //the right-clicked file is the source; every other selected file is a target
    async function onCopyFieldsToSelection() {
        const source = lastRightClicked;
        const targets = state.selection.filter((f) => f !== source);
        if (!source || targets.length === 0) {
            setStatus("Select multiple files, then right-click one to copy its fields to the rest");
            return;
        }
        const populated = state.schema.fields.filter((f) => source.getValue(f.tag).length > 0);
        if (populated.length === 0) {
            await showMessage("Copy fields", `'${source.name}' has no populated fields to copy.`);
            return;
        }
        $("copyFieldsIntro").textContent = `Copy from '${source.name}' to the other ${targets.length} selected file${targets.length === 1 ? "" : "s"}:`;
        const truncate = (s) => (s.length <= 40 ? s : s.slice(0, 40) + "…");
        const checks = populated.map((f) => ({
            tag: f.tag,
            ...checkRow(`${f.label}: ${truncate(source.getValue(f.tag))}`, !COPY_DEFAULT_OFF.has(f.tag)),
        }));
        $("copyFieldsList").replaceChildren(...checks.map((c) => c.row));
        if ((await showDialog($("copyFieldsDialog"))) !== "ok")
            return;
        const tags = checks.filter((c) => c.box.checked).map((c) => c.tag);
        if (tags.length === 0)
            return;
        for (const target of targets) {
            for (const tag of tags)
                target.setValue(tag, source.getValue(tag));
        }
        afterToolEdit();
        setStatus(`Copied ${tags.length} field${tags.length === 1 ? "" : "s"} from '${source.name}' to ${targets.length} file${targets.length === 1 ? "" : "s"}`);
    }

    //tools edit files directly, so the list, editor and status all catch up afterwards, like RefreshEditor
    function afterToolEdit() {
        refreshDisplayed();
        render();
        updateStatus();
    }

    //---------------------------------------------------------------- file list menu

    let lastRightClicked = null;

    //right-clicking outside the current selection replaces it first
    function onFileRowMenu(file, e) {
        e.preventDefault();
        lastRightClicked = file;
        if (!state.selection.includes(file))
            select(file);
        closePopups();
        const has = state.selection.length > 0;
        const batch = state.selection.length > 1;
        const item = (label, enabled, run) => el("button", {
            class: "popup-item",
            type: "button",
            disabled: !enabled,
            onclick: (ev) => {
                ev.stopPropagation();
                closePopups();
                run();
            },
        }, label);
        $("contextMenu").replaceChildren(
            item("Save", has, onSave),
            item("Revert", has, onRevert),
            item("Remove from List", has, () => removeWithConfirm([...state.selection])),
            el("div", { class: "menu-sep" }),
            item("Copy Fields to Rest of Selection", batch, onCopyFieldsToSelection),
            el("div", { class: "menu-sep" }),
            el("button", { class: "popup-item desktop-only", type: "button", dataset: { desktop: "OpenContainingFolder" } },
                "Open Containing Folder", el("span", { class: "desktop-tag" }, "desktop app")));
        showPopupNear($("contextMenu"), { left: e.clientX, right: e.clientX, top: e.clientY, bottom: e.clientY });
    }

    //---------------------------------------------------------------- desktop-only features and help

    //what each desktop-only menu item, toolbar button or setting would do, and why it can't here
    const DESKTOP_ONLY = {
        OpenRecent: ["Open Recent",
            "A web page never learns where a file lives on disk - it only sees the files you pick or drop each time - so it can't reopen one later by itself.\n\nReopen Last Closed File (in the File menu) works for files closed since this page was loaded."],
        OpenContainingFolder: ["Open Containing Folder",
            "A web page can't open folders on your computer, or even know which folder a file came from."],
        SearchComicVine: ["Search ComicVine",
            "The desktop app looks books up on ComicVine with your own API key and fills in their metadata for you.\n\ncbzLite makes no network requests at all, so it can't."],
        Combine: ["Combine Archives",
            "Joining a split trade paperback back into one book means reading several whole archives and writing a new one. That heavy lifting is left to the desktop app, so this page stays quick and light."],
        TrimFooters: ["Trim Branding Footers",
            "Finding and cutting the branding strip some sources add to pages means decoding and re-encoding page images - slow and memory-hungry in a browser. The desktop app does it natively."],
    };

    function explainDesktopOnly(id) {
        const [title, why] = DESKTOP_ONLY[id];
        return showMessage(`${title} needs the desktop app`, why, { link: true });
    }

    function onAbout() {
        $("aboutVersion").textContent = VERSION;
        return showDialog($("aboutDialog"));
    }

    function onLimits() {
        return showDialog($("limitsDialog"));
    }

    //a page loaded from the website is always the newest; a saved copy is frozen at its version
    function onCheckForUpdates() {
        const online = location.protocol === "https:" || location.protocol === "http:";
        return showMessage("Check for Updates", online
            ? `You're on cbzLite ${VERSION}. cbzLite updates itself: the copy on the website is always the latest, so reloading the page gets any new version.`
            : `You're on cbzLite ${VERSION}, saved to open offline, so it doesn't update itself. Open ${SITE_URL} for the latest version, and save that copy if you want an updated offline one.`,
            { link: true });
    }

    //---------------------------------------------------------------- menus and toolbar

    const actions = {
        open: onOpen,
        save: onSave,
        saveAs: onSaveAs,
        saveAll: onSaveAll,
        toggleShowAll: () => {
            state.showAllFields = !state.showAllFields;
            renderChecks();
            renderEditor();
        },
        toggleShowExtra: () => {
            state.showExtraFields = !state.showExtraFields;
            renderChecks();
            renderEditor();
        },
        toggleGrid: onToggleGrid,
        about: onAbout,
        limits: onLimits,
        checkUpdates: onCheckForUpdates,
        openFolder: onOpenFolder,
        revert: onRevert,
        remove: () => removeWithConfirm([...state.selection]),
        closeFile: () => removeWithConfirm(current() ? [current()] : []),
        closeAll: () => removeWithConfirm([...state.files]),
        reopenClosed: onReopenLastClosed,
        guessFromFilename: onGuessFromFilename,
        autoPageCount: onAutoPageCount,
        extractSummaryHeader: onExtractSummaryHeader,
        copyXml: onCopyXml,
        pasteXml: onPasteXml,
        findReplace: onFindReplace,
        validateAll: onValidateAll,
        chooseColumns: onChooseColumns,
        customizeToolbar: onCustomizeToolbar,
        settings: onSettings,
        toolbarLeft: () => $("toolbarScroll").scrollBy({ left: -200 }),
        toolbarRight: () => $("toolbarScroll").scrollBy({ left: 200 }),
    };

    function closeMenus() {
        for (const m of document.querySelectorAll(".menu.open, .menu-sub.open"))
            m.classList.remove("open");
    }

    function wireMenus() {
        const menus = [...document.querySelectorAll("[data-menu]")];
        for (const menu of menus) {
            const header = menu.querySelector(".menu-header");
            header.addEventListener("click", (e) => {
                e.stopPropagation();
                const wasOpen = menu.classList.contains("open");
                closeMenus();
                if (!wasOpen)
                    menu.classList.add("open");
            });
            //like a desktop menu bar, hovering another header while one is open switches to it
            header.addEventListener("pointerenter", () => {
                if (menus.some((m) => m.classList.contains("open")) && !menu.classList.contains("open")) {
                    closeMenus();
                    menu.classList.add("open");
                }
            });
        }
        document.addEventListener("click", () => {
            closeMenus();
            closePopups();
        });

        //delegated, since the toolbar is rebuilt whenever it's customised
        document.addEventListener("click", (e) => {
            const node = e.target.closest("[data-action], [data-desktop]");
            if (!node || node.disabled)
                return;
            e.preventDefault();
            e.stopPropagation();
            closeMenus();
            closePopups();
            if (node.dataset.desktop)
                explainDesktopOnly(node.dataset.desktop);
            else
                actions[node.dataset.action]();
        }, true);

        //submenus open on hover or click, like a desktop flyout
        for (const sub of document.querySelectorAll(".menu-sub")) {
            const open = () => {
                for (const other of sub.parentElement.querySelectorAll(".menu-sub.open"))
                    other.classList.remove("open");
                sub.classList.add("open");
            };
            sub.addEventListener("pointerenter", open);
            sub.querySelector(".has-sub").addEventListener("click", (e) => {
                e.stopPropagation();
                open();
            });
        }
        for (const item of document.querySelectorAll(".menu-flyout > .menu-item")) {
            item.addEventListener("pointerenter", () => {
                for (const sub of item.parentElement.querySelectorAll(".menu-sub.open"))
                    sub.classList.remove("open");
            });
        }
    }

    function wireKeyboard() {
        document.addEventListener("keydown", (e) => {
            const ctrl = e.ctrlKey || e.metaKey;
            const key = e.key.toLowerCase();
            if (ctrl && !e.altKey && key === "o") {
                e.preventDefault();
                if (e.shiftKey)
                    onOpenFolder();
                else
                    onOpen();
            } else if (ctrl && e.altKey && !e.shiftKey && key === "s") {
                e.preventDefault();
                onSaveAll();
            } else if (ctrl && !e.altKey && key === "s") {
                e.preventDefault();
                if (e.shiftKey)
                    onSaveAs();
                else
                    onSave();
            } else if (e.key === "Escape") {
                closeMenus();
                closePopups();
            } else if (e.altKey && !ctrl && ["f", "v", "t", "h"].includes(key)) {
                e.preventDefault();
                closeMenus();
                const index = ["f", "v", "t", "h"].indexOf(key);
                document.querySelectorAll("[data-menu]")[index].classList.add("open");
            }
            $("menubar").classList.toggle("mnemonics", e.altKey);
        });
        document.addEventListener("keyup", (e) => $("menubar").classList.toggle("mnemonics", e.altKey));
        window.addEventListener("blur", () => $("menubar").classList.remove("mnemonics"));
    }

    function wireSplitter() {
        const splitter = $("splitter");
        const apply = (w) => document.documentElement.style.setProperty("--sidebar-width", `${w}px`);
        apply(state.settings.sidebarWidth);
        splitter.addEventListener("pointerdown", (e) => {
            splitter.setPointerCapture(e.pointerId);
            splitter.classList.add("dragging");
            const startX = e.clientX;
            const startW = $("sidebar").getBoundingClientRect().width;
            const move = (ev) => {
                state.settings.sidebarWidth = Math.round(Math.min(600, Math.max(180, startW + ev.clientX - startX)));
                apply(state.settings.sidebarWidth);
            };
            const up = () => {
                splitter.classList.remove("dragging");
                splitter.removeEventListener("pointermove", move);
                saveSettings();
            };
            splitter.addEventListener("pointermove", move);
            splitter.addEventListener("pointerup", up, { once: true });
        });
    }

    function wireDragDrop() {
        let depth = 0;
        const overlay = $("dropOverlay");
        const hasFiles = (e) => e.dataTransfer && [...e.dataTransfer.types].includes("Files");
        window.addEventListener("dragenter", (e) => {
            if (!hasFiles(e))
                return;
            depth++;
            overlay.hidden = false;
        });
        window.addEventListener("dragleave", () => {
            depth = Math.max(0, depth - 1);
            if (depth === 0)
                overlay.hidden = true;
        });
        window.addEventListener("dragover", (e) => {
            if (hasFiles(e))
                e.preventDefault();
        });
        window.addEventListener("drop", async (e) => {
            if (!hasFiles(e))
                return;
            e.preventDefault();
            depth = 0;
            overlay.hidden = true;
            //handles have to be requested synchronously, before the drop event's data goes away
            const items = [...e.dataTransfer.items].filter((i) => i.kind === "file");
            const pending = items.map((item) => ({
                file: item.getAsFile(),
                handle: item.getAsFileSystemHandle ? item.getAsFileSystemHandle() : null,
            }));
            const sources = [];
            for (const p of pending) {
                if (!p.file || !isSupported(p.file.name))
                    continue;
                let handle = null;
                try {
                    handle = p.handle ? await p.handle : null;
                } catch {
                    handle = null;
                }
                sources.push({ file: p.file, handle: handle && handle.kind === "file" ? handle : null });
            }
            if (sources.length === 0) {
                setStatus("Drop cbz/cbr files to open them");
                return;
            }
            await openSources(sources);
        });
    }

    function wireInputs() {
        for (const button of document.querySelectorAll("[data-dialog-cancel]"))
            button.addEventListener("click", () => button.closest("dialog").close("cancel"));
        for (const button of document.querySelectorAll("[data-dialog-value]"))
            button.addEventListener("click", () => button.closest("dialog").close(button.dataset.dialogValue));
        //keeps focus (and the text selection) in the field while its menu is used
        $("contextMenu").addEventListener("mousedown", (e) => e.preventDefault());
        $("fileInput").addEventListener("change", (e) => {
            const files = [...e.target.files].map((file) => ({ file, handle: null }));
            if (files.length > 0)
                openSources(files);
        });
        $("fileList").addEventListener("keydown", onFileListKey);
        $("folderInput").addEventListener("change", onFolderInput);
        $("gridScroll").addEventListener("keydown", onGridKey);
        $("gridScroll").addEventListener("scroll", closePopups);
        //popups are placed in viewport coordinates, so they'd drift away from their field
        $("fieldScroll").addEventListener("scroll", closePopups);
        window.addEventListener("resize", closePopups);
        $("searchBox").addEventListener("input", (e) => {
            state.search = e.target.value;
            renderEditor();
        });
        $("fileFilter").addEventListener("input", (e) => {
            state.filter = e.target.value;
            refreshDisplayed();
            renderFileList();
        });
        $("sortCombo").value = state.settings.sortMode;
        $("sortCombo").addEventListener("change", (e) => {
            state.settings.sortMode = e.target.value;
            saveSettings();
            refreshDisplayed();
            renderFileList();
        });
        window.addEventListener("beforeunload", (e) => {
            if (state.files.some((f) => f.isDirty))
                e.preventDefault();
        });
    }

    //---------------------------------------------------------------- startup

    async function boot() {
        loadSettings();
        document.title = `cbzLite ${VERSION}`;
        const [schemaDoc, themesDoc] = await Promise.all([
            loadJson("cbz-schema", "../cbzLab/Assets/schema.json"),
            loadJson("cbz-themes", "../cbzLab/Assets/themes.json").catch(() => null),
        ]);
        state.schema = new LiteSchema.Schema(schemaDoc);
        state.themesDoc = themesDoc;
        applyTheme(state.settings.theme);

        state.showAllFields = state.settings.showAllFieldsDefault;
        state.showExtraFields = state.settings.showExtraFieldsDefault;
        const tabIndex = state.settings.rememberLastTab ? state.settings.activeTab : 0;
        state.activeTab = LiteSchema.TAB_ORDER[tabIndex] || LiteSchema.TAB_ORDER[0];
        applyPreferences();

        wireMenus();
        wireKeyboard();
        wireSplitter();
        wireDragDrop();
        wireInputs();
        $("app").hidden = false;
        render();
    }

    boot().catch((e) => {
        document.body.textContent = `cbzLite failed to start: ${e.message}`;
    });
})();
