# Config Viewer Panel Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a read-only TreeList-based merged-effective Claude Code config viewer to ClaudeViewer as an MDI document tab.

**Architecture:** Five new files mirroring the existing `ArtifactPanel`/`ArtifactForm` + pure-services component split. `Services/ClaudeSettingsReader` reads one `settings.json` file (returns a `ScopeContents` record); `Services/ClaudeSettingsMerger` is a pure static method that turns four scope inputs into a `BindingList<MergedSetting>` with synthetic parent/child rows for the TreeList. `Controls/ConfigPanel` is the reusable UserControl; `Controls/ConfigForm` is the thin XtraForm shell. Read-only by design — double-click a row to open the winning scope's `settings.json` in the OS-registered editor.

**Tech Stack:** .NET 10 (`net10.0-windows`), DevExpress.Win 25.2.5 (XtraTreeList, XtraEditors), `System.Text.Json` with permissive options for JSONC, xUnit + FluentAssertions for tests, existing `Services/FileReader.cs` for retry-aware file reads.

**Design doc:** `docs/plans/2026-05-11-config-viewer-panel-design.md` (commit `3d8ad7b`).

---

## Prerequisites for the implementer

- Read `docs/plans/2026-05-11-config-viewer-panel-design.md` end-to-end.
- Read `ARCHITECTURE.md` for the existing component split, threading rules, and the **no-write-path** invariant.
- Confirm `dotnet build` is clean on `main` before starting.
- Work on `main` directly (single-developer repo; no PR review gate).
- The user's CLAUDE.md mandates: invoke `systematic-debugging` before proposing any bug fix, and `verification-before-completion` before claiming work is done.

---

## Task 1: Add `ClaudeViewer.Tests` project to the solution

**Files:**
- Create: `ClaudeViewer.Tests/ClaudeViewer.Tests.csproj`
- Create: `ClaudeViewer.Tests/Usings.cs`
- Modify: `ClaudeViewer.sln` (add the project reference — done by `dotnet sln add`)

**Step 1: Scaffold the project**

Run from `C:\Projects\ClaudeViewer`:
```powershell
dotnet new xunit -n ClaudeViewer.Tests -o ClaudeViewer.Tests --framework net10.0
dotnet sln ClaudeViewer.sln add ClaudeViewer.Tests/ClaudeViewer.Tests.csproj
dotnet add ClaudeViewer.Tests/ClaudeViewer.Tests.csproj reference ClaudeViewer/ClaudeViewer.csproj
dotnet add ClaudeViewer.Tests/ClaudeViewer.Tests.csproj package FluentAssertions
```

**Step 2: Replace the template `UnitTest1.cs` with a `Usings.cs`**

Delete `ClaudeViewer.Tests/UnitTest1.cs`. Create `ClaudeViewer.Tests/Usings.cs`:
```csharp
global using Xunit;
global using FluentAssertions;
global using ClaudeViewer.Models;
global using ClaudeViewer.Services;
```

**Step 3: Verify the test project builds and the empty test set "passes"**

Run: `dotnet test ClaudeViewer.Tests/ClaudeViewer.Tests.csproj`
Expected: build succeeds; "Total tests: 0. Passed: 0. Failed: 0." (or similar — zero tests, zero failures).

**Step 4: Commit**
```powershell
git add ClaudeViewer.Tests/ ClaudeViewer.sln
git commit -m "Add ClaudeViewer.Tests xUnit project"
```

---

## Task 2: Add test fixtures for the Reader

**Files:**
- Create: `ClaudeViewer.Tests/Fixtures/well_formed.json`
- Create: `ClaudeViewer.Tests/Fixtures/jsonc.json`
- Create: `ClaudeViewer.Tests/Fixtures/malformed.json`

**Step 1: Create `well_formed.json`**
```json
{
  "model": "claude-opus-4-7",
  "defaultMode": "default",
  "permissions": {
    "allow": ["Bash(git:*)"],
    "deny": []
  }
}
```

**Step 2: Create `jsonc.json` (with comments + trailing commas)**
```jsonc
{
  // top-level scalar
  "model": "claude-sonnet-4-6",
  /* block comment */
  "permissions": {
    "allow": [
      "Bash(npm:*)", // trailing item-level comment
    ],
  },
}
```

**Step 3: Create `malformed.json`**
```json
{ "model": "claude-opus", "permissions": { "allow":
```

**Step 4: Mark fixtures as content in the csproj**

Edit `ClaudeViewer.Tests/ClaudeViewer.Tests.csproj`. Inside the `<Project>` element, add:
```xml
<ItemGroup>
  <None Update="Fixtures\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

**Step 5: Verify**

Run: `dotnet build ClaudeViewer.Tests/ClaudeViewer.Tests.csproj`
Expected: build succeeds. Confirm fixtures land in output: `ls ClaudeViewer.Tests/bin/Debug/net10.0/Fixtures` should list all three files.

**Step 6: Commit**
```powershell
git add ClaudeViewer.Tests/Fixtures/ ClaudeViewer.Tests/ClaudeViewer.Tests.csproj
git commit -m "Add reader test fixtures (well-formed/jsonc/malformed)"
```

---

## Task 3: Create `Models/MergedSetting.cs` DTO

**Files:**
- Create: `ClaudeViewer/Models/MergedSetting.cs`

**Step 1: Write the DTO**
```csharp
namespace ClaudeViewer.Models;

