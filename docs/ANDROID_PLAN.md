# cbzLab for Android - plan

Status: **planned**, not started. The open decisions at the end are asked when the work reaches
them; each lists the recommendation first.

## Goal

A native Android build of cbzLab, from the same codebase as the desktop app, that works well on
phones and tablets in either orientation. Not a wrapped cbzLite: a browser can't read CBR, call
ComicVine, or save over the file you opened on Android, and a native app can do all three.

## Starting point

- The desktop app is Avalonia 12.1.4 on .NET 10. Avalonia publishes `Avalonia.Android` 12.1.4, so
  the same UI framework runs on Android.
- `Services/`, `ViewModels/` and `Models/` are already nearly platform-independent: SharpCompress
  (CBR reading) and SkiaSharp (Trim Footers) both run on Android.
- The desktop app and tests moved from .NET 8 to .NET 10 on 2026-11-01 (release 2.2.0), ahead of
  .NET 8's end of support on 10 November 2026.
- Android toolchain on this machine, set up 2026-11-01:
  - the .NET `android` workload (Android SDK pack 36.1);
  - the Android SDK at `%LOCALAPPDATA%\Android\Sdk`, with platforms 35 and 36;
  - Microsoft OpenJDK 17 at `%LOCALAPPDATA%\Android\openjdk-17`. Android Studio's own JDK (25) is
    too new for .NET's Android build, which accepts up to 21.
  - Builds pass both locations explicitly:
    `-p:JavaSdkDirectory=... -p:AndroidSdkDirectory=...`. A throwaway `net10.0-android` app built
    and produced a signed APK.

## What doesn't carry over

| Area | Why | What replaces it |
| --- | --- | --- |
| Dialogs (about 20) | Each is its own `Window`; an Avalonia Android app has one view and no separate windows | Overlay dialogs inside the main view |
| `MainWindow` | Same | A `MainView` user control, hosted by a window on desktop and directly on Android |
| File paths | Android hands apps document references (`content://` URIs) read and written as streams, not paths | A stream-based archive source; see phase 3 |
| Safe save (write a temp file, then swap it in) | No atomic replace for a document you don't own | Write-and-verify with a backup; see the save decision |
| CBR writing | Needs an external RAR tool, which Android doesn't have | CBR read-only; save as CBZ, as cbzLite does |
| Self-updater, Open Containing Folder, RAR tool path | Desktop-only concepts | Updates through the app's store or release page; the others are dropped |
| Keyboard shortcuts, hover, right-click | No mouse or keyboard as standard | Touch equivalents; see phase 6 |

## Phases

Each phase ends with something built and checked on a device or emulator, the way the desktop
port was done slice by slice.

### Phase 0 - prerequisites and a spike

- Done: moved the solution to .NET 10 (release 2.2.0).
- Done: installed the .NET Android workload, with JDK 17 and Android platform 36.
- Done (2026-11-01): a throwaway Avalonia Android app, run on an Android 15 emulator (`cbzlab-test`,
  Pixel 6), checked everything this plan relies on:
  - **Toolchain:** builds a signed APK; Avalonia.Android 12.1.4 starts and renders.
  - **Shared code:** the desktop's own `ComicInfoXml.cs` and `schema.json`, used unchanged, read a
    real book (Series and all 34 pages).
  - **File access:** the Android file picker hands back a `content://` document, not a path. Its read
    stream works (and seeks).
  - **Saving in place works:** writing a book back over the opened document succeeded, read back
    identical, and the file on the device was updated. Writing a *shorter* version was trimmed to
    the new length, so a smaller archive won't leave the old tail behind - at least with the
    Downloads provider. Other providers (SD cards, cloud drives) aren't tested yet and should be
    before phase 3 relies on this.
  - **Long press** raises `ContextRequested` (what the desktop's right-click menus already use) and
    `Holding`.
  - **On-screen keyboard:** `TopLevel.InputPane` reports Open with the area it covers, and Closed,
    through its `StateChanged` event. Sampling its state once after a delay is unreliable; listen
    to the event.
  - **Rotation:** with the activity handling configuration changes itself, rotating keeps the app
    and its state, and the width-based layout switches between stacked and side by side.
  - **App icon:** the badge as an adaptive icon showed correctly in the launcher (see phase 4).
- Findings to carry forward:
  - Avalonia's Android activity is AppCompat-based, so it needs an AppCompat theme
    (`Theme.AppCompat.NoActionBar`), not a plain Material one - the app crashes on start otherwise.
  - Debug builds leave the app's code out of the APK for Visual Studio's fast deployment; an APK
    installed by hand needs `-p:EmbedAssembliesIntoApk=true` or it crashes on start.
  - The Android project's automatic namespace imports clash with Avalonia names (`Application`,
    `Button`), so shared UI code needs aliases or those imports turned off.
  - **Open question:** the first tap after a fresh launch was ignored, twice in a row. It may be an
    emulator input quirk; check on a real phone before building around it.

