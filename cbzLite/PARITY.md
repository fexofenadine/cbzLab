# cbzLite parity with the cbzLab desktop app

Every desktop menu item and feature, and where it stands in cbzLite. **Same** means it behaves as the
desktop app does. **Desktop only** items still appear in cbzLite, dimmed, and say why when clicked.
Keep this up to date when either edition gains or changes a feature.

## File menu

| Desktop | cbzLite | Notes |
| --- | --- | --- |
| Open… (Ctrl+O) | Same | CBR files get an explanation instead of opening |
| Open Folder… (Ctrl+Shift+O) | Same | Chrome/Edge ask once for edit access to the folder; other browsers open it read-only |
| Open Recent | Desktop only | A page never learns a file's location, so it can't reopen one by itself |
| Save (Ctrl+S) | Same | Saves in place in Chrome/Edge; elsewhere the edited copy is downloaded |
| Save As… (Ctrl+Shift+S) | Same, CBZ only | |
| Save All (Ctrl+Alt+S) | Same | No per-file format picker, since there's only CBZ |
| Revert | Same | Files opened without a handle revert to their last opened or saved state |
| Remove from List | Same | |
| Close (Ctrl+W) | Same, menu only | Browsers keep Ctrl+W for closing the tab |
| Close All | Same | |
| Reopen Last Closed File (Ctrl+Shift+T) | Same, menu only | Last ten files closed since the page loaded; Ctrl+Shift+T belongs to the browser |
| Quit (Ctrl+Q) | Not included | Close the tab instead |

## View menu

| Desktop | cbzLite | Notes |
| --- | --- | --- |
| Show All Fields | Same | |
| Show Extra Fields | Same | |
| Choose Grid Columns… | Same | |
| Theme | Same | All of the desktop's themes |

## Tools menu

| Desktop | cbzLite | Notes |
| --- | --- | --- |
| Search ComicVine… | Desktop only | cbzLite makes no network requests |
| Guess from Filename | Same | Folder-name fallback only for books opened with Open Folder |
| Auto Page Count | Same | |
| Extract Publisher/Year from Summary | Same | |
| Copy ComicInfo.xml | Same | |
| Paste ComicInfo.xml | Same | Falls back to a paste box where the browser blocks clipboard reads |
| Find & Replace… | Same | |
| Validate All Open Files… | Same | |
| Combine Archives… | Desktop only | Left to the desktop as one of the heavy jobs |
| Trim Branding Footers… | Desktop only | Re-encodes page images |
| Customize Toolbar… | Same | Desktop-only buttons can be added, and show dimmed |
| Settings… | Same, minus desktop-only settings | Those are listed in the dialog with the reason |

## Help menu

| Desktop | cbzLite | Notes |
| --- | --- | --- |
| Check for Updates… | Different | The website copy is always current; a saved copy points you back to it |
| About cbzLab | About cbzLite | Same layout and fixed palette, cbzLite branding |
| (none) | Why Is cbzLite Limited? | cbzLite only |

## Editor and file list

| Desktop | cbzLite | Notes |
| --- | --- | --- |
| Five editor tabs, field search, show all/extra filters | Same | |
| Composite Publication Date, Issue #/Count/Volume rows | Same | Date shown in the browser's own date format |
| Live validation (as you type / on leaving / off) | Same | |
| Save-time validation dialog | Same | |
| Batch editing, mixed values, value pickers | Same | |
| Recently typed values picker | Not included | The desktop never records any values, so its picker never appears either |
| Revert to Saved, Cut/Copy/Paste field menu | Same | Shift+right-click still gives the browser's menu |
| Grid view, sort, resize, Edit selection | Same | Columns can't be dragged into a new order |
| File list filter, sort, multi-select, Delete, row ✕ | Same | |
| File list menu: Open Containing Folder | Desktop only | A page can't open folders |
| Drag and drop to open | Same | |
| Unreadable ComicInfo.xml warning | Same | |
| Autosave and crash recovery | Not included | The browser warns before leaving with unsaved changes |
| Settings backup export/import | Not included | Preferences live in the browser's storage |
| Window position and size remembered | Not applicable | |

## Shared behaviour checked by tests

`test/parity/` holds cases run against both editions - by `ParityFixtureTests` in `cbzLab.Tests` and
by `node --test cbzLite/test/` - for validation messages, date parsing and display, filename guessing,
and summary header detection.
