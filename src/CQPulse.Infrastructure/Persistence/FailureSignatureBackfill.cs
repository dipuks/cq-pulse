using CQPulse.Application.Analysis;
using CQPulse.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CQPulse.Infrastructure.Persistence;

/// <summary>"Insights:SignatureBackfill" configuration.</summary>
public sealed class SignatureBackfillOptions
{
    public const string SectionName = "Insights:SignatureBackfill";

    /// <summary>Run the backfill in the background at startup until no failed result is missing its signature.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Rows per batch. Each UPDATE touches at most this many rows, below SQL Server's 5,000-lock escalation threshold.</summary>
    public int BatchSize { get; set; } = 1_000;
    /// <summary>Pause between batches, so the backfill yields to the simulator and API requests.</summary>
    public int PauseMilliseconds { get; set; } = 50;

    public bool IsValid => BatchSize is >= 1 and <= 2_000 && PauseMilliseconds is >= 0 and <= 60_000;
}

/// <summary>
/// Fills <c>TestCaseResults.Signature</c> for failed rows saved before the column existed (or inserted by raw SQL).
/// Deterministic and resumable: each batch reads the oldest unsigned failed rows (by completion time, then id), computes
/// their signatures with the same function every writer uses, and sets the column only where it is still null, in short
/// autocommit statements. Stopping at any point leaves every row either null or correct, and the next run continues
/// from what is still null. Rows the application saves meanwhile already carry their signature.
/// </summary>
internal sealed class FailureSignatureBackfill(IServiceScopeFactory scopeFactory, ILogger<FailureSignatureBackfill> logger)
{
    /// <summary>Failed results that do not have a stored signature yet.</summary>
    public static Task<int> CountPendingAsync(CQPulseDbContext db, CancellationToken cancellationToken) =>
        db.TestCaseResults.CountAsync(x => x.Outcome == TestOutcome.Failed && x.Signature == null, cancellationToken);

    /// <param name="maxBatches">Stop after this many batches (null: until nothing is pending). A later run resumes.</param>
    /// <returns>The number of rows that received a signature.</returns>
    public async Task<int> RunAsync(int batchSize, TimeSpan pause, CancellationToken cancellationToken, int? maxBatches = null)
    {
        var filled = 0;
        var batches = 0;
        DateTimeOffset? resumeFrom = null; // keyset position; rows at that instant are re-read only while still null
        while (maxBatches is null || batches < maxBatches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CQPulseDbContext>();
            var query = db.TestCaseResults.AsNoTracking().Where(x => x.Outcome == TestOutcome.Failed && x.Signature == null);
            if (resumeFrom is { } from) query = query.Where(x => x.CompletedAtUtc >= from);
            var batch = await query.OrderBy(x => x.CompletedAtUtc).ThenBy(x => x.Id).Take(batchSize)
                .Select(x => new { x.Id, x.FailureCategory, x.ErrorMessage, x.CompletedAtUtc })
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                if (resumeFrom is null) break;
                resumeFrom = null; // one more pass from the start picks up rows inserted behind the keyset
                continue;
            }

            foreach (var group in batch.GroupBy(x => FailureSignature.Compute(x.FailureCategory, x.ErrorMessage)))
            {
                var signature = group.Key;
                var ids = group.Select(x => x.Id).ToList();
                filled += await db.TestCaseResults.Where(x => ids.Contains(x.Id) && x.Signature == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Signature, signature), cancellationToken);
            }
            resumeFrom = batch[^1].CompletedAtUtc;
            if (++batches % 50 == 0)
                logger.LogInformation("Failure signature backfill: {Filled} rows so far (up to {CompletedAt:O}).", filled, resumeFrom);
            if (pause > TimeSpan.Zero) await Task.Delay(pause, cancellationToken);
        }
        return filled;
    }
}

/// <summary>
/// Operator command: <c>dotnet run --project src/CQPulse.Api -- backfill-signatures</c> runs the backfill in the foreground
/// with the configured batch size and pause, prints progress and exits (0 when nothing is pending, 1 when stopped early,
/// 2 on error). It does not start the web server, background services or development seeders. Ctrl+C stops it after the
/// current batch; running it again resumes.
/// </summary>
public static class SignatureBackfillCommand
{
    public const string Name = "backfill-signatures";

    public static async Task<int> RunAsync(IServiceProvider services, TextWriter output)
    {
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            var settings = services.GetRequiredService<IOptions<SignatureBackfillOptions>>().Value;
            if (!settings.IsValid) throw new InvalidOperationException($"{SignatureBackfillOptions.SectionName} is invalid.");
            int before;
            await using (var scope = services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CQPulseDbContext>();
                var connection = db.Database.GetDbConnection();
                await output.WriteLineAsync($"Target: database '{connection.Database}' on '{connection.DataSource}'.");
                before = await FailureSignatureBackfill.CountPendingAsync(db, stop.Token);
            }
            await output.WriteLineAsync($"Failed results without a stored signature: {before:N0}. Batch size {settings.BatchSize:N0}, pause {settings.PauseMilliseconds} ms.");

            var started = System.Diagnostics.Stopwatch.StartNew();
            var filled = 0;
            try
            {
                filled = await services.GetRequiredService<FailureSignatureBackfill>()
                    .RunAsync(settings.BatchSize, TimeSpan.FromMilliseconds(settings.PauseMilliseconds), stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                await output.WriteLineAsync("Stopped by Ctrl+C after the current batch; run the command again to resume.");
            }

            int after;
            await using (var scope = services.CreateAsyncScope())
                after = await FailureSignatureBackfill.CountPendingAsync(scope.ServiceProvider.GetRequiredService<CQPulseDbContext>(), CancellationToken.None);
            await output.WriteLineAsync($"Signed {(filled > 0 ? filled : before - after):N0} rows in {started.Elapsed.TotalSeconds:N1} s. Still without a signature: {after:N0}.");
            return after == 0 ? 0 : 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await output.WriteLineAsync($"Signature backfill failed: {exception.GetBaseException().Message}");
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }
}

/// <summary>Runs <see cref="FailureSignatureBackfill"/> once at startup (retrying after errors) until nothing is pending.</summary>
internal sealed class SignatureBackfillService(
    FailureSignatureBackfill backfill,
    IOptions<SignatureBackfillOptions> options,
    ILogger<SignatureBackfillService> logger) : BackgroundService
{
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Failure signature backfill is disabled ({Section}:Enabled).", SignatureBackfillOptions.SectionName);
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var filled = await backfill.RunAsync(settings.BatchSize, TimeSpan.FromMilliseconds(settings.PauseMilliseconds), stoppingToken);
                if (filled > 0) logger.LogInformation("Failure signature backfill complete: {Filled} rows updated.", filled);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Failure signature backfill stopped by shutdown; it resumes at the next start.");
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failure signature backfill failed; retrying in {Delay}.", RetryDelay);
                try { await Task.Delay(RetryDelay, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