### Phase 1 - shared core

- Done (2026-11-02): `cbzLab.Core` holds `Services/`, `ViewModels/` and `Models/` (27 files, moved
  with their history; namespaces unchanged). The desktop app and the tests reference it, and it
  grants the tests the same internal access the desktop project does. Builds with no warnings, all
  254 tests pass, and the desktop app behaves the same. Not released on its own; it ships with the
  next desktop change.
  - `UpdateService` (the self-updater) stayed in the desktop app, since Android won't use it.
  - The RAR tool search stayed in `ArchiveService`; it simply finds nothing on Android. Interfaces
    for platform differences arrive with the storage work in phase 3, where they're needed.
  - The bundled `schema.json` and themes stayed in `cbzLab/Assets`: the website build, cbzLite, the
    branding script, the Pages workflow and the tests all read them there, and the shared code reads
    them from beside the app at run time, not from a source folder. Where they live is decided in
    phase 4, when the Android app needs them packaged.

### Phase 2 - shared main view and in-window dialogs

The first phase with a visible desktop change: dialogs open inside the main window instead of as
separate windows. Done in slices, each one building, passing every test and checked in the running
app before the next starts.

**Where the code goes.** A new `cbzLab.UI` library holds everything both apps show: `MainView`, the
dialogs, the converters, and the shared styles now in `App.axaml` (Fluent theme, the DataGrid style
include, toolbar and dialog styles). The desktop app keeps only what only a desktop has: `Program`,
`App`, a thin `MainWindow`, and `UpdateService`.

**What's in the way today.**

- `MainWindow.axaml.cs` is 2,400 lines, but only about 15 places use anything only a window has:
  saving and restoring its position and size, its icon, its title, the closing check for unsaved
  edits, and opening files passed on the command line. Those stay in `MainWindow`; everything else
  moves to `MainView`.
- 18 dialogs, each its own `Window` shown with `ShowDialog(owner)`, from 37 lines (the message box,
  opened from 40 places) to 462 (Settings). Seven open further dialogs of their own, such as
  Settings asking before Reset to Defaults, so dialogs must be able to stack.
- The progress window (opening many files, saving, combining, trimming) is shown without blocking,
  updated as work runs, and can be cancelled.
- File pickers stay the operating system's own; they aren't part of this.

**How in-window dialogs behave.** A dialog host sits over `MainView`'s content:

- A dimmed backdrop covers the app. Nothing behind it can be clicked, and keyboard shortcuts and
  menus are blocked while a dialog is open.
- The dialog is a card centred in the window, at its current width at most, never wider or taller
  than the window allows. Its content scrolls when there isn't room; its buttons stay visible.
- Focus moves into the dialog when it opens, Tab stays inside it, and focus returns to where it was
  when it closes.
- Enter and Esc do what each dialog's default and cancel buttons do today. Clicking the backdrop
  does nothing, so a stray click can't throw away a half-filled dialog.
- Dialogs stack: a confirm opened from Settings sits above Settings, with its own backdrop.
- Every dialog keeps its current wording, buttons and results, so no calling code changes meaning.
- The About box keeps its fixed synthwave palette.
- The main window gets a minimum size, so every dialog still fits on screen.

**Slices.**

1. **Shared UI library and `MainView`.** Done (2026-11-02). `cbzLab.UI` holds `MainView`, the 18
   dialogs, the converters and the shared app styles, all moved with their history. `MainWindow` is
   now a thin desktop host: its size and position, icon, title, closing and the self-updater. The
   view asks its host for anything only a window can do: it raises `QuitRequested` and
   `CheckForUpdatesRequested` (the update item is hidden unless the host supports updates), answers
   `ConfirmCloseAsync` before a close, and is told `OnShownAsync` and `PrepareForExit`. The version
   now comes from `AppVersion` in Core, read from the running app. No visible change: builds without
   warnings, all 254 tests pass, and the published app starts with a book, shows the theme, About
   (version 2.2.0) and the update item, asks about unsaved edits on close (Cancel keeps it open,
   Discard closes without touching the book), and saves the open tab and window position on exit.
