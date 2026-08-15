# cbzLab

A native Windows metadata editor for comic book archives. cbzLab opens CBZ and CBR
files, lets you view and edit the `ComicInfo.xml` metadata inside them (single files
or whole batches at once), and writes changes back safely without ever touching the
page images.

Built with C# / .NET 8 and WinUI 3 (Windows App SDK 1.6), deployed as an unpackaged
self-contained executable — no MSIX, no installer, no store.

---

## 1. Environment setup

You need two things: the **.NET 8 SDK** and **Visual Studio 2022** with the right
workload. Total download is a few GB; allow half an hour on a fresh machine.

### 1.1 Install Visual Studio 2022

1. Download **Visual Studio 2022 Community** (free) from
   <https://visualstudio.microsoft.com/downloads/>. Version 17.10 or later.
2. Run the installer. On the **Workloads** tab, tick:
   - **WinUI application development** — this is the one that matters. It pulls in
     the Windows App SDK templates, XAML tooling and the Windows SDK.
   - If you don't see that workload (older installer versions), instead tick
     **.NET desktop development**, then switch to the **Individual components** tab
     and add **Windows App SDK C# Templates** and a **Windows 11 SDK** (10.0.22621
     or newer).
3. Let it install. The .NET 8 SDK comes bundled with current VS 2022 releases — you
   can confirm afterwards by opening a terminal and running `dotnet --list-sdks`;
   you want an `8.0.x` entry. If it's missing, grab it from
   <https://dotnet.microsoft.com/download/dotnet/8.0>.

### 1.2 First-time Visual Studio orientation

Since you're new to VS but not to coding, the short version:

- **Solution Explorer** (right-hand panel) is your file tree. The `.sln` is the
  workspace; the `.csproj` is the project (roughly equivalent to a `pyproject.toml`
  plus build config in one).
- The toolbar dropdowns near the Run button select **configuration** (Debug/Release),
  **platform** (x64/ARM64) and the **launch profile**.
- **F5** = build and run with debugger. **Ctrl+F5** = run without debugger.
  **Ctrl+Shift+B** = just build.
- NuGet packages (the .NET equivalent of pip packages) restore automatically on
  first build. If anything looks unresolved, right-click the solution →
  **Restore NuGet Packages**.

### 1.3 Opening the project

1. Unzip the source anywhere sensible (avoid deeply nested paths; Windows path
   length limits are less painful than they used to be but still exist).
2. Double-click `cbzLab.sln`, or in VS use **File → Open → Project/Solution**.
3. In the toolbar, set platform to **x64** (or **ARM64** on an ARM machine) and the
   launch profile to **cbzLab (Unpackaged)**.
4. Press **F5**. First build takes a while (NuGet restore + XAML compile); after
   that it's quick.

---

## 2. Building a distributable executable

Debug builds run from `bin\x64\Debug\...` and are fine for development. For a
build you can copy to another machine:

### From Visual Studio

Right-click the **cbzLab** project → **Publish** is overkill for unpackaged apps;
the simpler route is the command line below.

### From the command line (recommended)

Open a terminal in the solution folder and run:

```powershell
dotnet publish cbzLab\cbzLab.csproj -c Release -r win-x64 --self-contained true
```

For ARM64:

```powershell
dotnet publish cbzLab\cbzLab.csproj -c Release -r win-arm64 --self-contained true
```

