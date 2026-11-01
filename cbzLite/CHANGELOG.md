# cbzLite changelog

cbzLite is the browser edition of cbzLab. It has its own version line, separate from the
desktop app's.

## 0.5.1

- Fixed: on phones and other narrow screens, the file list and editor header took most of the
  screen, leaving little room for the fields being edited. The file list now takes only the space
  its files need (at most a third of the screen, scrolling beyond that), the editor header that
  repeated the selected file is hidden, the tabs stay on one line (scrolling sideways if they must), and the page fits the visible screen instead of running
  under the browser's toolbars.

## 0.5.0

Finishing touches: its own logo, an About box, and clear explanations of what needs the desktop app.

- **A cbzLite logo**, drawn from the same design as cbzLab's so the two match, plus a favicon.
- **Help menu** (Alt+H): Check for Updates, Why Is cbzLite Limited?, and About cbzLite. The About box
  uses the desktop app's fixed synthwave look whatever theme is active.
- **Why Is cbzLite Limited?** explains, in one place, everything that needs the desktop app and why:
  saving in place outside Chrome and Edge, CBR files, Open Recent, ComicVine, Combine Archives, Trim
  Branding Footers, settings backup and crash recovery.
- **Desktop-only features stay visible.** Open Recent, Search ComicVine, Combine Archives, Trim Branding
  Footers and Open Containing Folder appear dimmed where the desktop app has them, and say why they
  need the desktop app when clicked, with a link to it. Desktop-only toolbar buttons can be added
  through Customize Toolbar and behave the same way.
- **Check for Updates** explains that the website copy is always the latest, or, for a copy saved to
  open offline, where to get a newer one.
- cbzLite is now published at https://fexofenadine.github.io/cbzLab/lite/, and linked from the cbzLab
  website and README.
- Fixed: long menu items no longer wrap onto two lines.

## 0.4.0

The Tools menu and the rest of the File menu, so everyday edits no longer need the desktop app.

- **File → Open Folder…** (Ctrl+Shift+O, and on the toolbar) opens every archive in a folder and
  its subfolders, in natural order, asking first above 100 files. In Chrome and Edge the browser
  asks once for permission to edit the folder, and every book in it can then be saved in place.
  Other browsers open the folder's books read-only and save copies as downloads.
- **Revert, Remove from List, Close, Close All and Reopen Last Closed File** in the File menu, a
  ✕ on each file in the list, and Delete in the file list. Anything with unsaved changes is
  listed and confirmed first. Browsers keep Ctrl+W and Ctrl+Shift+T for their own tabs, so Close
  and Reopen have no shortcut here. Reopen works for the last ten files closed this session.
- **Right-click in the file list** for Save, Revert, Remove from List and Copy Fields to Rest of
  Selection.
- **Tools → Guess from Filename**: fills empty Series, Issue #, Volume, Year and Count from each
  file's name, falling back to its folder's name for files called things like issue_01.cbz.
  Folder names are only known for books opened with Open Folder.
- **Tools → Auto Page Count** and **Extract Publisher/Year from Summary**.
- **Tools → Copy ComicInfo.xml / Paste ComicInfo.xml**. If the browser won't let the page read the
  clipboard, a box opens to paste the XML into instead.
- **Tools → Find & Replace…** and **Validate All Open Files…**.
- The toolbar's Open Folder, Remove, Revert, Copy XML, Paste XML, Guess from Filename and Extract
  Pub/Year buttons now work, so the default toolbar is now the desktop app's default toolbar.
- Hovering a file in the list shows its folder as well as its name.
- Dropping files that aren't comic archives now says so in the status bar.

## 0.3.0

Grid view, themes, a customisable toolbar and a Settings dialog, all matching the desktop app.

- **Grid view** (the Grid View toolbar button): every open file as a row in a table, with Size,
  Modified and your chosen field columns. Click a header to sort, click again to reverse, and drag
  a header's edge to resize. Click, Ctrl+click and Shift+click select rows; the arrow keys, Home,
  End and Ctrl+A work too. Double-click a row, or press Enter, to edit that book. Right-click for
  "Edit This Book" (or "Edit N Books in Batch Editor") and Choose Columns. The grid starts out with
  the editor's selection.
