# Architecture

How Claude Viewer is wired internally. README is for users, SESSION_HANDOFF
is state-of-play, this is the map you want when you're about to change code.

## One-paragraph summary

A `FileSystemWatcher` (`Services/ArtifactWatcher.cs`) feeds a
`BindingList<Artifact>` that a DevExpress `GridControl` is bound to. Clicking
a row opens an `ArtifactForm` MDI tab; selecting two rows + Compare opens a
`CompareForm` with two panes. Both forms host the same reusable
`ArtifactPanel` (a WebView2 + Markdown pipeline). When the watcher sees a
file change, it raises `ArtifactUpdated`; `MainForm` looks up any tabs that
reference that path and tells them to reload. A separate Config tab reads the
four Claude Code settings.json scopes (Managed/User/Project/Local), merges
them into a `BindingList<MergedSetting>`, and displays them in a DevExpress
`TreeList` — read-only, single-instance MDI tab.

## Component map

```
                        ┌──────────────────────────┐
                        │      Program.cs          │  skin = WXICompact
                        │      (entry point)       │
                        └────────────┬─────────────┘
                                     │
                                     ▼
┌────────────────────────────────────────────────────────────────────┐
│                            MainForm                                │
│  ┌────────────────────────┐  ┌──────────────────────────────────┐  │
│  │  Left dock panel       │  │  DocumentManager / TabbedView    │  │
│  │  ────────────────────  │  │  (MDI tab host)                  │  │
│  │  header strip          │  │                                  │  │
│  │   · folder label       │  │   ┌─────────────┐                │  │
│  │   · Compare button     │  │   │ ArtifactForm│ × N            │  │
│  │   · Config button      │  │   │  (single)   │                │  │
│  │   · Change… button     │  │   └─────────────┘                │  │
│  │  GridControl           │  │   ┌─────────────┐                │  │
│  │   ↳ bound to           │  │   │ CompareForm │ × N            │  │
│  │     watcher.Artifacts  │  │   │  (two-pane) │                │  │
│  └────────────────────────┘  │   └─────────────┘                │  │
│                              │   ┌─────────────┐                │  │
│                              │   │ ConfigForm  │ × 1            │  │
│                              │   │ (read-only  │                │  │
│                              │   │  TreeList)  │                │  │
│                              │   └─────────────┘                │  │
│                              └──────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────┘
              │                                      │
              ▼                                      ▼
   ┌────────────────────┐                ┌──────────────────────────┐
   │  ArtifactWatcher   │  ArtifactUpdated │  ArtifactPanel (×1..N) │
   │  (FileSystemWatcher├─────────────────►│  WebView2 +            │
   │   + BindingList)   │                  │  MarkdownRenderer      │
   └─────────┬──────────┘                  └──────────────────────────┘
             │ Settings.Save()
             ▼
   %LOCALAPPDATA%\ClaudeViewer\settings.json
```

## File-by-file responsibilities

| File | Responsibility |
|---|---|
| `Program.cs` | Set DevExpress skin, run `MainForm`. Nothing else. |
| `MainForm.cs` | Owns the watcher, the grid, the tab host, and the dictionaries that map open file paths → tab forms. All wiring lives here. `RebuildWatcher()` is the single helper used by both folder-change and recursive-toggle paths. |
| `Models/Artifact.cs` | Immutable record-ish DTO for a tracked file. `Folder` is the path relative to the watched root (`""` for root files). `KindDisplay` / `SizeDisplay` exist purely so the grid can bind directly without converters. |
| `Models/MergedSetting.cs` | DTO bound to the config TreeList. One row per leaf key path, or an intermediate group row. No logic. |
| `Services/ArtifactWatcher.cs` | Watches one folder, exposes `BindingList<Artifact>`, raises `ArtifactUpdated`. Ctor takes `bool recursive` which controls both `IncludeSubdirectories` and the seed enumeration's `SearchOption`. Computes each `Artifact.Folder` via `Path.GetRelativePath(_root, …)`. Title sniffing + locked-file tolerance live here. |
| `Services/MarkdownRenderer.cs` | Markdig pipeline + the broadsheet CSS theme. Pure function: `(markdown, title) → html`. |
| `Services/DiffRenderer.cs` | DiffPlex `SideBySideDiffBuilder` wrapped to produce two HTML documents (one per pane) with line-aligned diff backgrounds. Pure function: `(leftText, rightText, ...) → (leftHtml, rightHtml)`. |
| `Services/FileReader.cs` | `ReadAllTextWithRetryAsync` with `FileShare.ReadWrite \| Delete` and a 4-attempt retry. Shared by `ArtifactPanel` and `CompareForm`. |
| `Services/Settings.cs` | Tiny JSON file for the watched-folder path and `Recursive` flag. Best-effort I/O; defaults silently on read failure. |
| `Services/ClaudeSettingsReader.cs` | Reads one settings.json. Returns `ScopeContents(Root, Error)`: success → Root non-null; missing file → both null; malformed → Error set. Permissive JSONC (comments + trailing commas via `JsonDocumentOptions`). |
| `Services/ClaudeSettingsMerger.cs` | Pure static method: 4 `ScopeContents` → `BindingList<MergedSetting>` with parent/child group rows. Scalars use precedence (Managed > Local > Project > User); arrays union across scopes. Parse-error scopes emit a sentinel row. Precedence is encoded in one place (`Precedence` array). |
| `Controls/ArtifactPanel.cs` | The reusable core. WebView2 init + two load paths: `LoadAsync(Artifact)` routes by kind (HTML navigate / Markdown render); `LoadHtmlAsync(html, source)` injects pre-rendered HTML (used by the diff path). |
| `Controls/ArtifactForm.cs` | Thin MDI shell around one `ArtifactPanel`. |
| `Controls/CompareForm.cs` | Two `ArtifactPanel`s in a vertical-splitter `SplitContainer`. `RenderAsync` decides between diff mode (both files MD → `DiffRenderer` + `LoadHtmlAsync` on each side) and straight render (everything else → `LoadAsync` per side, with a `changedOnly` short-circuit so a refresh only reloads the side that actually changed). |
| `Controls/ConfigPanel.cs` + `Controls/ConfigForm.cs` | DevExpress TreeList view of merged-effective config. UserControl + thin XtraForm shell, mirroring the ArtifactPanel/ArtifactForm pattern. Single-instance MDI tab. |

