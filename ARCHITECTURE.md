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
reference that path and tells them to reload. That's the whole app.

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
│  │   · Change… button     │  │   │  (single)   │                │  │
│  │  GridControl           │  │   └─────────────┘                │  │
│  │   ↳ bound to           │  │   ┌─────────────┐                │  │
│  │     watcher.Artifacts  │  │   │ CompareForm │ × N            │  │
│  └────────────────────────┘  │   │  (two-pane) │                │  │
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
| `MainForm.cs` | Owns the watcher, the grid, the tab host, and the dictionaries that map open file paths → tab forms. All wiring lives here. |
| `Models/Artifact.cs` | Immutable record-ish DTO for a tracked file. `KindDisplay` / `SizeDisplay` exist purely so the grid can bind directly without converters. |
| `Services/ArtifactWatcher.cs` | Watches one folder, exposes `BindingList<Artifact>`, raises `ArtifactUpdated`. Title sniffing + locked-file tolerance live here. |
| `Services/MarkdownRenderer.cs` | Markdig pipeline + the broadsheet CSS theme. Pure function: `(markdown, title) → html`. |
| `Services/DiffRenderer.cs` | DiffPlex `SideBySideDiffBuilder` wrapped to produce two HTML documents (one per pane) with line-aligned diff backgrounds. Pure function: `(leftText, rightText, ...) → (leftHtml, rightHtml)`. |
| `Services/FileReader.cs` | `ReadAllTextWithRetryAsync` with `FileShare.ReadWrite \| Delete` and a 4-attempt retry. Shared by `ArtifactPanel` and `CompareForm`. |
| `Services/Settings.cs` | One-property JSON file for the watched-folder path. Best-effort I/O; defaults silently on read failure. |
| `Controls/ArtifactPanel.cs` | The reusable core. WebView2 init + two load paths: `LoadAsync(Artifact)` routes by kind (HTML navigate / Markdown render); `LoadHtmlAsync(html, source)` injects pre-rendered HTML (used by the diff path). |
| `Controls/ArtifactForm.cs` | Thin MDI shell around one `ArtifactPanel`. |
| `Controls/CompareForm.cs` | Two `ArtifactPanel`s in a vertical-splitter `SplitContainer`. `RenderAsync` decides between diff mode (both files MD → `DiffRenderer` + `LoadHtmlAsync` on each side) and straight render (everything else → `LoadAsync` per side, with a `changedOnly` short-circuit so a refresh only reloads the side that actually changed). |

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
| Dark theme for Markdown | New CSS variant in `MarkdownRenderer`, switched on `UserLookAndFeel.Default.StyleChanged`. |
| Per-row context menu (open externally, reveal, copy path) | Hook `GridView.PopupMenuShowing` in `MainForm`. |
| Window/layout persistence | Extend `Settings` with size + dock layout; save on `FormClosing`, apply in ctor. |

## What this app deliberately doesn't do

- **No write path.** It's a viewer. Claude Code writes; we read.
- **No DI container.** One form, three services, all `new`'d in the ctor.
  Adding a container would be more code than the savings.
- **No MVVM.** WinForms data binding to a `BindingList` is enough; a VM
  layer would be ceremony with no payoff at this scale.
- **No background indexing.** The watched folder is expected to be small
  (artifacts from one developer). If it ever holds 10k+ files,
  `SeedExisting`'s `OrderByDescending` on `LastWriteTime` becomes the
  bottleneck.
