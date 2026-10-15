# Operations

Procedures that change or recover data, and the background work that keeps CQ Pulse's own records complete. Setup, configuration and troubleshooting are in the [README](../README.md#getting-started).

## Stored failure signatures (migration `AddFailureSignatures`)

Insights and decision-support recurrence group failures by their **failure signature** (category + normalized message, see `FailureSignature`). Since `AddFailureSignatures`, the signature is stored on every failed `TestCaseResults` row when the row is saved, and grouping runs in SQL. Every count is exact, whatever the number of distinct raw messages. Before this change, insights merged only the 500 most frequent raw messages in memory and silently undercounted messages that contain ids, and recurrence was refused above 10,000 distinct messages.

The migration only changes the schema:

| Change | Effect |
|---|---|
| `TestCaseResults.Signature char(12) NULL` | Metadata-only column add; no row is rewritten |
| `IX_TestCaseResults_Outcome_CompletedAtUtc` on (`Outcome`, `CompletedAtUtc`) INCLUDE (`Signature`, `TestRunId`, `TestCaseId`, `FailureCategory`, `DurationMs`) | Covers the time-window queries of analytics, insights and decision support |

Failed rows saved before the migration have no signature until the **backfill** fills them. Until then, insights report them as `unsignedFailures` (*Signature counts are not final yet* in the UI). Decision support refuses (409) a project whose 30-day window still contains such rows, so recurrence is never undercounted.

### Backfill

The backfill reads the oldest unsigned failed rows (by `CompletedAtUtc`, then `Id`) in batches. It computes their signatures in .NET with the same function the writers use, and sets the column only where it is still `NULL`. It is:

- **Deterministic:** the signature is a pure function of category and message.
- **Resumable:** every row is either `NULL` or correct at any moment. Stopping (Ctrl+C, shutdown, crash) loses nothing, and the next run continues with what is still `NULL`.
- **Bounded:** each `UPDATE` is its own short autocommit statement and touches at most `BatchSize` rows (default 1,000), below SQL Server's 5,000-lock escalation threshold. It pauses `PauseMilliseconds` (default 50) between batches.

It runs in two ways:

- In the background when the API starts (`Insights:SignatureBackfill:Enabled`, default `true`). Progress is logged every 50 batches.
- In the foreground with the operator command, which prints its target database, then exits. It does not start the web server, background services or development seeders. Exit codes: 0 when nothing is pending, 1 when stopped early, 2 on error.

  ```powershell
  dotnet run --project src\CQPulse.Api --launch-profile https -- backfill-signatures
  ```

### Locking

- **Column add:** a brief schema lock, independent of table size.
- **Index build:** SQL Server Express and Standard build indexes offline. The table stays readable, but inserts, updates and deletes on `TestCaseResults` wait until the build finishes. Stop the API first, or accept that a running simulation pauses for that long.
- **Backfill:** row or page locks on at most `BatchSize` rows per statement. The application keeps reading and writing meanwhile.

### Rehearsal results

Both rehearsals ran on isolated databases on SQL Server Express 2022. The development database was only read (a `COPY_ONLY` backup), and its fingerprint was identical before and after.

| | Restored copy of the development database | Generated history (`CQPulse_Tests_*`) |
|---|---|---|
| Results / failed / distinct failure messages | 24,125 / 3,699 / 5 | 960,000 / 192,000 / 24,215 |
| Migration (idempotent script) | 0.34 s; re-applying it is a no-op | 2.9 s; index 82 MB |
| Backfill (batch 1,000, pause 50 ms) | 3,699 rows in 1.1 s | 192,000 rows in 29.3 s (9.6 s of it pauses) |
| Original columns after migration and backfill | SHA-256 of every original `TestCaseResults` column, runs, analyses and evaluations unchanged | Row counts and checksums unchanged |
| Stored signatures | All 3,699 recomputed by an independent implementation (Node.js): 0 mismatches. 0 failed rows unsigned, 0 other rows signed | All 192,000 (26,661 distinct category/message pairs): 0 mismatches |
| Rollback (down script), then re-apply | Data identical to before the migration (SHA-256); column and index removed; re-apply succeeded | — |

On the generated history, the 30-day failure-signature report was compared with SQL ground truth:

| | Before (top-500 merge in memory) | After (stored signature) | SQL ground truth |
|---|---:|---:|---:|
| Failures in window | 163,357 | **189,600** | 189,600 |
| `Order <n> failed…` (Timeout / ApiError / UiElement / DataValidation) | 505 / 245 / 15 / 247 | **6,873 / 6,873 / 6,873 / 6,636** | 6,873 / 6,873 / 6,873 / 6,636 |
| Affected runs per `Order` signature | up to 505 (more than the 237 runs that exist) | **237** | 237 |
| Report latency, all projects / one project (median of 5) | 2,745 / 1,947 ms | **397 / 388 ms** | — |
| Decision support (latest run) | 409, more than 10,000 distinct messages | **200, 8 options**, 249 ms | — |

