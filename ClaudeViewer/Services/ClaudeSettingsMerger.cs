using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
        int[] nextId = [1];
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
                Id = nextId[0]++,
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
}
