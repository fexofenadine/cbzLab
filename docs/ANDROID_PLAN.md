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
- A throwaway Android head: an Avalonia app that starts on an emulator, loads `schema.json`, opens a
  CBZ through the Android file picker and shows its Series. Proves the toolchain and the
  stream-based file access before anything is restructured.
- Check the Avalonia APIs this plan leans on but hasn't confirmed on Android: long-press raising
  `ContextRequested`, the on-screen keyboard (`TopLevel.InputPane`), and width-based layout
  switching.

### Phase 1 - shared core

- New `cbzLab.Core` library holding `Services/`, `ViewModels/`, `Models/` and the bundled assets.
  `cbzLab` (desktop) and `cbzLab.Tests` reference it. No behaviour change on desktop.
- Anything that touches the desktop directly (process launching, `UpdateService`'s swap scripts,
  the RAR tool search) moves behind small interfaces so Core stays platform-free.

### Phase 2 - shared main view and overlay dialogs

- Move the window's contents into `MainView`, hosted by `MainWindow` on desktop.
- An overlay dialog host, then port every dialog to it, the simple ones first (message, confirm,
  validation), then settings, columns, toolbar, ComicVine, combine, trim. See the dialog decision.

### Phase 3 - storage

- An archive source abstraction: open a stream to read, open a stream to write, a display name, a
  stable id. Desktop implements it with paths, Android with document URIs.
- `ArchiveService`, autosave, recent files and the save planner move to it.
- Android: multi-file open, folder open with persisted access (which also makes Open Recent and
  saving in place work across app restarts), and the save strategy from the save decision.

### Phase 4 - Android head

- `cbzLab.Android` project: app icon (an adaptive icon built from `branding/badge.png`), package
  id, permissions, "open with" for `.cbz`/`.cbr` from file managers, and the activity set up so
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