The new index also speeds up existing time-window queries on the same data: anomalies 695 → 521 ms, quality trends 655 → 410 ms, analytics summary 411 → 338 ms (index disabled vs enabled). The cost is on writes. Three seeded 10,000-test runs took a median 2.69 s before and 2.97–3.31 s after, in two rounds each (about 10–20% slower), because of index maintenance and hashing.

### Upgrading an existing database

Run from the repository root with the API stopped. Replace the backup path if your instance differs (`SELECT SERVERPROPERTY('InstanceDefaultBackupPath')`).

```powershell
# 1. Back up (copy-only: does not affect any existing backup chain)
sqlcmd -S .\SQLEXPRESS -E -C -b -Q "BACKUP DATABASE [CQPulse] TO DISK = N'C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\CQPulse_before_AddFailureSignatures.bak' WITH COPY_ONLY, CHECKSUM, INIT"

# 2. Generate the migration script, review it, then apply it
dotnet ef migrations script 20261014131810_AddDecisionEvaluations AddFailureSignatures --idempotent --project src\CQPulse.Infrastructure --startup-project src\CQPulse.Api -o AddFailureSignatures.sql
sqlcmd -S .\SQLEXPRESS -E -C -d CQPulse -b -I -i AddFailureSignatures.sql

# 3. Fill signatures for existing failed results (prints the target database first)
dotnet run --project src\CQPulse.Api --launch-profile https -- backfill-signatures

# 4. Verify: both counts must be 0
sqlcmd -S .\SQLEXPRESS -E -C -d CQPulse -Q "SELECT COUNT(*) AS failed_without_signature FROM dbo.TestCaseResults WHERE Outcome = 1 AND Signature IS NULL; SELECT COUNT(*) AS others_with_signature FROM dbo.TestCaseResults WHERE Outcome <> 1 AND Signature IS NOT NULL"
```

`dotnet ef database update --project src\CQPulse.Infrastructure --startup-project src\CQPulse.Api` applies the same migration without the reviewable script. Step 3 is optional: the API runs the same backfill in the background when it starts.

### Recovery

| Situation | Action |
|---|---|
| Backfill interrupted | Run the command again (or restart the API). |
| Remove the change (requires the previous application version) | `dotnet ef database update 20261014131810_AddDecisionEvaluations --project src\CQPulse.Infrastructure --startup-project src\CQPulse.Api`, or apply the reviewed output of `dotnet ef migrations script AddFailureSignatures 20261014131810_AddDecisionEvaluations --idempotent …`. It drops the index and the column; the rehearsal showed all other data unchanged. |
| Return to the backup | Stop the API, then `RESTORE DATABASE [CQPulse] FROM DISK = N'…\CQPulse_before_AddFailureSignatures.bak' WITH REPLACE, CHECKSUM`. Any results written after the backup are lost. |
| The signature rules change | Clear the column (`UPDATE dbo.TestCaseResults SET Signature = NULL WHERE Signature IS NOT NULL`; in batches on large tables), then run the backfill. Triage severities stored under old signatures stop matching: decision support says so in its evidence notes until the run is re-analysed. |

## Failure-analysis queue

Analyses are persisted first and queued in memory second. The in-memory queue (`Integrations:LogTriage:QueueCapacity`, default 500) never makes a caller wait:

| State | Meaning | What happens next |
|---|---|---|
| Pending, queued | In the in-memory queue | Processed in order |
| Pending, `waitingForQueueSlot` | The queue was full when it was requested (the run page says *Waiting for a free analysis slot*), or the API has just started | A sweep queues Pending analyses oldest first, at startup and every `PendingSweepSeconds` (default 15), as room frees up |
| Running | Sent to LogTriageAssistant | Succeeded, Failed or Unavailable |
| Unavailable: *interrupted by an application restart* | The API stopped while the request was in flight | Retry it from the run page |

- A slow or unavailable LogTriageAssistant cannot hold up the simulator or an HTTP request. Run completion only records the analysis, and each completion hook is limited to 10 seconds.
- Shutdown is not delayed by a full queue. Pending analyses stay in the database and are queued again at the next start, even when there are more of them than the queue holds.
- A request cancelled after its analysis was saved leaves it Pending; the next sweep picks it up.

## Severity sample sent to LogTriageAssistant

LogTriageAssistant reports findings for at most 500 lines per document. CQ Pulse therefore sends at most `Integrations:LogTriage:MaxFailures` failed results (default and maximum 500; larger values are rejected at startup), within `MaxDocumentBytes` (1.5 MB).

When a run has more failures, the sample takes lines round-robin across the run's signatures, most frequent first. That is the earliest failure of each signature, then the second of each, and so on, written in completion order. So every signature is represented whenever a run has at most 500 signatures, and the 25 signatures that decision support ranks are always covered first. The analysis is marked truncated, and the evidence note states how many of the run's failed results the severity reflects.