/// <summary>
/// One row in the merged-effective config TreeList. Either a leaf (scalar or array entry)
/// or an intermediate group node. The DTO has no logic — Merger fills it, TreeList binds to it.
/// </summary>
public sealed class MergedSetting
{
    public int Id { get; init; }
    public int? ParentId { get; init; }
    public string Key { get; init; } = "";
    public string KeyPath { get; init; } = "";
    public string? Managed { get; init; }
    public string? User { get; init; }
    public string? Project { get; init; }
    public string? Local { get; init; }
    public string? Effective { get; init; }
    public string Winner { get; init; } = "";
    public bool IsGroup { get; init; }
}
```

**Step 2: Verify**

Run: `dotnet build ClaudeViewer/ClaudeViewer.csproj`
Expected: build succeeds, zero warnings.

**Step 3: Commit**
```powershell
git add ClaudeViewer/Models/MergedSetting.cs
git commit -m "Add MergedSetting DTO for config TreeList rows"
```

---

## Task 4: Reader test — well-formed JSON

**Files:**
- Create: `ClaudeViewer.Tests/ClaudeSettingsReaderTests.cs`

**Step 1: Write the failing test**
```csharp
using System.IO;
using System.Text.Json;

namespace ClaudeViewer.Tests;

public class ClaudeSettingsReaderTests
{
    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Read_WellFormed_ReturnsRootWithNoError()
    {
        var result = ClaudeSettingsReader.Read(Fixture("well_formed.json"));

        result.Root.Should().NotBeNull();
        result.Error.Should().BeNull();
        result.Root!.Value.GetProperty("model").GetString().Should().Be("claude-opus-4-7");
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test ClaudeViewer.Tests --filter "Read_WellFormed_ReturnsRootWithNoError"`
Expected: compile error — `ClaudeSettingsReader` doesn't exist yet.

**Step 3: Create the minimal implementation**

Create `ClaudeViewer/Services/ClaudeSettingsReader.cs`:
```csharp
using System.IO;
using System.Text.Json;

namespace ClaudeViewer.Services;

/// <summary>
/// One scope's parsed settings.json. Exactly one of Root/Error is non-null when the file
/// existed (Root on success, Error on parse failure). Both null = file missing.
/// </summary>
public sealed record ScopeContents(JsonElement? Root, string? Error)
{
    public static ScopeContents Missing { get; } = new(null, null);
}

public static class ClaudeSettingsReader
{
    private static readonly JsonDocumentOptions JsoncOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ScopeContents Read(string path)
    {
        if (!File.Exists(path)) return ScopeContents.Missing;

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json, JsoncOptions);
            return new ScopeContents(doc.RootElement.Clone(), null);
        }
        catch (JsonException ex)
        {
            return new ScopeContents(null, ex.Message);
        }
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test ClaudeViewer.Tests --filter "Read_WellFormed_ReturnsRootWithNoError"`
Expected: PASS.

**Step 5: Commit**
```powershell
git add ClaudeViewer/Services/ClaudeSettingsReader.cs ClaudeViewer.Tests/ClaudeSettingsReaderTests.cs
git commit -m "Add ClaudeSettingsReader for well-formed JSON"
```

---

## Task 5: Reader test — missing file

**Files:**
- Modify: `ClaudeViewer.Tests/ClaudeSettingsReaderTests.cs`

**Step 1: Add the failing test (append to the class)**
```csharp
[Fact]
public void Read_MissingFile_ReturnsMissingSentinel()
{
    var result = ClaudeSettingsReader.Read(Path.Combine(AppContext.BaseDirectory, "Fixtures", "does_not_exist.json"));

    result.Root.Should().BeNull();
    result.Error.Should().BeNull();
}
```

**Step 2: Run test**

Run: `dotnet test ClaudeViewer.Tests --filter "Read_MissingFile"`
Expected: PASS (reader already handles missing in Task 4).

**Step 3: Commit**
```powershell
git add ClaudeViewer.Tests/ClaudeSettingsReaderTests.cs
git commit -m "Add reader test: missing file returns sentinel"
```

---

## Task 6: Reader test — malformed JSON

**Files:**
- Modify: `ClaudeViewer.Tests/ClaudeSettingsReaderTests.cs`

**Step 1: Add the test**
```csharp
[Fact]
public void Read_Malformed_ReturnsErrorMessage()
{
    var result = ClaudeSettingsReader.Read(Fixture("malformed.json"));

    result.Root.Should().BeNull();
    result.Error.Should().NotBeNullOrWhiteSpace();
}
```

**Step 2: Run, expect PASS** (handled by the try/catch in Task 4).

Run: `dotnet test ClaudeViewer.Tests --filter "Read_Malformed"`

**Step 3: Commit**
```powershell
git add ClaudeViewer.Tests/ClaudeSettingsReaderTests.cs
git commit -m "Add reader test: malformed JSON returns error"
```

---

## Task 7: Reader test — JSONC support

**Files:**
- Modify: `ClaudeViewer.Tests/ClaudeSettingsReaderTests.cs`

**Step 1: Add the test**
```csharp
[Fact]
public void Read_Jsonc_ParsesCommentsAndTrailingCommas()
{
    var result = ClaudeSettingsReader.Read(Fixture("jsonc.json"));

    result.Root.Should().NotBeNull();
    result.Error.Should().BeNull();
    result.Root!.Value.GetProperty("model").GetString().Should().Be("claude-sonnet-4-6");
    result.Root!.Value
        .GetProperty("permissions").GetProperty("allow")[0].GetString()
        .Should().Be("Bash(npm:*)");
}
```

**Step 2: Run, expect PASS** (`JsoncOptions` already handles both).

Run: `dotnet test ClaudeViewer.Tests --filter "Read_Jsonc"`

**Step 3: Run the whole reader suite to confirm all green**

Run: `dotnet test ClaudeViewer.Tests --filter "ClaudeSettingsReaderTests"`
Expected: 4 passed.

**Step 4: Commit**
```powershell
git add ClaudeViewer.Tests/ClaudeSettingsReaderTests.cs
git commit -m "Add reader test: JSONC (comments + trailing commas)"
```

---

## Task 8: Merger — scaffold + empty-input test

**Files:**
- Create: `ClaudeViewer/Services/ClaudeSettingsMerger.cs`
- Create: `ClaudeViewer.Tests/ClaudeSettingsMergerTests.cs`

**Background — precedence:** Claude Code merges scalars by precedence (highest wins) and concatenates arrays. The implementer MUST verify the current precedence order against [Claude Code's settings docs](https://code.claude.com/docs/en/settings#configuration-scopes) before encoding it. As of the design date the assumption is: **Managed > Local > Project > User** (Managed is enterprise policy and can't be overridden; Local overrides Project which overrides User). If docs disagree, fix the `Precedence` constant — the rest of the merger reads it.

**Step 1: Write the failing test**
```csharp
using System.ComponentModel;
using System.Text.Json;

namespace ClaudeViewer.Tests;

public class ClaudeSettingsMergerTests
{
    private static ScopeContents Empty => ScopeContents.Missing;

    private static ScopeContents Parse(string json)
        => new(JsonDocument.Parse(json).RootElement.Clone(), null);

    [Fact]
    public void Merge_AllScopesEmpty_ReturnsEmptyList()
    {
        var list = ClaudeSettingsMerger.Merge(Empty, Empty, Empty, Empty);

        list.Should().BeEmpty();
    }
}
```

**Step 2: Run, expect compile error**

Run: `dotnet test ClaudeViewer.Tests --filter "Merge_AllScopesEmpty"`
Expected: compile error — `ClaudeSettingsMerger` doesn't exist.

**Step 3: Write the minimal implementation**
```csharp
using System.ComponentModel;
using System.Text.Json;
using ClaudeViewer.Models;

namespace ClaudeViewer.Services;

public static class ClaudeSettingsMerger
{
    // Precedence: highest first. Verify against current Claude Code docs before changing.
    public static readonly string[] Precedence = ["Managed", "Local", "Project", "User"];

    public static BindingList<MergedSetting> Merge(
        ScopeContents managed,
        ScopeContents user,
        ScopeContents project,
        ScopeContents local)
    {
        var list = new BindingList<MergedSetting>();
        // TODO: walk each scope's JSON, build parent/child rows
        return list;
    }
}
```

**Step 4: Run, expect PASS**

Run: `dotnet test ClaudeViewer.Tests --filter "Merge_AllScopesEmpty"`
Expected: PASS.

**Step 5: Commit**
```powershell
git add ClaudeViewer/Services/ClaudeSettingsMerger.cs ClaudeViewer.Tests/ClaudeSettingsMergerTests.cs
git commit -m "Scaffold ClaudeSettingsMerger with empty-input case"
```

---

## Task 9: Merger — single scalar in one scope

**Files:**
- Modify: `ClaudeViewer.Tests/ClaudeSettingsMergerTests.cs`
- Modify: `ClaudeViewer/Services/ClaudeSettingsMerger.cs`

**Step 1: Write the failing test**
```csharp
[Fact]
public void Merge_ScalarInUserOnly_RowWinnerIsUser()
{
    var user = Parse("""{"model":"claude-opus-4-7"}""");

    var list = ClaudeSettingsMerger.Merge(Empty, user, Empty, Empty);

    var row = list.Should().ContainSingle(r => r.KeyPath == "model").Subject;
    row.Winner.Should().Be("User");
    row.User.Should().Be("\"claude-opus-4-7\"");
    row.Managed.Should().BeNull();
    row.Project.Should().BeNull();
    row.Local.Should().BeNull();
    row.Effective.Should().Be("\"claude-opus-4-7\"");
    row.IsGroup.Should().BeFalse();
}
```

**Step 2: Run, expect FAIL** — list is currently empty.

**Step 3: Implement the scalar walker**

Replace the `Merge` method body with:
```csharp
public static BindingList<MergedSetting> Merge(
    ScopeContents managed,
    ScopeContents user,
    ScopeContents project,
    ScopeContents local)
{
    var list = new BindingList<MergedSetting>();
    var nextId = 1;
    var pathToId = new Dictionary<string, int>(); // KeyPath → row Id (for parent linking, future use)

    // Accumulate per-scope value snippets per KeyPath
    var scalars = new Dictionary<string, ScalarAccum>(StringComparer.Ordinal);

    void WalkScalars(JsonElement el, string path, string scope)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    var childPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
                    WalkScalars(prop.Value, childPath, scope);
                }
                break;

            case JsonValueKind.Array:
                // Arrays handled in Task 11 — placeholder for now.
                break;

            default:
                if (!scalars.TryGetValue(path, out var acc))
                    scalars[path] = acc = new ScalarAccum();
                acc.Set(scope, el.GetRawText());
                break;
        }
    }

    if (managed.Root is { } m) WalkScalars(m, "", "Managed");
    if (user.Root is { } u) WalkScalars(u, "", "User");
    if (project.Root is { } p) WalkScalars(p, "", "Project");
    if (local.Root is { } l) WalkScalars(l, "", "Local");

    foreach (var (path, acc) in scalars)
    {
        var winner = Precedence.FirstOrDefault(s => acc.Get(s) is not null) ?? "";
        var key = path.Contains('.') ? path[(path.LastIndexOf('.') + 1)..] : path;
        list.Add(new MergedSetting
        {
            Id = nextId++,
            ParentId = null, // parent linking added in Task 12
            Key = key,
            KeyPath = path,
            Managed = acc.Managed,
            User = acc.User,
            Project = acc.Project,
            Local = acc.Local,
            Effective = winner == "" ? null : acc.Get(winner),
            Winner = winner,
            IsGroup = false,
        });
    }

    return list;
}

