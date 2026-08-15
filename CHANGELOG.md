# Changelog

## 0.1.48 — 2026-08-15

- Fixed grid view ctrl+click only extending row selection when clicked in the
  leftmost (dirty-indicator) column. WinUI.TableView's default `SelectionUnit`
  is `CellOrRow`, which treats every data cell as independently selectable, so
  ctrl+click on a data cell toggled just that cell rather than the row. Set
  `SelectionUnit="Row"` on the grid — it's read-only, so there's no per-cell
  selection/copy use case that would want cell-level selection anyway.
  User-confirmed fixed.
- Wired up the "Always review matches before applying" ComicVine setting,
  which previously did nothing — both the single-file and batch ComicVine
  flows always showed the before/after review dialog regardless. Turning it
  off now applies every proposed field straight through, same as if every row
  had been left checked in the review dialog.
- Fixed the grid view's right-click context menu (Edit / Choose Columns)
  appearing over the column header row, where it doesn't apply. Right-clicks
  on a header cell now defer to the header's own sort/filter options instead.

## 0.1.47 — 2026-08-05

- Fixed cover thumbnails sometimes showing the page after the real cover.
  The cover was being picked as whichever image entry the archive happened
  to list first internally — its storage order, not page order — which
  doesn't always match the filenames. Now picks by filename, natural-sorted
  (handles zero-padding and 0- vs 1-indexed page numbering correctly, so
  "000"/"00"/"0" and "001"/"01"/"1" both resolve to the true lowest page).
  Same fix applies to "last page as cover" mode. As a side effect, "first
  page as cover" (the default) now costs a second archive pass like "last"
  already did, since the correct cover can't be known until every page
  filename has been seen — a deliberate correctness-over-speed trade.

## 0.1.46 — 2026-08-05

- Fixed dropdown fields (Black & White, Manga, Age Rating) offering no way to
  set a value when editing multiple books at once. Batch mode replaces the
  dropdown with a picker listing values found across the selection, so when
  none of the selected books had the field set, the only entry was "(blank)"
  and the value couldn't be changed at all. The picker now always offers the
  field's full list of valid choices alongside whatever was detected.

## 0.1.45 — 2026-07-15

- Fixed the Series/Number file-list sort treating issue numbers as text
  instead of numbers — 1, 10, 11, 12 ... 2, 20 is now 1, 1.5, 2 ... 10, 11,
  12 ... 20. Non-numeric issue numbers (annuals, specials) still sort
  consistently, just after every numbered issue rather than interleaved
  alphabetically among them.

## 0.1.44 — 2026-07-13

- Fixed a bug in the 0.1.41 date-picker fix: opening the picker on a file
  with a partial date (Year set but no Month/Day) would silently write the
  seeded placeholder Month=1/Day=1 into the file as real edits, marking it
  modified without any user action. The calendar seeding no longer feeds
  back through the pick handler.
- New JsonFileStore helper consolidating the identical json load/save/
  fallback/log pattern previously repeated across SettingsService,
  RecentValuesService, ComicVineCacheService and SchemaService — one shared
  implementation, four call sites, ~90 lines removed. schema.json's own load
  deliberately stays fatal-on-failure (an app with no schema has no fields
  to edit) and is documented as such.
- ComicVine's Test Connection now builds its request through the same url
  helper as every other request, instead of hand-assembling its own.
- Removed a redundant loop-variable capture in the recent-files menu.

## 0.1.43 — 2026-07-13

Code-quality sweep — no user-visible feature changes, aside from one minor
status-message consistency fix noted below.

- Fixed a misplaced doc comment in the grid-columns picker that had ended up
  documenting the wrong thing.
- Fixed the recent-values history losing its exact-match comparison after
  being loaded from disk.
- Consolidated the single-file and batch ComicVine search flows, which had
  duplicated their volume/issue lookup logic — now shared. As a small side
  effect, cancelling a batch ComicVine search now sets the same status
  message the single-file flow already used, where before it set none.
- Consolidated the duplicated row-selection/highlighting logic in the
  ComicVine series-search and issue-browse dialogs into one shared helper.
- Simplified ComicFileViewModel's field-mutation methods (SetValue,
  RevertField, SeedValue), which all repeated the same dirty-check/subtitle-
  refresh tail.

## 0.1.42 — 2026-07-13

- Menu bar and menu items now render with a consistent text colour
  throughout. Previously, plain menu items (Open, Save, etc) were themed
  brighter than menu bar headers, submenus, and toggle items, which don't
  honour the app's custom colours in the same way — most visible in dark
  themes. All menu text now uses the same system-default tone.

## 0.1.41 — 2026-07-13

- Fixed the date picker jumping to today instead of showing the date already
  set on the field.
- Fixed the Theme and Open Recent menus (and the theme list itself) showing
  in a duller colour than the rest of the menus.

## 0.1.40 — 2026-07-13

Composite date field and narrow numeric-field row-sharing — the editor's
biggest layout change since the original 5-tab structure.

- Corrected a wrong assumption made while planning this: schema.json's JSON
  section names ("Publication Date", "Publication Details") aren't real
  editor tabs — SchemaService.TabMap already merges both into one runtime
  "Publication" tab. There was never a tab-folding decision to make; Year/
  Month/Day already sat alongside Publisher/Genre/PageCount/etc. Worth
  knowing since I'd asked about folding tabs based on that wrong premise
  before checking the real code.
- New FieldViewModel.RowCompanions/MonthCompanion/DayCompanion: other
  fields that render inline on a field's own row instead of getting a full
  row each. A companion is excluded from the normal rendered list entirely
  (RebuildVisibleFields) and is edited only through its own Value — still
  going through the exact same Edited/validation/revert pipeline as if
  rendered normally. This only changes layout, never how an edit is
  applied — chosen specifically to avoid touching OnFieldEdited,
  ValidateLive, or the XML-writing path at all.
- New FieldViewModel.DateDisplayValue: Year's own composite localized date.
  Get composes from Year+Month+Day using the current culture's own
  DateTime formatting (dd/MM/yyyy on an AU-configured machine, MM/dd/yyyy on
  a US one, etc. — reads the OS's actual regional setting, not hardcoded to
  any locale); set parses full dates, bare years, or "MM/yyyy" via new
  DateFieldHelper, and assigns Year (itself) plus both companions through
  their normal setters. Deliberately does not attempt fully generic
  arbitrary-culture partial-date ordering (e.g. working out whether a bare
  two-part input means month/year or year/month from the current culture's
  full-date pattern) — real complexity for a case (year+month, no day) far
  rarer in practice than a full date or year-only.
- New DateFieldTemplate: the composite text entry plus a calendar-icon
  button opening a CalendarView flyout for convenience. Picking a date
  assigns Year/Month/Day directly from the picked date's own components, no
  string round-trip through the parser. The text field stays freely
  typable — ComicInfo allows partial dates, which a calendar-only control
  can't represent, so the picker is a convenience layered on top, not a
  replacement.
