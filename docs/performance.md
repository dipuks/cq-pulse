# Simulator Performance

Related: [README](../README.md) · [architecture](architecture.md#simulator-pipeline) (how the pipeline works) · [feature audit](feature-audit.md) (all verification results)

Measured 2026-10-13 with `PerformanceTests` (`tests/CQPulse.IntegrationTests/PerformanceTests.cs`, trait `Category=Performance`):

```powershell
dotnet test tests\CQPulse.IntegrationTests --filter "Category=Performance"
```

Each run goes through the real HTTP API, the bounded queue, the worker, the result channel and batched SQL Server persistence, against a temporary `CQPulse_Tests_<guid>` database. Each run is checked for exact totals: persisted rows = test count, one row per test (no duplicates), and passed/failed/skipped counters = the preview's exact allocation.

## Environment and settings

- One Windows 11 developer machine, local SQL Server Express, .NET 10 Debug build, API hosted in-process by `WebApplicationFactory`.
- Synthetic workload, concurrency 64, per-test duration 0–2 ms (to measure pipeline throughput rather than artificial waiting), 70/20/10 outcome split, three failure categories.
- Defaults: result buffer 2,000, batch size 500, progress updates ~100 per run. Each failed test also writes an execution-log row in the same transaction as its result.
- Memory is sampled every 50 ms in the test process, which also hosts the test runner, so working-set figures include it.

## Results

| Workload | Run | Server time (start → finish) | Wall time (queue → terminal) | Throughput | Peak managed heap | Peak working set | Verified |
|---:|---:|---:|---:|---:|---:|---:|---|
| 1,000 | 1 | 0.38 s | 0.44 s | 2,625 tests/s | 27 MB | 208 MB | ✅ 700/200/100 |
| 1,000 | 2 | 1.09 s | 1.33 s | 917 tests/s | 20 MB | 163 MB | ✅ (first run in the session: cold JIT and connection pool) |
| 1,000 | 3 | 0.60 s | 0.67 s | 1,661 tests/s | 31 MB | 181 MB | ✅ |
| 10,000 | 1 | 3.56 s | 3.68 s | 2,806 tests/s | 29 MB | 206 MB | ✅ 7,000/2,000/1,000 |
| 10,000 | 2 | 5.46 s | 5.59 s | 1,831 tests/s | 27 MB | 178 MB | ✅ |
| 10,000 | 3 | 3.59 s | 3.62 s | 2,786 tests/s | 30 MB | 209 MB | ✅ |
| **50,000** | 1 | **19.39 s** | 19.46 s | 2,579 tests/s | **36 MB** | 211 MB | ✅ 35,000/10,000/5,000 |

xUnit ran the cases in this order: 1k#2, 10k#2, 1k#3, 50k#1, 1k#1, 10k#3, 10k#1. The slowest figures come from the first runs (warm-up).

## Interpretation

- **Bounded memory:** the peak managed heap grows from ~20–31 MB (1k) to 36 MB (50k) while the workload grows 50×. The result channel (2,000) and per-batch DbContexts keep results from accumulating in memory.
- **Throughput** is roughly 2,500–2,800 tests/s once warm, limited by SQL Server inserts (results plus failure logs, 500 per transaction) rather than by simulated execution.
- **50,000 cases: verified** on this machine (one run). It is not a capacity guarantee for other hardware, remote databases or long per-test durations. With realistic durations, run time is roughly `testCount × averageDuration ÷ concurrency`.

## Re-run on 2026-10-14 (after decision support)

All 7 cases passed with exact totals. The machine was also running LogTriageAssistant, SmartDog, a second CQ Pulse API and the Angular dev server for end-to-end checks, so treat these figures as indicative.

| Workload | Runs (server time) | Throughput | Peak managed heap |
|---:|---|---|---|
| 1,000 | 0.33 s, 1.22 s (first, cold), 0.66 s | 822–3,021 tests/s | 25–33 MB |
| 10,000 | 3.21 s, 6.71 s, 3.35 s | 1,491–3,110 tests/s | 30–39 MB |
| 50,000 | 17.18 s | 2,910 tests/s | 38 MB |

**Decision support at the largest run size** (end to end through the dev proxy, real SmartDog): a 50,000-test synthetic run (35,000/12,500/2,500) finished in 18.0 s. Its automatic LogTriageAssistant analysis succeeded after 1.0 s; its document was truncated at 2,000 failures and the evidence notes say so. Three decision requests took **596, 242 and 208 ms** each, covering evidence queries, the SmartDog round trip and persistence. The evidence counted all 12,500 failures exactly across 4 signatures.

## Large-history queries (2026-10-15)

Insights and decision support were measured on 960,000 generated results (192,000 failed, 24,215 distinct messages) before and after storing failure signatures. The failure-signature report went from 2.7 s and undercounted (163,357 of 189,600 failures) to 0.4 s and exact; decision support went from refused (409) to 0.25 s. Writes became about 10–20% slower (10,000-test runs: median 2.69 s before, 2.97–3.31 s after). Details, method and ground truth are in [operations](operations.md#rehearsal-results).

## Not measured

- Concurrent runs (the worker executes one run at a time by design).
- Remote or cloud SQL latency, a Release build, and HTTP/SignalR overhead with many connected browsers.
