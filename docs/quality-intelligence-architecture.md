# Quality Intelligence Architecture

Related: [README](../README.md) (overview, setup, troubleshooting) · [architecture](architecture.md) · [performance](performance.md) · [feature audit](feature-audit.md)

Status: implemented and verified end to end on 2026-10-14, and again across all three services after the 2026-10-15 remediation (see the [feature audit](feature-audit.md#cross-repository-end-to-end-verification-2026-10-15)). The LogTriageAssistant integration (Q2), historical insights (Q3) and SmartDog decision support (Q4) are complete. The design decisions are recorded in §8.

CQ Pulse is the user-facing application. LogTriageAssistant and SmartDog stay independent applications in their own folders and repositories, with their own UIs, storage and release cycles. CQ Pulse talks to them only over HTTP, through the contracts below. No code or domain logic is copied between them.

## 1. Components

| | CQ Pulse | LogTriageAssistant (LTA) | SmartDog |
|---|---|---|---|
| Location | [dipuks/cq-pulse](https://github.com/dipuks/cq-pulse) | [dipuks/LogTriageAssistant](https://github.com/dipuks/LogTriageAssistant) | [dipuks/SmartDog](https://github.com/dipuks/SmartDog). The evaluation API was first built in a local prototype (commits `f377a89`, `af04474`, `90a19b0`, kept on the local branch `conversational-prototype`) and ported into the public application on 2026-10-15 |
| Stack | .NET 10 API + Angular 22 + SQL Server | .NET 10 minimal API + React/Vite + SQLite | .NET 10 layered API + Angular 22 + SQL Server |
| Local URL | API `https://localhost:7256`, client `http://localhost:4200` | `https://localhost:7291` (`http://localhost:5219`) | `https://localhost:7259` (`http://localhost:5272`) |
| Tests (2026-10-15) | 91 unit + 122 integration + 52 client | 108 + 5 Playwright end-to-end | 155 + 11 client |
| Auth used by CQ Pulse | JWT bearer, Administrator role (its own users) | Cookie session + CSRF header with a dedicated service account | `X-Api-Key` on the evaluation API only (SHA-256 digests compared in constant time). SmartDog's own users sign in with JWT and see only their own decisions |

### LogTriageAssistant contract (used unchanged)

- `GET /api/auth/csrf` → `{ token }` (sets the antiforgery cookie). `POST /api/auth/login {username, password}` with `X-CSRF-TOKEN` → 204 + `__Host-LogTriage.Auth` cookie (HTTPS only).
- `POST /api/analyze {logText, fileName}` (max 2 MB, CSRF required) → `LogAnalysisResult`: `id`, `analyzedAt`, `summary`, `severity` (CRITICAL/HIGH/MEDIUM/INFO), `confidence` (a heuristic signal score; LTA's README says it is **not** a calibrated probability), `findingsTruncated`, `statistics`, `findings[] {lineNumber, endLineNumber, timestamp, message, severity, category, evidence (redacted), suggestion}`, `recommendations[]`. The result is saved in LTA's history for the calling account.
- `GET /health`, `GET /health/ready` (anonymous).
- The analyzer is deterministic keyword/signature ranking. AI briefs (`/api/ai/analyze`) are optional and **not used** by CQ Pulse.

### SmartDog contract (built in prototype commit `af04474`; published in SmartDog `3b7455b`)

- `POST /api/decisions/evaluate` with `X-Api-Key`: criteria (id, name, weight > 0 and ≤ 1000, `maximize`/`minimize`), options with one value per criterion (finite, at most 1e15 in magnitude), and optional hard constraints (`<`, `<=`, `>`, `>=`, `==`, `!=`). Limits: 20 criteria, 200 options, 50 constraints, 256 KB per request, 300 requests per minute per client IP.
- **Method:** options violating a hard constraint are excluded and reported in `infeasible`. Each criterion is min–max normalized to 0–1 across the remaining options (1 = best; identical values normalize to 1 and cannot change the order). Weights are normalized to sum to 1. Score = Σ weight share × normalized value. Scores are ranked at a resolution of 1e-9: options on the same step are ties, ordered by option id, listed and given the same score. Values within 1e-12 of their magnitude count as equal, for constant criteria and for every constraint operator.
- **Response:** `status` (`Ranked` / `NoFeasibleOption`), `ranking[]` with per-criterion `contributions` and an `explanation`, `criteria[]`, `infeasible[]`, `notes[]`, `method`, `limitations`, `evaluationId`. `400` returns a validation problem keyed by field path, `401` a missing or wrong key, `413` an oversized body, `429` a rate-limited client, and `503` an instance without configured keys.
- Stateless; no language model or machine learning; no predictions. Full contract: SmartDog `README.md`.

## 2. Data flow

```
Simulator run (CQ Pulse)  ──persists──▶ TestRuns / TestCaseResults / ExecutionLogs   (authoritative; never altered by analysis)
        │ run reaches a terminal state with failures (or user clicks "Analyze")
        ▼
FailureAnalysis record (Pending) ──bounded background queue──▶ LTA client
        │ bounded JSONL document from persisted failures (runId, resultId, testCaseId, signature, category, message)
        ▼
LTA POST /api/analyze ──▶ severity, findings, evidence, suggestions, external id
        │ stored on FailureAnalysis, including the highest finding severity per CQ Pulse signature
        ▼
CQ Pulse historical analytics (own data): failure signatures, recurrence, flaky candidates, anomalies
        │
        ▼ user asks "what to investigate first?" (Run detail → Decision support)
Evidence from persisted data ──▶ SmartDog POST /api/decisions/evaluate ──▶ ranking + contributions + explanations
        │ stored on DecisionEvaluation (request, evidence, notes, result or error, correlation id)
        ▼
Run detail panel: ranking, breakdown, notes, evidence, history; links to each signature's failed results
```

## 3. Responsibilities (no logic duplication)

| Concern | Owner |
|---|---|
| Executing and persisting runs; counters; run status | CQ Pulse only |
| Failure signatures, recurrence counts, flaky candidates, trend baselines and anomalies | CQ Pulse (computed from its own persisted results) |
| Log classification, severity, redacted evidence, suggested next steps | LogTriageAssistant |
| Ranking and explaining options against weighted criteria and hard constraints | SmartDog |
| Choosing the evidence and deciding what is missing | CQ Pulse (§6) |

## 4. Reliability rules

- Analysis and decision support are **never part of run execution**. A run's results, counters and terminal status are final before either starts. External failures only change `FailureAnalysis` / `DecisionEvaluation` records.
- Per-service typed `HttpClient` with configurable base URL, per-attempt timeout and bounded retries for transient failures only (network errors, 408, 429, 5xx), plus a circuit breaker (`AddStandardResilienceHandler`). A 4xx contract error is not retried.
- Lifecycle states are persisted. Analyses: `Pending → Running → Succeeded | Failed | Unavailable`. Decision evaluations: `Succeeded`, `Failed` (rejected key, validation error, malformed response) or `Unavailable` (down, timeout, not configured). Each record keeps its error, timestamps and correlation id.
- **Idempotency (analyses):** each request carries a SHA-256 digest of its input document. Re-requesting an unchanged analysis returns the existing record unless the user explicitly retries. A retry re-submits stored run data only; it never re-runs the workload.
- Decision evaluations are explicit user requests. Each one is stored, so the history shows how rankings changed with weights or evidence.
- Malformed or partial responses are recorded as `Failed` with the reason. Nothing is fabricated: an unavailable service produces no ranking, only the recorded evidence and the error.
- Startup recovery moves interrupted `Running` analyses to `Unavailable` and re-queues `Pending` ones.

## 5. Observability and security

- An `X-Correlation-ID` header is accepted or created per request and echoed. It is added to the log scope and persisted on analysis and decision records. It is also sent on outbound calls: SmartDog echoes it and adds it to its own log scope, while LTA receives it but does not log it. Scopes are message templates (`CorrelationId:{CorrelationId}`, `RunId:{RunId}`, `AnalysisId:{AnalysisId} …`), so the simple console formatter shows them with `Logging:Console:IncludeScopes=true` and structured providers get named properties.
- Automatic analyses are queued by the worker after a run finishes, so they get their own correlation id. The id of the request that queued the run is not stored on the run.
- `GET /api/integrations/status` (admin) checks each configured dependency in parallel with a 5-second limit. LTA is checked at `/health/ready` and SmartDog at `/health`; the result is NotConfigured, Healthy or Unavailable with the reason. CQ Pulse's own `/health/ready` covers only its database, so an optional dependency outage degrades features instead of taking CQ Pulse down.
- Credentials (`Integrations:LogTriage:Username/Password`, `Integrations:SmartDog:ApiKey`) live only in user secrets or environment variables. Log documents sent to LTA and evidence sent to SmartDog contain only simulator data (test names, categories, messages, counts).

## 6. Decision-support evidence rules

Each failure signature in the run is one option; at most 25 (the most frequent in the run, ties by signature) are submitted, and a note says when more exist.

| Criterion (id) | Default weight | Value | When it is unavailable |
|---|---:|---|---|
| Occurrences in this run (`occurrencesInRun`) | 0.35 | Exact count of the run's failed results with the signature | Always available for a finished run with failures |
| Project recurrence, 30 days (`recurrence30d`) | 0.25 | Exact count over the last 30 UTC days in the project, this run included; 0 when none. Counted in SQL by the signature stored on each failed result | While failed results in the window still wait for the signature backfill, the request is refused (409) instead of undercounting |
| Affected catalog test cases (`affectedTestCases`) | 0.15 | Distinct catalog test cases that failed with the signature in the run (0 for synthetic workloads, which have none) | — |
| Triage severity (`triageSeverity`) | 0.25 | Highest LTA finding severity on the signature's document lines: INFO 1, MEDIUM 2, HIGH 3, CRITICAL 4 | Left out for **all** options, with the reason, unless the run's latest successful analysis has per-signature severities, was built from exactly the run's current failure data (same digest) and covers every option. A missing severity is never treated as 0 |

- Weights are relative (> 0 and ≤ 1000) and SmartDog normalizes them. A weight given for an unavailable criterion is reported as not applied.
- *Minimum occurrences* (1–1,000,000) becomes a hard constraint, and options below it are reported as infeasible rather than hidden.
- LTA reports findings for at most 500 lines, so the document holds at most 500 failed results (and 1.5 MB). A larger run is sampled round-robin across its signatures, most frequent first, so every ranked option is covered; the note says how many of the run's failed results the severity reflects ([operations](operations.md#severity-sample-sent-to-logtriageassistant)).
- Requests for an active run, or a run without failures, are rejected (409) and nothing is sent.

## 7. Historical analysis approach (Q3)

- **Failure signature:** failure category + normalized error message (GUIDs, hex ids, quoted values and digit runs replaced; whitespace collapsed; first 300 characters), 12-hex SHA-256 id. It is stored on each failed result when it is saved, and insights and recurrence group by it in SQL, so counts are exact for any number of distinct raw messages. The counts and first/last seen are directly verifiable. Rules and their limits are pinned by `FailureSignatureTests`; changing them requires re-running the backfill ([operations](operations.md#recovery)).
- **Flaky candidate:** the same catalog test case has both passed and failed within a window, with at least N executions. For simulated cases with `SimulatedOutcome = Random`, flakiness is **by design**, and the UI says so.
- **Anomaly:** daily failure rate compared with a trailing baseline (mean ± k·standard deviation, with minimum baseline days and results). This is a statistical rule, not machine learning.
- **Prediction:** not offered; the API and UI say so.

## 8. Decisions (resolved)

| Decision | Outcome |
|---|---|
| Snapshot SmartDog before changing it | `git init`; baseline commit `f377a89` of the existing code, excluding build output, databases and local secrets |
| SmartDog structured endpoint | `POST /api/decisions/evaluate` with transparent weighted ranking and API-key auth, first in a local prototype (commit `af04474`). On 2026-10-15 it was ported into the public SmartDog application with tie and tolerance fixes, per-user decision ownership and its own tests; the prototype commits are preserved on a local branch |
| LTA service account | A dedicated account registered in the LTA instance; credentials in CQ Pulse user secrets |
| Migrations | `AddRunConcurrencyAndActiveRunIndex`, `AddRunRerunLink`, `AddFailureAnalyses` and `AddDecisionEvaluations` applied to the development database after backups, a rehearsal on a restored copy, and data verification (see `feature-audit.md`) |

## 9. Operations and limitations

Setup, configuration, startup order, credentials, test commands and troubleshooting for all three services are in the README: [getting started](../README.md#getting-started), [tests](../README.md#tests) and [troubleshooting](../README.md#troubleshooting).

| Service | Local URL (https profile) | Credentials CQ Pulse needs | Test command |
|---|---|---|---|
| CQ Pulse API | `https://localhost:7256` | — (its own JWT users) | `dotnet test CQPulse.slnx --filter "Category!=Performance"` |
| LogTriageAssistant | `https://localhost:7291` | A dedicated account in LTA: `Integrations:LogTriage:Username/Password` | `dotnet test Tests` |
| SmartDog | `https://localhost:7259` | One shared key: SmartDog `DecisionApi:ApiKeys:N` = CQ Pulse `Integrations:SmartDog:ApiKey` | `dotnet test SmartDog.slnx` |

Limitations:

- SmartDog's scores prioritize the observed evidence. They are not predictions, and they are relative to the submitted signatures and weights.
- Ranking is limited to 25 failure signatures per run.
- Triage severity is based on at most 500 failed results per run (the most lines LTA reports findings for), sampled across signatures; the evidence notes say when a run has more.
- SmartDog's own web app and optional OpenAI assistant are separate; CQ Pulse uses only the evaluation API.
- 30-day recurrence is refused (409) while failures in the window still wait for the signature backfill, rather than undercounted.
