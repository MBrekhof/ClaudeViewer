# Claude Viewer

A small WinForms / DevExpress companion for Claude Code. It watches a folder
for HTML and Markdown artifacts and renders them in tabbed WebView2 panes —
so the CLI writes files, the viewer shows them, and the two stay in sync.

## What it does

- **Watch a folder** (`C:\Projects\.artifacts` by default) and list every
  `.html` / `.md` file there in a DevExpress grid, newest at the top.
- **Render on click** — HTML loads via `WebView2.Navigate(file://…)`,
  Markdown is run through Markdig with a broadsheet CSS theme so MD output
  looks like a real document, not a code dump.
- **Live-reload** — overwrite a file Claude Code wrote earlier and the open
  tab refreshes automatically. Reads tolerate locked files mid-write.
- **Title sniffing** — pulls `<title>` from HTML and the first `# heading`
  from Markdown to populate the grid's *Title* column.
- **Folder picker** — `Change…` button on the artifact panel; choice is
  persisted to `%LOCALAPPDATA%\ClaudeViewer\settings.json`.

## Requirements

- Windows 10 / 11
- .NET 10 SDK (`net10.0-windows`)
- DevExpress WinForms (using `DevExpress.Win` 25.2.5 — adjust to whatever
  is licensed on the machine)
- WebView2 Runtime (shipped with Windows 11)

## Run

```powershell
dotnet build
dotnet run --project ClaudeViewer
```

Or open `ClaudeViewer.sln` in Visual Studio.

## Configuration

| Setting | Where | Notes |
|---|---|---|
| Watched folder | `Change…` button OR `%LOCALAPPDATA%\ClaudeViewer\settings.json` | Persists across runs. |
| Override (CI / launchers) | `CLAUDE_VIEWER_DIR` env var | Wins over the settings file; the picker is disabled while this is set. |

## Project layout

```
ClaudeViewer/
├── Program.cs             entry point + skin
├── MainForm.cs            DockManager (left) + DocumentManager (tabs)
├── Models/Artifact.cs     file metadata
├── Services/
│   ├── ArtifactWatcher.cs FileSystemWatcher → BindingList<Artifact>
│   ├── MarkdownRenderer.cs Markdig + broadsheet CSS
│   └── Settings.cs        JSON persistence
└── Controls/ArtifactForm.cs  XtraForm hosting WebView2 (one per tab)
```

## Workflow with Claude Code

Tell Claude Code (in `CLAUDE.md` or per request) to write generated HTML /
Markdown artifacts under your watched folder, e.g. `C:\Projects\.artifacts\`.
Anything it writes appears at the top of the grid and renders on click.
