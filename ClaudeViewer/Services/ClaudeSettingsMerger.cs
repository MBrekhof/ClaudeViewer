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
                    {
                        var arrayParentId = GetOrCreateGroup(path);
                        var i = 0;
                        foreach (var item in el.EnumerateArray())
                        {
                            var itemPath = $"{path}[{i}]";
                            // Array items are emitted as standalone leaves with Winner = the contributing scope.
                            // We do this inline instead of via the scalars dict because there's no merge — union, not winner-take-all.
                            list.Add(new MergedSetting
                            {
                                Id = nextId[0]++,
                                ParentId = arrayParentId == 0 ? null : arrayParentId,
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

                default:
                    if (!scalars.TryGetValue(path, out var acc))
                        scalars[path] = acc = new ScalarAccum();
                    acc.Set(scope, el.GetRawText());
                    break;
            }
        }

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

        if (managed.Root is { } m) WalkScalars(m, "", "Managed");
        if (user.Root is { } u) WalkScalars(u, "", "User");
        if (project.Root is { } p) WalkScalars(p, "", "Project");
        if (local.Root is { } l) WalkScalars(l, "", "Local");

        foreach (var (path, acc) in scalars.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var winner = Precedence.FirstOrDefault(s => acc.Get(s) is not null) ?? "";
            var dotIdx = path.LastIndexOf('.');
            var bracketIdx = path.LastIndexOf('[');
            var splitIdx = Math.Max(dotIdx, bracketIdx);
            var parentPath = splitIdx > 0 ? path[..splitIdx] : "";
            var parentId = GetOrCreateGroup(parentPath);
            var key = splitIdx > 0 ? path[(splitIdx + (path[splitIdx] == '.' ? 1 : 0))..] : path;
            list.Add(new MergedSetting
            {
                Id = nextId[0]++,
                ParentId = parentId == 0 ? null : parentId,
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
