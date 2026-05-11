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
}
