# Session Handoff

**Last session:** 2026-05-09
**Status:** Scaffold + folder picker + compare mode pushed to `MBrekhof/ClaudeViewer` (private). Builds clean, launches clean.
**Repo:** https://github.com/MBrekhof/ClaudeViewer · `main` · last commit `015eede`

## What this is

A WinForms / DevExpress companion app for Claude Code. Watches a folder
for HTML / Markdown artifacts and renders them in tabbed WebView2 panes,
with a side-by-side compare mode for two files at once.

## Current file layout

```
ClaudeViewer/
├── ClaudeViewer.sln
├── README.md · TODO.md · SESSION_HANDOFF.md
├── .gitignore                       (bin/, obj/, *.user, .vs/, *.suo, *.bak)
└── ClaudeViewer/
    ├── ClaudeViewer.csproj          (net10.0-windows; DevExpress.Win 25.2.5;
    │                                 Microsoft.Web.WebView2 1.0.2792.45;
    │                                 Markdig 0.37.0; ApplicationHighDpiMode = PerMonitorV2)
    ├── app.manifest                 (asInvoker; DPI handled via csproj, not manifest)
    ├── Program.cs                   (skin: WXICompact)
    ├── MainForm.cs                  (DockManager left + DocumentManager tabbed view +
    │                                 GridControl bound to ArtifactWatcher.Artifacts;
    │                                 header with Compare + Change… buttons)
    ├── Models/Artifact.cs
    ├── Services/
    │   ├── ArtifactWatcher.cs       (FileSystemWatcher → BindingList; title sniffing;
    │   │                             retry on lock; raises ArtifactUpdated event)
    │   ├── MarkdownRenderer.cs      (Markdig + broadsheet CSS theme)
    │   └── Settings.cs              (%LOCALAPPDATA%\ClaudeViewer\settings.json)
    └── Controls/
        ├── ArtifactPanel.cs         (UserControl: WebView2 + Markdown rendering — the reusable core)
        ├── ArtifactForm.cs          (XtraForm shell hosting one ArtifactPanel; MDI child)
        └── CompareForm.cs           (XtraForm with stock SplitContainer Orientation.Vertical;
                                      two ArtifactPanels with file-name labels above each pane)
```

## Behaviour wired

- Default watch folder: `C:\Projects\.artifacts` (created on first run).
- `CLAUDE_VIEWER_DIR` env var overrides the folder and **disables** the
  picker (with tooltip explaining why), so a launcher override can't be
  silently overwritten by a click.
- `Change…` button → stock `FolderBrowserDialog` (modern shell picker on
  .NET 6+; DevExpress doesn't ship a folder picker — only file pickers).
- Folder choice persists to `%LOCALAPPDATA%\ClaudeViewer\settings.json`.
- Grid: `MultiSelect = true`, `MultiSelectMode = RowSelect`. `Compare`
  button is disabled until exactly two rows are selected; tooltip explains
  the gating in either state.
- Compare → opens a new MDI tab with a vertical-splitter `SplitContainer`
  (`Orientation.Vertical`) → panels left/right; one `ArtifactPanel` per
  side with a small file-name/title label header; splitter centres at
  half-width on first show.
- Live-reload: `ArtifactWatcher.ArtifactUpdated` fires on any tracked
  file change; both single-file tabs (`_openTabs`) and compare tabs
  (`_openCompareTabs`) refresh the matching side(s).
- File reads use `FileShare.ReadWrite | FileShare.Delete` + 4-attempt
  retry, so opening a file Claude Code is mid-writing doesn't crash.

## Verification status

| Confirmed | How |
|---|---|
| `dotnet build` is clean (0 warnings, 0 errors) | last run after each file change |
| App launches and stays alive ≥ 3 s | `Start-Process` smoke test |

| **Not** yet confirmed visually | Why it matters |
|---|---|
| Split layout actually renders left/right (not top/bottom) | Code says `Orientation.Vertical`. Flip to `Horizontal` if it comes up wrong (one line in `Controls/CompareForm.cs:11`). |
| Compare button enables on multi-select selection change | Wired to `GridView.SelectionChanged`; relies on event firing as expected. |
| Live-reload through the compare path end-to-end | Needs Claude Code pointed at the watched folder while a compare tab is open. |
| Folder picker switches the watcher cleanly | Disposes/recreates the watcher; should work but unverified at runtime. |
| Markdown CSS rendering on real content | The broadsheet theme falls back to Georgia/Consolas if the Google Fonts aren't available — fine but unverified. |

## Decisions worth remembering

- **`DevExpress.Win` meta-package** instead of thin slices. Heavier graph
  but one line in csproj. Easy to thin-slice later if the build feels slow.
- **`BonusSkins.Register()` removed.** Lives in a separate
  `DevExpress.BonusSkins` assembly the meta package doesn't pull. Default
  `WXICompact` is fine without it.
- **High-DPI via `<ApplicationHighDpiMode>` csproj property**, not manifest.
  Manifest entries trigger WFO0003 on .NET 6+ WinForms.
- **Stock `FolderBrowserDialog`** rather than a hypothetical
  `XtraFolderBrowserDialog` (doesn't exist; DX only ships file pickers).
  Modern Vista-style shell dialog on .NET 6+.
- **Stock `SplitContainer`** in `CompareForm` rather than DevExpress
  `SplitContainerControl`. The `Horizontal` boolean is ambiguously named;
  `SplitContainer.Orientation` is unambiguous — stick with stock there.
- **Reusable `ArtifactPanel` UserControl** holds the WebView2 + Markdown
  pipeline; `ArtifactForm` and `CompareForm` are thin hosts. Future
  features (frontmatter, themes, etc.) belong in `ArtifactPanel`.
- **Parallel collections for tab tracking:** `_openTabs` (single-file,
  keyed by full path) and `_openCompareTabs` (list of compare forms, with
  a `Mentions(fullPath)` predicate). The single-file dict gives O(1)
  reuse on double-click; the compare list is small enough that linear
  scan is fine.

## Pick up next at

1. **Filter / search box** above the grid (now top of TODO). Smaller
   change than compare mode and immediately useful once the folder has
   more than a dozen artifacts.
2. **Actually exercise compare mode** end-to-end — open the app, drop two
   different `.html` files into `C:\Projects\.artifacts\`, multi-select
   them, click Compare. Confirm split orientation, button gating, and
   live-reload on overwrite of one side. Move the existing
   `C:\Projects\artifact-demo.html` into the folder as a fixture.
3. **Frontmatter parsing** in `ArtifactWatcher` — if Markdown starts with
   `---` … `---` YAML, surface `title`, `prompt`, `tags` as columns.
   Lets Claude Code stamp metadata that's actually useful in the grid.

See `TODO.md` for the full prioritized list (compare mode is now in the
"Done" section there).

## Known caveats

- DevExpress version `25.2.5` is pinned. Bump if the licensed install on
  this machine differs — NuGet restore will fail loud if it's wrong.
- The Markdown CSS uses Fraunces / Newsreader / JetBrains Mono with
  fallbacks. WebView2 has no enforced internet here; falls back to
  Georgia / Consolas. The standalone HTML at `C:\Projects\artifact-demo.html`
  *does* fetch Google Fonts, so it'll look different from inline-rendered
  Markdown when offline.
- LF→CRLF git warnings on commit are noise (Windows default `core.autocrlf`)
  — not worth a `.gitattributes` for a one-developer repo.
