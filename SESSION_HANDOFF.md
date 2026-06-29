# Session Handoff

**Last session:** 2026-06-29
**Status:** Config viewer panel merged to `main` (11 unit tests pass: 4 reader + 7 merger). Visual smoke verification of the Config tab still pending. This session was housekeeping only — no feature code.
**Repo:** https://github.com/MBrekhof/ClaudeViewer · `main` · last commit `71227fc` (pushed)

## This session (2026-06-29)

- Ran `extract-learnings` consolidation (auditor + discoverer). Result: no
  removals/merges/new learnings; one sharpening EDIT to
  `feedback_devexpress_builtins.md` (TreeList shares the same DX built-ins as
  GridView — Config viewer's `ConfigPanel` uses `ShowAutoFilterRow`). Memory
  corpus stays at 3 lean feedback entries.
- Added `.claude/agent-memory/` to `.gitignore` (machine-local agent memory
  must never be committed) and committed + pushed as `71227fc`.
- Confirmed the **duetgpt MCP is read-only** (`query_knowledge` +
  `ask_question` only — no upload/add-document tool) and **ContextBoard is a
  kanban board, not a doc/RAG store**. Neither can ingest documents from here;
  doc ingestion needs duetgpt's own UI/API.

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
├── ClaudeViewer/
│   ├── ClaudeViewer.csproj          (net10.0-windows; DevExpress.Win 25.2.5;
│   │                                 Microsoft.Web.WebView2 1.0.2792.45;
│   │                                 Markdig 0.37.0; DiffPlex 1.9.0;
│   │                                 ApplicationHighDpiMode = PerMonitorV2)
│   ├── app.manifest                 (asInvoker; DPI handled via csproj, not manifest)
│   ├── Program.cs                   (skin: WXICompact)
│   ├── MainForm.cs                  (DockManager left + DocumentManager tabbed view +
│   │                                 GridControl bound to ArtifactWatcher.Artifacts;
│   │                                 header with Recursive + Compare + Config + Change… controls;
│   │                                 group panel enabled — drag a column header up to group)
│   ├── Models/
│   │   ├── Artifact.cs              (FileName, FullPath, Folder, ModifiedAt, Kind, Title, SizeBytes)
│   │   └── MergedSetting.cs         (DTO for config TreeList rows; leaf or group; no logic)
│   ├── Services/
│   │   ├── ArtifactWatcher.cs       (FileSystemWatcher → BindingList; ctor takes
│   │   │                             bool recursive; computes Folder relative to root;
│   │   │                             title sniffing; retry on lock; raises ArtifactUpdated)
│   │   ├── MarkdownRenderer.cs      (Markdig + broadsheet CSS theme)
│   │   ├── DiffRenderer.cs          (DiffPlex SideBySideDiffBuilder → (leftHtml, rightHtml)
│   │   │                             code-style diff with line numbers + colored backgrounds)
│   │   ├── FileReader.cs            (shared retry-aware text reader)
│   │   ├── Settings.cs              (%LOCALAPPDATA%\ClaudeViewer\settings.json;
│   │   │                             ArtifactDirectory + Recursive)
│   │   ├── ClaudeSettingsReader.cs  (reads one settings.json; returns ScopeContents(Root, Error);
│   │   │                             permissive JSONC via JsonDocumentOptions)
│   │   └── ClaudeSettingsMerger.cs  (pure static: 4 ScopeContents → BindingList<MergedSetting>;
│   │                                 scalar precedence Managed > Local > Project > User;
│   │                                 array union; parse-error sentinel rows)
│   └── Controls/
│       ├── ArtifactPanel.cs         (UserControl: WebView2 + Markdown rendering — the reusable core;
│       │                             LoadAsync routes by kind, LoadHtmlAsync injects raw HTML)
│       ├── ArtifactForm.cs          (XtraForm shell hosting one ArtifactPanel; MDI child)
│       ├── CompareForm.cs           (XtraForm with stock SplitContainer Orientation.Vertical;
│       │                             two ArtifactPanels with file-name labels above each pane;
│       │                             RenderAsync routes MD/MD → DiffRenderer, else straight render)
│       ├── ConfigPanel.cs           (UserControl: DevExpress TreeList of MergedSetting rows;
│       │                             LoadAsync reads 4 scopes + merges; Reload button)
│       └── ConfigForm.cs            (XtraForm shell hosting one ConfigPanel; single-instance MDI tab)
└── ClaudeViewer.Tests/
    ├── ClaudeViewer.Tests.csproj    (net10.0; xUnit 2.9; FluentAssertions; references main project)
    ├── Fixtures/                    (sample settings.json files for reader + merger tests)
    ├── ClaudeSettingsReaderTests.cs (4 tests: happy path, missing file, malformed JSON, empty object)
    └── ClaudeSettingsMergerTests.cs (7 tests: scalar precedence, array union, parse-error sentinel,
                                      group rows, winner column, all-missing, mixed scopes)
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
- **Config tab** — opens from a "Config" button in the header strip.
  Single-instance MDI tab; tied to the watched folder; manual Reload
  button; per-scope columns (Managed / User / Project / Local) with
  Effective value + Winner indicator; double-click opens the winning
  scope's settings.json externally.

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

1. **Visually verify the Config tab** — point ClaudeViewer at
   `C:\Projects\duetgpt` (real `.claude/settings.local.json` present),
   click Config, walk the Task 18 checklist in
   `docs/plans/2026-05-11-config-viewer-panel.md`. (Merge to main already done.)
2. Then the older outstanding items: recursive toggle eyeball test, diff
   viewer eyeball, find panel (`_gridView.OptionsFind.AlwaysVisible = true`),
   frontmatter parsing.

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