- Fixed a real correctness gap caught while building this: reverting Year's
  composite row now also reverts Month/Day. Without this, "Revert to
  Saved" would have reverted only Year while Month/Day kept whatever
  unsaved values they had — a half-reverted date.
- New NumericGroupTemplate: Number+Count+Volume share one row, as do
  AlternateNumber+AlternateCount — each still fully independent (own label,
  own narrow textbox, own Revert to Saved), just laid out side by side
  since all are genuinely short values. PageCount and CommunityRating stay
  on their own rows per instruction, rather than force a pairing with a
  non-numeric field just to save space.
- Simplification made along the way: both new templates dropped the
  recent-value/batch picker dropdown every other entry field has. For the
  date field this is arguably more correct anyway — a picker of bare
  recorded "Year" values wouldn't match editing a full composite date
  string. For the numeric group fields it's more of a genuine scope cut,
  given how much this feature already grew from what was originally
  discussed.
- Year's label changed to "Publication Date" to reflect its new role;
  Month/Day's own schema entries are untouched (still real fields, just
  never rendered separately now).

## 0.1.39 — 2026-07-13

Grid view interaction fixes, following real usage feedback.

- Fixed double-click doing nothing. Replaced DoubleTapped (which wasn't
  firing reliably — most likely swallowed internally by the grid's own
  row/selection input handling before it could bubble up to the control's
  own event) with manual timing-based detection on Tapped: two taps on the
  same file within 500ms counts as a double-click. Tapped is the gesture
  selection itself already depends on and was already confirmed working, so
  building on it rather than the less-certain DoubleTapped.
- Explicitly set SelectionMode="Extended" on the grid, as a moderate-
  confidence attempt at two reported issues that may share a root cause:
  Ctrl+click not extending the selection (despite Ctrl+A and the right-
  click multi-select path both already working), and multi-selected rows
  having no visible/obvious indicator. Confirmed via ThemeService that the
  app's own selected-row colors (list_sel/list_sel_fg) are already mapped
  onto the standard, application-wide ListViewItemBackgroundSelected/
  ListViewItemForegroundSelected resource keys — since TableView is
  ListView-derived it should already be inheriting these with no extra code
  needed, so if the visual indicator is still too subtle after this change,
  that points at TableView using its own distinct selected-row template/
  resource keys rather than a missing style override on this app's side,
  which would need further diagnosis rather than another guessed property.
- Investigated (not yet fixed) the Open Recent menu appearing darkened even
  when populated: BuildRecentMenu's item-population logic is identical in
  shape to BuildThemeMenu's (which is never empty and wasn't reported as
  looking the same way), and nothing sets IsEnabled=false on the RecentMenu
  MenuFlyoutSubItem itself anywhere in the code. No confirmed bug found on
  inspection — needs a visual comparison against the Theme submenu to tell
  whether this is an actual functional issue or just how a populated
  MenuFlyoutSubItem normally renders.

## 0.1.38 — 2026-07-13

Bulk editing grid, Stage 2 of 2 — closes out the grid view feature.

- New dirty-indicator column, leftmost, fixed (not part of the column
  picker). Declared statically in xaml rather than built in code like the
  dynamic columns, since its DataTemplate never changes — a colored dot
  (ThListDirty, same convention as the sidebar's own dirty indicator) shown
  only when IsDirty is true. RebuildGridColumns now only clears/rebuilds
  columns after index 0, so Choose Columns can never wipe it out.
- Double-click always acts on just the row under the cursor, regardless of
  whatever else is currently selected — a distinct, unambiguous action from
  right-click. Switches back to the normal sidebar+editor view with that
  one file selected.
- Right-click adapts to selection size, same convention the sidebar's own
  context menu already uses: "Edit This Book" for one row, "Edit N Books in
  Batch Editor" for several. Right-clicking outside the current selection
  replaces it first, same as the sidebar. Both double-click and the context
  menu's Edit item route through one shared SwitchToEditorForSelection.
- Selection now carries across the toggle in both directions. Entering grid
  view (via the toolbar button or View menu) seeds ComicsGrid's selection
  from the sidebar's current one; leaving it does the reverse. Double-click/
  right-click routing sets IsGridViewActive directly rather than through the
  toggle's own Click handler, so it handles its own more specific selection
  instead of going through that sync path.
- Residual uncertainty carried over from Stage 1, worth restating: none of
  WinUI.TableView's selection API (SelectedItems, SelectedItem,
  RightTapped/DoubleTapped bubbling with DataContext intact) has been
  compile-verified from this environment. It's built against the same
  patterns already proven working for the sidebar's own ListView-based
  FileList, on the strength of TableView being explicitly ListView-derived
  — reasonable confidence, not certainty.

## 0.1.37 — 2026-07-13

- Fixed toolbar scroll buttons working when the window launched small but
  going inert if the window was shrunk after launch. Root cause: the
  ScrollViewer's ScrollableWidth/HorizontalOffset weren't reliably
  re-evaluating after a live resize the way they do during initial layout —
  a known category of WinUI layout staleness, and (unlike the grid-view
  binding issue) core framework behaviour rather than the less-certain
  third-party WinUI.TableView surface, so higher confidence in this fix.
  Two changes: the toolbar's outer Grid now calls
  ToolbarScroll.InvalidateMeasure() on every SizeChanged, and both scroll
  button handlers call ToolbarScroll.UpdateLayout() before reading
  HorizontalOffset/ScrollableWidth, so a click can never act on numbers
  left over from before the last resize. OnToolbarScrollRight also now
  explicitly clamps to ScrollableWidth rather than relying on ChangeView's
  own clamping alone.

## 0.1.36 — 2026-07-13

Four refinements following real usage of the grid view (now confirmed
populating with correct values).

- Removed the standalone "Choose Columns…" button row above the grid — it
  was the only thing on that row and cost a full row of vertical space for
  one button. Moved to two places instead: a new "Choose Grid Columns…"
  item in the View menu, and a right-click context menu on the grid itself.
  The context menu is scoped to the whole TableView, not specifically the
  column-header row — WinUI.TableView doesn't expose a confirmed way to
  distinguish a header-specific right-click from this environment, so this
  is a deliberate, disclosed simplification rather than the more targeted
  version originally suggested.
- Reordered the column picker's field list. It was following schema.json's
  own section order, which clusters niche fields (Count, AlternateSeries,
  AlternateNumber, AlternateCount) right next to Series/Number purely
  because they sit early in the Basic Info section — this is exactly what
  put "Count" (total issues in series) near the top while "Page Count"
  ended up near the bottom, despite Page Count being the far more commonly
  wanted column. New GridColumnPriorityOrder in AppDialogs.cs: a curated
  "most likely wanted in a library-wide table" ordering — Series/Number/
  Title first, then creators, then publication facts, then story/narrative
  fields, then the genuinely niche ones last. This is a judgment call about
  what people commonly want, not an authoritative ranking. Any schema field
  not explicitly listed (a future addition) falls to the end in its
  original schema order rather than silently vanishing from the picker.
