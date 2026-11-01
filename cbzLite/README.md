# cbzLite

The browser edition of cbzLab, for quick ComicInfo.xml edits without installing anything. It
looks and works like the desktop app, minus the things a browser can't do: reading or writing
CBR, ComicVine lookups, Combine Archives, Trim Branding Footers, and saving in place outside
Chrome/Edge. For those, use the [cbzLab desktop app](https://github.com/fexofenadine/cbzLab/releases).

Everything runs locally in the page. There are no libraries, no CDN requests and no network
calls, and your comics never leave your machine.

Live at https://fexofenadine.github.io/cbzLab/lite/. [PARITY.md](PARITY.md) lists every desktop
feature and where it stands here.

## Building

```
py site/build_site.py
```

This writes `_site/lite/index.html`: one self-contained file with the stylesheet, scripts, and
the desktop app's `cbzLab/Assets/schema.json` and themes inlined. It can be hosted, or saved and
opened locally.

For development without building, serve the repo root over http (`py -m http.server`) and open
`/cbzLite/`. The page then loads the schema and themes straight from `cbzLab/Assets/`.

## Layout

- `index.html`, `styles.css` - the page, mirroring `cbzLab/MainWindow.axaml`
- `js/zip.js` - zip reading and re-writing (page entries copied verbatim)
- `js/comicinfo.js` - port of `ComicInfoXml.cs`
- `js/schema.js` - port of `SchemaService.cs`
- `js/validation.js` - port of `ValidationService.cs`
- `js/datefield.js` - port of `DateFieldHelper.cs`
- `js/filenameguess.js` - port of `FilenameGuessService.cs`
- `js/summaryheader.js` - port of `SummaryHeaderParser.cs`
- `js/archive.js` - port of the cbz parts of `ArchiveService.cs`
- `js/model.js` - port of `ComicFileViewModel.cs`
- `js/app.js` - the ui, standing in for `MainWindow` and `MainViewModel`
- `test/parity/` - json cases run against both editions, so the ports can't drift from the
  originals: `node --test cbzLite/test/` here, and `ParityFixtureTests` in `cbzLab.Tests`
