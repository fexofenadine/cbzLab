<img src="../cbzLab/Assets/logo.png" width="120" alt="cbzLab logo">

# Building cbzLab from source

cbzLab is a plain SDK-style .NET 10 project - no platform-specific workload,
no Windows-only tooling. If you have the .NET 10 SDK, you can build and run it.

## Prerequisites

Just the **.NET 10 SDK** - <https://dotnet.microsoft.com/download/dotnet/10.0>.
Confirm it's installed with:

```powershell
dotnet --list-sdks
```

You want a `10.0.x` entry. Any editor works: Visual Studio 2026, VS Code (with
the C# Dev Kit extension), JetBrains Rider, or a plain text editor plus the
`dotnet` CLI - none of it is required, just convenient.

## Building and running

```powershell
git clone https://github.com/fexofenadine/cbzLab.git
cd cbzLab
dotnet build cbzLab.sln
dotnet run --project cbzLab/cbzLab.csproj
```

NuGet packages restore automatically on first build.

## Running the tests

```powershell
dotnet test cbzLab.Tests/cbzLab.Tests.csproj
```

29 xUnit tests cover the pure/injectable logic - `DateFieldHelper`,
`ComicInfoXml`, `JsonFileStore`, `AutosaveService`. `SettingsService`,
`SchemaService`, and `ValidationService` aren't tested directly: all three
resolve to the real, shared `%APPDATA%\cbzLab` directory with no injectable
override today, so constructing them for real in a test would risk touching
your actual settings/schema/logs.

## Building a distributable executable

Debug builds (`dotnet build`) are fine for development. For a self-contained,
single-file build you can copy to another machine, publish for the target
platform:

```powershell
# Windows
dotnet publish cbzLab/cbzLab.csproj -c Release -r win-x64 --self-contained true

# Linux
dotnet publish cbzLab/cbzLab.csproj -c Release -r linux-x64 --self-contained true
```

These are exactly the commands the project's own release workflow
(`.github/workflows/release.yml`) runs for each platform, so a passing publish
here means the same thing a real release build does.

Without an explicit `-o`, the output lands in
`cbzLab/bin/Release/net10.0/<rid>/publish/`. Managed dependencies (including
Avalonia's Skia/HarfBuzz native libraries) are collapsed into the single
`cbzLab`/`cbzLab.exe` via `PublishSingleFile` - nothing else needs installing
on the target machine. On Linux, mark the output executable before running it:

```bash
chmod +x cbzLab/bin/Release/net10.0/linux-x64/publish/cbzLab
```

To land the exe somewhere more convenient, add `-o <path>`, e.g.
`-o publish/win-x64`.

**No macOS build.** Avalonia itself targets macOS fine (`-r osx-x64`/
`-r osx-arm64` both work if you want to build it yourself), but the project
doesn't publish or ship one - nobody working on it has Apple hardware to
verify an unsigned, unnotarized build actually runs, and shipping one nobody
can test isn't worth the CI time.

## Dependencies

| Package | Version | Used for |
|---|---|---|
| `Avalonia` / `Avalonia.Desktop` / `Avalonia.Themes.Fluent` | 12.1.4 | the UI framework itself, desktop windowing, and the Fluent theme (default light/dark chrome - cbzLab's own themes layer on top via `ThemeService`) |
| `Avalonia.Controls.DataGrid` | 12.1.2 | grid view's table control - needs its own `StyleInclude` in `App.axaml`, since `FluentTheme` alone doesn't style it |
| `SharpCompress` | 0.50.7 | reading `.cbr`/RAR archives (`ArchiveService`, in `cbzLab.Core`) |
| `SkiaSharp` | 3.119.4 | cropping and re-encoding pages for Trim Branding Footers (`FooterTrimService`, in `cbzLab.Core`) - pinned to the version Avalonia's own renderer uses; never bump it on its own |

`Avalonia.Diagnostics` (the F12 dev-time inspector) is deliberately not
referenced - as of this writing it has no 12.x release compatible with the
12.1.1 core packages.

## Building the Android app

The Android app (`cbzLab.Android/`) isn't part of `cbzLab.sln`, so the desktop build and its CI
never need the Android tools. To build it you need, in addition to the .NET 10 SDK:

- the .NET Android workload: `dotnet workload install android`
- a JDK between 17 and 21 (Android Studio's own bundled JDK is too new)
- the Android SDK with platform 36 and the matching build tools; `dotnet build
  cbzLab.Android/cbzLab.Android.csproj -t:InstallAndroidDependencies -f net10.0-android
  -p:AndroidSdkDirectory=<sdk> -p:JavaSdkDirectory=<jdk> -p:AcceptAndroidSDKLicenses=True`
  installs them

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"; $jdk = "<path to a JDK 17>"
# debug build for an emulator or a phone
dotnet build cbzLab.Android/cbzLab.Android.csproj -c Debug -p:AndroidSdkDirectory=$sdk -p:JavaSdkDirectory=$jdk
adb install -r cbzLab.Android/bin/Debug/net10.0-android/io.github.fexofenadine.cbzlab-Signed.apk
```

A release build adds the signing key (never kept in the repository; the release workflow reads
it from repository secrets) and produces one APK for arm64, arm and x64:

```powershell
$env:KS_PASS = "<store password>"
dotnet publish cbzLab.Android/cbzLab.Android.csproj -c Release -p:AndroidSdkDirectory=$sdk -p:JavaSdkDirectory=$jdk `
  -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=<keystore> -p:AndroidSigningKeyAlias=cbzlab `
  -p:AndroidSigningStorePass=env:KS_PASS -p:AndroidSigningKeyPass=env:KS_PASS -o publish/android
```

The app's version is read from `cbzLab/cbzLab.csproj`, so one version bump numbers both apps.
Regenerate the adaptive icon with `py branding/apply_logos.py` if the badge artwork changes.

## Project layout

```
cbzLab/                    the desktop app
  cbzLab.csproj          project config: net10.0, WinExe, self-contained + single-file on Release
  Program.cs              entry point, AppBuilder setup
  App.axaml / .cs         application start-up; includes cbzLab.UI's shared styles
  MainWindow.axaml / .cs  the desktop window around MainView: its size and position, icon, title,
                           closing, and the self-updater
  Assets/                 icon, logo, bundled schema.json/themes.json (seeded to %APPDATA% on first run)
  Assets/themes/          custom theme JSON files bundled with the app (Synthwave Dark, etc.)
  Services/UpdateService.cs  the desktop-only self-updater
cbzLab.Android/             the Android app: activity, document storage (content uris), asset unpacking,
                           the back button and keyboard handling, around the same MainView
cbzLab.UI/                  everything the apps show, shared with the Android app
  MainView.axaml / .cs    the whole main UI: file list, editor, grid view, menu, toolbar. Never assumes
                           a window exists - window-only jobs are left to its host
  AppStyles.axaml         the Fluent theme and DataGrid styles every app includes
  Dialogs/                 one dialog per .axaml + .cs pair; DialogHost shows a DialogBase dialog
                           inside the window, over a dimmed backdrop
  Converters/              FieldTemplateSelector (widget dispatch) and FieldValueConverter (grid cell values)
cbzLab.Core/                the code shared with the Android app (see docs/ANDROID_PLAN.md);
                           namespaces are unchanged, still cbzLab.Services etc.
  Services/               settings, schema, themes, xml, archives, validation, ComicVine, autosave
  ViewModels/              main/file/field view models - data flow, dirty tracking, composite fields
  Models/                  settings, schema, and ComicVine data classes
cbzLab.Tests/              xUnit tests for the pure/injectable services and helpers
```

A few things worth knowing before changing this code:

- Archive writes always go temp-file-then-atomic-replace (`ArchiveService`).
  Don't "optimise" that away.
- `ThemeService` mutates a fixed set of shared `SolidColorBrush` instances
  registered into `Application.Resources` as `Th*` keys. Any
  `DynamicResource`-bound control repaints automatically when a brush's
  `.Color` changes - no dictionary replace needed. Adding a themed control
  usually just means binding to an existing `Th*` brush.
- `ComicInfoXml.Build` layers edits on top of the original raw XML bytes so
  complex elements (`<Pages>`) survive untouched. Parsing and writing are
  DTD-disabled - archive contents are untrusted input.
- The five editor tabs are filters over one shared field list. Tab assignment
  is `SchemaService`'s tab map; unknown fields land on Extras.
- `FieldTemplateSelector` dispatches in a specific order - `MonthCompanion is
  not null` (date fields) → `RowCompanions.Count > 0` (numeric row-sharing,
  e.g. Issue #/Count/Volume) → the normal widget-type switch (entry/text/
  combo). Follow this same order for any new composite field, or it'll fall
  through to the wrong template.

## Building the archived WinUI 3 version

The original Windows-only WinUI 3 version is no longer developed, but its
source is kept at `cbzLab.winui3/` for history - see
[`cbzLab.winui3/ARCHIVED.md`](../cbzLab.winui3/ARCHIVED.md). It needs Visual
Studio 2022 with the WinUI application development workload (or .NET desktop
development + the Windows App SDK C# templates and a Windows 11 SDK), and is
no longer part of `cbzLab.sln` - open `cbzLab.winui3/cbzLab.csproj` directly.
Publish with:

```powershell
dotnet publish cbzLab.winui3/cbzLab.csproj -c Release -r win-x64 --self-contained true
```

Output lands in
`cbzLab.winui3/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/publish/`,
alongside a handful of native Windows App SDK files
(`Microsoft.ui.xaml.dll`, `DWriteCore.dll`, the WindowsAppRuntime bootstrapper,
`resources.pri`) that the OS loads directly and can't be folded into the exe -
true of any unpackaged WinUI 3 app, not specific to this project.
