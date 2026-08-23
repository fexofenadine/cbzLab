<img src="cbzLab/Assets/logo.png" width="120" alt="cbzLab logo">

# cbzLab

[![build](https://github.com/fexofenadine/cbzLab/actions/workflows/build.yml/badge.svg)](https://github.com/fexofenadine/cbzLab/actions/workflows/build.yml)

A metadata editor for comic book archives. cbzLab opens CBZ and CBR files, lets you
view and edit the `ComicInfo.xml` metadata inside them (single files or whole
batches at once), and writes changes back safely without ever touching the page
images.

Built with C# / .NET 8 and **Avalonia UI**, cross-platform (Windows + Linux + macOS),
deployed as an unpackaged self-contained executable — no installer, no store.

> **Note:** an earlier WinUI 3 / Windows-only version of cbzLab lives under
> `cbzLab.winui3/` and is now archived — see
> [`cbzLab.winui3/ARCHIVED.md`](cbzLab.winui3/ARCHIVED.md). All active development
> is in `cbzLab/` (the Avalonia rewrite — it took over the plain `cbzLab` name once
> it reached parity and the WinUI original moved aside).

## Documentation

- **[Building from source](docs/BUILDING.md)** — environment setup, publishing a
  distributable executable, and the project layout. (Written for the archived
  WinUI version — the build commands below are current for the Avalonia `cbzLab/`.)
- **[User guide](docs/USER_GUIDE.md)** — opening files, the editor, batch editing,
  grid view, saving, ComicVine lookup, settings and themes. The workflow is the
  same in the Avalonia version; menu/dialog wording matches unless noted.
- **[Changelog](CHANGELOG.md)** — release history for the archived WinUI version.

## Quick start

Grab a [published release](../../releases), or build from source:

```powershell
# Windows
dotnet publish cbzLab\cbzLab.csproj -c Release -r win-x64 --self-contained true

# Linux
dotnet publish cbzLab/cbzLab.csproj -c Release -r linux-x64 --self-contained true
```

Then run the published `cbzLab` executable — nothing needs installing.
