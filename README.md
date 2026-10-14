# CQ Pulse

CQ Pulse is a .NET 10 and Angular 22 test automation management platform. The current MVP includes a SQL Server-backed domain model, development seed data, a bounded in-process simulator, persisted results, SignalR progress delivery, and a responsive light/dark dashboard.

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

## Current limitations

The API provides JWT login and a development-only admin seeding mechanism. The browser login flow, route authorization, management screens, batched result persistence for very large workloads, analytics, and CI remain in active development. The simulator is an MVP in-process worker and is not a durable distributed job system.

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
