# Session Handoff

**Last session:** 2026-05-09
**Status:** Initial scaffold pushed to private GitHub repo. Builds clean, launches clean.

## What exists

A WinForms / DevExpress companion app for Claude Code that watches a folder
for HTML / Markdown artifacts and renders them in tabbed WebView2 panes.

### Files in this commit (`0ab7798`) and the README/TODO commit

```
ClaudeViewer/
├── ClaudeViewer.sln
├── README.md
├── TODO.md
├── SESSION_HANDOFF.md
├── .gitignore                       (bin/, obj/, *.user, .vs/, *.suo, *.bak)
└── ClaudeViewer/
    ├── ClaudeViewer.csproj          (net10.0-windows, DevExpress.Win 25.2.5,
    │                                 Microsoft.Web.WebView2 1.0.2792.45,
    │                                 Markdig 0.37.0)
    ├── app.manifest                 (asInvoker; HighDpi handled via csproj)
    ├── Program.cs                   (skin: WXICompact)
    ├── MainForm.cs                  (DockManager left + DocumentManager
    │                                 tabbed view + GridControl bound to
    │                                 ArtifactWatcher.Artifacts)
    ├── Models/Artifact.cs
    ├── Services/
    │   ├── ArtifactWatcher.cs       (FileSystemWatcher → BindingList,
    │   │                             title sniffing, retry on lock)
    │   ├── MarkdownRenderer.cs      (Markdig + broadsheet CSS)
    │   └── Settings.cs              (%LOCALAPPDATA%\ClaudeViewer\settings.json)
    └── Controls/ArtifactForm.cs     (XtraForm hosting WebView2; MDI child)
```

### Behaviour wired

- Default watch folder: `C:\Projects\.artifacts` (created on first run).
- `CLAUDE_VIEWER_DIR` env var overrides the folder and **disables** the
  picker (with tooltip explaining why) so a launcher override can't be
  silently overwritten by a click.
- `Change…` button → stock `FolderBrowserDialog` (modern Windows shell
  picker on .NET 6+; DevExpress doesn't ship a folder picker).
- File overwrite while a tab is open → tab auto-reloads via the
  `ArtifactUpdated` event.
- Reads use `FileShare.ReadWrite | FileShare.Delete` + retry, so opening a
  file Claude Code is mid-writing doesn't crash.

## Verification

- `dotnet build` → 0 warnings, 0 errors.
- Smoke-tested startup: PID stays alive past 3 s and no crash dialog.
- **Not yet tested:** picking a new folder at runtime, switching watcher,
  live-reload of an in-flight Claude Code write, the broadsheet Markdown
  CSS rendering on real content.

## Decisions made (and why)

- **DevExpress.Win meta-package** instead of thin slices. Trades a heavier
  reference graph for one line in the csproj; easy to thin-slice later.
  See comment in `ClaudeViewer.csproj`.
- **`BonusSkins.Register()` removed.** That helper lives in the separate
  `DevExpress.BonusSkins` assembly which the meta package doesn't pull.
  Default `WXICompact` skin loads fine without it.
- **High-DPI via `<ApplicationHighDpiMode>` csproj property** instead of
  manifest entries — manifest-based DPI conflicts with the modern
  `Application.SetHighDpiMode` path WinForms uses on .NET 6+.
- **Stock `FolderBrowserDialog`** instead of a hypothetical
  `XtraFolderBrowserDialog` (which doesn't exist; DevExpress only ships
  `XtraOpenFileDialog` / `XtraSaveFileDialog`).

## Next session — pick up at

1. Implement **compare mode** (top entry in `TODO.md`). The grid already
   supports multi-select; flip `OptionsSelection.MultiSelect = true`, add
   a "Compare" button to the header strip alongside "Change…", and on
   click open a new tab whose root is a `SplitContainer` with two
   `ArtifactForm`-equivalent panels side by side.
2. Or add the **filter / search box** — smaller change, immediate payoff.
3. Once one of those is in, exercise the **live-reload path** end-to-end
   by pointing Claude Code at the watched folder and asking it to
   regenerate `artifact-demo.html` (lives at `C:\Projects\artifact-demo.html`
   from the prior conversation — could be moved into `.artifacts\` to use
   as a fixture).

## Known caveats

- DevExpress version `25.2.5` is pinned; bump if the licensed install on
  this machine differs. NuGet restore will fail loud if it's wrong.
- The Markdown CSS references Fraunces / Newsreader / JetBrains Mono via
  fallback chains. WebView2 has no internet here, so it'll fall back to
  Georgia / Consolas — fine, but the HTML artifact at
  `C:\Projects\artifact-demo.html` does load Google Fonts so it'll look
  different from inline-rendered Markdown.

## Repo

`https://github.com/MBrekhof/ClaudeViewer` (private, `main`).