2. **The dialog host and the everyday dialogs:** message, confirm, validation, unsaved changes and
   progress. Together these cover nearly every dialog the app shows day to day. Done (2026-11-02).
   Each dialog is now a `DialogBase` shown by a `DialogHost` that sits over `MainView`: a dimmed
   backdrop disables everything behind it, the card is sized to the window, Esc does what the
   dialog's cancel does, Tab stays inside the card and focus goes to the default button. A dialog
   opened from one that is still a separate window (Reset to Defaults from Settings) falls back to a
   small `DialogWindow` showing the same card, until slices 3 and 4 convert the rest. Checked in the
   published app: all five open inside the main window, with the menus and toolbar disabled behind
   them; Esc, Enter and Tab work; Revert's Cancel keeps the edit and Revert reloads; unsaved changes
   on close keeps the window on Esc and closes on Discard; the progress card updates and Esc switches
   it to cancelling. Two things for later: at very small window sizes the unsaved-changes file list
   shrinks to nothing (slice 5's minimum size covers it), and the footer scan never checks for
   cancel, so its Cancel button only takes effect once the scan ends (unchanged from before).
3. **The rest of the editing dialogs (done 2026-11-02):** Settings, About, Choose Columns, Customize Toolbar, Find &
   Replace, Copy Fields, Multi Save.
4. **ComicVine, Combine Archives and Trim Branding Footers (done 2026-11-02):** search, match, both review dialogs,
   combine and trim.
5. **Fit and usability check, then release (done 2026-11-02, released as 2.3.0; the window fallback was removed and the main window now has a 560 x 420 minimum).** Every dialog opened at a normal window size, at the
   new minimum size, and stacked where it can stack; then the release.

**Checks for each slice.** Build with no warnings; all tests pass; and in the published app, every
dialog the slice touches is opened, used with the keyboard (Enter, Esc, Tab), and its result
checked, with your settings file backed up and restored around the run as before. In-window dialogs
are part of the main window's accessibility tree, which makes them easier to drive in these checks
than separate windows were.

### Phase 3 - storage

- An archive source abstraction: open a stream to read, open a stream to write, a display name, a
  stable id. Desktop implements it with paths, Android with document URIs.
- `ArchiveService`, autosave, recent files and the save planner move to it.
- Android: multi-file open, folder open with persisted access (which also makes Open Recent and
  saving in place work across app restarts), and the save strategy from the save decision.

### Phase 4 - Android head

- `cbzLab.Android` project: the app icon is the same badge as the desktop's - an adaptive icon
  built from `branding/badge-square.png` (the badge uncropped, so each launcher crops it to its own
  shape), with the badge filling the visible middle and a blurred copy of it to the edges, generated
  by `branding/apply_logos.py` alongside the other icons. Then the package id, permissions, "open with" for `.cbz`/`.cbr` from file managers, and the activity set up so
  rotation doesn't restart the app.
- The Back button: closes an open dialog, else returns from the editor to the file list, else asks
  about unsaved edits before leaving.
- Unsaved edits survive the app being killed in the background, through the existing autosave.

### Phase 5 - adaptive layout

- Width decides the layout, not the device:
  - narrow (phone portrait): the file list is one screen, tapping a book opens the editor as the
    next screen, Back returns;
  - wide (tablet, phone landscape, desktop): today's side-by-side layout.
- Rotating moves between the two without losing selection or edits.
- Menu bar and toolbar become a top app bar with the most-used actions and a "⋮" overflow menu on
  narrow screens. The desktop keeps its menu bar.

### Requirements for every screen (checked in phases 2, 5 and 6)

Every screen, menu and dialog has to work in both portrait and landscape, on a phone and a tablet,
and keep working when the device is rotated while it's open:

- Nothing runs off the screen. A menu or list too long for the space scrolls; a menu or popup that
  would pass an edge moves or opens the other way; a submenu with no room beside its item opens
  inline beneath it.
- Every dialog fits the screen in both orientations: its content scrolls when it doesn't fit, and its
  buttons stay visible and reachable without scrolling the whole dialog away. Decorative areas (the
  About box's logo) shrink on short screens rather than pushing the buttons off.
- With a phone on its side (wide but short), the editor gives the fields most of the height: no
  large cover header, compact search and tabs.
- Rotating keeps the selection, the current tab, any half-typed text and any open dialog's contents.
  Anything positioned for the old size (a popup, a menu) closes or re-places itself.
- The on-screen keyboard never covers the field being typed into, in either orientation.

These are checked the way cbzLite's were for 0.5.2: an automated pass that opens every menu, submenu,
popup and dialog at a phone portrait size (360x780) and landscape size (780x360) and checks each is
on screen, visible, scrollable where it overflows, and has a reachable button - plus a manual pass
on a real phone, rotating with each one open.

### Phase 6 - touch

- Long press replaces right-click (field menu, file menu).
- Long press in the file list starts selection mode, with checkboxes and a count in the app bar,
  replacing Ctrl/Shift-click for batch editing.
- Value pickers and context menus become bottom sheets on narrow screens.
- Field tooltips become a small info button, since touch has no hover.
- Touch targets at least 48dp; the compact-spacing setting is hidden on touch.
- The focused field scrolls clear of the on-screen keyboard; number fields (Issue #, Count,
  Volume, Year, Page Count) open the number keypad; the Publication Date field gets the system date
  picker alongside typing.
- Grid view becomes a card per book (your chosen columns as lines) on narrow screens; the table
  stays for wide ones.

### Phase 7 - feature parity (after the first release)

- ComicVine (works natively; no browser restrictions).
- Combine Archives and Trim Branding Footers, with checks on memory use for large books on phones.
- Settings: the desktop list minus the desktop-only items, each explained like cbzLite does.
- An Android column added to `cbzLite/PARITY.md`, or a parity table of its own.

### Phase 8 - release

- Signing key, a CI workflow building the APK (and an AAB if the Play Store is used), versioning,
  and distribution per the distribution decision.
- User guide section for Android.

## Testing

- Unit tests run against Core on the desktop as now, so the shared logic stays covered.
- Each phase is checked on the Android emulator. Touch gestures, the keyboard and rotation also need
  a check on a real phone, which needs you: what phone and Android version you have decides the
  minimum version to support.
- The settings-file discipline doesn't apply on Android (the app has its own storage), but the
  desktop's still does whenever the desktop app is run during this work.

## Decisions

Answered decisions move to "Decided" with the date.

### Open, for when the phase reaches them

- **Saving over a file on Android** (phase 3). *Recommended:* copy the original to the app's own
   storage, write the new archive over the document, read it back to check it, then delete the
   backup - and restore from the backup if anything fails. Not atomic like the desktop's swap; the
   backup is what makes an interrupted save recoverable. *Alternative:* always save a new copy
   beside the original, the way cbzLite does outside Chrome.
- **Minimum Android version** (phase 4) - depends on your phone; recommended Android 8 or later.
- **App name and package id** (phase 4) - recommended "cbzLab" and `io.github.fexofenadine.cbzlab`.
- **Version numbers** (phase 8) - the same version line as the desktop app (one release builds
   both) or a separate one like cbzLite's.
- **Signing key** (phase 8) - where the release key lives: recommended a GitHub Actions secret,
    with a backup you keep offline. Losing it means users can't update without uninstalling.

### Decided

- **Shared UI library** (2026-11-02): `MainView`, the dialogs, converters and shared styles move into
  a new `cbzLab.UI` library in phase 2, slice 1, so the Android app uses them as they are.
- **Releasing in-window dialogs** (2026-11-02): phase 2's slices are pushed without a version bump;
  one release (2.3.0) ships once every dialog is converted, so desktop users never see a mix of
  in-window and separate-window dialogs.
- **.NET 10** (2026-11-01): the desktop app and tests move to .NET 10 now, as their own release
  (2.2.0), ahead of .NET 8's end of support.
- **Shared code** (2026-11-01): one `cbzLab.Core` library used by both apps, so a fix lands in
   both.
- **Dialogs on the desktop** (2026-11-01): both apps use the new in-window overlay dialogs. On the
   desktop they'll appear inside the main window rather than as separate windows - a visible desktop
   change, called out in that release's changelog.
- **First Android release scope** (2026-11-01): the editor first - open, edit, batch, validate,
   save, CBR read-only, the Tools menu. ComicVine, Combine Archives and Trim Branding Footers follow
   in a later release, so phase 7 comes after the first release rather than before it.
- **Distribution** (2026-11-01): APKs on the GitHub releases page first. F-Droid probably later;
   the Play Store probably not, but not ruled out. Because F-Droid builds apps from source and
   accepts only open-source dependencies, every dependency added along the way should stay
   open-source (Avalonia, SkiaSharp and SharpCompress all are), and the build shouldn't rely on
   anything only this machine or CI has.
