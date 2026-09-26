<img src="cbzLab/Assets/logo.png" width="120" alt="cbzLab logo">

# Changelog

> Covers **cbzLab**, the Avalonia rewrite - the current, actively developed
> version, cross-platform on Windows/Linux/macOS. The archived WinUI 3
> version's own history (`cbzLab.winui3/`, see
> [`cbzLab.winui3/ARCHIVED.md`](cbzLab.winui3/ARCHIVED.md)) follows below it,
> kept for the record.

## cbzLab (Avalonia)

### 2.0.10 - 2026-09-27

- Fixed Check for Updates being unable to install anything since 2.0.9. The
  updater only looked for a `.zip` (Windows) or `.tar.gz` (Linux) download,
  and 2.0.9 stopped publishing those in favour of the bare executable, so it
  could only offer a link. The updater now prefers the bare executable, falls
  back to the archive, and never mistakes a debug zip for an update.
- Releases publish the `.zip`/`.tar.gz` archives again, containing just the
  executable, so installs older than this one can still update themselves.
- Release notes now open with a short guide to which download to pick.

### 2.0.9 - 2026-09-27

- GitHub releases no longer bundle debug symbols into the main download. Each
  release now ships four assets: a plain Windows exe and a plain Linux binary
  (no `.pdb`, no zip - the executable is the whole download), plus a
  win-x64/linux-x64 debug zip for each platform containing the executable
  together with its `.pdb`, for anyone who actually needs to debug a crash.
- Fixed a version-string drift bug: 2.0.8's release bumped `cbzLab.csproj`'s
  `<Version>` but missed `MainWindow.axaml.cs`'s `DisplayVersion` constant and
  `MainWindow.axaml`'s `Title`, so the title bar and the About/Check for
  Updates dialogs kept reporting 2.0.7 even after 2.0.8 shipped. All three are
  back in sync as of this release.

### 2.0.8 - 2026-09-21

- Fixed the CI build, which had been failing since SkiaSharp was auto-bumped
  from 3.119.4 to 4.152.1. The app compiled fine, but every FooterTrimService
  test crashed at runtime: the managed SkiaSharp had jumped to 152 while the
  native libSkiaSharp bundled by Avalonia.Skia stayed at 119, and the two
  refuse to run together. SkiaSharp is pinned back to 3.119.4, the version
  Avalonia.Skia actually resolves, and told to move only when Avalonia does.
- Told Dependabot to leave SkiaSharp alone, so the same incompatible bump
  cannot land again on its own.

### 2.0.7 - 2026-09-13

- Extracting a "Publisher - Year" header from the summary is now a Tools
  command rather than something that happens on open. Running on every open
  meant a folder import could mark hundreds of files dirty at once, before
  anyone had asked for it. Tools > Extract Publisher/Year from Summary applies
  it to the selected files, or to every open file when nothing is selected,
  the same scope rule Find and Replace uses, and reports how many it changed.
- Dropped the settings toggle that went with the old behaviour. A command you
  choose to run needs no switch, and a switch that gates nothing is just
  another setting that appears to do something and does not.

### 2.0.6 - 2026-09-13

- Some sources put the publisher and year at the top of the Summary instead of
  in the proper fields, as a "Publisher - Year" line followed by a blank line.
  On open, that line is now read: the publisher and year go into those fields
  if they are empty, and the line is removed from the summary along with the
  blank line and any surrounding whitespace. Fields that already have a value
  are never overwritten, and the edit is left unsaved so it shows in the editor
  and can be reverted. Controlled by a new Settings toggle, on by default.
- The match is deliberately fussy, because getting it wrong would eat someone's
  prose: the line must stand alone, end in a four digit year, and be followed
  by a blank line or nothing. A summary opening "Batman - 1939 was the year it
  all began." is left completely alone. Any of the three dash characters works
  as the separator, since the summary was written by whoever packaged the book.

### 2.0.5 - 2026-09-13

- New File > Open Folder... (Ctrl+Shift+O, and an optional toolbar button):
  opens every cbz/cbr/zip/rar under a folder, including subfolders, in one go.
  An unreadable branch is skipped rather than aborting the scan, results are
  natural-sorted so issue 10 follows issue 2, and opening more than 100 at once
  asks first. Verified on a 2000-book tree spread across 17 subfolders: the scan
  found all 2000 and the import completed in about 3 seconds.