private sealed class ScalarAccum
{
    public string? Managed { get; private set; }
    public string? User { get; private set; }
    public string? Project { get; private set; }
    public string? Local { get; private set; }

    public void Set(string scope, string value)
    {
        switch (scope)
        {
            case "Managed": Managed = value; break;
            case "User": User = value; break;
            case "Project": Project = value; break;
            case "Local": Local = value; break;
        }
    }

    public string? Get(string scope) => scope switch
    {
        "Managed" => Managed,
        "User" => User,
        "Project" => Project,
        "Local" => Local,
        _ => null,
    };
}
```

**Step 4: Run, expect PASS**

Run: `dotnet test ClaudeViewer.Tests --filter "Merge_ScalarInUserOnly"`

**Step 5: Commit**
```powershell
git add ClaudeViewer/Services/ClaudeSettingsMerger.cs ClaudeViewer.Tests/ClaudeSettingsMergerTests.cs
git commit -m "Merge scalar values from one scope"
```

---

## Task 10: Merger — precedence with multiple scopes

**Files:**
- Modify: `ClaudeViewer.Tests/ClaudeSettingsMergerTests.cs`

**Step 1: Write the failing test (no impl change needed — already handled by Task 9)**
```csharp
[Fact]
public void Merge_ScalarInMultipleScopes_HighestPrecedenceWins()
{
    var user = Parse("""{"model":"u"}""");
    var project = Parse("""{"model":"p"}""");
    var local = Parse("""{"model":"l"}""");

    var list = ClaudeSettingsMerger.Merge(Empty, user, project, local);

    var row = list.Should().ContainSingle().Subject;
    row.Winner.Should().Be("Local");
    row.Effective.Should().Be("\"l\"");
    row.User.Should().Be("\"u\"");
    row.Project.Should().Be("\"p\"");
    row.Local.Should().Be("\"l\"");
}

