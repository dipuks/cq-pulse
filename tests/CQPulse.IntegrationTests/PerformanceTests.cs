using System.Diagnostics;
using System.Text;
using CQPulse.Application.Simulation;
using CQPulse.Domain;
using CQPulse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using static CQPulse.IntegrationTests.Infrastructure.ApiTestHelpers;

namespace CQPulse.IntegrationTests;

/// <summary>
/// Opt-in workload measurements (excluded from CI and quick runs with <c>--filter Category!=Performance</c>).
/// Each run goes through the real API, queue, worker and SQL Server persistence, and is verified for exact totals and
/// duplicate-free persistence. Memory is sampled in the test process, which hosts the API in memory, so the figures
/// include the test host itself.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "Performance")]
public sealed class PerformanceTests(ApiFixture fixture, ITestOutputHelper output)
{
    public static TheoryData<int, int> Workloads => new()
    {
        { 1_000, 1 }, { 1_000, 2 }, { 1_000, 3 },
        { 10_000, 1 }, { 10_000, 2 }, { 10_000, 3 },
        { 50_000, 1 }
    };

    [Theory]
    [MemberData(nameof(Workloads))]
    public async Task Workload_completes_with_exact_bounded_persistence(int testCount, int repetition)
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var request = NewRequest(project.Id, testCount, seed: 1_000 + repetition) with
        {
            MaxConcurrency = 64, MinimumDurationMs = 0, MaximumDurationMs = 2, RunLabel = $"Performance {testCount:N0} #{repetition}"
        };
        var preview = await (await client.PostJsonAsync("/api/simulator/preview", request)).ReadAsync<SimulationPreview>();

        using var sampler = new MemorySampler();
        var wall = Stopwatch.StartNew();
        var run = await client.WaitForTerminalAsync(await client.QueueRunAsync(request), TimeSpan.FromMinutes(10));
        wall.Stop();
        sampler.Stop();

        var (rows, distinctNames) = await fixture.WithDbContextAsync(async db =>
        {
            var results = db.TestCaseResults.Where(x => x.TestRunId == run.Id);
            return (await results.CountAsync(), await results.Select(x => x.TestCaseNameSnapshot).Distinct().CountAsync());
        });

        Assert.Equal(RunStatus.CompletedWithFailures, run.Status);
        Assert.Equal(testCount, rows);
        Assert.Equal(testCount, distinctNames);
        Assert.Equal((preview.ExpectedPassed, preview.ExpectedFailed, preview.ExpectedSkipped), (run.PassedCount, run.FailedCount, run.SkippedCount));

        var serverSeconds = run.DurationMs!.Value / 1000.0;
        var line = new StringBuilder()
            .Append($"PERF | {testCount,6} | #{repetition} | wall {wall.Elapsed.TotalSeconds,6:F2}s | server {serverSeconds,6:F2}s | ")
            .Append($"{testCount / serverSeconds,8:F0} tests/s | peak managed {sampler.PeakManagedMb,6:F0} MB | ")
            .Append($"peak working set {sampler.PeakWorkingSetMb,6:F0} MB | rows {rows} | P/F/S {run.PassedCount}/{run.FailedCount}/{run.SkippedCount}")
            .ToString();
        output.WriteLine(line);
        await File.AppendAllTextAsync(Path.Combine(AppContext.BaseDirectory, "performance-results.txt"), line + Environment.NewLine);
    }

    private sealed class MemorySampler : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private long _peakManaged;
        private long _peakWorkingSet;

        public MemorySampler()
        {
            _loop = Task.Run(async () =>
            {
                using var process = Process.GetCurrentProcess();
                while (!_stop.IsCancellationRequested)
                {
                    process.Refresh();
                    _peakManaged = Math.Max(_peakManaged, GC.GetTotalMemory(false));
                    _peakWorkingSet = Math.Max(_peakWorkingSet, process.WorkingSet64);
                    try { await Task.Delay(50, _stop.Token); } catch (OperationCanceledException) { }
                }
            });
        }

        public double PeakManagedMb => _peakManaged / 1024.0 / 1024.0;
        public double PeakWorkingSetMb => _peakWorkingSet / 1024.0 / 1024.0;

        public void Stop()
        {
            _stop.Cancel();
            _loop.Wait();
        }

        public void Dispose() => _stop.Dispose();
    }
}
