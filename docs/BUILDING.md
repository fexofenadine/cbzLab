# Building cbzLab from source

## Environment setup

You need two things: the **.NET 8 SDK** and **Visual Studio 2022** with the right
workload. Total download is a few GB; allow half an hour on a fresh machine.

### Install Visual Studio 2022

1. Download **Visual Studio 2022 Community** (free) from
   <https://visualstudio.microsoft.com/downloads/>. Version 17.10 or later.
2. Run the installer. On the **Workloads** tab, tick:
   - **WinUI application development** — this is the one that matters. It pulls in
     the Windows App SDK templates, XAML tooling and the Windows SDK.
   - If you don't see that workload (older installer versions), instead tick
     **.NET desktop development**, then switch to the **Individual components** tab
     and add **Windows App SDK C# Templates** and a **Windows 11 SDK** (10.0.22621
     or newer).
3. Let it install. The .NET 8 SDK comes bundled with current VS 2022 releases —
   confirm afterwards by opening a terminal and running `dotnet --list-sdks`; you
   want an `8.0.x` entry. If it's missing, grab it from
   <https://dotnet.microsoft.com/download/dotnet/8.0>.

### First-time Visual Studio orientation

If Visual Studio itself is new to you:

- **Solution Explorer** (right-hand panel) is the file tree. The `.sln` is the
  workspace; the `.csproj` is the project (roughly a `pyproject.toml` plus build
  config in one).
- The toolbar dropdowns near the Run button select **configuration** (Debug/Release),
  **platform** (x64/ARM64) and the **launch profile**.
- **F5** = build and run with debugger. **Ctrl+F5** = run without debugger.
  **Ctrl+Shift+B** = just build.
- NuGet packages (the .NET equivalent of pip packages) restore automatically on
  first build. If anything looks unresolved, right-click the solution →
  **Restore NuGet Packages**.

### Opening the project

1. Unzip the source anywhere sensible (avoid deeply nested paths).
2. Double-click `cbzLab.sln`, or in VS use **File → Open → Project/Solution**.
3. In the toolbar, set platform to **x64** (or **ARM64** on an ARM machine) and the
   launch profile to **cbzLab (Unpackaged)**.
4. Press **F5**. First build takes a while (NuGet restore + XAML compile); after
   that it's quick.

## Building a distributable executable

Debug builds run from `bin\x64\Debug\...` and are fine for development. For a build
you can copy to another machine, use the command line rather than Visual Studio's
Publish dialog (overkill for an unpackaged app):

```powershell
dotnet publish cbzLab\cbzLab.csproj -c Release -r win-x64 --self-contained true
```

For ARM64:

```powershell
dotnet publish cbzLab\cbzLab.csproj -c Release -r win-arm64 --self-contained true
```

The output lands in `cbzLab\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish\`
(adjust for platform). Managed dependencies are collapsed into `cbzLab.exe` via
`PublishSingleFile`. A handful of native Windows App SDK files
(`Microsoft.ui.xaml.dll`, `DWriteCore.dll`, the WindowsAppRuntime bootstrapper,
`resources.pri`) still sit alongside it — the OS loads these directly rather than
through .NET, so they can't be folded into the exe. This is true of any unpackaged
WinUI 3 app, not specific to this project. Copy the whole folder wherever you like
and run `cbzLab.exe`; nothing needs installing on the target machine.

## Project layout

```
cbzLab.sln
cbzLab/
  cbzLab.csproj            project config: unpackaged WinUI 3, self-contained
  app.manifest             dpi awareness
  App.xaml / App.xaml.cs   service wiring, theme bootstrap, cli handling
  MainWindow.xaml / .cs    full ui + orchestration of i/o and dialogs
  Assets/                  icons, bundled schema.json, themes (seeded to %APPDATA%)
  Models/                  schema, settings and validation data classes
  Services/                settings, schema, themes, xml, archives, validation
  ViewModels/              main/file/field view models (data flow, dirty tracking)
  Dialogs/                 settings, multi-save, progress, validation, about
  Converters/              xaml value converters and the field template selector
```

A few things worth knowing before changing this code:

- Archive writes always go temp-file-then-atomic-replace. Don't "optimise" that away.
- The theme system mutates a fixed set of `SolidColorBrush` instances that both the
  app's own styles and a set of overridden system control resources point at. Adding
  a themed control usually just means binding to an existing `Th*` brush.
- `ComicInfoXml.Build` layers edits on top of the original raw XML bytes so complex
  elements (`<Pages>`) survive untouched. Parsing and writing are DTD-disabled —
  archive contents are untrusted input.
- The five editor tabs are filters over one shared field list. Tab assignment is
  the `TabMap` in `SchemaService`; unknown fields land on Extras.