[Fact]
public void Merge_ManagedAlwaysWins_WhenAllFourSet()
{
    var managed = Parse("""{"model":"m"}""");
    var user = Parse("""{"model":"u"}""");
    var project = Parse("""{"model":"p"}""");
    var local = Parse("""{"model":"l"}""");

    var list = ClaudeSettingsMerger.Merge(managed, user, project, local);

    var row = list.Should().ContainSingle().Subject;
    row.Winner.Should().Be("Managed");
    row.Effective.Should().Be("\"m\"");
}
```

**Step 2: Run, expect PASS for both** (precedence array drives it).

Run: `dotnet test ClaudeViewer.Tests --filter "Merge_Scalar"`
Expected: 3 passed (the two new + the one from Task 9).

**Step 3: Commit**
```powershell
git add ClaudeViewer.Tests/ClaudeSettingsMergerTests.cs
git commit -m "Test merger precedence ordering across scopes"
```

---

## Task 11: Merger — array union

**Files:**
- Modify: `ClaudeViewer.Tests/ClaudeSettingsMergerTests.cs`
- Modify: `ClaudeViewer/Services/ClaudeSettingsMerger.cs`

**Step 1: Write the failing test**
```csharp
[Fact]
public void Merge_ArrayConcatenatedAcrossScopes_EachEntryHasOwnWinner()
{
    var user = Parse("""{"permissions":{"allow":["Bash(git:*)"]}}""");
    var project = Parse("""{"permissions":{"allow":["Bash(npm:*)"]}}""");

    var list = ClaudeSettingsMerger.Merge(Empty, user, project, Empty);

    var leaves = list.Where(r => !r.IsGroup && r.KeyPath.StartsWith("permissions.allow[")).ToList();
    leaves.Should().HaveCount(2);
    leaves.Should().ContainSingle(r => r.Winner == "User" && r.Effective == "\"Bash(git:*)\"");
    leaves.Should().ContainSingle(r => r.Winner == "Project" && r.Effective == "\"Bash(npm:*)\"");
}
```

**Step 2: Run, expect FAIL** — arrays are currently skipped.

**Step 3: Extend `WalkScalars` to handle arrays**

Replace the `case JsonValueKind.Array:` block in `WalkScalars` with:
```csharp
case JsonValueKind.Array:
    {
        var i = 0;
        foreach (var item in el.EnumerateArray())
        {
            var itemPath = $"{path}[{i}]";
            // Array items are emitted as standalone leaves with Winner = the contributing scope.
            // We do this inline instead of via the scalars dict because there's no merge — union, not winner-take-all.
            list.Add(new MergedSetting
            {
                Id = nextId++,
                ParentId = null, // parent linking added in Task 12
                Key = $"[{i}]",
                KeyPath = itemPath,
                Managed = scope == "Managed" ? item.GetRawText() : null,
                User = scope == "User" ? item.GetRawText() : null,
                Project = scope == "Project" ? item.GetRawText() : null,
                Local = scope == "Local" ? item.GetRawText() : null,
                Effective = item.GetRawText(),
                Winner = scope,
                IsGroup = false,
            });
            i++;
        }
        break;
    }