## Data flow

### Cold start
1. `MainForm` ctor → `Settings.Load()` → resolve folder (env var wins).
2. `ArtifactWatcher` ctor → ensures folder exists → seeds `Artifacts` from
   `EnumerateFiles` → starts the OS watcher.
3. `_grid.DataSource = _watcher.Artifacts;` — the grid is now live.

### File changed externally (Claude Code writes a file)
1. `FileSystemWatcher` raises `Created`/`Changed`/`Renamed`/`Deleted`.
2. Handler calls `_ui.Post(...)` to marshal to the UI thread, then `Upsert`.
3. `Upsert` rebuilds the `Artifact`, removes any stale entry, inserts at
   index 0 (newest-first), and fires `ArtifactUpdated`.
4. `MainForm.OnArtifactUpdated`:
   - looks up `_openTabs[fullPath]` → if hit, `LoadAsync` reloads the tab.
   - scans `_openCompareTabs` for forms whose `Mentions(path)` returns true
     → calls `RefreshIfMatchesAsync`, which only reloads the matching side.

### User opens a file
- Single click or double click → `OpenAtRow` → `OpenArtifact`. Reuses the
  existing tab if the path is already in `_openTabs`; otherwise creates an
  `ArtifactForm`, registers it, and loads.

### User compares two files
- Grid `SelectionChanged` updates `_compareBtn.Enabled` (true iff exactly
  two rows selected). Clicking the button materializes the selection and
  opens a `CompareForm`. The compare form is registered in
  `_openCompareTabs`, removed on close.

## Key design decisions

**Single-source `BindingList`.** The watcher owns the list; the grid binds
directly. No view model, no copy. Inserting at index 0 is what gives the
grid its newest-first ordering without a sort comparer.

**`ArtifactPanel` is the reusable unit.** Both `ArtifactForm` (single tab)
and `CompareForm` (two panes) host the same control. Anything that affects
how a file *renders* — frontmatter parsing, dark theme, CSV/JSON support —
belongs in `ArtifactPanel` or its dependencies (`MarkdownRenderer`), not in
the form shells.

**Diff is a `CompareForm` concern, not an `ArtifactPanel` concern.** The
panel exposes `LoadHtmlAsync(html, source)` so the form can inject any
pre-baked HTML (the diff document) without the panel needing to know about
diffs. This keeps `ArtifactPanel` a pure renderer and `DiffRenderer` a pure
function — no view state. If a future feature needs to inject custom HTML
(rendered notebook, JSON viewer, etc.) it follows the same pattern.

**Two parallel collections for tab tracking.** `_openTabs` is a
`Dictionary<string, ArtifactForm>` for O(1) reuse on click. `_openCompareTabs`
is a `List<CompareForm>` because compare tabs are keyed by *two* paths and
linear scan is fine at the scale a human will ever open. Don't over-engineer
this into a unified registry.

**Stock `SplitContainer` over DevExpress `SplitContainerControl`.** The DX
control's `Horizontal` boolean is ambiguously named; stock
`SplitContainer.Orientation` is unambiguous. See
`Controls/CompareForm.cs:11`.

