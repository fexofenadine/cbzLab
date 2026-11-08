# cbzLab for Android - Material redesign plan

Status: **proposed, awaiting approval.** Nothing here is built yet.

## Goal

Make the Android app look and behave like a normal Android app, following Material Design 3, instead
of the desktop layout with bigger buttons that it is today. The desktop app must look and work exactly
as it does now.

## Decisions taken

- **Scope: a full navigation redesign**, not a restyle. App bar, separate screens, bottom sheets,
  a selection bar, snackbars.
- **Colours: both.** The phone's own Material You colours, or any of cbzLab's themes.
- **Tablets: included.** List and editor side by side on wide screens.
- **Delivery: one release** (2.5.0), with every phase below finished and checked before it ships.

## Where it starts from

The Android app hosts the same `MainView` as the desktop: menu bar, scrolling text toolbar, Fluent
controls, a status bar. Phone behaviour is `TouchMode` branches inside it, and `TouchStyles.axaml` only
enlarges targets. Almost every action (Open, Save, ComicVine, Combine, Trim, Find & Replace...) is
written as a click handler inside `MainView.axaml.cs` (2,800 lines), so a new layout can't call
them without first moving them out.

## Approach

**A separate phone layout in `cbzLab.UI`, sharing everything below the view.** View models, services,
storage and most dialogs are reused as they are. The desktop keeps `MainView` and Fluent; Android
gets a new `MobileShell` and a Material theme.

Rejected:

- **More `TouchMode` branches in `MainView`.** It's already the bottleneck, and every branch is a
  desktop regression risk.
- **A native Android UI** (Kotlin/Compose, or .NET MAUI). Every screen rewritten, and the shared code,
  the main thing the port achieved, given up.

**The one change that touches the desktop:** the actions move out of `MainView.axaml.cs` into a
shared `AppActions` class (opening, saving, lifecycle, tools, ComicVine, Combine, Trim, Settings)
that both layouts call. The desktop's behaviour must not change; this is checked with the test suite
plus a manual pass over every menu item on the desktop before the Android work builds on it.

## The screens

Sizes follow Material's window classes: **compact** (under 600 units wide: phones), **medium**
(600-839: small tablets, phones on their side) and **expanded** (840 and up: tablets).

### Library (the book list)

- **Top app bar:** "cbzLab", a search icon (filters the list, replacing the filter box), a sort
  icon (a bottom sheet with the sort choices), and the ⋮ overflow menu.
- **Book rows:** cover thumbnail, title, "Series #N" line, unsaved dot. Tap opens the editor.
- **Open button:** a floating "+" button opening a small menu: Open books, Open folder, Open with
  another file manager, Open folder with another file manager, Recent.
- **Empty state:** the logo, a line of text and an Open button, instead of a blank list.
- **Selection:** long-press starts it. The app bar becomes a selection bar ("3 selected", Select all,
  Edit, Save, Close, ⋮ for the rest). Back or ✕ ends it. Replaces the Select button.
- **Grid view** becomes a toggle in the overflow menu that shows **cards** (cover plus the chosen
  columns as lines) instead of the sideways-scrolling desktop table. Fixes a known left-over from
  the first Android plan.

### Editor

- **Top app bar:** ← back, the book's title (or "3 books" in batch), Save, ComicVine search (when
  enabled), ⋮ for the rest.
- **Tabs:** the five tabs as Material scrollable tabs, swipeable left and right.
- **Fields:** Material outlined text fields (label inside the box that floats up when filled).
  Number fields keep the number keypad. Combo fields open a bottom-sheet picker. The date field
  gets the system-style date picker as well as typing, closing another left-over.
- **Batch mode:** mixed values show as "Multiple values" placeholders; the picker is a bottom sheet.

### Overflow menus: where every current menu item goes

