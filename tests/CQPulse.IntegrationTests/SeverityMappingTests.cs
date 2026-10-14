using CQPulse.Application.Decisions;
using CQPulse.Infrastructure.Integrations.LogTriage;

namespace CQPulse.IntegrationTests;

/// <summary>Pure rules used to turn LogTriageAssistant findings into per-signature severity evidence.</summary>
public sealed class SeverityMappingTests
{
    private static LogTriageFinding Finding(int line, string? severity, int? endLine = null) =>
        new(line, endLine ?? line, null, "m", severity, "General", "e", "s");

    [Theory]
    [InlineData("INFO", 1)]
    [InlineData("medium", 2)]
    [InlineData(" HIGH ", 3)]
    [InlineData("CRITICAL", 4)]
    [InlineData("WARNING", null)]
    [InlineData(null, null)]
    public void Severity_labels_map_to_an_ordinal_and_unknown_labels_have_no_rank(string? label, int? expected) =>
        Assert.Equal(expected, SeverityScale.Rank(label));

    [Fact]
    public void Each_signature_gets_the_highest_severity_of_its_lines()
    {
        string[] lines = ["sigA", "sigB", "sigA", "sigC"];

        var map = AnalysisWorker.SeveritiesBySignature(
            [Finding(1, "MEDIUM"), Finding(3, "CRITICAL"), Finding(2, "INFO"), Finding(2, "HIGH")], lines);

        Assert.Equal(new Dictionary<string, string> { ["sigA"] = "CRITICAL", ["sigB"] = "HIGH" }, map);
        Assert.False(map.ContainsKey("sigC")); // no finding: no severity (not "INFO", not 0)
    }

    [Fact]
    public void Multi_line_findings_cover_their_whole_range()
    {
        var map = AnalysisWorker.SeveritiesBySignature([Finding(2, "HIGH", endLine: 3)], ["a", "b", "c", "d"]);

        Assert.Equal(["b", "c"], map.Keys.Order());
    }

    [Fact]
    public void Findings_outside_the_document_or_with_unknown_labels_are_ignored()
    {
        var map = AnalysisWorker.SeveritiesBySignature(
            [Finding(0, "HIGH"), Finding(9, "CRITICAL"), Finding(1, "WARNING"), Finding(2, "MEDIUM", endLine: 1)], ["a", "b"]);

        Assert.Equal(new Dictionary<string, string> { ["b"] = "MEDIUM" }, map);
    }
}
