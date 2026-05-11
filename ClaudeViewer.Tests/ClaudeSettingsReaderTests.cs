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
