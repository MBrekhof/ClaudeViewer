namespace ClaudeViewer.Models;

public sealed class Artifact
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public required DateTime ModifiedAt { get; init; }
    public required ArtifactKind Kind { get; init; }
    public string? Title { get; init; }
    public long SizeBytes { get; init; }
    public string Folder { get; init; } = "";

    public string SizeDisplay => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        _ => $"{SizeBytes / (1024.0 * 1024):F1} MB",
    };

    public string KindDisplay => Kind switch
    {
        ArtifactKind.Html => "HTML",
        ArtifactKind.Markdown => "MD",
        _ => "—",
    };
}

public enum ArtifactKind
{
    Html,
    Markdown,
    Other,
}
