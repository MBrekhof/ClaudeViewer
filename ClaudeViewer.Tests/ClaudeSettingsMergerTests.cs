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