**Stock `FolderBrowserDialog`.** DevExpress doesn't ship a folder picker.
On .NET 6+ the stock dialog is the modern Vista-style shell picker, so the
DX gap doesn't matter.

**`DevExpress.Win` meta-package, not thin slices.** Heavier dependency
graph but one csproj line. Easy to thin-slice later if build time bites.
See `ClaudeViewer.csproj:22`.

**High-DPI in csproj, not manifest.** `<ApplicationHighDpiMode>` avoids
the WFO0003 warning .NET 6+ throws when DPI is set in `app.manifest`.

**`CLAUDE_VIEWER_DIR` env var disables the picker.** Otherwise a launcher
override could be silently overwritten by a user click. The Change… button
is disabled with a tooltip explaining why.

**Recursive watching is opt-in (default off).** The viewer's original use
case is a flat artifact dump folder; pointing it at a project root with
recursion on surfaces noisy subfolders (`.git`, `.vs`, `.claude`, etc.).
The `Recursive` `CheckEdit` in the header persists to `settings.json` and
calls `RebuildWatcher()` to swap the watcher in place.

**Config viewer is read-only.** Honors the "no write path" invariant. To
edit, double-click a row to open the winning scope's settings.json in the
OS-registered editor (`Process.Start` with `UseShellExecute=true`). A
curated editor with LayoutControl sections + JSON fallback is a deliberate
v2 that would relax this stance.

**Folder column + DevExpress group panel instead of a `TreeList`.** When
recursive is on, each `Artifact` carries a `Folder` (relative path). The
`OptionsView.ShowGroupPanel = true` setting lets the user drag the
`Folder` column header up to get a tree-like grouped view — same grid,
no extra component to wire. Picked over a real `TreeList` because that
would mean rewriting selection, multi-select, compare, and find-panel
plumbing for one signal.

## Threading model

- The OS `FileSystemWatcher` callbacks fire on threadpool threads.
  `ArtifactWatcher` captures the UI `SynchronizationContext` in its
  constructor and `Post`s every mutation back to it before touching the
  `BindingList`.
- WebView2 init is async; `ArtifactPanel` queues `LoadAsync` for after
  init completes if a request arrives early.
- File reads use `FileShare.ReadWrite | FileShare.Delete` and a 4-attempt
  retry (60ms × attempt) so opening a file Claude Code is mid-writing
  doesn't crash the panel.

## Extension points

| Want to add | Touch |
|---|---|
| A new file kind (e.g. `.json` pretty-printed) | `ArtifactKind` enum, `ArtifactWatcher.IsTracked` + `MakeArtifact`, `ArtifactPanel.LoadAsync` switch. |
| Frontmatter columns (title, prompt, tags) | Extend `Artifact`, parse in `ArtifactWatcher.MakeArtifact`/`TryReadTitle`, add `_gridView.Columns.AddVisible(...)` in `MainForm.ConfigureGridColumns`. |
| Filter / search box | Don't build one. Set `_gridView.OptionsFind.AlwaysVisible = true` for the built-in find panel (cross-column substring search) and/or `_gridView.OptionsView.ShowAutoFilterRow = true` for per-column filter inputs. Both ship with DevExpress. |
| Group by folder / kind / etc. | Already wired — `ShowGroupPanel = true`. Drag any column header to the panel above the grid. No code needed. |
| Dark theme for Markdown | New CSS variant in `MarkdownRenderer`, switched on `UserLookAndFeel.Default.StyleChanged`. |
| Per-row context menu (open externally, reveal, copy path) | Hook `GridView.PopupMenuShowing` in `MainForm`. |
| Window/layout persistence | Extend `Settings` with size + dock layout; save on `FormClosing`, apply in ctor. |
| New scope source (e.g. workspace) | Add path to `ClaudeSettingsReader.Read` callsite in `ConfigPanel.LoadAsync`; add column in TreeList; extend `Precedence` array; update `ScalarAccum.Set/Get` switch; update sentinel-emission. |

## What this app deliberately doesn't do

- **No write path.** It's a viewer. Claude Code writes; we read. The config
  viewer reads the four scope settings.json files but never writes — edits
  go through the OS-registered .json editor.
- **No DI container.** One form, three services, all `new`'d in the ctor.
  Adding a container would be more code than the savings.
- **No MVVM.** WinForms data binding to a `BindingList` is enough; a VM
  layer would be ceremony with no payoff at this scale.
- **No background indexing.** The watched folder is expected to be small
  (artifacts from one developer). If it ever holds 10k+ files,
  `SeedExisting`'s `OrderByDescending` on `LastWriteTime` becomes the
  bottleneck.
