# Quality Intelligence Architecture

Status: design confirmed from source inspection on 2026-10-13. Sections marked **Proposed** need the decisions listed at the end.

CQ Pulse is the user-facing application. LogTriageAssistant and SmartDog stay independent applications in their own folders and repositories, with their own UIs, storage and release cycles. CQ Pulse talks to them only over HTTP, through the contracts below. No code or domain logic is copied between them.

## 1. Components as verified

| | CQ Pulse | LogTriageAssistant (LTA) | SmartDog |
|---|---|---|---|
| Location | `C:\Projects\CQPulse` (git, branch `feature/product-completion`) | `C:\Projects\LogTriageAssistant` (git `main`, **large uncommitted user rewrite in progress**) | `C:\Projects\SmartDog` (**not a git repository**) |
| Stack | .NET 10 API + Angular 22 + SQL Server | .NET 10 minimal API + React/Vite + SQLite | .NET 10 controllers + static UI; in-memory sessions |
| Local URL | API `https://localhost:7256`, client `http://localhost:4200` | `https://localhost:7291` (`http://localhost:5219`) | `https://localhost:7018` (`http://localhost:5021`) |
| Build/tests at audit | 43 unit + 52 integration + 30 client tests pass | `dotnet test`: 57/57 pass (current working tree, built to a scratch folder) | Builds (0 warnings); **no tests** |
| Auth | JWT bearer, Administrator role | Cookie session + CSRF header (`X-CSRF-TOKEN`) on every API write; open registration; per-account history | **None** |

### LogTriageAssistant contract (verified in `Program.cs` and `Models/LogAnalysisModels.cs`)