```

> Note: `list` and `nextId` must be captured by the local function. If C# complains about capturing `nextId` (an int), refactor: keep `nextId` as `int[] nextId = [1]` and use `nextId[0]++`, OR move the array emit code into the calling site. Simplest: change `int nextId = 1` to `var nextId = new int[] { 1 };` and use `nextId[0]++` everywhere.

**Step 4: Run, expect PASS**

Run: `dotnet test ClaudeViewer.Tests --filter "Merge_ArrayConcatenated"`

**Step 5: Commit**
```powershell
git add ClaudeViewer/Services/ClaudeSettingsMerger.cs ClaudeViewer.Tests/ClaudeSettingsMergerTests.cs
git commit -m "Merger: union array entries across scopes"
```

---

## Task 12: Merger — parent group rows

**Files:**
- Modify: `ClaudeViewer.Tests/ClaudeSettingsMergerTests.cs`
- Modify: `ClaudeViewer/Services/ClaudeSettingsMerger.cs`

**Step 1: Write the failing test**
```csharp
[Fact]
public void Merge_NestedPath_CreatesGroupRowsAsParents()
{
    var user = Parse("""{"permissions":{"allow":["Bash(git:*)"]}}""");

    var list = ClaudeSettingsMerger.Merge(Empty, user, Empty, Empty);

    var permissions = list.Should().ContainSingle(r => r.KeyPath == "permissions").Subject;
    permissions.IsGroup.Should().BeTrue();
    permissions.ParentId.Should().BeNull();
    permissions.Winner.Should().BeEmpty();

    var allow = list.Should().ContainSingle(r => r.KeyPath == "permissions.allow").Subject;
    allow.IsGroup.Should().BeTrue();
    allow.ParentId.Should().Be(permissions.Id);

    var leaf = list.Should().ContainSingle(r => r.KeyPath == "permissions.allow[0]").Subject;
    leaf.ParentId.Should().Be(allow.Id);
}
```

**Step 2: Run, expect FAIL** — no group rows are emitted.

**Step 3: Add a `GetOrCreateGroup` helper and use it everywhere a leaf is emitted**

Add inside `Merge`, near the top:
```csharp
int GetOrCreateGroup(string parentPath)
{
    if (string.IsNullOrEmpty(parentPath)) return 0; // sentinel for "no parent"
    if (pathToId.TryGetValue(parentPath, out var existing)) return existing;

    // Recursively ensure grandparent exists first
    var dotIdx = parentPath.LastIndexOf('.');
    var bracketIdx = parentPath.LastIndexOf('[');
    var splitIdx = Math.Max(dotIdx, bracketIdx);
    var grandparentPath = splitIdx > 0 ? parentPath[..splitIdx] : "";
    var grandparentId = GetOrCreateGroup(grandparentPath);

    var key = splitIdx > 0 ? parentPath[(splitIdx + (parentPath[splitIdx] == '.' ? 1 : 0))..] : parentPath;
    var id = nextId[0]++;
    pathToId[parentPath] = id;
    list.Add(new MergedSetting
    {
        Id = id,
        ParentId = grandparentId == 0 ? null : grandparentId,
        Key = key,
        KeyPath = parentPath,
        IsGroup = true,
    });
    return id;
}
```

Then, when emitting any leaf (scalar or array item), set `ParentId`:
- For scalars: compute `parentPath` as everything before the final `.segment`; call `GetOrCreateGroup(parentPath)`; assign to the new row's `ParentId` (use `null` when result is `0`).
- For array items: parent path is `path` itself (the array key, e.g. `permissions.allow`); call `GetOrCreateGroup(path)`.

Concretely, in the scalar leaf emit (the existing `foreach (var (path, acc) in scalars)` loop), replace:
```csharp
ParentId = null,
```
with:
```csharp
var dotIdx = path.LastIndexOf('.');
var bracketIdx = path.LastIndexOf('[');
var splitIdx = Math.Max(dotIdx, bracketIdx);
var parentPath = splitIdx > 0 ? path[..splitIdx] : "";
var parentId = GetOrCreateGroup(parentPath);
// ...then in the new MergedSetting: ParentId = parentId == 0 ? null : parentId,
```

And in the array item emit, replace `ParentId = null` with `ParentId = GetOrCreateGroup(path)` (computed once outside the inner loop) and assign `parentId == 0 ? null : parentId`.

After these changes, also iterate `scalars` in sorted-by-path order so parent groups are emitted before leaves — important for the TreeList:
```csharp
foreach (var (path, acc) in scalars.OrderBy(kv => kv.Key, StringComparer.Ordinal))
```

**Step 4: Run, expect PASS**

Run: `dotnet test ClaudeViewer.Tests --filter "Merge_NestedPath"`

Then run the full merger suite to confirm prior tests still pass:
```powershell
dotnet test ClaudeViewer.Tests --filter "ClaudeSettingsMergerTests"
```
Expected: 5 passed.

**Step 5: Commit**
```powershell
git add ClaudeViewer/Services/ClaudeSettingsMerger.cs ClaudeViewer.Tests/ClaudeSettingsMergerTests.cs
git commit -m "Merger: create parent group rows for hierarchical keys"
```

---

## Task 13: Merger — parse-error sentinel propagation

**Files:**
- Modify: `ClaudeViewer.Tests/ClaudeSettingsMergerTests.cs`
- Modify: `ClaudeViewer/Services/ClaudeSettingsMerger.cs`

**Step 1: Write the failing test**
```csharp
[Fact]
public void Merge_ScopeWithParseError_EmitsSentinelRow()
{
    var user = new ScopeContents(null, "Unexpected end of JSON at line 3");

    var list = ClaudeSettingsMerger.Merge(Empty, user, Empty, Empty);

    var sentinel = list.Should().ContainSingle().Subject;
    sentinel.Key.Should().Be("<parse error>");
    sentinel.Winner.Should().Be("User");
    sentinel.User.Should().Contain("Unexpected end of JSON");
}
```

**Step 2: Run, expect FAIL.**

**Step 3: Add error-row emission at the top of `Merge`**

Just before the `WalkScalars` calls, add:
```csharp
void EmitErrorIfAny(ScopeContents sc, string scope)
{
    if (sc.Error is null) return;
    list.Add(new MergedSetting
    {
        Id = nextId[0]++,
        ParentId = null,
        Key = "<parse error>",
        KeyPath = $"<{scope}:error>",
        Managed = scope == "Managed" ? sc.Error : null,
        User = scope == "User" ? sc.Error : null,
        Project = scope == "Project" ? sc.Error : null,
        Local = scope == "Local" ? sc.Error : null,
        Effective = null,
        Winner = scope,
        IsGroup = false,
    });
}

