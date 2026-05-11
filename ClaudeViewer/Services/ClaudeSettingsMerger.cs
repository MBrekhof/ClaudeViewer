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
