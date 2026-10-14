using System.Text.Json;
using CQPulse.Application.Analysis;
using CQPulse.Application.Decisions;
using CQPulse.Application.Insights;
using CQPulse.Domain;
using CQPulse.Infrastructure.Integrations.LogTriage;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Infrastructure.Integrations.SmartDog;

internal sealed record DecisionEvidence(
    IReadOnlyList<DecisionOptionEvidenceDto> Options, bool SeverityAvailable, Guid? FailureAnalysisId, IReadOnlyList<string> Notes);

/// <summary>
/// Gathers the evidence for ranking a run's failure signatures, from persisted data only. Rules:
/// <list type="bullet">
/// <item>Options are the run's failure signatures, top <see cref="MaxOptions"/> by occurrences in the run (ties by signature).</item>
/// <item>Occurrences, 30-day project recurrence and distinct affected catalog test cases are exact counts.</item>
/// <item>Triage severity is used only if the run's latest completed LogTriageAssistant analysis has per-signature
/// severities, was made from exactly the run's current failure data (same input digest), and covers every option.
/// Otherwise the criterion is left out for all options and the reason is recorded: missing severity is never treated as 0.</item>
/// </list>
/// </summary>
internal static class DecisionEvidenceBuilder
{
    public const int MaxOptions = 25;
    private const int MaxGroups = 10_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<DecisionEvidence> BuildAsync(
        CQPulseDbContext db, Guid runId, Guid projectId, LogTriageOptions logTriageOptions, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var groups = await db.TestCaseResults.AsNoTracking()
            .Where(x => x.TestRunId == runId && x.Outcome == TestOutcome.Failed)
            .GroupBy(x => new { x.FailureCategory, x.ErrorMessage, x.TestCaseId })
            .Select(g => new { g.Key.FailureCategory, g.Key.ErrorMessage, g.Key.TestCaseId, Count = g.Count() })
            .Take(MaxGroups)
            .ToListAsync(cancellationToken);

        var signatures = groups
            .GroupBy(g => FailureSignature.Compute(g.FailureCategory, g.ErrorMessage))
            .Select(s =>
            {
                var sample = s.GroupBy(x => x.ErrorMessage).OrderByDescending(m => m.Sum(x => x.Count)).First();
                return new
                {
                    Signature = s.Key,
                    s.First().FailureCategory,
                    Sample = sample.Key,
                    Occurrences = s.Sum(x => x.Count),
                    AffectedTestCases = s.Where(x => x.TestCaseId != null).Select(x => x.TestCaseId).Distinct().Count()
                };
            })
            .OrderByDescending(s => s.Occurrences).ThenBy(s => s.Signature, StringComparer.Ordinal)
            .ToList();
        if (signatures.Count > MaxOptions)
            notes.Add($"The run has {signatures.Count} failure signatures; the {MaxOptions} most frequent were ranked.");
        var selected = signatures.Take(MaxOptions).ToList();

        // Project-wide recurrence over the last 30 UTC days (including this run).
        var window = InsightWindows.LastDays(30, now);
        var recurrenceGroups = await db.TestCaseResults.AsNoTracking()
            .Where(x => x.Outcome == TestOutcome.Failed && x.TestRun.ProjectId == projectId &&
                        x.CompletedAtUtc >= window.FromUtc && x.CompletedAtUtc < window.ToUtc)
            .GroupBy(x => new { x.FailureCategory, x.ErrorMessage })
            .Select(g => new { g.Key.FailureCategory, g.Key.ErrorMessage, Count = g.Count() })
            .Take(MaxGroups)
            .ToListAsync(cancellationToken);
        var recurrence = recurrenceGroups
            .GroupBy(g => FailureSignature.Compute(g.FailureCategory, g.ErrorMessage))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        var (severities, analysisId) = await ResolveSeveritiesAsync(db, runId, logTriageOptions, selected.Select(s => s.Signature).ToList(), notes, cancellationToken);

        var options = selected.Select((s, i) => new DecisionOptionEvidenceDto(
            $"sig-{s.Signature}", s.Signature, s.FailureCategory, FailureSignature.Normalize(s.Sample), s.Sample ?? "(no message)",
            s.Occurrences, recurrence.GetValueOrDefault(s.Signature), s.AffectedTestCases, // exact count in the window (0 if none)
            severities?.GetValueOrDefault(s.Signature))).ToList();
        if (options.All(o => o.AffectedTestCases == 0))
            notes.Add("No failure in this run is linked to a catalog test case (synthetic workload), so affected test cases is 0 for every option and cannot change the order.");
        return new DecisionEvidence(options, severities is not null, severities is null ? null : analysisId, notes);
    }

    private static async Task<(Dictionary<string, string>? Severities, Guid? AnalysisId)> ResolveSeveritiesAsync(
        CQPulseDbContext db, Guid runId, LogTriageOptions logTriageOptions, IReadOnlyList<string> signatures, List<string> notes,
        CancellationToken cancellationToken)
    {
        const string NotUsed = "Triage severity was not used: ";
        var analysis = await db.FailureAnalyses.AsNoTracking()
            .Where(x => x.TestRunId == runId && x.Status == AnalysisStatus.Succeeded)
            .OrderByDescending(x => x.CompletedAtUtc)
            .Select(x => new { x.Id, x.InputDigest, x.SignatureSeveritiesJson })
            .FirstOrDefaultAsync(cancellationToken);
        if (analysis is null)
        {
            notes.Add(NotUsed + "this run has no completed LogTriageAssistant analysis.");
            return (null, null);
        }
        if (string.IsNullOrWhiteSpace(analysis.SignatureSeveritiesJson))
        {
            notes.Add(NotUsed + "the latest analysis has no per-signature severities (re-analyze the run).");
            return (null, analysis.Id);
        }

        var document = await AnalysisDocumentBuilder.BuildAsync(db, runId, logTriageOptions, cancellationToken);
        if (!string.Equals(document.Digest, analysis.InputDigest, StringComparison.OrdinalIgnoreCase))
        {
            notes.Add(NotUsed + "the latest analysis was made from different input than the run's current failures (re-analyze the run).");
            return (null, analysis.Id);
        }

        Dictionary<string, string>? map;
        try { map = JsonSerializer.Deserialize<Dictionary<string, string>>(analysis.SignatureSeveritiesJson, Json); }
        catch (JsonException) { map = null; }
        if (map is null)
        {
            notes.Add(NotUsed + "the stored per-signature severities could not be read.");
            return (null, analysis.Id);
        }

        var covered = signatures.Count(s => map.TryGetValue(s, out var label) && SeverityScale.Rank(label) is not null);
        if (covered < signatures.Count)
        {
            notes.Add(NotUsed + $"LogTriageAssistant reported findings for {covered} of {signatures.Count} ranked signatures, and a missing severity is not treated as zero.");
            return (null, analysis.Id);
        }
        notes.Add("Triage severity comes from LogTriageAssistant analysis " + analysis.Id + " (highest finding severity per signature).");
        return (map, analysis.Id);
    }
}