- `GET /api/auth/csrf` → `{ token }` (sets the antiforgery cookie). `POST /api/auth/login {username, password}` with `X-CSRF-TOKEN` → 204 + `__Host-LogTriage.Auth` cookie (HTTPS only).
- `POST /api/analyze {logText, fileName}` (max 2 MB, CSRF required) → `LogAnalysisResult`: `id`, `analyzedAt`, `summary`, `severity` (CRITICAL/HIGH/MEDIUM/INFO), `confidence` (a heuristic signal score; LTA's README says it is **not** a calibrated probability), `findingsTruncated`, `statistics {totalLines, errorLikeLines, uniqueCategories, topCategory}`, `findings[] {lineNumber, endLineNumber, timestamp, message, severity, category, evidence (redacted), suggestion}`, `recommendations[]`. The result is saved in LTA's history for the calling account (capped, default 1,000 analyses).
- `GET /api/recent/{id}` returns a saved analysis. `GET /health`, `GET /health/ready` (anonymous).
- The analyzer is deterministic keyword/signature ranking and parses JSON/JSONL log fields. AI briefs (`/api/ai/analyze`) are optional, need Ollama or a hosted key, and are not persisted. **CQ Pulse uses only the deterministic analyzer.**

### SmartDog contract (verified in `DecisionSessionsController.cs`, `DecisionSolver.cs`)

- Only conversational endpoints: `POST /api/decision-sessions`, `…/{id}/messages`, `…/{id}/interpret` (needs a local Ollama model to turn text into a `DecisionProblem`), `…/{id}/analyze`, `…/{id}/chat`.
- There is **no endpoint that accepts a structured `DecisionProblem`**.
- `DecisionSolver` v0.1 only checks hard numeric constraints (`property op number`) on referenced entities. `DecisionAlternative.Score` is never set, and objective-based selection is explicitly "not yet implemented" (it returns `NoUniqueDecision`). **SmartDog cannot currently rank options, and makes no predictions.**

## 2. Data flow

```
Simulator run (CQ Pulse)  ──persists──▶ TestRuns / TestCaseResults / ExecutionLogs   (authoritative; never altered by analysis)
        │ run reaches a terminal state with failures (or user clicks "Analyze")
        ▼
FailureAnalysis record (Pending) ──bounded background queue──▶ LTA client
        │ builds a bounded, redaction-safe JSONL document from persisted failures + logs
        │ (runId, resultId, testCaseId, signature, category, message, correlationId)
        ▼
LTA POST /api/analyze ──▶ severity, findings, evidence, suggestions, external id
        │ stored on FailureAnalysis (Succeeded / Failed / Unavailable + error, attempts, timestamps)
        ▼
CQ Pulse historical analytics (own data): failure signatures, recurrence, flaky candidates, anomalies
        │
        ▼ (Proposed, see decisions)
SmartDog structured evaluation ──▶ ranked investigation options + criteria + rationale (advisory)
```

## 3. Responsibilities (no logic duplication)

| Concern | Owner |
|---|---|
| Executing and persisting runs; counters; run status | CQ Pulse only |
| Failure signatures, recurrence counts, flaky candidates, trend baselines and anomalies | CQ Pulse (computed from its own persisted results) |
| Log classification, severity, redacted evidence, suggested next steps | LogTriageAssistant |
| Ranking and explaining candidate actions against criteria and constraints | SmartDog (**Proposed** extension) |

## 4. Reliability rules

- Analysis is **asynchronous and never part of run execution**. A run's results, counters and terminal status are final before any analysis starts. External failures only change the `FailureAnalysis`/`Recommendation` records.
- Per-service typed `HttpClient` with configurable base URL, timeouts and bounded retries for transient failures only (network errors, 408, 429, 5xx), plus a circuit breaker. A 4xx contract error is not retried.
- Lifecycle states are persisted: `Pending → Running → Succeeded | Failed | Unavailable`. Each record keeps attempts, last error, timestamps and correlation ID.
- **Idempotency:** each analysis request carries a SHA-256 digest of its input document. Re-requesting an analysis whose input has not changed returns the existing successful record unless the user explicitly retries. A retry re-submits stored run data only; it never re-runs the workload.
- Malformed or partial responses are recorded as `Failed` with the reason; nothing is fabricated.
- A bounded in-process channel (the same pattern as the simulator) processes analysis jobs. Startup recovery moves interrupted `Running` analyses to `Failed` with a retry hint. No broker or cache is needed at this scale.

## 5. Observability and security

- A `X-Correlation-ID` header is accepted or created per request, added to the log scope, persisted on analysis records, and sent on outbound calls.
- Structured logs carry `RunId`, `TestCaseResultId`, `AnalysisId`, `CorrelationId`. Log documents sent to LTA contain only simulator data (test names, categories, messages). The LTA service password and any keys live in user secrets or environment variables.
- `GET /api/integrations/status` (admin) reports each dependency's health (`/health/ready`) and configuration. CQ Pulse's own `/health/ready` stays limited to its database, so an optional dependency outage degrades features instead of taking CQ Pulse down.
- **LTA authentication without changing LTA:** a dedicated LTA account for CQ Pulse (`IntegrationServices:LogTriage:Username/Password` in user secrets). The client performs the CSRF + cookie login and re-authenticates on 401.

## 6. Historical analysis approach (Phase 5)

- **Failure signature:** normalized error message (digits, GUIDs and quoted values stripped) + failure category + test case identity. The counts and first/last seen are directly verifiable.
- **Flaky candidate:** the same test case (by `TestCaseId`) has both passed and failed within a window, with at least N executions. Shown with counts and the window. For simulated cases with `SimulatedOutcome = Random`, flakiness is **by design**, and the UI says so.
- **Anomaly:** daily failure rate compared with a trailing baseline (mean ± k·standard deviation, with a minimum number of baseline days and results). Shown with the baseline, threshold and data volume. This is a statistical rule, not machine learning.
- **Prediction:** not offered unless there is enough history for an evaluable method (holdout comparison). Otherwise the UI shows "insufficient history".

## 7. Decisions required

1. **SmartDog (not under git):** initialize a git repository there to snapshot the current state before any change?
2. **SmartDog extension:** add a structured `POST /api/decisions/evaluate` endpoint (accepts a `DecisionProblem` directly, no LLM) and implement transparent weighted-objective ranking (scores per criterion, explanations), with API-key auth for service callers and a test project. Without this, SmartDog can only report feasibility and cannot rank, so CQ Pulse's decision-support view would be limited to that.
3. **LTA service account:** create a dedicated account in your local LTA instance, or let CQ Pulse use an account you register yourself.
4. **Apply the M5 migration** to the development database (SQL in `feature-audit.md`).
