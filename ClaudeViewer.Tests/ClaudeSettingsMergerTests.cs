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
}
