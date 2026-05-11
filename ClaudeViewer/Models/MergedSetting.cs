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
