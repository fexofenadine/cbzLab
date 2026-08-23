# Archived — this WinUI 3 version of cbzLab is no longer maintained

As of 2026-08-23, active development has moved entirely to the Avalonia UI
rewrite (now living at the repo root as **`cbzLab/`**, formerly
`cbzLab.Avalonia/`), which reached feature parity with this WinUI 3 original,
is cross-platform (Windows + Linux + macOS), and is now versioned `2.0.0` with
the beta tag dropped. This folder was renamed from `cbzLab/` to
`cbzLab.winui3/` once the Avalonia project took over the plain `cbzLab/` name.

This folder is kept for historical reference only:

- It's no longer part of `cbzLab.sln`'s active build (removed via `dotnet sln remove`).
- No further fixes or features will land here.
- Its own internal project references (paths, namespaces, etc.) were left
  exactly as they were before the rename — this folder was moved, not edited.
- Full commit history and the WinUI-era changelog remain in place — see
  `CHANGELOG.md` at the repo root for that history, and `CLAUDE.md` for the
  Avalonia port's own development log.

If you're looking to build or run cbzLab today, use the repo root `cbzLab/`
instead — see the repo root `README.md`.