EmitErrorIfAny(managed, "Managed");
EmitErrorIfAny(user, "User");
EmitErrorIfAny(project, "Project");
EmitErrorIfAny(local, "Local");
```

**Step 4: Run, expect PASS**

Run: `dotnet test ClaudeViewer.Tests --filter "Merge_ScopeWithParseError"`

Then full merger suite:
```powershell
dotnet test ClaudeViewer.Tests
```
Expected: all merger + reader tests pass.

**Step 5: Commit**
```powershell
git add ClaudeViewer/Services/ClaudeSettingsMerger.cs ClaudeViewer.Tests/ClaudeSettingsMergerTests.cs
git commit -m "Merger: surface scope parse errors as sentinel rows"
```

---

## Task 14: `ConfigPanel` UserControl (UI scaffolding, no logic)

**Files:**
- Create: `ClaudeViewer/Controls/ConfigPanel.cs`

**Step 1: Write the control**
```csharp
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using ClaudeViewer.Models;
using ClaudeViewer.Services;
using DevExpress.XtraEditors;
using DevExpress.XtraTreeList;
using DevExpress.XtraTreeList.Columns;

namespace ClaudeViewer.Controls;

public sealed class ConfigPanel : XtraUserControl
{
    private readonly TreeList _treeList = new();
    private readonly SimpleButton _reloadBtn = new() { Text = "Reload", Width = 90 };
    private readonly LabelControl _rootLabel = new() { AutoSizeMode = LabelAutoSizeMode.None, Dock = DockStyle.Fill };

    private string? _currentRoot;

    public ConfigPanel()
    {
        Dock = DockStyle.Fill;

        var header = new PanelControl
        {
            Dock = DockStyle.Top,
            Height = 38,
            BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder,
        };
        _reloadBtn.Dock = DockStyle.Right;
        _reloadBtn.Click += async (_, _) => await ReloadAsync();
        header.Controls.Add(_rootLabel);
        header.Controls.Add(_reloadBtn);

        _treeList.Dock = DockStyle.Fill;
        _treeList.OptionsBehavior.Editable = false;
        _treeList.OptionsView.ShowAutoFilterRow = true;
        _treeList.KeyFieldName = nameof(MergedSetting.Id);
        _treeList.ParentFieldName = nameof(MergedSetting.ParentId);
        AddColumn(nameof(MergedSetting.Key), "Key", 220);
        AddColumn(nameof(MergedSetting.Managed), "Managed", 100);
        AddColumn(nameof(MergedSetting.User), "User", 100);
        AddColumn(nameof(MergedSetting.Project), "Project", 100);
        AddColumn(nameof(MergedSetting.Local), "Local", 100);
        AddColumn(nameof(MergedSetting.Effective), "Effective", 160);
        AddColumn(nameof(MergedSetting.Winner), "Winner", 90);
        _treeList.DoubleClick += OnDoubleClick;

        Controls.Add(_treeList);
        Controls.Add(header);
    }

