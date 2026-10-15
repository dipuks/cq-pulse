using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CQPulse.Application.Analysis;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Infrastructure.Integrations.LogTriage;

/// <summary>The submitted document. <c>LineSignatures[i]</c> is the failure signature on line i + 1 (lines are 1-based).</summary>
internal sealed record AnalysisDocument(
    string Content, string Digest, int FailureCount, bool Truncated, string FileName, IReadOnlyList<string> LineSignatures);

/// <summary>
/// Builds the JSONL document submitted for analysis from a run's persisted failed results (oldest first), one line per
/// failure (no summary line: keyword analyzers would count its wording as a finding). Each line carries
/// run, result and test-case ids plus a CQ Pulse failure signature, so findings can be traced back to their source.
/// The document contains only simulator data (test names, categories, messages). It is size-bounded; the digest is
/// deterministic for the same persisted data and is used to avoid duplicate analyses.
/// <para>
/// A run with more failed results than <see cref="LogTriageOptions.MaxFailures"/> (at most 500, the most lines
/// LogTriageAssistant reports findings for) is sampled deterministically: signatures are ordered as decision support ranks
/// them (occurrences in the run descending, then signature), and lines are taken round-robin (the earliest failure of each
/// signature, then the second of each, and so on), so every signature is represented whenever there are no more signatures
/// than lines. The sampled lines are written in completion order.
/// </para>
/// </summary>
internal static class AnalysisDocumentBuilder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<AnalysisDocument> BuildAsync(
        CQPulseDbContext db, Guid runId, LogTriageOptions options, CancellationToken cancellationToken)
    {
        var run = await db.TestRuns.AsNoTracking().Where(x => x.Id == runId)
            .Select(x => new { x.Id, x.RunLabel, x.Status, x.TotalTests, x.PassedCount, x.FailedCount, x.SkippedCount, x.CompletedAtUtc })
            .SingleAsync(cancellationToken);
        var failed = db.TestCaseResults.AsNoTracking().Where(x => x.TestRunId == runId && x.Outcome == TestOutcome.Failed);
        var failures = await Details(failed).Take(options.MaxFailures + 1).ToListAsync(cancellationToken);
        var truncated = failures.Count > options.MaxFailures;
        if (truncated)
        {
            var ids = await SampleAsync(failed, options.MaxFailures, cancellationToken);
            failures = await Details(failed.Where(x => ids.Contains(x.Id))).ToListAsync(cancellationToken);
        }

        var builder = new StringBuilder();
        var included = 0;
        var lineSignatures = new List<string>();
        var bytes = 0;
        foreach (var failure in failures)
        {
            var signature = FailureSignature.Compute(failure.FailureCategory, failure.ErrorMessage);
            var line = JsonSerializer.Serialize(new
            {
                timestamp = failure.CompletedAtUtc, level = "ERROR", source = "CQPulse.Simulator",
                message = $"Test '{failure.TestCaseNameSnapshot}' failed: {failure.ErrorMessage ?? "no error message"}",
                category = failure.FailureCategory?.ToString(),
                signature,
                runId = run.Id, resultId = failure.Id, testCaseId = failure.TestCaseId, durationMs = failure.DurationMs
            }, Json);
            var lineBytes = Encoding.UTF8.GetByteCount(line) + 1;
            if (bytes + lineBytes > options.MaxDocumentBytes)
            {
                truncated = true;
                break;
            }
            builder.Append(line).Append('\n');
            bytes += lineBytes;
            lineSignatures.Add(signature);
            included++;
        }

        var content = builder.ToString();
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new AnalysisDocument(content, digest, included, truncated, $"cqpulse-run-{run.Id:N}.jsonl", lineSignatures);
    }

    private static IQueryable<FailureLine> Details(IQueryable<TestCaseResult> failed) =>
        failed.OrderBy(x => x.CompletedAtUtc).ThenBy(x => x.Id).Select(x => new FailureLine(
            x.Id, x.TestCaseId, x.TestCaseNameSnapshot, x.ErrorMessage, x.FailureCategory, x.CompletedAtUtc, x.DurationMs));

    private sealed record FailureLine(
        Guid Id, Guid? TestCaseId, string TestCaseNameSnapshot, string? ErrorMessage, FailureCategory? FailureCategory,
        DateTimeOffset CompletedAtUtc, int DurationMs);

    /// <summary>The ids of a round-robin sample across signatures (see the class remarks).</summary>
    private static async Task<List<Guid>> SampleAsync(IQueryable<TestCaseResult> failed, int max, CancellationToken cancellationToken)
    {
        var rows = await failed.OrderBy(x => x.CompletedAtUtc).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Signature, x.FailureCategory, ErrorMessage = x.Signature == null ? x.ErrorMessage : null })
            .ToListAsync(cancellationToken); // bounded by the run's failed results (at most Simulator:MaxTestCount)
        var groups = rows
            .GroupBy(x => x.Signature ?? FailureSignature.Compute(x.FailureCategory, x.ErrorMessage)) // keeps completion order
            .Select(g => (Signature: g.Key, Ids: g.Select(x => x.Id).ToList()))
            .OrderByDescending(g => g.Ids.Count).ThenBy(g => g.Signature, StringComparer.Ordinal)
            .ToList();

        var selected = new List<Guid>(max);
        for (var round = 0; selected.Count < max; round++)
        {
            var added = false;
            foreach (var group in groups)
            {
                if (round >= group.Ids.Count) break; // groups are ordered by size, so the rest are exhausted too
                selected.Add(group.Ids[round]);
                added = true;
                if (selected.Count == max) break;
            }
            if (!added) break;
        }
        return selected;
    }
}
