# cbzLab

A native Windows metadata editor for comic book archives. cbzLab opens CBZ and CBR
files, lets you view and edit the `ComicInfo.xml` metadata inside them (single files
or whole batches at once), and writes changes back safely without ever touching the
page images.

Built with C# / .NET 8 and WinUI 3 (Windows App SDK 1.6), deployed as an unpackaged
self-contained executable — no MSIX, no installer, no store.

## Documentation

- **[Building from source](docs/BUILDING.md)** — environment setup, publishing a
  distributable executable, and the project layout.
- **[User guide](docs/USER_GUIDE.md)** — opening files, the editor, batch editing,
  grid view, saving, ComicVine lookup, settings and themes.
- **[Changelog](CHANGELOG.md)** — release history.

## Quick start

Grab a published build, or see [Building from source](docs/BUILDING.md):

```powershell
dotnet publish cbzLab\cbzLab.csproj -c Release -r win-x64 --self-contained true
```

Then run `cbzLab.exe` — nothing needs installing.