- **View → Choose Grid Columns…**, with the fields listed in the same order as the desktop app.
- **View → Theme**: all 18 of the desktop app's themes, remembered between visits.
- **Tools → Customize Toolbar…**: pick and reorder the toolbar buttons, or reset them. Buttons
  cbzLite doesn't have yet are listed and marked, and so are the desktop-only ones, so a toolbar set
  up here carries its full layout across.
- **Tools → Settings…**: theme, editor font and size, cover image source, field width, remembering
  the last tab, compact spacing, the show-fields defaults, auto page count, selecting the first file
  after opening, clearing the file filter on open, and when live validation runs. Reset to Defaults
  asks first. A "Desktop app only" section lists the desktop settings that can't work in a browser,
  and why.
- Alt+T opens the Tools menu.

## 0.2.0

The editor catches up with the desktop app: batch editing, validation, and the combined
publication date.

- **Batch editing.** Ctrl+click and Shift+click in the file list select several files, and Ctrl+A
  (with the list focused) selects them all; the arrow keys, Home and End move through it. With
  more than one file selected, an edit applies to all of them. Fields whose values differ across
  the selection say so, and a ▾ picker next to each field lists the values in use with how many
  files have each, so one can be applied to every file at once. Dropdown fields become a button
  with the same picker, which also lists the schema's own options. A Batch scope panel at the foot
  of the sidebar names the selected files, as on the desktop.
- **Validation.** Number fields (Page Count, Year, Month, Day, Count, Volume, Alternate Count,
  Community Rating) are checked as you type, with the problem and a suggested fix under the field.
  Saving checks every file being saved and lists anything wrong; Fix goes back to the editor, Save
  Anyway writes the values as they are. The messages are the desktop app's own, word for word.
- **Publication Date** is now one field again, as on the desktop: type a full date, a month and
  year, or just a year, and Year, Month and Day are filled in to match. A month and year never
  gets a made-up day, and a half-typed date changes nothing until it reads as a date. The date is
  shown in your browser's date format.
- **Revert to Saved** for a single field, from the field's right-click menu, which also has Cut,
  Copy and Paste. Shift+right-click still gives the browser's own menu.
- **Save All** (Ctrl+Alt+S, the File menu, and a toolbar button with an unsaved-files count, as
  on the desktop). Save with several files selected saves the ones with changes.

## 0.1.0

First version: open, edit and save CBZ files entirely in the browser.

- Opens CBZ/ZIP archives from File → Open…, the toolbar, Ctrl+O, or by dropping files on the
  page. Several files can be opened at once.
- Same layout as the desktop app: menu bar, toolbar, sidebar with cover thumbnails, filter and
  sort, editor header, field search, the five editor tabs, and the status bar. Uses the desktop
  app's own `schema.json` and default theme, so fields, labels, tabs and colours match.
- Issue #, Count and Volume share one row, as do Alternate Number and Alternate Count.
- View → Show All Fields / Show Extra Fields, and their toolbar toggles.
- Fields found in a file that aren't in the schema become editable Extras fields, remembered in
  this browser.
- Page Count is filled in on open when it's empty, without marking the file as edited.
- Save (Ctrl+S) and Save As… (Ctrl+Shift+S). In Chrome and Edge, a file opened through the picker
  or dropped onto the page is saved in place. Other browsers can't write back to the original,
  so the edited archive is downloaded as a new copy, and a one-time note explains why and points
  to the desktop app.
- Saving copies every page through exactly as stored (never decompressed or re-encoded) and
  replaces only `ComicInfo.xml`, written in the same format the desktop app writes. Large
  archives, including zip64 ones, are saved without being loaded into memory.
- CBR files get a clear explanation (browsers can't read RAR) with a link to the desktop app,
  rather than a generic error.
- A warning before leaving the page with unsaved edits.
- Works offline as a single self-contained HTML file. Nothing is uploaded anywhere.

Every desktop feature that can work in a browser is now in cbzLite - see PARITY.md for the full list.
