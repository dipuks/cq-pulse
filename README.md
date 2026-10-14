# CQ Pulse — Cloud QA

*See every run. Know every risk.*

CQ Pulse is a test management and **simulated** execution platform built with .NET 10, Angular 22 and SQL Server. It organizes projects, suites and test cases, runs large simulated workloads with live progress, and turns persisted results into analytics and explainable quality insights. The execution engine is a simulator: it does not drive real browsers or UI automation.

## Features

- **Catalog:** projects, suites and test cases with search, filters, paging, enable/disable, and per-case execution history that survives edits and deletion (results keep a name snapshot).
- **Simulator:** runs real catalog test cases (fixed or randomized outcomes, per-case durations) or synthetic workloads; exact, previewable outcome totals; deterministic seeds; configurable limits; cancel and rerun with linked history. Measured at 1k, 10k and 50k tests with bounded memory ([performance](docs/performance.md)).
- **Live progress:** SignalR updates with REST polling as the authoritative fallback; elapsed time and throughput.
- **Run history:** server-side filters (project, status, label, UTC dates), sorting and paging; run detail with configuration, timing, results and execution logs.
- **Analytics and insights:** UTC-consistent trends and failure categories; recurring failure signatures, flaky-test candidates and baseline anomaly detection, each with its rule and evidence. No forecasts.
- **Failure triage (optional):** integration with a separately running [LogTriageAssistant](https://github.com/dipuks/LogTriageAssistant) for diagnostic analysis, with retries, idempotency, correlation ids and visible dependency health.
- **Engineering:** layered architecture, RFC 7807 errors, JWT with lockout and rate limiting, optimistic concurrency, startup recovery, 150 backend tests on real SQL Server (temporary databases), 41 client tests, CI with a SQL Server service container.

Documentation: [architecture](docs/architecture.md) · [quality intelligence integration](docs/quality-intelligence-architecture.md) · [performance](docs/performance.md) · [feature audit](docs/feature-audit.md) · [implementation plan](docs/implementation-plan.md)

## Local prerequisites

- .NET SDK 10
- Node.js 24+
- SQL Server Express or SQL Server LocalDB

## Run locally

The local connection string is intentionally stored outside source control. For SQL Express:

```powershell
dotnet user-secrets set 'ConnectionStrings:CQPulse' 'Server=.\SQLEXPRESS;Database=CQPulse;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True' --project src\CQPulse.Api
dotnet user-secrets set 'Jwt:SigningKey' '<a-random-32-character-minimum-key>' --project src\CQPulse.Api
dotnet user-secrets set 'DevelopmentAdmin:Email' 'you@example.test' --project src\CQPulse.Api
dotnet user-secrets set 'DevelopmentAdmin:Password' '<a-strong-local-development-password>' --project src\CQPulse.Api
dotnet ef database update --project src\CQPulse.Infrastructure --startup-project src\CQPulse.Api
dotnet run --project src\CQPulse.Api --launch-profile https
```

In another terminal:

```powershell
Set-Location src\CQPulse.Client
npm install
npm start
```

Open `http://localhost:4200`. The API listens on `https://localhost:7256`; `npm start` proxies `/api`, `/hubs` (including SignalR WebSockets) and `/health` to it (`src/CQPulse.Client/proxy.conf.json`), so the browser talks to a single origin in development. Start the API first; if it is not running, the sign-in page reports that the API cannot be reached.

For other hosting setups, serve the client from the same host as the API, or set the `API_BASE_URL` injection token (`src/app/core/api-config.ts`) to the API origin and add the client origin to `Cors:AllowedOrigins`.

## Verification

```powershell
dotnet test CQPulse.slnx --filter "Category!=Performance"   # quick run; see docs/performance.md for the opt-in workload measurements
Set-Location src\CQPulse.Client
npm run build
npm test -- --watch=false
```

### Integration tests

`tests/CQPulse.IntegrationTests` hosts the real API in memory (`WebApplicationFactory`, environment `Testing`, so user secrets and development seeders are not loaded). Each test collection gets its own temporary database named `CQPulse_Tests_<guid>`, migrated on start and dropped on finish, including when tests fail. The development `CQPulse` database is never used: database management is restricted to names matching `CQPulse_Tests_<32 hex>`. Test databases left behind by a killed test run are removed on the next run once they are older than 6 hours.

By default the tests connect to `.\SQLEXPRESS` with Windows authentication. To use another server, set a **server-level** connection string (no database name):

```powershell
$env:CQPULSE_TEST_SQL = 'Server=localhost,1433;User Id=sa;Password=<password>;TrustServerCertificate=True'
```

Tests get users through `ApiFixture`: `CreateAdministratorClientAsync()`, `CreateClientAsync(fixture.NonAdministrator)` (authenticated, no role → 403 on data endpoints), and `CreateAnonymousClient()`. Each fixture creates its users with random passwords and signs them in through the real `/api/auth/login` endpoint.

## Configuration reference

| Setting | Where | Purpose |
|---|---|---|
| `ConnectionStrings:CQPulse`, `Jwt:SigningKey` (≥ 32 chars) | user secrets / environment | Required to start the API |
| `DevelopmentAdmin:Email`, `DevelopmentAdmin:Password` | user secrets | Seeds an Administrator in the Development environment only |
| `Simulator:*` | `appsettings.json` | Limits and tuning (max tests, concurrency, durations, queue, batch size) |
| `RateLimiting:Login:PermitLimit` / `WindowSeconds` | `appsettings.json` | Sign-in attempts per client IP (default 10 per 60 s) |
| `Cors:AllowedOrigins` | `appsettings.json` | Only needed when the client is served from another origin |
| `Integrations:LogTriage:*` | user secrets / environment | Optional failure triage (see below) |

Environment variables use `__` as the separator, e.g. `ConnectionStrings__CQPulse`.

## Current limitations

- Execution is **simulated**; there is no real test runner.
- The run and analysis queues are in-process (one API instance). Restarts are handled by recovery (interrupted runs are marked failed; pending analyses are re-queued), but there is no distributed worker.
- One global Administrator role; no project-level membership or multi-tenant isolation.
- The JWT is stored in browser `localStorage` and there are no refresh tokens.
- SmartDog decision support is not integrated yet (its current API has no structured decision endpoint or ranking; see the quality-intelligence document).

## Failure triage with LogTriageAssistant (optional)

CQ Pulse can send a run's persisted failures to a separately running [LogTriageAssistant](https://github.com/dipuks/LogTriageAssistant) for diagnostic analysis (severity, categorized findings with redacted evidence, suggested next steps). Nothing in LogTriageAssistant changes: CQ Pulse uses its existing API with a dedicated account.

1. Start LogTriageAssistant over HTTPS (its session cookie is Secure-only), e.g. its `https` profile on `https://localhost:7291`.
2. Register a dedicated account in LogTriageAssistant for CQ Pulse (sign-up page or `POST /api/auth/register`).
3. Configure CQ Pulse with user secrets (never in appsettings files):

```powershell
dotnet user-secrets set 'Integrations:LogTriage:BaseUrl' 'https://localhost:7291' --project src\CQPulse.Api
dotnet user-secrets set 'Integrations:LogTriage:Username' '<lta-service-account>' --project src\CQPulse.Api
dotnet user-secrets set 'Integrations:LogTriage:Password' '<lta-service-password>' --project src\CQPulse.Api
```

Runs that finish with failures are analysed automatically (`Integrations:LogTriage:AutoAnalyzeFailedRuns`, default `true`), or from **Run detail → Failure triage**. Analyses are asynchronous and never change a run. If the service is down, the analysis is recorded as *Unavailable* with the reason and can be retried without re-running the workload. **Settings → Connected services** shows whether each integration is configured and reachable.