    public async Task LoadAsync(string root)
    {
        _currentRoot = root;
        _rootLabel.Text = $"Project root: {root}";
        _reloadBtn.Enabled = false;
        try
        {
            var list = await Task.Run(() =>
            {
                var managed = ClaudeSettingsReader.Read(@"C:\ProgramData\ClaudeCode\managed-settings.json");
                var user = ClaudeSettingsReader.Read(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"));
                var project = ClaudeSettingsReader.Read(Path.Combine(root, ".claude", "settings.json"));
                var local = ClaudeSettingsReader.Read(Path.Combine(root, ".claude", "settings.local.json"));
                return ClaudeSettingsMerger.Merge(managed, user, project, local);
            });

            _treeList.DataSource = list;
            _treeList.ExpandAll();
        }
        finally
        {
            _reloadBtn.Enabled = true;
        }
    }

    private Task ReloadAsync() => _currentRoot is null ? Task.CompletedTask : LoadAsync(_currentRoot);

    private void AddColumn(string fieldName, string caption, int width)
    {
        var col = new TreeListColumn
        {
            FieldName = fieldName,
            Caption = caption,
            Width = width,
            VisibleIndex = _treeList.Columns.Count,
        };
        _treeList.Columns.Add(col);
    }

    private void OnDoubleClick(object? sender, EventArgs e)
    {
        var node = _treeList.FocusedNode;
        if (node is null) return;
        if (_treeList.GetDataRecordByNode(node) is not MergedSetting row) return;
        if (row.IsGroup || string.IsNullOrEmpty(row.Winner) || _currentRoot is null) return;

        var path = ResolveScopeFile(row.Winner, _currentRoot);
        if (!File.Exists(path)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            XtraMessageBox.Show($"Couldn't open {path}: {ex.Message}");
        }
    }

    private static string ResolveScopeFile(string scope, string root) => scope switch
    {
        "Managed" => @"C:\ProgramData\ClaudeCode\managed-settings.json",
        "User" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"),
        "Project" => Path.Combine(root, ".claude", "settings.json"),
        "Local" => Path.Combine(root, ".claude", "settings.local.json"),
        _ => "",
    };
}
```

**Step 2: Verify it builds**

Run: `dotnet build ClaudeViewer/ClaudeViewer.csproj`
Expected: 0 warnings, 0 errors.

**Step 3: Commit**
```powershell
git add ClaudeViewer/Controls/ConfigPanel.cs
git commit -m "Add ConfigPanel UserControl (TreeList + reload + double-click)"
```

---

## Task 15: `ConfigForm` thin shell

**Files:**
- Create: `ClaudeViewer/Controls/ConfigForm.cs`

**Step 1: Write the form**
```csharp
using DevExpress.XtraEditors;

namespace ClaudeViewer.Controls;

public sealed class ConfigForm : XtraForm
{
    public ConfigPanel Panel { get; }