The output lands in `cbzLab\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish\`
(adjust for platform). Managed dependencies (including SharpCompress) are
collapsed into `cbzLab.exe` itself via `PublishSingleFile`. A number of
native Windows App SDK files (`Microsoft.ui.xaml.dll`, `DWriteCore.dll`, the
WindowsAppRuntime bootstrapper, `resources.pri`) still sit alongside it —
the OS loads these directly rather than through .NET, so they can't be
folded into the exe; this is true of every unpackaged WinUI 3 app, not
something specific to this project. `SatelliteResourceLanguages` is set to
`en` so the dozens of per-culture localization folders .NET and WindowsAppSDK
ship by default aren't copied — this app has no localized UI of its own.
Copy the whole folder wherever you like and run `cbzLab.exe`; nothing needs
installing on the target machine.

An earlier version of this project reverted `PublishSingleFile`, having
initially (and incorrectly) blamed it for a launch crash. The crash turned
out to be an unrelated WindowsAppSDK/OS version mismatch — see the
changelog around 0.1.23–0.1.27 for the full story if you're curious.
`PublishSingleFile` is officially supported for unpackaged, self-contained
WinUI 3 apps as of WindowsAppSDK 1.5+, which this project now exceeds.

Note: the publish folder must stay together — the exe depends on every
native file beside it.

---

## 3. Using cbzLab

### Opening files

- **File → Open** (Ctrl+O), the toolbar button, or pass paths on the command line:
  `cbzLab.exe "C:\comics\Batman 001.cbz" "C:\comics\Batman 002.cbz"`.
- CBZ/ZIP and CBR/RAR are both supported for reading. Mislabelled files (a RAR
  renamed to .cbz, say) are detected by content, not extension.
- Recently opened files live under **File → Open Recent**.
- You can also drag cbz/cbr files from Explorer straight onto the window to open them.

### The editor

- Open files appear in the left sidebar with a small cover thumbnail (taken
  from the first image in the archive), the filename, and a derived subtitle
  (series/number/volume). A coloured dot marks unsaved changes.
- Above the file list, a **Filter files…** box narrows the sidebar by filename
  or subtitle — this is separate from the field search box lower down, which
  only searches fields within the file(s) currently open in the editor.
- The sort dropdown next to it offers **Name**, **Series / No.**, and
  **Modified** (unsaved files first). Name and Series/No. sort only update when
  you change the sort mode or the filter — not on every keystroke while you're
  editing a field, so the list doesn't reorder itself under your cursor mid-edit.
  Modified updates live as files become dirty, since that's the whole point of it.
- Selecting a single file shows a larger cover and title banner above the
  search box; it's hidden during batch editing since it wouldn't apply to
  more than one file.
- Metadata fields are grouped into five tabs: **Basic Info**, **Publication**,
  **Creators**, **Story** and **Extras**. The tabs filter one shared form — they're
  views onto the same data, not separate pages.
- By default only fields with values are shown. Toggle **All Fields** (toolbar or
  View menu) to see everything, including empty fields.
- The search box above the tabs filters the current tab's fields by label or value.
- Hover any field label for a tooltip explaining what the field is for.
- Fields not part of the official ComicInfo v2.0 schema that are found in your
  files are registered automatically, appear on the **Extras** tab, and are
  remembered between sessions. The **Extras** toggle shows/hides them.
- Single-line fields (Writer, Publisher, Imprint, and so on) remember what you've
  typed into them before. A small picker button appears beside the field once
  it has any history — click it to pick a recent value instead of retyping.
  Doesn't apply to multi-line fields (Summary, Notes) or dropdown fields (which
  already have their own curated list of options).

### Batch editing

- Select multiple files in the sidebar (Ctrl-click / Shift-click). The editor
  switches to batch mode and a panel appears listing the files in scope.
- Fields where the selected files agree show the common value. Fields that differ
  show a coloured *"(multiple values — edit to override all)"* placeholder.
- Anything you type or pick applies to **every** selected file, immediately marking
  them all dirty. Untouched fields keep their per-file values.
- Dropdown fields in batch mode become a picker listing each distinct value with a
  count of how many files carry it — click one to apply it to the whole selection.
- Search is disabled while in batch mode.
- **Copy Fields to Rest of Selection** (right-click a file within a multi-select):
  copies the right-clicked file's own field values onto every other selected
  file. Only overwrites the fields you tick in the confirmation list — Number
  and Page Count start unticked since they're almost always specific to one
  issue.

### Grid view

The **Grid View** toggle (toolbar, far right) swaps the whole sidebar+editor
layout for a full-width table of your open files — one row per file, one
column per field, useful for spotting which books are missing a field at a
glance. A dot in the leftmost column marks unsaved files, same as the
sidebar. Right-click the grid, or **View → Choose Grid Columns…**, to pick
which fields appear — defaults to Series, Number, Writer, Publisher, listed
in a curated "most likely wanted" order rather than raw schema order; your
choice is remembered either way. The grid is backed by the same file list as
the sidebar, so it already reflects your current sort and filter.

Cells themselves are read-only — editing happens back in the normal view.
**Double-click** any row to jump straight there for that one file, regardless
of anything else you've selected. **Right-click** adapts to your selection:
one file offers "Edit This Book", several offer "Edit N Books in Batch
Editor". Your selection carries across the toggle either way — switching
into grid view seeds it from whatever's selected in the sidebar, switching
back out (via the toolbar toggle, not double/right-click) does the reverse.

If the toolbar is wider than the window, small arrow buttons appear at each
end to scroll it left/right rather than wrapping to a second row.

### Keyboard navigation

- **Ctrl+1** through **Ctrl+5** jump straight to a tab; **Ctrl+Tab** /
  **Ctrl+Shift+Tab** cycle through them.
- **F6** moves keyboard focus to the file list; **Shift+F6** moves it to the
  search box in the editor pane.
- Right-click any single field for **Revert to Saved**, which discards a
  pending edit to just that one field (single-file mode only).

### Saving

- **Save** (Ctrl+S) writes the selected file(s) back in place, same format and path.
- **Save As** (Ctrl+Shift+S) writes a single file to a new path, as CBZ or CBR.
- **Save All** (Ctrl+Alt+S) writes every file with unsaved changes, showing a
  confirmation list with a per-file format selector first.
- Values are validated on save (whole-number fields, Community Rating range and so
  on). Problems are listed with suggested fixes and you can fix or save anyway.
- Numeric fields also validate as you type: an invalid value gets a red outline
  and an inline message straight away, rather than waiting until you save.
- Writes are atomic: the new archive is built as a temporary file and swapped in
  only when complete, so a crash mid-save can't corrupt your comics.
- Page images and any `<Pages>` element in the existing XML are preserved
  byte-for-byte in spirit — only the flat metadata elements are touched.

### CBR (RAR) writing — important

Reading CBR needs nothing extra. **Writing** CBR requires an external tool because
the RAR format can only be created by WinRAR's own `rar.exe`:

- If WinRAR is installed and `rar` is on your PATH (or you set the path in
  Settings), CBR saves work fully — in-place updates and CBZ→CBR conversion.
- 7-Zip (`7z`/`7za`/`7zz`) is accepted as a configured tool but **cannot create
  RAR archives**; attempts will fail with the tool's own error message. Its
  practical use is limited — if you don't have WinRAR, save as CBZ instead
  (arguably the better format anyway).

### Tools

- **Guess from Filename** — parses Series, Number, Volume and Year out of the
  selected file(s)' own filename, falling back to the parent folder name for
  Series when the filename alone doesn't have enough to go on. Only fills
  fields that are currently empty; never overwrites anything you've already
  set. Works across a whole batch selection, since each file's own path is
  used independently.
- **Auto Page Count** — counts image files in the current archive and writes the
  result to the Page Count field. Also runs automatically on open (fills the field
  only when it's empty; configurable in Settings).
- **Copy XML** — puts the current file's ComicInfo.xml (with your pending edits)
  on the clipboard.
- **Paste XML** — replaces the current file's metadata from ComicInfo XML on the
  clipboard. Handy for cloning metadata between files.

### Settings, themes and storage

**Tools → Settings** covers theme, editor font size and font, default field
visibility, default save format, batch-save confirmation, auto page count,
recent-files length, the RAR tool path, whether the first opened file is
auto-selected, whether the sidebar filter clears when new files are opened,
when live field validation runs (as you type / when you leave the field /
off), how many recently typed values are remembered per field, which page of
the archive becomes the cover thumbnail (first or last), whether fields fill
the available editor width or stay capped at a fixed width, whether the last
active tab is remembered between sessions, and a compact spacing option that
fits more on screen.

A **Reset to Defaults** button in the same dialog resets all of the above
back to their defaults (confirmed first, since it includes your ComicVine
API key). It only touches your preferences — auto-discovered unofficial
fields, recently typed values, and cached ComicVine lookups are separate
accumulated data and aren't affected.

**Online metadata lookup (ComicVine)** — off by default, near the bottom of
Settings. Turning it on reveals an API key field (get a free one at
comicvine.gamespot.com/api), a Test Connection button to check the key works,
and an "always review matches before applying" option — and adds a **Search
ComicVine** action (Tools menu and toolbar) that's otherwise not present
anywhere in the app at all.

Search ComicVine looks up a single selected file's series (using its own
Series field if set, or a filename/folder guess otherwise), then helps you
find the matching issue — confirming an auto-match rather than applying it
silently, since issue-number matching can be genuinely ambiguous (variant
covers, facsimile editions, reprints). A series you've already matched is
remembered, so tagging several issues from the same run only needs one real
search. Once an issue is confirmed, a review dialog shows only the fields
where ComicVine's data actually differs from what's already in the file —
current value and proposed value shown side by side — so you can judge each
one before it's applied. Nothing is written unless you tick it and confirm.

Selecting **multiple files** and running Search ComicVine does one series
search for the whole batch, then matches each file to its own issue
individually — every file always gets its own matched data, never one
file's values forced onto the rest. The review step covers every matched
file at once: a field where every file's match agrees (Writer, Publisher,
Characters, and similar series-level facts) shows as a single line and is
ticked by default; a field that actually differs across the matched files
is flagged and left unticked, listing every distinct value and how many
files carry it, since that's exactly the situation worth a second look
before applying — it might be a genuine change partway through the run, or
it might be a mismatch. Fields that are naturally expected to vary issue to
issue (Number, Title, dates, Summary) aren't held to that comparison at
all. Any file that can't be matched is skipped and named at the end so it
can be re-run on its own.

Two more preferences are remembered automatically, without a Settings control,
since they're already directly adjustable in the main window itself: the sidebar
sort mode (the dropdown above the file list) and the window's size and position.

Everything lives in `%APPDATA%\cbzLab`:

| File / folder            | Purpose                                              |
|--------------------------|------------------------------------------------------|
| `cbzLab_settings.json`   | preferences and recent files                         |
| `schema.json`            | field definitions — editable, seeded on first run    |
| `schema_extra.json`      | auto-registered unofficial fields                    |
| `recent_values.json`     | recently typed values per field, for the picker      |
| `comicvine_cache.json`   | cached ComicVine lookups (foundation only for now — see below) |
| `themes.json`            | built-in theme definitions — editable                |
| `themes\*.json`          | custom themes, one file per theme                    |
| `logs\cbzLab-yyyyMMdd.log` | one plain-text log file per day (Settings → Open logs folder) |

To make your own theme, copy one of the files in `themes\` (e.g.
`Synthwave Dark.json`), rename it — the filename becomes the theme name — and
change the colour values. Keys you omit fall back to Solarized Dark. Themes are
picked up at launch and apply instantly when selected, no restart needed. Keys
beginning with `_` are treated as comments.

---

## 4. Project layout

```
cbzLab.sln
cbzLab/
  cbzLab.csproj            project config: unpackaged WinUI 3, self-contained
  app.manifest             dpi awareness
  App.xaml / App.xaml.cs   service wiring, theme bootstrap, cli handling
  MainWindow.xaml / .cs    full ui + orchestration of i/o and dialogs
  Assets/                  icons, bundled schema.json, themes (seeded to %APPDATA%)
  Models/                  schema, settings and validation data classes
  Services/                settings, schema, themes, xml, archives, validation
  ViewModels/              main/file/field view models (data flow, dirty tracking)
  Dialogs/                 settings, multi-save, progress, validation, about
  Converters/              xaml value converters and the field template selector
```

Design notes for future maintenance:

- Archive writes always go temp-file-then-atomic-replace. Don't "optimise" that away.
- The theme system works by mutating a fixed set of `SolidColorBrush` instances
  that both the app's own styles and a set of overridden system control resources
  point at. Adding a themed control usually just means binding to an existing
  `Th*` brush.
- `ComicInfoXml.Build` layers edits on top of the original raw XML bytes so
  complex elements (`<Pages>`) survive untouched. Parsing and writing are
  DTD-disabled — archive contents are untrusted input.
- The five editor tabs are filters over one shared field list. Tab assignment is
  the `TabMap` in `SchemaService`; unknown fields land on Extras.
