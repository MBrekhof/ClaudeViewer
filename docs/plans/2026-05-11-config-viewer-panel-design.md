# Config Viewer Panel — Design

**Date:** 2026-05-11
**Status:** Approved (brainstorming complete; ready for implementation plan)

## Purpose

Add a read-only viewer for Claude Code's merged-effective configuration to ClaudeViewer. Diagnoses "which scope wins?" questions when a permission, hook, or model setting isn't behaving as expected.

Inspired by [`kuri-sun/cc-config-viewer`](https://github.com/kuri-sun/cc-config-viewer) (a Node-based web UI for the same problem). Native WinForms version fits this codebase's stack and uses the existing `ArtifactPanel`/`ArtifactForm` reusable-control pattern.

## Constraints inherited from `ARCHITECTURE.md`

- **No write path.** ClaudeViewer is a viewer; Claude Code writes, we read. This feature must not break that invariant — editing happens out-of-app (double-click → OS-registered .json editor).
- **No DI container.** Services are `new`'d in component constructors.
- **No MVVM.** Bind controls directly to `BindingList`-shaped data.
- **Reusable UserControl + thin XtraForm shell.** Mirror the `ArtifactPanel` + `ArtifactForm` split.
- **Pure services.** Data-fetching and transform logic live in `Services/`, callable from tests without instantiating a form.

## Scope (v1)

In:
- Read all four scope `settings.json` files: Managed, User, Project, Local.
- Show every leaf key in a DevExpress `TreeList` with per-scope columns + an Effective column + a Winner column.
- Honest array semantics: array keys (permissions, hooks) are unioned across scopes; each entry is its own row.
- Single-instance MDI tab opened from a new "Config" button in `MainForm`'s header strip.
- Config view follows the watched folder — when `RebuildWatcher` runs, the open config tab reloads against the new root.
- Manual Reload button in the panel.
- Double-click a row → `Process.Start` the winning scope's `settings.json` via shell-execute.
- New `ClaudeViewer.Tests` xUnit project covering the Merger and Reader.

Out (deferred / explicitly not in v1):
- Inline editing of settings (would break the no-write-path rule).
- FileSystemWatcher on scope files (Reload button is enough).
- JSON-Schema-driven generic form UI (cc-config-viewer's main feature; not justifiable for a curated WinForms reimplementation).
- Project picker independent of the watched folder.
- MRU list of detected projects.
- Multi-scope diff visualization beyond "Winner" column chips.

## Architecture

Five new files plus a small `MainForm.cs` edit, organized to match the existing component layout:

```
ClaudeViewer/
├── Models/
│   └── MergedSetting.cs              ← DTO bound to TreeList
├── Services/
│   ├── ClaudeSettingsReader.cs       ← reads + parses one settings.json
│   └── ClaudeSettingsMerger.cs       ← pure: 4 ClaudeSettings DTOs → BindingList<MergedSetting>
└── Controls/
    ├── ConfigPanel.cs                ← UserControl: TreeList + Reload button
    └── ConfigForm.cs                 ← XtraForm shell hosting one ConfigPanel

MainForm.cs                            ← + "Config" button + _configTab field + RebuildWatcher hook

ClaudeViewer.Tests/                    ← new xUnit project
├── ClaudeViewer.Tests.csproj          (xUnit + FluentAssertions)
├── ClaudeSettingsMergerTests.cs       ← ~10 cases
├── ClaudeSettingsReaderTests.cs       ← ~4 cases
└── Fixtures/
    ├── managed.json
    ├── user.json
    ├── project.json
    ├── project.jsonc                  (comments + trailing commas)
    └── malformed.json
```

Single-instance tab tracked by a `ConfigForm? _configTab` field on `MainForm` — no dictionary needed (only one).

## Scope path resolution

| Scope | Path |
|---|---|
| Managed | `C:\ProgramData\ClaudeCode\managed-settings.json` |
| User | `%USERPROFILE%\.claude\settings.json` |
| Project | `<watchedFolder>\.claude\settings.json` |
| Local | `<watchedFolder>\.claude\settings.local.json` |

Missing files = empty contribution from that scope. Not an error, no log line, no row.

## Merge semantics

The trickiest part. Two distinct rules depending on the JSON value's type at the key.

**Scalars** (strings, numbers, booleans, plain objects without arrays):
- Last-writer-wins by documented precedence.
- One row per leaf key.
- All four per-scope columns populated (or empty if not set in that scope).
- "Winner" = the highest-precedence scope that set the key.
- "Effective" = that winner's value.

**Arrays** (`permissions.allow`, `permissions.deny`, `permissions.ask`, `hooks.*[]`, etc.):
- Concatenated across all scopes. Claude Code does not override arrays.
- Each array entry is its own row.
- Per-scope columns: empty in all but the contributing scope.
- "Winner" = the contributing scope.
- "Effective" = the entry's value (same as the contributing scope's value).

Precedence order is encoded in one place in the Merger (constant or enum) and is the single source of truth for the rest of the code.

## DTO: `MergedSetting`

```csharp
public sealed class MergedSetting
{
    public int    Id        { get; init; }      // synthetic, TreeList parent linking
    public int?   ParentId  { get; init; }
    public string Key       { get; init; } = "";// leaf segment, e.g. "allow[0]"
    public string KeyPath   { get; init; } = "";// full dotted path
    public string? Managed   { get; init; }
    public string? User      { get; init; }
    public string? Project   { get; init; }
    public string? Local     { get; init; }
    public string? Effective { get; init; }
    public string  Winner    { get; init; } = "";// "", "Managed", "User", "Project", "Local"
    public bool   IsGroup   { get; init; }      // intermediate node, no values
}
```

Group rows carry only `Key`/`KeyPath` to provide the TreeList's hierarchy. The Merger builds intermediate parents lazily as it walks each scope's JSON.

## TreeList configuration

- `KeyFieldName = "Id"`, `ParentFieldName = "ParentId"`.
- Columns: Key | Managed | User | Project | Local | Effective | Winner.
- `OptionsBehavior.Editable = false`.
- `OptionsView.ShowAutoFilterRow = true` — filter on Winner = "Local" to see what's overridden locally, etc.
- Conditional formatting on Winner column: small colored chip per scope (4 distinct colors).
- Key column wider; per-scope columns narrow with ellipsis on overflow; Effective wider than per-scope columns.

## Data flow

```
              MainForm "Config" button click
                          │
                          ▼
              _configTab is null?
              ├── yes ──► new ConfigForm()
              │           panel.LoadAsync(_watchedRoot)
              │           _configTab = form
              │           form.FormClosed += () => _configTab = null
              └── no ───► focus existing tab

              panel.LoadAsync(root):
                Task.Run:
                  managed  = Reader.Read(MANAGED_PATH)
                  user     = Reader.Read(USER_PATH)
                  project  = Reader.Read(Path.Combine(root, ".claude", "settings.json"))
                  local    = Reader.Read(Path.Combine(root, ".claude", "settings.local.json"))
                  list     = Merger.Merge(managed, user, project, local)
                Marshal to UI:
                  _treeList.DataSource = list
                  _treeList.ExpandAll()

              MainForm.RebuildWatcher(newRoot):
                ... existing logic ...
                if (_configTab != null) _configTab.Panel.LoadAsync(newRoot)
```

## Double-click → open in editor

```csharp
private void OnRowDoubleClick(object? sender, RowClickEventArgs e)
{
    if (_treeList.GetDataRecordByNode(e.Node) is not MergedSetting row) return;
    if (row.IsGroup || string.IsNullOrEmpty(row.Winner)) return;

    var path = ResolveScopeFile(row.Winner, _currentRoot);
    if (!File.Exists(path)) return;

    try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
    catch (Exception ex) { XtraMessageBox.Show($"Couldn't open {path}: {ex.Message}"); }
}
```

`ResolveScopeFile` is a tiny switch over `row.Winner` mirroring the Reader's path constants.

## Error handling

| Case | Behavior |
|---|---|
| Scope file missing | Empty contribution. No error. |
| Malformed JSON | Reader catches `JsonException`, returns a DTO containing a single sentinel entry. Merger emits one placeholder row: `Key = "<parse error>"`, scope column shows the message. App stays alive. |
| File locked mid-write | Reuse `Services/FileReader.cs` — `FileShare.ReadWrite \| Delete` + 4-attempt 60ms retry. |
| Watched folder doesn't exist | Watcher already handles; config panel just shows no Project/Local contributions. |
| Process.Start fails (no .json handler registered) | Try/catch → `XtraMessageBox.Show`. |
| Reload clicked during in-flight load | `_reloadBtn.Enabled = false` for the duration. No cancellation needed — load is ms-scale. |
| settings.json with comments or trailing commas (JSONC) | `JsonDocumentOptions { CommentHandling = Skip, AllowTrailingCommas = true }`. |

## Threading

Reader I/O on `Task.Run`; Merger is pure CPU on the same task; marshal back to UI via captured `SynchronizationContext` (same pattern as `Services/ArtifactWatcher.cs`).

## Testing

### Unit tests — `ClaudeViewer.Tests` (new)

xUnit + FluentAssertions. Tests target only the pure services — no UI tests.

**`ClaudeSettingsMergerTests`** (~10 cases):
1. All four scopes empty → empty list.
2. One scalar in User scope → one row, Winner = User, only User column populated.
3. Same scalar in User and Project → one row, Winner = Project, both columns populated.
4. Same scalar in all four scopes → one row, Winner = highest precedence, all columns populated.
5. Array key in User only → entries become rows with Winner = User.
6. Array key in User and Project → all entries appear; each Winner matches its contributing scope; no override.
7. Nested object path (`permissions.allow[0]`) → group rows created for `permissions` and `allow`.
8. Group rows have empty value columns and empty Winner.
9. Precedence enum order is the documented order (asserted via constant).
10. Mixed scalar + array under same parent merge correctly.

**`ClaudeSettingsReaderTests`** (~4 cases):
1. Well-formed JSON → DTO populated.
2. Missing file → empty DTO, no exception.
3. Malformed JSON → DTO with parse-error sentinel.
4. JSONC (block comments + line comments + trailing commas) → parses cleanly.

Fixtures live in `Tests/Fixtures/`.

### Visual smoke (manual, per CLAUDE.md mandate)

Point ClaudeViewer at `C:\Projects\duetgpt` (real project with real `.claude/`), click Config, verify:
- TreeList populates with Managed, User, Project, Local contributions.
- A known-overridden key (create one: add a `model` override in `.claude/settings.local.json`) shows Local as Winner.
- Double-click on a row opens that file in the registered editor.
- Reload button picks up an external edit to any scope file.
- Toggling `Change…` to a different project reloads the config view automatically.
- Closing the tab and reopening produces a fresh instance, not stale data.

## File-by-file responsibilities

| File | Responsibility |
|---|---|
| `Models/MergedSetting.cs` | DTO. No logic. |
| `Services/ClaudeSettingsReader.cs` | Read one settings.json; handle missing/malformed/JSONC/locked. Returns a `ClaudeSettings` DTO (or `null`/sentinel for missing/malformed). Path constants for Managed and User; Project/Local paths derived from a passed-in root. |
| `Services/ClaudeSettingsMerger.cs` | Pure static method `Merge(managed, user, project, local) → BindingList<MergedSetting>`. Walks each scope's JSON, builds the parent/child row tree, applies scalar last-writer-wins and array union rules. |
| `Controls/ConfigPanel.cs` | UserControl. TreeList + Reload button. `LoadAsync(string root)`. `OnRowDoubleClick` handler. Exposes nothing public except `LoadAsync`. |
| `Controls/ConfigForm.cs` | XtraForm hosting one `ConfigPanel`. Exposes the panel via property so `MainForm.RebuildWatcher` can call `LoadAsync(newRoot)`. |
| `MainForm.cs` | Add `_configBtn` to header strip. Add `ConfigForm? _configTab` field. Click handler: focus-or-create. `RebuildWatcher` extension: if open, reload. `FormClosed` cleanup. |
| `ClaudeViewer.Tests/*` | xUnit project; Merger + Reader tests; JSON fixtures. |

## Out-of-scope / future enhancements

- Inline editing via curated LayoutControl sections + JSON fallback (the "v3" idea — needs proper schema mapping, write path, per-scope save). Revisit after the read-only view proves valuable in actual use.
- FileSystemWatcher on scope files for auto-refresh.
- Per-row context menu: "Copy key path", "Reveal in Explorer", "Open all 4 scope files side-by-side in compare mode".
- Dark theme styling for the Winner chips (pair with the existing dark Markdown theme work in `TODO.md`).
- Frontmatter-like badges on settings entries (e.g., flag a hook as "always runs" vs "conditional").
