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
