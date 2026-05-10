# Session Handoff

**Last session:** 2026-05-10
**Status:** Recursive watch is now opt-in (default off). Folder column + DevExpress group panel added so files in subfolders are visible/groupable when recursive is on. Builds clean (0 warn / 0 err); visual verification still pending.
**Repo:** https://github.com/MBrekhof/ClaudeViewer · `main` · last commit `22b03f5` (uncommitted: Recursive setting, Folder column, group panel, related doc updates)

## What this is

A WinForms / DevExpress companion app for Claude Code. Watches a folder
for HTML / Markdown artifacts and renders them in tabbed WebView2 panes,
with a side-by-side compare mode for two files at once.

## Current file layout

```
ClaudeViewer/
├── ClaudeViewer.sln
├── README.md · TODO.md · SESSION_HANDOFF.md · ARCHITECTURE.md
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
    │                                 header with Recursive + Compare + Change… controls;
    │                                 group panel enabled — drag a column header up to group)
    ├── Models/Artifact.cs           (FileName, FullPath, Folder, ModifiedAt, Kind, Title, SizeBytes)
    ├── Services/
    │   ├── ArtifactWatcher.cs       (FileSystemWatcher → BindingList; ctor takes
    │   │                             bool recursive; computes Folder relative to root;
    │   │                             title sniffing; retry on lock; raises ArtifactUpdated)
    │   ├── MarkdownRenderer.cs      (Markdig + broadsheet CSS theme)
    │   ├── DiffRenderer.cs          (DiffPlex SideBySideDiffBuilder → (leftHtml, rightHtml)
    │   │                             code-style diff with line numbers + colored backgrounds)
    │   ├── FileReader.cs            (shared retry-aware text reader)
    │   └── Settings.cs              (%LOCALAPPDATA%\ClaudeViewer\settings.json;
    │                                 ArtifactDirectory + Recursive)
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
- **`Recursive` toggle** (new, default **off**): when off, only files
  directly in the watched folder are tracked (matches Explorer's
  top-level view). When on, the watcher recurses into subfolders and the
  `Folder` column shows the relative path. Persists to `settings.json`.
  Toggling rebuilds the watcher via `RebuildWatcher()` (same helper that
  the folder picker uses).
- **Group panel enabled** (`OptionsView.ShowGroupPanel = true`). Drag any
  column header up to group; `Folder` is the obvious one in recursive
  mode, giving a tree-like grouped view without changing components.
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
| App launches and stays alive ≥ 4 s | `Start-Process` smoke test |

| **Not** yet confirmed visually | Why it matters |
|---|---|
| Recursive toggle CheckEdit fits the 38 px header | Header is shared with Compare/Change buttons — eyeball it. |
| Folder column populates correctly in recursive mode | `Path.GetRelativePath(_root, dir)`; `.` → `""`. Drop a file in a subfolder of the watched root and confirm. |
| Dragging Folder to the group panel produces collapsible groups | Stock DevExpress feature; no custom code. |
| Sort/filter on Folder works, other columns unchanged | All columns are bound directly; filtering via `OptionsView.ShowAutoFilterRow` is still off (see TODO). |
| Toggling Recursive rebuilds watcher cleanly with no leaks | `RebuildWatcher()` disposes the old watcher, creates a new one, swaps `DataSource`. |
| Setting persists across restart | `Settings.Save()` is called before `RebuildWatcher()` on each toggle. |
| Split layout actually renders left/right (not top/bottom) | Code says `Orientation.Vertical`. Flip to `Horizontal` if it comes up wrong (one line in `Controls/CompareForm.cs`). |
| Live-reload through the compare path end-to-end | Needs Claude Code pointed at the watched folder while a compare tab is open. |
| Markdown CSS rendering on real content | The broadsheet theme falls back to Georgia/Consolas if the Google Fonts aren't available — fine but unverified. |
| MD diff colours / line alignment look right | Drop two related `.md` files into the watched folder, multi-select, Compare. |

## Decisions worth remembering

- **`Recursive` defaults to off.** The original `.artifacts` use case is
  flat, and pointing the viewer at a project root with recursion on
  surprises with subfolder noise (.git, .vs, .claude, …). Off is the
  conservative default; users opt in.
- **Folder column instead of a TreeList.** A `TreeList` would be visually
  closest to Explorer but means re-wiring selection / multi-select /
  compare / find. A `Folder` column + DevExpress's built-in group panel
  gets the same tree-grouped view at a fraction of the change.
- **`MakeArtifact` is now an instance method on `ArtifactWatcher`.** It
  needs `_root` to compute `Path.GetRelativePath`. `TryReadTitle` stays
  static — no instance state needed there.
- **`RebuildWatcher()` helper.** Used by both `SwitchTo` (folder change)
  and `OnRecursiveToggled`. Disposes the old watcher, creates a fresh one
  with current settings, swaps `DataSource`. Single source of truth for
  watcher reconstruction.
- **`DevExpress.Win` meta-package** instead of thin slices. Heavier graph
  but one line in csproj.
- **`BonusSkins.Register()` removed.** Lives in a separate
  `DevExpress.BonusSkins` assembly the meta package doesn't pull. Default
  `WXICompact` is fine without it.
- **High-DPI via `<ApplicationHighDpiMode>` csproj property**, not manifest.
- **Stock `FolderBrowserDialog`** rather than a hypothetical
  `XtraFolderBrowserDialog` (doesn't exist; DX only ships file pickers).
- **Stock `SplitContainer`** in `CompareForm` rather than DevExpress
  `SplitContainerControl`.
- **Reusable `ArtifactPanel` UserControl** holds the WebView2 + Markdown
  pipeline; `ArtifactForm` and `CompareForm` are thin hosts.
- **Parallel collections for tab tracking:** `_openTabs` (single-file,
  keyed by full path) and `_openCompareTabs` (list of compare forms, with
  a `Mentions(fullPath)` predicate).

## Pick up next at

1. **Visually verify the Recursive toggle + Folder column** — point at a
   project root (e.g. `C:\Projects\mcpOffice`), confirm the toggled-off
   list matches Explorer's root view, toggle on and confirm subfolder
   files appear with `Folder` populated.
2. **Eyeball the diff view** (still outstanding from last session). Drop
   two related `.md` files into the watched folder, Compare. Confirm
   colours, alignment, and that live-reload still routes correctly when
   one side is overwritten.
3. **Expose the built-in find panel.** One-line change in `MainForm.cs`:
   `_gridView.OptionsFind.AlwaysVisible = true`.
4. **Frontmatter parsing** in `ArtifactWatcher` — if Markdown starts with
   `---` … `---` YAML, surface `title`, `prompt`, `tags` as columns.
5. **Diff polish**: synchronized scroll, intra-line highlighting on
   `Modified` rows, HTML source-level diff toggle.

See `TODO.md` for the full prioritized list.

## Known caveats

- DevExpress version `25.2.5` is pinned. Bump if the licensed install on
  this machine differs — NuGet restore will fail loud if it's wrong.
- The Markdown CSS uses Fraunces / Newsreader / JetBrains Mono with
  fallbacks. WebView2 has no enforced internet here; falls back to
  Georgia / Consolas.
- LF→CRLF git warnings on commit are noise (Windows default `core.autocrlf`)
  — not worth a `.gitattributes` for a one-developer repo.
- With `Recursive` on against a deep tree, the `Folder` column can get
  long. Group by Folder (drag to group panel) for a cleaner view.