    public ConfigForm()
    {
        Text = "Claude Code Configuration";
        Panel = new ConfigPanel();
        Controls.Add(Panel);
        ClientSize = new System.Drawing.Size(1100, 600);
    }
}
```

**Step 2: Build**

Run: `dotnet build`
Expected: clean.

**Step 3: Commit**
```powershell
git add ClaudeViewer/Controls/ConfigForm.cs
git commit -m "Add ConfigForm thin XtraForm shell"
```

---

## Task 16: Integrate Config tab into `MainForm`

**Files:**
- Modify: `ClaudeViewer/MainForm.cs`

**Background:** Before making changes, run `dotnet build` to confirm a clean baseline. Open `MainForm.cs` and locate (a) the header strip where `_compareBtn` and `_changeBtn` are added, (b) the `RebuildWatcher` method, (c) the existing tab-open patterns (`OpenArtifact`, `_openTabs`).

**Step 1: Add the field, button, and handler**

Near the existing `_openTabs` field, add:
```csharp
private Controls.ConfigForm? _configTab;
```

In the header construction (next to `_compareBtn`/`_changeBtn`), add:
```csharp
var configBtn = new DevExpress.XtraEditors.SimpleButton
{
    Text = "Config",
    Width = 90,
    Dock = DockStyle.Right,
};
configBtn.Click += OnConfigClick;
// add `configBtn` to the header's Controls collection in the same place the other buttons go
```

Add the handler method:
```csharp
private async void OnConfigClick(object? sender, EventArgs e)
{
    if (_configTab is not null && !_configTab.IsDisposed)
    {
        _configTab.Activate();
        return;
    }

    _configTab = new Controls.ConfigForm();
    _configTab.FormClosed += (_, _) => _configTab = null;
    _configTab.MdiParent = this;
    _configTab.Show();
    await _configTab.Panel.LoadAsync(_watcher.Root);
    // ^^ replace `_watcher.Root` with whatever the field/property on ArtifactWatcher is called
    //    for the current watched folder. If it isn't exposed, expose it.
}
```

**Step 2: Hook `RebuildWatcher` to reload the open config tab**

At the end of `RebuildWatcher` (after the new watcher is assigned), add:
```csharp
if (_configTab is { IsDisposed: false })
{
    _ = _configTab.Panel.LoadAsync(_watcher.Root);
}
```

**Step 3: Build**

Run: `dotnet build`
Expected: clean. If `ArtifactWatcher.Root` doesn't exist as a public property, add it:
```csharp
public string Root => _root;
```
in `Services/ArtifactWatcher.cs`.

**Step 4: Commit**
```powershell
git add ClaudeViewer/MainForm.cs ClaudeViewer/Services/ArtifactWatcher.cs
git commit -m "Wire Config tab into MainForm header and RebuildWatcher"
```

---

## Task 17: Run the full test suite

**Step 1: Run all tests**

Run: `dotnet test ClaudeViewer.Tests --logger "console;verbosity=normal"`
Expected: all reader + merger tests pass (4 + ~6 = ~10).

**Step 2: If anything fails**, invoke `superpowers:systematic-debugging` per the project's CLAUDE.md. Do not patch over failing tests; find the root cause.

**Step 3: Build clean**

Run: `dotnet build ClaudeViewer.sln`
Expected: 0 warnings, 0 errors across both projects.

---

## Task 18: Manual visual smoke test

**Per CLAUDE.md:** "Build passing + type-check clean + unit tests green ≠ 'it works.' Any UI change requires manual verification."

**Step 1: Prepare a real project for testing**

Create or confirm a local Claude Code override at `C:\Users\marti\.claude\settings.json` (the User scope; it may already exist).

Create a deliberate Local override in a real project, e.g. `C:\Projects\duetgpt\.claude\settings.local.json`:
```json
{
  "model": "claude-sonnet-4-6"
}
```

**Step 2: Launch ClaudeViewer**

```powershell
dotnet run --project ClaudeViewer
```

**Step 3: Run through the verification checklist**

| Check | Expected |
|---|---|
| App launches, no exceptions | Window appears, stays alive ≥4 s |
| Header now shows a "Config" button | Visible next to Compare and Change… |
| Click `Change…` → pick `C:\Projects\duetgpt` | Watched folder updates |
| Click `Config` | New MDI tab opens titled "Claude Code Configuration" |
| Tree populates with at least User-scope rows | Visible after ~ms |
| `model` row Winner = "Local", Local column = `"claude-sonnet-4-6"`, User column shows the User-scope value | Override semantics correct |
| Group rows (e.g., `permissions`) are expandable | Hierarchy renders |
| Auto-filter row works (filter Winner = "Local") | Only Local-winning rows visible |
| Double-click a leaf row → opens that scope's `settings.json` in the OS-registered .json editor | File opens externally |
| Click `Reload` → reflects an external edit made while the tab was open | Tree refreshes |
| Click `Change…` → switch to a different project root | Config tab automatically reloads against the new root |
| Click `Config` again while the tab is open | Focuses the existing tab, no duplicate |
| Close the tab, click `Config` again | Fresh instance opens, no stale data |

**Step 4: Take a screenshot of the populated tree for the SESSION_HANDOFF**

Save to `assets/config-tab.png` (or similar). Won't commit — just for the next session's reference.

**Step 5: Stop the app**

Per the user's CLAUDE.md service-lifecycle rule, the app must be stopped after testing — kill the `dotnet` process via Task Manager or `taskkill //PID <pid> //F //T`.

---

## Task 19: Update documentation

**Files:**
- Modify: `ARCHITECTURE.md` (add Config Panel to component map and file-by-file table)
- Modify: `SESSION_HANDOFF.md` (current status)
- Modify: `TODO.md` (move "config panel" to Done; add the deferred follow-ups)

**Step 1: ARCHITECTURE.md updates**

In the component map, add a `ConfigForm` box alongside `ArtifactForm` and `CompareForm`. In the file-by-file table, add four rows:
- `Models/MergedSetting.cs` — TreeList row DTO
- `Services/ClaudeSettingsReader.cs` — reads one settings.json; permissive JSONC; parse-error sentinel
- `Services/ClaudeSettingsMerger.cs` — pure: 4 ScopeContents → BindingList<MergedSetting> with parent/child rows; scalars use precedence, arrays union
- `Controls/ConfigPanel.cs` + `Controls/ConfigForm.cs` — read-only TreeList view of merged-effective config

Update the "What this app deliberately doesn't do" section — clarify "no write path" still holds: config edits happen via `Process.Start` to the OS-registered editor, not in-app.

**Step 2: SESSION_HANDOFF.md updates**

Bump Last session date to today. Append "Config viewer panel added: …" with file paths.

**Step 3: TODO.md updates**

Under "Done", add:
```
- [x] **Config viewer panel.** Read-only TreeList view of Claude Code's merged settings
  across Managed/User/Project/Local scopes. New MDI tab opened from a Config
  button. Per-scope columns + Winner. Double-click opens the winning scope's
  settings.json externally. Tied to the watched folder; manual Reload button.
  New `ClaudeViewer.Tests` xUnit project covers Reader + Merger semantics.
```

Under "Maybe / later" (or a new "Config panel polish" section), add:
- Inline editing via LayoutControl curated sections + JSON fallback (would relax the no-write-path invariant)
- FileSystemWatcher auto-refresh on the 4 scope files
- "Copy key path" / "Reveal in Explorer" context menu
- Dark theme styling for the Winner-column chips

**Step 4: Commit**
```powershell
git add ARCHITECTURE.md SESSION_HANDOFF.md TODO.md
git commit -m "Document config viewer panel"
```

---

## Done criteria

Before declaring the feature complete (per `verification-before-completion` skill):

1. `dotnet test ClaudeViewer.Tests` — all tests green, output captured.
2. `dotnet build ClaudeViewer.sln` — 0 warnings, 0 errors, output captured.
3. Task 18 visual smoke checklist — every row checked off.
4. Process killed; no orphaned dotnet processes (`Get-Process dotnet` is empty).
5. Working tree clean except for `assets/config-tab.png` if not committed.
6. Git log shows the expected sequence of commits.

Only after all six are confirmed: claim the feature is done.

---

## Notes for the implementer

- The merger code in Task 9 captures `nextId` and `list` in a local function. C# may complain about modifying a captured local `int`. The recommended workaround (mentioned in Task 11) is `int[] nextId = [1]; ... nextId[0]++;`. Apply this pattern from Task 9 onward so subsequent tasks don't need rework.
- `JsonElement.Clone()` is needed in the Reader because `JsonDocument` is `IDisposable` and the `using` statement disposes the doc at end of method. Cloning detaches the element so it can outlive the doc.
- The precedence assumption (`Managed > Local > Project > User`) is encoded once in `ClaudeSettingsMerger.Precedence`. Verify against current Claude Code docs before merging the feature. If wrong, change that one array — tests in Task 10 will need their expected Winner values updated accordingly.
- The visual smoke test (Task 18) IS the verification gate. Do not skip it because the unit tests passed — the design doc explicitly notes that merge correctness across real-world JSON shapes is hard to eyeball from the TreeList alone, and visual verification is how we catch wiring bugs the unit tests don't see.