| Today | Library ⋮ | Editor ⋮ | Selection bar |
|---|---|---|---|
| Open, Open Folder, Open with..., Recent | + button | | |
| Save | | app bar icon | icon |
| Save As | | ⋮ | |
| Save All | ⋮ (when anything unsaved) | ⋮ | |
| Revert | | ⋮ | ⋮ |
| Remove / Close | swipe a row, or selection | ⋮ | icon |
| Close All, Reopen Last Closed | ⋮ | | |
| Grid View, Choose Grid Columns | ⋮ | | |
| Show All Fields, Show Extra Fields | | ⋮ | ⋮ |
| Theme | Settings | | |
| Search ComicVine | | app bar icon | ⋮ |
| Guess from Filename, Auto Page Count, Extract from Summary, Copy/Paste ComicInfo.xml | | ⋮ Tools | ⋮ Tools |
| Find & Replace, Validate All | ⋮ | | ⋮ |
| Combine Archives, Trim Branding Footers | ⋮ | | ⋮ |
| Customize Toolbar | dropped on Android (there's no toolbar) | | |
| Settings, What's New, About | ⋮ | | |
| Quit | dropped (Android apps don't quit; Back from the library leaves, as now) | | |

### Dialogs

- **Small questions** (confirm, unsaved changes, messages) become Material dialogs: title, text, text
  buttons bottom right.
- **Choices** (sort, combo-field values, Save As format, Open menu) become **bottom sheets**.
- **Big flows** (Settings, ComicVine search, issue match and review, Combine, Trim, Find &
  Replace, Validation results, What's New) become **full-screen pages** with an app bar (✕ or ←,
  title, a confirm action such as Apply). On expanded screens they show as centred dialogs
  instead, as Material recommends.
- The existing dialogs' contents are reused where they work. They are restyled by the theme rather
  than rewritten, and only re-laid-out where a phone needs it.

### Messages

The status bar goes. Results ("Saved 3 books", "Applied 15 fields from ComicVine") appear as
**snackbars** at the bottom, with Undo where the app can actually undo (Remove/Close via Reopen Last
Closed). Errors that need reading stay as dialogs.

### Settings

A full-screen preferences page in Material's list style, grouped: Appearance (colours, theme, font
size, density), Editing, ComicVine, Backup (Export/Import), About. Desktop-only settings stay hidden,
as now.

## Colours

A new **Colours** setting on Android: **System colours** or **cbzLab theme**.

- **System colours:** read from Android's dynamic palette (Android 12 and later; the system
  accent and neutral colour tones), mapped onto Material's colour roles. Light or dark follows the
  phone. On Android 8-11, which have no dynamic palette, the option uses a fixed Material
  palette taken from the cbzLab logo, and says so.
- **cbzLab theme:** any of the 15 existing themes, mapped onto the same Material roles (accent to
  primary, backgrounds to surfaces, and so on). Each theme keeps its own light or dark.
- The choice is stored in the shared settings, but only Android shows or reads it.

## Theme library: a spike decides

**Material.Avalonia** (third-party, MIT) 3.21.0 was published 2026-09-25 and declares Avalonia
12.1.1 or later, so it may work with our 12.1.5. Not yet verified, and not yet known: how much of
Material 3 it covers (it began as Material 2), whether its palette can be set at run time (needed
for theme switching), and how it treats `DataGrid` and our own controls.

Phase 0 builds a throwaway page on the emulator with it. **Use it** if it builds against 12.1.5,
renders an app bar, outlined fields, tabs, a bottom sheet and a dialog correctly, and accepts a
palette change at run time. **Otherwise** write our own Material 3 styles on top of Fluent (more
work, but no dependency risk). I'll report the result before going past phase 0.

**Icons:** Google's Material Symbols (Apache 2.0, free to bundle), drawn as vector paths, so
no icon font to ship.

## Phases (all in one release)

0. **Spike** - Material.Avalonia on the emulator, decision as above. Also a check that a
   bottom sheet and a navigation stack behave with Back and the keyboard.
1. **Move the actions out** - `AppActions`, used by the desktop `MainView` first. Desktop checked
   by the full test suite and a pass over every menu item before going on.
2. **Shell and library** - `MobileShell` with a navigation stack (Back pops it; the existing
   dialog-then-editor-then-leave order stays), app bar, book rows, + button, empty state, search,
   sort, Android hosting `MobileShell` instead of `MainView`.
3. **Editor** - app bar, swipeable tabs, Material fields, date picker, bottom-sheet pickers,
   keyboard lift carried over.
4. **Selection, batch and grid cards.**
5. **Dialogs and full-screen flows** - every existing dialog reachable and usable on a phone.
6. **Colours** - System colours and theme mapping, the new setting.
7. **Tablets** - list-detail on expanded screens, dialogs as centred dialogs there, rotation
   moving between layouts without losing the open book or unsaved edits.
8. **Polish** - snackbars with Undo, edge-to-edge drawing under the system bars, the system's
   predictive Back animation if Avalonia supports it (not checked), touch replacements for the
   field tooltips.
9. **Check and release** - see below. 2.5.0.

## Testing

- Unit tests for `AppActions` where it can be tested without a window, and the existing 259 tests.
- The desktop checked by hand after phase 1 and again before release.
- The `cbzlab-test` phone emulator for every phase, **asked for first** each time, since other work
  may be using the emulator.
- **A tablet emulator is needed** (for medium and expanded sizes). That means creating a new AVD,
  which I'd ask before doing.
- A real phone (yours) before release: touch feel, keyboard, rotation, the system colours from a
  real wallpaper, other makers' file managers.

## Risks

- **Phase 1 changes desktop code.** The biggest risk of the whole plan, and the reason it's checked
  on its own before anything is built on it.
- **Material.Avalonia might not fit.** Hand-written styles are the fallback; it adds time, not risk.
- **APK size.** A theme library and icons add some weight to the 19 MB arm64 download. To be
  measured, not guessed.
- **Avalonia limits on Android.** Things like predictive Back or the system date picker may need
  Android-side code in `cbzLab.Android`; the spike checks the riskiest of these early.

## Not changing

- The desktop app's layout, theme and behaviour.
- What the app can do on Android: every feature available today stays reachable (see the menu table).
- Saving rules: still byte-for-byte page copies, a read-back check before replacing a book, CBR
  saved as a CBZ copy.
