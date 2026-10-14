using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CQPulse.Application.Analysis;
using CQPulse.Domain;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Infrastructure.Integrations.LogTriage;

internal sealed record AnalysisDocument(string Content, string Digest, int FailureCount, bool Truncated, string FileName);

/// <summary>
/// Builds the JSONL document submitted for analysis from a run's persisted failed results (oldest first), one line per
/// failure (no summary line: keyword analyzers would count its wording as a finding). Each line carries
/// run, result and test-case ids plus a CQ Pulse failure signature, so findings can be traced back to their source.
/// The document contains only simulator data (test names, categories, messages). It is size-bounded; the digest is
/// deterministic for the same persisted data and is used to avoid duplicate analyses.
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
        var failures = await db.TestCaseResults.AsNoTracking()
            .Where(x => x.TestRunId == runId && x.Outcome == TestOutcome.Failed)
            .OrderBy(x => x.CompletedAtUtc).ThenBy(x => x.Id)
            .Take(options.MaxFailures + 1)
            .Select(x => new { x.Id, x.TestCaseId, x.TestCaseNameSnapshot, x.ErrorMessage, x.FailureCategory, x.CompletedAtUtc, x.DurationMs })
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        var included = 0;
        var bytes = 0;
        var truncated = failures.Count > options.MaxFailures;
        foreach (var failure in failures.Take(options.MaxFailures))
        {
            var line = JsonSerializer.Serialize(new
            {
                timestamp = failure.CompletedAtUtc, level = "ERROR", source = "CQPulse.Simulator",
                message = $"Test '{failure.TestCaseNameSnapshot}' failed: {failure.ErrorMessage ?? "no error message"}",
                category = failure.FailureCategory?.ToString(),
                signature = FailureSignature.Compute(failure.FailureCategory, failure.ErrorMessage),
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
            included++;
        }

        var content = builder.ToString();
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new AnalysisDocument(content, digest, included, truncated, $"cqpulse-run-{run.Id:N}.jsonl");
    }
}