- Fixed the main toolbar running off-screen at smaller window widths. Wrapped
  it in a ScrollViewer flanked by left/right chevron buttons (fixed 200px
  step per click, clamped at zero on the left) — deliberately not a
  wrap-to-second-row layout, which would have cost double the vertical
  height permanently rather than only needing horizontal scrolling when the
  window is actually narrow. The arrows are always shown rather than
  hidden-when-not-needed, kept simple for now — clicking them when there's
  nothing to scroll is a harmless no-op, not worth the extra complexity of
  tracking overflow state reactively for this pass.

## 0.1.35 — 2026-07-13

- Fixed grid view showing correct row count and column headers but every
  cell blank. RebuildGridColumns set Binding.Path = new PropertyPath(".")
  intending "bind to the whole row object" — a WPF idiom not reliably
  equivalent in WinUI's binding engine, same category of gap as the
  Thickness 2-argument constructor earlier this session (a WPF convenience
  that doesn't carry over). Fix: don't set Path at all — an unqualified
  binding with no path is the standard, well-tested way to bind to the
  whole source object across the WPF/UWP/WinUI family, and I'm more
  confident in it than the explicit dot-path attempt.
- Since this still couldn't be compile-tested from this environment,
  FieldValueConverter now logs a warning (with the actual runtime type it
  received) whenever the bound value isn't a ComicFileViewModel, instead of
  silently returning blank. If this fix doesn't fully resolve it, the next
  log capture settles it definitively rather than needing a third blind
  guess.
- Confirmed not bugs, just different things than what was reported: right-
  click/double-click doing nothing on grid rows is correct, expected Stage
  1 behaviour (routing them into the editor is Stage 2's job). The corner
  ellipsis menu (Select All etc.) is WinUI.TableView's own built-in
  "corner button" feature, never configured either way, showing its
  default. Choose Columns was never intended as a column-header right-click
  menu — it's a plain button placed directly above the grid, not a header
  context menu, and should already be visible there if rows and headers are
  rendering.

## 0.1.34 — 2026-07-13

Bulk editing grid, Stage 1 of 2 (the grid exists — display only, no
double-click/right-click routing or dirty column yet, that's Stage 2).

- Dependency research before writing anything: the "official"
  Microsoft/CommunityToolkit DataGrid has been abandoned since 7.1.2 in
  November 2021, and current Microsoft docs say so directly, recommending
  WinUI.TableView by name instead. Went with that recommendation —
  actively maintained (latest release 18 days old at time of writing),
  purpose-built for WinUI 3, derived from ListView (fluent styling and
  virtualization for free), IsReadOnly at both grid and column level out of
  the box. New dependency: WinUI.TableView 1.4.1. Flagging honestly: unlike
  the ComicVine work, there was no way to verify this library's exact API
  surface against a real compile from this environment — property names
  (Columns, IsReadOnly, AutoGenerateColumns, TableViewTextColumn.Header/
  Binding) are taken from the library's own README examples and feature
  list, not confirmed by building. More likely than usual for this specific
  change to need a follow-up compile-error fix.
- New toggle in the toolbar (its own group, end of the bar) swaps the whole
  sidebar+editor layout for a full-width table — both occupy the same
  RootGrid row with opposite Visibility bindings on MainViewModel's new
  IsGridViewActive, persisted like every other UI preference.
- New Choose Columns dialog (AppDialogs.ChooseGridColumnsAsync): every
  schema field offered as a checkbox, schema order preserved on the
  returned list regardless of click order so column order stays
  predictable. Persisted as AppSettings.GridColumns, defaulting to a
  starter set (Series, Number, Writer, Publisher) rather than an empty grid
  on first use.
- Columns are built entirely in code (MainWindow.RebuildGridColumns), not
  static XAML, since which fields become columns is a runtime choice. Each
  column shares one binding shape: bind the whole row via PropertyPath("."),
  resolve its displayed value through a new FieldValueConverter keyed by
  that column's own tag as ConverterParameter. This is what lets columns be
  entirely dynamic without needing a hardcoded property on
  ComicFileViewModel for every one of the schema's ~39 fields.
- Grid is backed by the same MainViewModel.DisplayedFiles as the sidebar,
  so it already respects the current sort mode and file filter with no
  extra wiring. Column-header click-to-sort uses the library's own default
  sorting behaviour for now, unverified against the converter-based
  binding — if it turns out to sort by the wrong underlying value, that's a
  low-risk, easily-reproduced follow-up fix, not a data-safety concern
  (grid is read-only), so it wasn't worth pre-emptively building a custom
  sort mechanism for a risk that isn't confirmed real.

## 0.1.33 — 2026-07-13

- Added Reset to Defaults to the Settings dialog (Secondary button, next to
  Save/Cancel), confirmed before acting since it includes wiping the saved
  ComicVine API key. New SettingsService.ResetToDefaults, scoped to
  cbzLab_settings.json only — schema_extra.json (auto-discovered unofficial
  fields), recent_values.json (typed-value history), and
  comicvine_cache.json (cached lookups) are separate accumulated data, not
  "settings" in the sense this resets, and stay untouched. Recent Files is a
  known, accepted side effect of the reset (it lives inside the same
  preferences file), not an oversight.
- The existing post-Settings live-update logic in MainWindow (theme
  re-apply, font/width/density push, menu rebuilds) needed no changes at
  all for this to work correctly — it already just re-reads
  App.Settings.Settings after the dialog closes, and ResetToDefaults leaves
  that object correctly pointing at the fresh defaults either way.

## 0.1.32 — 2026-07-12

ComicVine integration, Stage 4 (Batch). Closes out the four-stage plan.

- Researched Genre availability before implementing anything: checked
  ComicVine's actual field lists for both the issue and volume resources
  against several independent real sources — genre does not appear in any
  of them. Ruling it out on evidence, not silence; this is apparently a
  known, deliberate ComicVine limitation, not a gap in the research.
- Found one genuinely available field that hadn't been wired in: Count
  (ComicInfo's total-issues-in-series field) maps directly to volume
  count_of_issues — data already fetched and stored as
  ComicVineVolume.IssueCount during search, just never connected to
  MapToComicInfoFields until now. Everything else checked (Imprint,
  LanguageISO, Format, AgeRating, CommunityRating, BlackAndWhite, Manga,
  Volume, AlternateSeries/Number/Count, SeriesGroup, ScanInformation,
  MainCharacterOrTeam, Notes, Review) has no clean, confirmed ComicVine
  source and stays out of the mapping.
- Batch flow: one series search shared across the whole selection (seeded
  from the first selected file), then per-file issue matching against the
  shared issue list. Clean single-number matches auto-accept without a
  per-file popup — confirming every file one at a time across a whole run
  would be its own kind of tedious, and the aggregated review dialog at the
  end is the real checkpoint. Ambiguous matches still get a picker, now
  labelled with which file it's for. Files that get cancelled during
  matching are skipped, not blocking, and reported by name at the end for
  re-running individually.
- New AppDialogs.ReviewComicVineBatchAsync: one field per row across every
  matched file at once. Fields where every file's matched issue agrees
  (Series, Publisher, Count, the creator fields, Characters/Teams/
  Locations/StoryArc) show as a single line, checked by default. Fields
  that genuinely differ across files are flagged and left unticked, with
  every distinct value and how many files carry it — could be a real
  mid-run creative change, could be a mismatch, and it's exactly the
  situation worth a conscious decision rather than a silent default.
  Per-issue fields (Number, Title, dates, Summary, Web) aren't held to the
  agreement check at all — of course a summary differs issue to issue.
  Regardless of a field's shared/divergent status, every file always gets
  its own individually-matched value when applied; the distinction only
  changes what's ticked by default and whether a warning shows, never which
  value gets written to which file — no blanket "same value to everyone"
  path exists in this feature at all.
- ComicVineAlwaysReview (the Stage 1 setting) remains unused by both the
  single-file and batch flows — known gap, to be revisited separately.

## 0.1.31 — 2026-07-12

ComicVine integration, Stage 3 of 4 (Field Mapping & Apply). Closes the loop
opened in Stage 2 — matched ComicVine data now actually reaches the file's
fields, with a before/after review step in between.

- New ComicVineService.MapToComicInfoFields: pure data transformation from a
  matched issue + its volume onto ComicInfo.xml tag names, verified directly
  against the bundled schema.json rather than assumed from memory. Notably
  Series (volume name) and Title (issue name, e.g. "The Cat and the Bat")
  are correctly kept as the two distinct real fields they are, not
  conflated. Year/Month/Day parsed from cover_date (yyyy-MM-dd, confirmed
  against a real response, with a general-parse fallback). Summary run
  through a new StripHtml helper (block-level tags become newlines before
  everything else is stripped; entities decoded via the framework's own
  WebUtility.HtmlDecode rather than a hand-rolled list). Writer/Penciller/
  Inker/Colorist/Letterer/CoverArtist/Editor parsed from person_credits
  using case-insensitive keyword-contains matching rather than exact role
  strings — ComicVine's precise role vocabulary/spelling isn't independently
  verified, and after two wrong guesses already made about this API's exact
  shapes, a loose match that's merely imprecise beats an exact match that's
  silently wrong. One person can land in multiple fields at once (e.g.
  "penciler, inks").
- New AppDialogs.ReviewComicVineMatchAsync: shows only the fields where
  ComicVine's proposed value actually differs from what's already in the
  file — nothing to decide if they already match — with Current and New
  values both shown per field, not just the proposed value alone. All
  default to checked: unlike CopyFieldsAsync's cautious per-field defaults
  (which exist because that dialog shows no comparison), here the user is
  already looking directly at both values before deciding, so a blanket
  cautious default doesn't pull its weight the same way.
- OnSearchComicVine now actually applies checked fields via the same
  ComicFileViewModel.SetValue every other feature already uses — real
  edits, dirty-tracked, undoable via Revert, nothing new invented for how
  writes happen.
- Batch ComicVine lookup remains out of scope — Stage 4.

## 0.1.30 — 2026-07-12

- Fixed the "Error in URL format" ComicVine returned after the 0.1.29 parse
  fix — that fix worked (no more parse crash), and correctly let through
  ComicVine's own error response for the first time, revealing the real
  problem underneath: GetIssueDetailAsync built the single-issue URL with a
  bare numeric id (.../issue/684877/), but ComicVine's single-resource
  detail endpoints require the id prefixed with a resource-type code
  (.../issue/4000-684877/ — 4000 specifically for issues). Confirmed against
  several independent real examples before fixing, not a third guess: the
  list endpoint's own api_detail_url field always includes the 4000- prefix,
  and the same pattern shows up in working third-party ComicVine API
  examples. GetIssuesForVolumeAsync's filter=volume:{id} correctly uses the
  bare id and was never affected — filter parameters and single-resource
  detail paths follow different id conventions, which is why search and
  issue-matching already worked while this one endpoint didn't.

## 0.1.29 — 2026-07-12

- Fixed the real, live-key-confirmed parse failure on GetIssueDetailAsync
  ("The JSON value could not be converted to CvIssueDetailRaw. Path:
  $.results"). This was the one endpoint I couldn't verify against an actual
  API response during Stage 1 — the search/list endpoints were confirmed
  against a real example, this one wasn't, and it turned out its actual
  response shape for "results" doesn't match what's documented. Rather than
  guess at the exact alternate shape and risk a second wrong fix, added
  SingleOrArrayConverter<T>, a small property-scoped JsonConverter that
  tolerates "results" being either a bare object or a single-element array
  wrapping one (the shape ComicVine's own list endpoints already use).
  Applied only to CvIssueDetailResponse.Results, not globally.
- Also added raw-body-on-parse-failure logging (truncated to 2000 chars) to
  GetAsync<T>'s existing JsonException handler, so any future shape surprise
  is diagnosable from one log capture instead of another guess-and-check
  round trip. Purely additive — no behaviour change for successful requests.

## 0.1.28 — 2026-07-12

ComicVine integration, Stage 2 of 4 (Search & Match UI). Single-file only;
nothing writes to any file yet — that's Stage 3.

- New MainViewModel.OnlineLookupEnabled, the master-toggle-driven property
  everything else binds to. Tools menu item, its separator, the toolbar
  button, and its separator are all Visibility-bound to it — with the
  toggle off, none of it renders, not just disabled. Set from
  ComicVineEnabled at construction and pushed live after Settings closes,
  same pattern as every other live-updated setting.
- New AppDialogs.SearchComicVineAsync: series search with a pre-filled query
  (the file's own Series field, falling back to a FilenameGuessService
  guess — direct reuse of the earlier filename-guessing work), results
  shown as manually-built rows (cover thumbnail via a plain BitmapImage
  pointed at ComicVine's own thumb URL, name, publisher, start year, issue
  count) rather than a DataTemplate+ItemsSource, matching how every other
  dialog in this codebase is already built.
- New AppDialogs.MatchIssueAsync: if the file's Number field cleanly matches
  exactly one issue, shows it for confirmation rather than applying it
  silently — issue-number matching is the least reliable part of this whole
  flow (variant covers, facsimile editions and reprints routinely share a
  number), so even a "confident" match gets a human glance. Anything else
  (zero or several matches) goes straight to a browsable issue list.
  Normalization for comparing numbers (leading zeros etc.) reuses the same
  convention already established in FilenameGuessService and
  RecentValuesService.
- New OnSearchComicVine orchestration in MainWindow: checks the feature is
  configured, resolves a starting query, checks ComicVineCacheService's
  series->volume memory before prompting a fresh search, fetches the issue
  list, runs the match/pick flow, then fetches full issue detail and shows
  a plain confirmation of what was found. Every ComicVineException is
  caught and shown as a specific message rather than a generic error.
- Batch ComicVine lookup (one file at a time, not "same value to everyone")
  is intentionally out of scope here, same reasoning Guess from Filename
  went through before it grew to cover a whole selection — that's Stage 4.

## 0.1.27 — 2026-07-12

- Re-enabled PublishSingleFile + IncludeNativeLibrariesForSelfExtract,
  reverting the 0.1.23 revert. The original diagnosis blaming
  PublishSingleFile for the launch crash was wrong: 0.1.23 kept the crash
  identical (same exception code, same fault offset) with PublishSingleFile
  already off, proving it was never the actual cause — the real fix was the
  WindowsAppSDK 1.6 -> 1.8 bump in 0.1.24/0.1.25. Current Microsoft docs
  confirm PublishSingleFile is officially supported for unpackaged,
  self-contained WinUI 3 apps as of WindowsAppSDK 1.5+ (cbzLab is both, and
  now exceeds that). The native WinAppSDK files still extract to a temp
  folder at runtime regardless of this setting — it only collapses the
  managed dependencies into cbzLab.exe.
- Added SatelliteResourceLanguages=en to stop copying the dozens of
  per-culture localization folders .NET and WindowsAppSDK ship by default —
  this app has no localized UI, so none of them do anything. Addresses the
  "hundreds of files and folders" complaint about the publish output
  directly, separately from the single-file question above.
- README's publish section corrected to match both changes and to stop
  repeating the disproven single-file claim.

## 0.1.26 — 2026-07-12

- Fixed a real, ordinary NullReferenceException (not framework/native — the
  WinAppSDK bump in 0.1.24/0.1.25 fixed the actual launch crash, and this is
  the first genuinely new problem to surface once the app could reach managed
  code at all). SortCombo has SelectedIndex="0" set directly in xaml, which
  fires SortCombo_SelectionChanged synchronously during InitializeComponent()
  — before the MainWindow constructor has reached the line that assigns
  ViewModel. The handler dereferenced ViewModel unconditionally and crashed.
  My own comment on the later, explicit SortCombo.SelectedIndex assignment
  claimed the re-fire was "harmless since OpenFiles is still empty" — wrong
  diagnosis; the actual risk was ViewModel not existing yet, not OpenFiles
  being empty, and that comment has been corrected.
- EditorTabs_SelectionChanged had the identical unguarded pattern —
  TabView commonly auto-selects its first tab by default, which plausibly
  triggers the same early-fire even without an explicit SelectedIndex
  attribute in xaml. Fixed defensively alongside the confirmed SortCombo bug
  rather than waiting for a separate crash report to prove it.
- Both handlers now guard with `if (ViewModel is null) return;`. Swept every
  other XAML-wired event handler in MainWindow.xaml.cs for the same pattern:
  everything else is either a Click handler (no "initial state" to
  auto-fire), a drag/key-accelerator/right-tap handler (only fires on actual
  user gesture), or bound to a data-driven ItemsSource with zero items until
  DataContext is set, which happens after ViewModel is already assigned —
  none of those can fire before ViewModel exists, so no further changes
  needed.

## 0.1.25 — 2026-07-12

- Fixed NU1605 package downgrade error (build-breaking here, since the
  project treats NU1605 as an error rather than a warning): WindowsAppSDK
  1.8.260529003 (bumped in 0.1.24) transitively requires
  Microsoft.Windows.SDK.BuildTools >= 10.0.26100.4654, but the project's own
  explicit pin was still 10.0.26100.1742. Bumped to 10.0.26100.4654 to match.

## 0.1.24 — 2026-07-12

- Bumped Microsoft.WindowsAppSDK 1.6.250108002 -> 1.8.260529003, chasing the
  launch crash from 0.1.23 (0xc000027b / STATUS_STOWED_EXCEPTION faulting in
  Microsoft.UI.Xaml.dll, combase.dll E_POINTER on the WER report, identical
  under both the published exe and F5/Debug). Root cause identified as a
  version gap, not a build-output or code problem: the machine reported
  Windows 11 Dev Channel build 29617.100, an Insider build almost certainly
  many builds ahead of anything the January 2025 WindowsAppSDK 1.6 release
  was ever tested against. 1.8.260529003 is the latest same-major-line
  servicing release (as opposed to the current 2.2.0 stable, which crossed a
  deliberate API break at 2.0) — chosen specifically to bring in newer native
  runtime/OS-compatibility fixes without also taking on 2.x's larger, riskier
  surface. Not guaranteed to fully resolve it: Windows Insider Dev Channel
  builds are pre-release by design, and some incompatibilities on that
  channel only get fixed by the next OS build landing, not by anything on
  the app side. Escalating to the 2.x line is the next lever if this doesn't
  clear it.

## 0.1.23 — 2026-07-12

- Fixed a hard crash on launch of the published exe: Exception code
  0xc000027b (STATUS_STOWED_EXCEPTION) faulting in Microsoft.UI.Xaml.dll,
  loaded from a %TEMP%\.net\cbzLab\... self-extraction path rather than the
  app's own folder. Root cause: PublishSingleFile + 
  IncludeNativeLibrariesForSelfExtract (added in 0.1.3 to shrink the publish
  output to just cbzLab.exe) extracts native DLLs into a randomly-named temp
  folder at runtime and loads them from there. WinUI 3's native XAML runtime
  depends on activation context and resource lookups (resources.pri,
  manifest-based type activation) that assume the exe and its native DLLs
  sit together in a stable, predictable folder — not a different temp path
  on every launch. When that assumption breaks, native init throws a stowed
  exception before any window can appear. This isn't a supported combination
  for WinUI 3 in practice, single-file publish or not.
- Reverted PublishSingleFile and IncludeNativeLibrariesForSelfExtract in
  cbzLab.csproj. SelfContained stays on (unrelated to the crash — just means
  the .NET runtime ships with the app). Publish output goes back to the
  standard multi-file shape: exe + managed and native dependencies + Assets,
  all in one folder together. README's publish section corrected to match,
  with the single-file attempt and revert noted so the history isn't lost.

## 0.1.22 — 2026-07-12

- Fixed CS7036: MainViewModel.FileRowMargin used WPF's 2-argument Thickness
  shorthand (horizontal, vertical) — Microsoft.UI.Xaml.Thickness has no such
  constructor, only the full 4-argument (left, top, right, bottom) form.
  Swept the rest of the codebase for the same mistake; every other Thickness
  call already used the 4-argument form correctly, so this was isolated to
  the one line introduced with density support back in 0.1.19.

## 0.1.21 — 2026-07-12

ComicVine integration, Stage 1 of 4 (Foundation). No search/match/tag UI yet
— this stage is the backend, fully testable in isolation, plus the Settings
controls needed to configure and verify it.

- Three new settings: ComicVineEnabled (master switch, off by default),
  ComicVineApiKey, ComicVineAlwaysReview (default on). The master switch is
  designed so the whole feature is genuinely invisible when off, not just
  disabled — Stage 2's menu items, toolbar button, and context-menu entries
  will all be Visibility-bound to it, matching how nothing about this exists
  in the running UI until explicitly turned on.
- New Settings section ("Online Lookup"): the master checkbox live-reveals
  the API key field, a "get a free key" link, a Test Connection button, and
  the always-review toggle — collapsed until checked, both on dialog open and
  live as you tick/untick it. Test Connection calls a new
  ComicVineService.TestApiKeyAsync that bypasses the cache and the live
  settings object entirely (tests the key you just typed, not necessarily
  the one currently saved) specifically so testing can never mutate live
  settings before Save is actually pressed — same discipline the rest of the
  Settings dialog already follows.
- New ComicVineService: search (by series name), list-issues-for-a-volume
  (paginated — ComicVine caps pages at 100, long runs like Batman need
  several; capped at 500 issues total as a sanity ceiling), and
  get-issue-detail. Every request paced to ~1/second against ComicVine's
  documented "velocity detection" blocks. Status-code interpretation (100 =
  bad key, 107 = rate limited, etc.) is based on community documentation, not
  a verified live test — this environment has no network access to
  comicvine.gamespot.com to confirm against, so treat the specific numeric
  mappings as best-effort until validated against a real key. Field *names*
  in the raw JSON DTOs are corroborated against an actual real API response
  example found via search, so those are on firmer ground than the status
  codes are.
- New ComicVineCacheService: persists to comicvine_cache.json (same pattern
  as recent_values.json) — caches search results, issue lists, and issue
  detail by id, plus a series-name -> volume-id memory so tagging several
  issues from the same run only needs one real series search. ComicVine's
  own API terms explicitly recommend caching to avoid duplicate requests, so
  this isn't just a nice-to-have given how touchy their rate limiting is in
  practice.
- New clean public models (Models/ComicVineModels.cs): ComicVineVolume,
  ComicVineIssueSummary, ComicVineIssueDetail, ComicVineCredit. Deliberately
  separate from the raw snake_case JSON DTOs, which stay private inside
  ComicVineService — nothing outside that file ever sees ComicVine's actual
  wire format.

## 0.1.20 — 2026-07-12

Batch C of the "settings and options" pass — new interactive features, not
settings. Closes out the full three-batch series.

- Keyboard navigation: Ctrl+1..Ctrl+5 jump directly to a tab, Ctrl+Tab /
  Ctrl+Shift+Tab cycle through them (wrapping), F6 moves focus to the file
  list, Shift+F6 moves it to the editor's search box. All wired as
  KeyboardAccelerators on the root grid so they work regardless of where
  focus currently is in the window.
- Right-click any single field for "Revert to Saved" — discards a pending
  edit to just that one field, leaving every other pending edit on the file
  alone. New ComicFileViewModel.RevertField and MainViewModel.RevertFieldToSaved.
  Single-file mode only: a per-file "revert to saved" doesn't have one clean
  meaning across a batch selection the way the other batch actions do, so
  it's a harmless no-op in batch mode rather than a guess.
- New batch tool: right-click a file within a multi-selection for "Copy
  Fields to Rest of Selection". The right-clicked file becomes the source
  (tracked via a new _lastRightTappedFile in MainWindow, set by the existing
  RightTapped handler); every other selected file is a target. A new
  AppDialogs.CopyFieldsAsync dialog lets you tick which of the source's
  populated fields to actually copy — Number and Page Count start unticked,
  since copying them by default across a batch of different issues would be
  a predictable footgun (every target ending up with the source's own issue
  number).
- More prominent unsaved-file indicator: a small numbered badge on the Save
  All toolbar button, visible only when something's unsaved. Uses dirty_fg as
  the badge's text colour against a normal panel background (ThBg2) rather
  than inverting it into a solid coloured fill — that's the pairing the
  theme's own "_fg" naming convention already establishes as safe, rather
  than inventing a new one that was never contemplated when the 15 themes
  were authored. New MainViewModel.DirtyCount/HasDirtyFiles, computed
  alongside the existing status-bar text in UpdateStatus.

## 0.1.19 — 2026-07-12

Batch B of the "settings and options" pass — visual/layout preferences.

- Cover image source: first page (unchanged default) or last page.
  ArchiveService.Read now takes a two-pass approach for "last" — a sequential
  reader can't seek backward once the final entry has gone by, and
  decompressing every image just to keep the last would waste work on every
  other page for every file opened, so the first pass only records which
  entry was last, and a second pass (ExtractSingleEntry) decompresses just
  that one. "First" mode is unchanged: still grabbed inline during the single
  existing pass, at no extra cost.
- Editor fields can now fill the available width instead of staying capped at
  780px. This needed a real structural fix, not just a MaxWidth toggle:
  EntryFieldTemplate's textbox+picker-button row was a horizontal StackPanel,
  which sizes each child to its own natural width regardless of how much
  space is available. Changed to a Grid with a star-sized textbox column and
  an auto-sized button column, so the textbox actually stretches when the
  cap is lifted. TextFieldTemplate and ComboFieldTemplate needed no changes —
  their controls are direct children of a vertical StackPanel, which already
  stretches children to fill available width by default.
- Compact density option, affecting field and sidebar-row spacing only nothing
  else. MainViewModel exposes FieldMargin/FileRowMargin as computed Thickness
  properties; the three field templates and the sidebar row template bind to
  them via ElementName=RootGrid, since a DataTemplate's own DataContext is
  the item (FieldViewModel/ComicFileViewModel), not the MainViewModel that
  actually owns the setting.
- Editor font family, alongside the existing font size — a curated list
  (Segoe UI, Segoe UI Variable, Consolas, Cascadia Code, Georgia, Comic Sans
  MS) rather than free text. Bound on the same outer ItemsControl as
  EditorFontSize already was, since FontFamily cascades down to every
  descendant control the same way FontSize does — no changes needed to the
  individual field templates.
- Remember-last-tab toggle: ActiveTab keeps being tracked regardless (harmless
  either way), only whether it's re-applied on the next launch is affected.

## 0.1.18 — 2026-07-12

Batch A of the "settings and options" pass — six new preferences, four with
explicit Settings dialog controls and two persisted silently since they're
already directly adjustable elsewhere in the main window.

- Sidebar sort mode now persists across sessions (restored into the
  MainViewModel constructor's backing field directly, bypassing the property
  setter's DisplayedFiles rebuild — pointless before any files are open).
- Window size and position are remembered on close and restored on next
  launch, behind a loose sanity check (plausible-range bounds only, not real
  multi-monitor awareness) so a corrupt or wildly out-of-range saved value
  can't strand the window off-screen.
- New toggle: auto-select the first file after opening (on by default,
  matching prior behaviour).
- New toggle: clear the sidebar filter when new files are opened, so a newly
  opened file that wouldn't match a stale filter is guaranteed visible.
- New live-validation timing setting: as you type (previous default and
  behaviour), on losing focus, or off entirely. ValidationService.CheckField
  and MainViewModel.ValidateLive are now the single choke point for this —
  "off" always clears rather than checking, regardless of which caller
  (keystroke, blur, or a selection-load refresh) triggered it, so the three
  callers can't drift out of sync with each other.
- Recently-typed-values cap is now configurable (was hardcoded at 12).
  RecentValuesService reads the cap live rather than caching it, both when
  recording and when reading, so lowering it in Settings takes effect
  immediately rather than only on each tag's next recorded value.

## 0.1.17 — 2026-07-12

- Improved the selected-sidebar-row text contrast (list_sel_fg vs list_sel) on
  three light themes, app-wide consistency pass following on from 0.1.16:
  - Gruvbox Light: list_sel #d5c4a1 -> #ebdbb2 (its own bg2, unmodified swatch
    from the palette). 3.57:1 -> 4.46:1 — the real ceiling for this theme,
    since its sidebar background already equals its page background, leaving
    no lighter tier to move into without the highlight becoming invisible.
  - Catppuccin Latte: list_sel #ccd0da -> #e6e9ef (its own bg2). 3.51:1 ->
    4.45:1, same structural ceiling as Gruvbox Light.
  - VS Code Light+: list_sel #cce4f7 -> #e8f2fb (a paler shade of the same
    blue-tint family it already used, not a new hue). 3.92:1 -> 4.53:1, fully
    clears the AA 4.5:1 threshold — achievable here specifically because this
    theme's sidebar background is already one tier above its page background,
    leaving headroom the other two don't have.
  - GitHub Light (4.56:1) and Solarized Light (3.00:1) intentionally left
    unchanged: GitHub Light already cleared the threshold; Solarized Light is
    already at the same technique's ceiling (list_sel already equals its own
    bg2) with no further room, and its only other lever — darkening the
    canonical Solarized blue — would compromise the one thing that theme is
    supposed to mean (exact fidelity to Ethan Schoonover's original spec).
  - Every accent/text colour (list_sel_fg) was left completely untouched in
    all three changes — only the highlight background moved, and always to
    either an existing, unmodified swatch already defined elsewhere in that
    theme's own palette, or a paler shade within its own established tint
    family. No new colours were introduced to any theme.

## 0.1.16 — 2026-07-12

- Added 7 new bundled themes to Assets/themes.json (now 15 total): Gruvbox
  Dark, Gruvbox Light, Catppuccin Mocha, Catppuccin Latte, Tokyo Night, True
  Black (pure #000000 for OLED screens, calm single accent rather than a
  stylised palette), and High Contrast (near-maximum contrast ratios, built
  for legibility rather than style).
- High Contrast needed one deliberate deviation from its own bright-
  yellow-on-black scheme: entry_sel (the text-selection highlight inside a
  textbox) uses blue rather than yellow, since TextBox has no separate
  "selected foreground" override in this app's theme resources — white text
  highlighted in yellow would have had poor contrast, which would have been a
  real problem specifically for the one theme whose entire purpose is
  legibility.
- Verified every new theme has the exact same 34 keys as the existing
  entries and that every value is valid hex, then ran actual WCAG contrast-
  ratio checks (not just eyeballing) on the key text/background pairs across
  all 7. High Contrast hits the maximum possible 21:1 on every primary pair.
  Two "low" results (Gruvbox Light and Catppuccin Latte's selected-sidebar-row
  accent colour) turned out to match the app's own existing light-theme
  precedent (Solarized Light already sits at exactly 3.00:1 for the same
  pairing) rather than being a new problem, so left as-is.

## 0.1.15 — 2026-07-11

- Added recently-typed values for single-line (entry-widget) fields, e.g.
  Writer, Publisher, Imprint. New RecentValuesService persists a per-tag
  history to recent_values.json in the config directory (same load/save
  pattern as schema_extra.json), most-recent-first, capped at 12 per tag.
  Recording happens on a field losing focus, not per-keystroke, so partial
  typing never pollutes the history.
- Scoped to entry fields only: not multi-line text (Summary/Notes — a recent-
  value list doesn't make sense there) and not combo (which already has a
  curated Options list serving the same purpose better).
- Reused the existing batch detected-values picker rather than adding a
  second button: in batch mode it still shows values detected across the
  selection with a file count, unchanged; in single-file mode it now shows
  recent values instead, with no count, whenever any exist. Same
  DistinctValues property and picker flyout in both cases — MainViewModel
  decides which kind of data to put in it depending on mode. A new ShowPicker
  property (batch always shows it; single-file only when there's history)
  replaces the old always-IsBatch-gated visibility on this one template. The
  picker's field refreshes immediately after a value is recorded, rather than
  waiting for the next file selection change, so a value you just typed shows
  up in its own picker right away.

## 0.1.14 — 2026-07-11

- Added sidebar sort and filter. A filter textbox above the file list (Filter
  files…) narrows by filename or subtitle — separate from the existing field
  search box, which only searches fields within the currently open file. A
  sort combo offers Name, Series/No. (by subtitle) and Modified (dirty files
  first, then name).
- New MainViewModel.DisplayedFiles: a sorted/filtered projection in front of
  OpenFiles, which stays in plain insertion order for anything that shouldn't
  care about display order (dirty-file lookups, path lookups). The sidebar
  ListView now binds to DisplayedFiles instead of OpenFiles directly.
- Rebuilds use only Insert/Remove/Move — never Clear — specifically so the
  bound ListView doesn't treat a sort-mode change as a full reset and drop
  the current selection; Move in particular is what keeps a moved item's
  selection state intact.
- Name and Series/Number sort deliberately do NOT react live to in-progress
  field edits — only to an explicit sort-mode/filter change, or files being
  opened/closed. Re-sorting the list under the cursor while typing into the
  very field it's sorted by would be genuinely disorienting. Modified-first
  is the one exception: it does react live to a file's dirty flag flipping,
  since reacting promptly is the entire point of that mode, and IsDirty only
  flips once per edit session rather than on every keystroke, so it doesn't
  have the same disorienting-reflow problem.

## 0.1.13 — 2026-07-11

- Fixed a real gap in Guess from Filename: "Batman\issue_01.cbz" left "issue"
  behind as the Series after the number was pulled out, and since "issue"
  isn't blank/short/purely-numeric it passed the old usability check —
  meaning the folder name ("Batman", the actually useful one) never got
  consulted at all. Added a small blocklist of generic filler words (issue,
  chapter, part, page, vol, no, book, file, untitled, etc.) so a leftover
  made up entirely of these now correctly falls back to the folder name
  instead. Verified via Python simulation against the reported case, a
  "Chapter 05" variant, and regression-checked against the earlier working
  cases before touching the real code.

## 0.1.12 — 2026-07-11

- Added Guess from Filename (Tools menu + toolbar): parses Series, Number,
  Volume and Year from each selected file's own filename, falling back to the
  parent folder name for Series when the filename alone leaves nothing
  usable. Only fills fields that are currently empty — never overwrites
  existing data. Works across a whole batch selection, unlike Auto Page
  Count, since it's pure string parsing (no archive read) and every file's
  path is genuinely different.
- New FilenameGuessService: recognises "(YYYY)" for Year, "Vol"/"Volume" for
  Volume (a bare "V2" is deliberately not matched — too ambiguous, e.g. "V for
  Vendetta"), "#123" or the last standalone number for Number, with leading
  zeros stripped consistently. Dot/underscore separators are normalized to
  spaces before any pattern matching, not after — regex word-boundaries treat
  "_" as a word character, so without this a name like "Saga_012.cbz" would
  silently fail to find the number at all. Caught and fixed via a Python
  simulation of the matching logic before shipping.

## 0.1.11 — 2026-07-11

- Corrected 0.1.10: the editor header banner's cover thumbnail now doubles in
  both dimensions (42x56 -> 84x112), keeping the original 3:4 aspect ratio
  instead of just growing taller.

## 0.1.10 — 2026-07-11

- Doubled the height of the cover thumbnail in the editor header banner
  (42x56 -> 42x112). Sidebar thumbnails are unaffected. Width stays fixed, so
  with UniformToFill the crop just shows a taller slice of the cover art
  rather than distorting it.

## 0.1.9 — 2026-07-09

- Fixed CS0103: BoolToErrorBrushConverter used Colors.Transparent without the
  Microsoft.UI using. In WinUI 3 the Color struct lives in Windows.UI but the
  Colors static helper (Colors.Transparent, Colors.Magenta, etc.) lives in
  Microsoft.UI — ThemeService already had both imports for this reason, the
  new converter only had the first.

## 0.1.8 — 2026-07-09

- Added cover previews. ArchiveService.Read now also captures the first image
  entry's bytes as the cover source (first-in-archive-order, not an
  alphabetical scan — archives are near-universally authored cover-first, and
  a full reorder would mean decompressing every page just to pick one).
  ComicFileViewModel decodes this into a small downscaled BitmapImage
  (LoadCoverAsync, called after open and after revert).
- Sidebar file rows now show a small cover thumbnail next to the
  filename/subtitle, in an always-visible placeholder slot that the image
  simply sits on top of once decoded.
- The editor gained a header banner (cover + filename + subtitle) above the
  search box, for single-file selection only — it collapses in batch mode and
  when nothing is selected, via a new MainViewModel.IsSingleFileMode.
- No resizing on the banner, per instruction; layout is fixed.

## 0.1.7 — 2026-07-09

- Added drag-and-drop: dragging cbz/cbr (or zip/rar) files from Explorer onto
  the window opens them through the same OpenPathsAsync pipeline as the Open
  dialog and command-line startup. A themed overlay appears while dragging
  over the window and clears on drop or drag-leave.
- Added live as-you-type validation: entry fields with numeric constraints
  (Year, Month, Day, Count, Volume, AlternateCount, PageCount,
  CommunityRating) now show a red outline and an inline message as soon as an
  invalid value is typed, and also on loading a file whose existing value is
  already invalid. Backed by a new ValidationService.CheckField, which
  Validate() (the save-time check) is now built on top of, so live and
  save-time validation can't drift apart. Mixed-value batch placeholders are
  left unvalidated since they aren't a real value.

## 0.1.6 — 2026-07-07

- File list: Ctrl+A selects all open files; Delete removes the selection (same
  unsaved-changes confirmation as the toolbar Remove button); right-click gets
  a context menu (Save, Revert, Remove from List, Open Containing Folder),
  with items greyed out when they wouldn't apply to the current selection
- Added a GitHub Actions build workflow (.github/workflows/build.yml) that
  restores and builds Release for x64 and ARM64 on every push and pull request
- Added structured logging: LogService writes a plain-text, timestamped,
  leveled log per day to %appdata%\cbzLab\logs. Wired into the previously
  silent catch blocks in SettingsService, SchemaService, ThemeService and
  ArchiveService (including the zip-slip extraction guard), plus a global
  unhandled-exception trap in App. Settings gained an "Open logs folder" link
  next to the existing config/themes links. A handful of catches in static
  utility methods were left as-is where the fallback is already self-evident
  to the user (extension-based format guess, a magenta colour swap, blank
  metadata fields) rather than truly silent.

## 0.1.5 — 2026-07-07

- Fixed: files were marked as modified immediately on open whenever auto page
  count filled in an empty PageCount field. Added ComicFileViewModel.SeedValue,
  which writes into both the saved baseline and current values, and switched
  the auto-fill-on-open path to use it instead of SetValue. Opening a file no
  longer dirties it on its own; manual Tools -> Auto Page Count still dirties
  the file as before, since that is a deliberate user action.

## 0.1.4 — 2026-07-07

- Batch editing: entry and text fields now get the same detected-values picker
  that combo fields already had, next to the textbox — pick "(blank)" or any
  value found across the selection, or still type custom text to override all.
  No view model changes needed; the distinct-value tracking already covered
  every field type, only the entry/text templates were missing the picker ui.

## 0.1.3 — 2026-07-07

- Enabled single-file publish (PublishSingleFile, IncludeNativeLibrariesForSelfExtract)
  so managed dependencies collapse into cbzLab.exe; a small set of Windows App SDK
  native files still has to sit alongside it — a platform constraint on unpackaged
  WinUI 3 apps, not something this setting can remove
- README publish section updated to describe the leaner output folder

## 0.1.2 — 2026-07-07

- Adapted to the SharpCompress 0.48.0 API: ReaderFactory.Open was renamed
  upstream to ReaderFactory.OpenReader (three call sites in ArchiveService);
  the reader interface itself is unchanged

## 0.1.1 — 2026-07-07

- Bumped SharpCompress 0.38.0 → 0.48.0 to clear advisory GHSA-6c8g-7p36-r338
  (CVE-2026-44788, directory traversal via WriteToDirectory)
- Hardened ArchiveService.ExtractAll independently of the library fix: entries are
  now extracted manually with per-entry path resolution, and anything resolving
  outside the extraction root (rooted paths, .. traversal, invalid characters) is
  skipped
- Fixed CS8604 nullable warning: the open-failure dialog guards against a null
  XamlRoot and falls back to a status bar message

## 0.1.0 — 2026-07-07

Initial WinUI 3 release of cbzLab (C#/.NET 8, Windows App SDK 1.6), rebuilt to
spec from the Python/tkinter ComicInfoLab.

- CBZ/CBR opening with format sniffing, ComicInfo.xml parsing, image page counting
- Full ComicInfo v2.0 field set from user-editable schema.json, plus automatic
  registration and persistence of unofficial fields (schema_extra.json)
- Five-tab editor (Basic Info, Publication, Creators, Story, Extras) filtering a
  single shared form; only-populated / all-fields / extras visibility toggles;
  field search
- Multi-select batch editing with mixed-value sentinels, batch scope panel and
  distinct-value pickers with counts on dropdown fields
- Save / Save As / Save All with per-file format selection, on-save type
  validation with fix/save-anyway, cancellable progress dialog and atomic
  temp-then-replace archive writes; page images and <Pages> elements preserved
- CBR writing via external tool (rar preferred; 7-Zip accepted but cannot create
  RAR — the tool's error is surfaced with a CBZ suggestion)
- Runtime JSON theming (mutable brush architecture, live switching, custom theme
  files, Solarized Dark fallback), light/dark fluent state flipping by luminance
- Auto page count on open and on demand; Copy/Paste XML via clipboard
- Recent files, command-line file opening, unsaved-changes guard on close
- Settings dialog persisting to %APPDATA%\cbzLab; first-run seeding of bundled
  schema and theme assets
- New retrowave CBZL branding icons (svg/png/multi-size ico) — provisional style