- Files opened this way save back to exactly where they came from. Each file
  carries its own absolute path and every save path writes to it, so a book in
  a nested subfolder is rewritten in place - confirmed by editing the most
  deeply nested book in a test tree, saving, and checking that the file on disk
  changed while the folder structure and every other book stayed untouched.
- File rows now show their full path as a tooltip, since after a recursive open
  two books in different subfolders can easily share a filename.

### 2.0.4 - 2026-09-13

- Opening a large batch of archives is dramatically faster. Measured on 2000
  books, a bulk import went from 6.52s to 0.12s (53x), and 10,000 books now
  import in 0.81s at a flat 0.08ms per book. Three things were responsible:
  the file list was re-filtered and re-sorted once per file (O(n^2 log n)
  overall), the whole settings file was rewritten to disk once per file for a
  recent-files list that only keeps 8 entries, and archives were read strictly
  one at a time. Reads are now parallel, and the list and settings are each
  updated once at the end.
- Cover thumbnails load lazily, per visible row, instead of being decoded for
  every open file up front. Holding one decoded cover per book cost ~234KB
  each - about 457MB at 2000 books - which was the real ceiling on library
  size rather than speed. Memory is now bounded by what is on screen, and a
  bulk import no longer pays for a second pass through each archive just to
  extract a cover it may never show.

### 2.0.3 - 2026-09-13

- New Tools → Combine Archives…, for reassembling a TPB that was released split
  across several files. Select two or more open archives, order them (defaulting
  to alphabetical by filename, with up/down reordering and Sort A-Z / Reverse),
  and they're joined into one new CBZ: every source's pages in order, renumbered
  into a single continuous sequence that follows the first book's own naming
  convention, plus the first book's ComicInfo.xml. The joined book is then opened
  and selected in the editor with its Page Count corrected and any trailing
  "(Part 1)"-style marker trimmed off Title/Series - left unsaved and visible, so
  the change can be reviewed or reverted rather than applied silently. Also
  available as an optional "Combine…" toolbar button.
- New Tools → Trim Branding Footers…, which finds and cuts the strip some
  sources graft onto the bottom of pages (a solid bar carrying a site name).
  It is self-calibrating - the bar's height is measured from the book itself,
  so nothing needs configuring per source and no reference images are stored -
  and every detected band is shown as an actual image strip in the dialog, so
  what would be cut can be checked before anything is written. Only the pages
  you confirm are re-encoded; every other page is copied through byte-for-byte,
  and the result is always written to a new file. Also available as an optional
  "Trim Footers…" toolbar button.
- **These are the first features that write page images**, which the project's
  founding constraint previously ruled out entirely. The constraint is now
  narrower rather than gone: both only ever write a *new* archive and leave
  every source file untouched on disk. Combining additionally copies page bytes
  verbatim without decoding them; footer trimming is the one path that genuinely
  re-encodes, and only for the pages actually being cropped.

### 2.0.2 - 2026-08-31

- Fixed the editor field context menu: right-clicking selected text in any
  field only ever showed "Revert to Saved", with no way to Cut/Copy/Paste.
  Assigning `TextBox.ContextMenu` in Avalonia replaces its built-in
  cut/copy/paste menu entirely rather than adding to it - `AttachRevertContextMenu`
  now rebuilds those items by hand alongside Revert to Saved.

### 2.0.1 - 2026-08-23

- New optional auto-update: on startup, checks GitHub Releases and - if
  enabled - downloads, swaps, and relaunches the app on confirm. Both checking
  and installing are off by default, and installing always needs an explicit
  per-update confirmation regardless of the setting.
- Renamed the Avalonia project from `cbzLab.Avalonia/` to `cbzLab/`, archiving
  the WinUI original to `cbzLab.winui3/` - this is now the only actively
  developed version.
- CI: added Dependabot (NuGet + GitHub Actions), macOS (Intel + Apple Silicon)
  release builds alongside Windows/Linux, and a guard that fails a release if
  the pushed tag and the project version disagree.
