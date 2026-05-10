# Session Handoff

**Last session:** 2026-05-10
**Status:** Diff viewer for Markdown compare added (DiffPlex 1.9.0); Modified column now full datetime; ARCHITECTURE.md added. Builds clean (0 warn / 0 err) — visual verification still pending.
**Repo:** https://github.com/MBrekhof/ClaudeViewer · `main` · last commit `015eede` (uncommitted: ARCHITECTURE.md, datetime column, diff viewer, doc updates)

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
    │                                 Markdig 0.37.0; DiffPlex 1.9.0;
    │                                 ApplicationHighDpiMode = PerMonitorV2)
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
    │   ├── DiffRenderer.cs          (DiffPlex SideBySideDiffBuilder → (leftHtml, rightHtml)
    │   │                             code-style diff with line numbers + colored backgrounds)
    │   ├── FileReader.cs            (shared retry-aware text reader)
    │   └── Settings.cs              (%LOCALAPPDATA%\ClaudeViewer\settings.json)
    └── Controls/
        ├── ArtifactPanel.cs         (UserControl: WebView2 + Markdown rendering — the reusable core;
        │                             LoadAsync routes by kind, LoadHtmlAsync injects raw HTML)
        ├── ArtifactForm.cs          (XtraForm shell hosting one ArtifactPanel; MDI child)
        └── CompareForm.cs           (XtraForm with stock SplitContainer Orientation.Vertical;
                                      two ArtifactPanels with file-name labels above each pane;
                                      RenderAsync routes MD/MD → DiffRenderer, else straight render)
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
| Split layout actually renders left/right (not top/bottom) | Code says `Orientation.Vertical`. Flip to `Horizontal` if it comes up wrong (one line in `Controls/CompareForm.cs`). |
| Compare button enables on multi-select selection change | Wired to `GridView.SelectionChanged`; relies on event firing as expected. |
| Live-reload through the compare path end-to-end | Needs Claude Code pointed at the watched folder while a compare tab is open. |
| Folder picker switches the watcher cleanly | Disposes/recreates the watcher; should work but unverified at runtime. |
| Markdown CSS rendering on real content | The broadsheet theme falls back to Georgia/Consolas if the Google Fonts aren't available — fine but unverified. |
| MD diff colours / line alignment look right | Drop two related `.md` files into the watched folder, multi-select, Compare. Expect: line numbers in left gutter, green = inserted (right side), red = deleted (left side), yellow = modified (both), faint cream = imaginary padding for alignment. |
| Modified column displays full datetime, not just `HH:mm` | Was `HH:mm:ss`, now `yyyy-MM-dd HH:mm` with `Width = 130`. |

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

1. **Eyeball the new diff view.** Drop two related `.md` files into the
   watched folder, Compare. Confirm colours, alignment, and that
   live-reload still routes correctly when one side is overwritten
   (the diff has to *recompute*, not just refresh one side).
2. **Expose the built-in find panel.** One-line change in `MainForm.cs`:
   `_gridView.OptionsFind.AlwaysVisible = true` (DevExpress ships the
   search box; no custom UI needed — see memory note).
3. **Frontmatter parsing** in `ArtifactWatcher` — if Markdown starts with
   `---` … `---` YAML, surface `title`, `prompt`, `tags` as columns.
   Lets Claude Code stamp metadata that's actually useful in the grid.
4. **Diff polish**: synchronized scroll between the two panes,
   intra-line highlighting on `Modified` rows (DiffPlex provides
   `SubPieces` already; we ignore them), and an HTML source-level
   diff toggle. All in the Polish section of TODO.

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