- Fixed CI's restore/build split leaving self-contained RID packages
  unrestored - every CI run since the Avalonia rewrite began had actually been
  failing silently.
- First release actually cut end-to-end via the new tag-triggered release
  workflow, rather than a hand-run publish.

### 2.0.0 - 2026-08-22

Full rewrite onto Avalonia UI (C# / .NET 8), cross-platform from one codebase -
Windows, Linux, and macOS (Intel and Apple Silicon). Reaches complete feature
parity with the archived WinUI 3 version: opening/editing/saving, batch
editing, grid view, all four stages of ComicVine integration, composite
date/numeric fields, keyboard accelerators, Save As/Save All, and runtime JSON
theming - plus a batch of features the WinUI version never had:

- New autosave/crash recovery: dirty files are snapshotted every 30s and
  offered back on the next launch if the last session ended uncleanly.
- New Tools → Find & Replace, scoped to the current selection or all open
  files, with a dry-run match count before applying.
- New Tools → Validate All Open Files, running the existing validation across
  every open file at once instead of one at a time.
- New File → Reopen Last Closed File.
- New Settings → Export/Import (backs up everything under `%APPDATA%\cbzLab`,
  aside from logs, into one `.cbzlab` file and back) and Clear ComicVine
  Cache.
- New Help → Check for Updates against the real GitHub Releases API.
- New Size/Modified grid columns.
- New branded About dialog, exe icon, and window/taskbar icon.
- New real test suite (`cbzLab.Tests`, xUnit) covering the pure/injectable
  logic - `DateFieldHelper`, `ComicInfoXml`, `JsonFileStore`, `AutosaveService`.
- Fixed the grid's column-header right-click leaking the row context menu -
  the same bug the WinUI version hit and fixed, reintroduced by the port and
  caught again here.

## cbzLab.winui3 (archived WinUI 3 version)

> Windows-only, no longer developed - see
> [`cbzLab.winui3/ARCHIVED.md`](cbzLab.winui3/ARCHIVED.md). Kept for history.

### 0.1.48 - 2026-08-15

- Fixed grid view ctrl+click only extending selection when clicked in the leftmost
  (dirty-indicator) column.
- Wired up "Always review matches before applying" ComicVine setting - turning it
  off now applies every proposed field directly.
- Fixed the grid's right-click menu appearing over the column header row.

### 0.1.47 - 2026-08-05

- Fixed cover thumbnails sometimes showing the page after the real cover - now
  picked by natural-sorted filename instead of archive storage order. Same fix
  applies to "last page as cover".

### 0.1.46 - 2026-08-05

- Fixed dropdown fields (Black & White, Manga, Age Rating) offering no way to set
  a value in batch mode when none of the selected books already had it set.

### 0.1.45 - 2026-07-15

- Fixed the Series/Number file-list sort treating issue numbers as text instead
  of numbers.

### 0.1.44 - 2026-07-13

- Fixed a bug in the 0.1.41 date-picker fix: opening the picker on a file with a
  partial date silently wrote a placeholder Month/Day as a real edit.
- New JsonFileStore helper consolidating repeated json load/save/fallback logic
  (~90 lines removed).

### 0.1.43 - 2026-07-13

Code-quality sweep - no user-visible changes besides one status-message fix.

- Fixed a misplaced doc comment and recent-values losing its exact-match
  comparer after loading from disk.
- Consolidated duplicated ComicVine search logic and dialog row-selection logic.
- Simplified ComicFileViewModel's field-mutation methods.

### 0.1.42 - 2026-07-13

- Menu bar and menu items now render with a consistent text colour (previously
  duller than plain menu items in dark themes).

### 0.1.41 - 2026-07-13

- Fixed the date picker jumping to today instead of the field's existing date.
- Fixed the Theme and Open Recent menus showing in a duller colour.

### 0.1.40 - 2026-07-13

Composite date field and narrow numeric-field row-sharing.

- New RowCompanions/MonthCompanion/DayCompanion: fields that render inline on
  another field's row instead of getting a full row of their own - Number+Count+
  Volume, AlternateNumber+AlternateCount, Year+Month+Day.
- New DateDisplayValue: Year's composite, localized date display; DateFieldHelper
  parses full dates, bare years, or "MM/yyyy".
- New calendar-picker flyout alongside the text entry.
- Fixed: reverting Year's row now also reverts Month/Day.

### 0.1.39 - 2026-07-13

- Fixed grid-view double-click doing nothing (switched from DoubleTapped to
  timing-based detection on Tapped).
- Set SelectionMode="Extended" to fix ctrl+click not extending selection.

### 0.1.38 - 2026-07-13

Bulk editing grid, stage 2 of 2.

- New fixed dirty-indicator column, leftmost.
- Double-click opens that row in the editor regardless of other selection;
  right-click adapts to selection size.
- Selection now carries across the sidebar/grid toggle in both directions.

### 0.1.37 - 2026-07-13

- Fixed toolbar scroll buttons going inert after resizing the window.

### 0.1.36 - 2026-07-13

- Moved "Choose Columns…" from its own button row into the View menu and a grid
  right-click menu.
- Reordered the column picker into a "most likely wanted" order instead of raw
  schema order.
- Fixed the toolbar running off-screen at smaller widths - now scrolls instead.

### 0.1.35 - 2026-07-13

- Fixed grid view showing correct rows/columns but blank cells (a WPF-only
  binding idiom that doesn't carry over to WinUI).

### 0.1.34 - 2026-07-13

Bulk editing grid, stage 1 of 2 (display only).

- New dependency: WinUI.TableView (actively maintained; the Microsoft/
  CommunityToolkit DataGrid is abandoned).
- New toggle swaps the sidebar+editor layout for a full-width table.
- New Choose Columns dialog; columns built dynamically via a new
  FieldValueConverter.

### 0.1.33 - 2026-07-13

- Added Reset to Defaults to Settings, scoped to preferences only (not schema
  extras, recent values, or the ComicVine cache).

### 0.1.32 - 2026-07-12

ComicVine integration, stage 4 of 4 (batch).

- One series search shared across the whole selection, then per-file issue
  matching; clean single-number matches auto-accept.
- New aggregated review dialog: fields that agree across every matched file show
  once and default checked; fields that differ are flagged and left unticked.

### 0.1.31 - 2026-07-12

ComicVine integration, stage 3 of 4 (field mapping & apply).

- New mapping from a matched issue+volume onto ComicInfo.xml fields (dates,
  HTML-stripped summary, credits parsed by role).
- New review dialog shows only fields that actually differ from the file's
  current values, current vs proposed side by side.

### 0.1.30 - 2026-07-12

- Fixed ComicVine's single-issue detail lookup, which needs the id prefixed with
  its resource-type code - unlike list/filter endpoints, which use a bare id.

### 0.1.29 - 2026-07-12

- Fixed a parse failure on issue-detail lookups: ComicVine's "results" field is
  sometimes a bare object, sometimes a single-element array.

### 0.1.28 - 2026-07-12

ComicVine integration, stage 2 of 4 (search & match UI, single-file only).

- New series search dialog, pre-filled from the file's Series field or a
  filename guess.
- New issue-match dialog: a clean number match still asks for confirmation
  rather than applying silently.

### 0.1.27 - 2026-07-12

- Re-enabled PublishSingleFile - the 0.1.23 revert had blamed the wrong cause;
  the real fix was the WindowsAppSDK bump in 0.1.24.
- Stopped copying unused per-culture localization folders.

### 0.1.26 - 2026-07-12

- Fixed a NullReferenceException: SortCombo's xaml-default selection fired its
  handler before ViewModel was assigned. Guarded that and an identical case in
  the tab-selection handler.

### 0.1.25 - 2026-07-12

- Fixed a package-downgrade build error from the WindowsAppSDK 1.8 bump.

### 0.1.24 - 2026-07-12

- Bumped WindowsAppSDK 1.6 → 1.8, fixing a launch crash traced to a version gap
  against a very new Windows Insider build.

### 0.1.23 - 2026-07-12

- Fixed the 0.1.3 launch crash: PublishSingleFile's native-DLL self-extraction
  isn't a supported combination for WinUI 3. Reverted single-file publish (later
  found not to be the actual cause - see 0.1.27).

### 0.1.22 - 2026-07-12

- Fixed a build error: WPF's 2-argument Thickness shorthand doesn't exist in
  WinUI's Thickness.

### 0.1.21 - 2026-07-12

ComicVine integration, stage 1 of 4 (foundation - no UI yet).

- New settings: enable switch (off by default), API key, always-review toggle,
  with a Test Connection button.
- New ComicVineService (search, issues-for-volume, issue detail) and
  ComicVineCacheService (persists lookups, remembers series→volume).

### 0.1.20 - 2026-07-12

- Keyboard navigation: Ctrl+1..5 jump to a tab, Ctrl+Tab cycles, F6/Shift+F6
  move focus.
- Right-click a field for "Revert to Saved" (single-file only).
- New "Copy Fields to Rest of Selection" batch tool.
- Unsaved-file count badge on the Save All button.

### 0.1.19 - 2026-07-12

- Cover image source: first or last page.
- Editor fields can now fill the available width instead of a fixed 780px cap.
- Compact density option; editor font family setting; remember-last-tab toggle.

### 0.1.18 - 2026-07-12

- Sidebar sort mode, window size/position now persist across sessions.
- New toggles: auto-select first file on open, clear filter on open.
- Live-validation timing setting (as you type / on blur / off).
- Recently-typed-values cap now configurable.

### 0.1.17 - 2026-07-12

- Improved selected-row text contrast on three light themes to meet WCAG AA.

### 0.1.16 - 2026-07-12

- Added 7 new bundled themes (15 total): Gruvbox Dark/Light, Catppuccin Mocha/
  Latte, Tokyo Night, True Black, High Contrast.

### 0.1.15 - 2026-07-11

- Added recently-typed values for single-line fields (Writer, Publisher, etc.) -
  a per-tag history, most-recent-first, capped at 12.

### 0.1.14 - 2026-07-11

- Added sidebar sort and filter, separate from the field search box.

### 0.1.13 - 2026-07-11

- Fixed Guess from Filename leaving generic leftover words ("issue", "chapter",
  etc.) as the Series instead of falling back to the folder name.

### 0.1.12 - 2026-07-11

- Added Guess from Filename: parses Series/Number/Volume/Year from the filename,
  falling back to the folder name for Series.

### 0.1.11 - 2026-07-11

- Corrected 0.1.10: the header banner's cover thumbnail now scales both
  dimensions, keeping its aspect ratio.

### 0.1.10 - 2026-07-11

- Doubled the height of the editor header banner's cover thumbnail.

### 0.1.9 - 2026-07-09

- Fixed a build error from a missing using directive.

### 0.1.8 - 2026-07-09

- Added cover previews: sidebar thumbnails and an editor header banner
  (single-file selection only).

### 0.1.7 - 2026-07-09

- Added drag-and-drop opening from Explorer.
- Added live as-you-type validation for numeric fields.

### 0.1.6 - 2026-07-07

- File list: Ctrl+A selects all, Delete removes selection, right-click context
  menu.
- Added a GitHub Actions build workflow.
- Added structured logging to `%appdata%\cbzLab\logs`.

### 0.1.5 - 2026-07-07

- Fixed files being marked modified on open whenever auto page count filled an
  empty field.

### 0.1.4 - 2026-07-07

- Batch editing: entry/text fields gained the same detected-values picker combo
  fields already had.

### 0.1.3 - 2026-07-07

- Enabled single-file publish (later reverted in 0.1.23, re-enabled in 0.1.27).

### 0.1.2 - 2026-07-07

- Adapted to a SharpCompress API rename.

### 0.1.1 - 2026-07-07

- Bumped SharpCompress to clear a directory-traversal advisory; hardened archive
  extraction independently of the library fix.

### 0.1.0 - 2026-07-07

Initial WinUI 3 release, rebuilt from the Python/tkinter ComicInfoLab.

- CBZ/CBR opening, full ComicInfo v2.0 field set from an editable schema,
  five-tab editor, batch editing.
- Save/Save As/Save All with validation and atomic writes; CBR writing via an
  external tool.
- Runtime JSON theming, recent files, command-line opening, Settings dialog.
