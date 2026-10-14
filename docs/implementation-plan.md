# CQ Pulse Implementation Plan

Updated 2026-10-13. Sources: the evidence in [feature-audit.md](feature-audit.md) and the *CQ Pulse Product Requirements and Technical Specification* (the "spec"; section numbers below refer to it).

The goal is a reliable, portfolio-quality application. Existing working code (layering, the `TestRun` state machine, bounded queue, batched persistence with short-lived contexts, startup recovery, result-name snapshots, analytics projections) is kept and fixed in place, not rewritten. Framework versions stay as installed (.NET 10, EF Core 10.0.12, Angular 22, Vitest).

## Ground rules

- Small milestones. Each one ends with a green build and tests, an updated `feature-audit.md` with evidence, and a commit (only with approval; never pushed without approval).
- The development database (`CQPulse` on SQL Express) is never reset, dropped or truncated. Integration and performance tests use a separate, disposable database (see "Test database").
- Schema changes go through additive EF migrations only. Applying a migration to the development database is called out explicitly.
- Secrets stay in user secrets and environment variables, and are never logged or committed.
- No new frameworks for novelty. New packages only where justified: `Microsoft.AspNetCore.Mvc.Testing` and `Microsoft.AspNetCore.SignalR.Client` (tests).
- **Mocking:** the repository has no mocking library. Unit tests use small hand-written fakes (for example, a recording `IRunProgressPublisher`). Integration tests use the real SQL Server test database.
- **Charts:** small accessible SVG chart components (each with a visually hidden data table and `aria-label`). No new chart dependency.
- The simulator is described everywhere as simulated execution, never as a real UI automation runner.

## Test database

Integration and performance tests use `CQPulse_Tests_<guid>` on the same local SQL Express instance. Each test fixture creates it with `Database.MigrateAsync()` and drops it afterwards, so it never touches the `CQPulse` development database. CI uses a `mcr.microsoft.com/mssql/server` service container on `ubuntu-latest` (free; no cloud resources). The connection string comes from the `CQPULSE_TEST_SQL` environment variable, falling back to `Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True`.

---

## Phase 0: Unblock the build and restore styling

| Change | Files |
|---|---|
| Move the design system from the component-scoped `app.scss` into global partials (`src/styles/_tokens.scss`, `_layout.scss`, `_components.scss`), all `@use`d at the **top** of `styles.scss`. Remove `App.styleUrl` so nothing is emitted twice. | `src/styles.scss`, `src/styles/*` (new), `src/app/app.scss` (removed), `src/app/app.ts` |
| Style `.row` and `.three-col`, fix `.sidebar nav button` → `a`, and remove dead pre-routing selectors. | style partials |
| Keep the uncommitted `SimulatorPage` project-preselect improvement. | `src/app/pages.ts` |

**Acceptance:** `npm run build` passes with no style-budget error. `ng test` runs. Every route renders styled in light and dark themes.

## Phase 1: Backend correctness and reliability (spec 3.5, 3.6, 3.10, 3.11, 6)

| # | Change | Files | Acceptance |
|---|---|---|---|
| 1.1 | **ProblemDetails mapping** through `IExceptionHandler`. Application-level `ValidationException` (400, with field errors), `NotFoundException` (404) and `ConflictException` (409). A unique-index `DbUpdateException` maps to 409. Unexpected errors → 500, logged with the run/route context. | `Api/Errors/*` (new), `Application/Common/Exceptions.cs` (new), `CatalogService`, `SimulationService`, `Program.cs` | Invalid simulator input → 400 with errors; unknown project → 404; cancelling a finished run → 409; duplicate suite name → 409 |
| 1.2 | **Worker failure handling:** any non-cancellation exception marks the run `Failed` with a summary, persists counters, logs with `RunId`, and publishes final progress. A faulted persistence consumer cancels the producers instead of deadlocking on the bounded channel. Shutdown leaves the run for startup recovery (documented). | `SimulationWorker.cs` | Injected persistence failure → run `Failed` within seconds; no run remains `Queued`/`Running` |
| 1.3 | **Cancel/complete race:** add a `rowversion` concurrency token to `TestRun`. The worker catches `DbUpdateConcurrencyException`, reloads, and respects `Cancelled`. Cancel persists partial counters equal to the saved rows. | `TestRun.cs`, `CQPulseDbContext.cs`, **additive migration** `AddTestRunRowVersion`, `SimulationWorker.cs`, `SimulationService.cs` | Cancel mid-run always ends `Cancelled`; counters = persisted rows; terminal runs never change |
| 1.4 | **Deterministic seed:** replace `HashCode.Combine` (randomized per process) with a stable mix. | `SimulationWorker.cs` | Same seed and configuration → identical outcome sequence across processes |
| 1.5 | **Configurable limits:** a `SimulatorOptions` section (max tests 50,000, max concurrency 128, max duration 60,000 ms, queue capacity 100, result batch 500, progress interval). Exposed read-only at `GET /api/simulator/limits` so the client validates against the same numbers. | `Infrastructure/Simulator/SimulatorOptions.cs` (new), `appsettings.json`, `SimulationService`, `SimulationQueue`, `SimulatorController` | Limits change through configuration without code changes; client and server agree |
| 1.6 | **Accurate preview:** distribute counts by largest remainder (sums exactly to `TestCount`); include the target project/suite names and the case source; state that seeded outcomes follow the configured probabilities. | `SimulationService.Preview`, `SimulationPreview` | Preview totals always equal the test count |
| 1.7 | **Execution logs:** write run lifecycle entries and one entry per failed test, batched with results. Logs are paged. | `SimulationWorker.cs` | New runs show diagnostics; the 10,000-case run stays bounded in memory |
| 1.8 | **Duplicate submission:** keep the per-target active-run check and add a filtered unique index (`ProjectId, TestSuiteId` where status is Queued/Running) or a serializable check, so double clicks cannot queue twice. | `SimulationService`, migration (combined with 1.3) | Two concurrent POSTs → one 202, one 409 |
| 1.9 | **Response DTOs:** `SimulatorController.GetRun` returns the run DTO (no EF entity). Typed DTO records replace `object` responses in Runs, Dashboard and Analytics. | `Application/Runs/*`, `Application/Analytics/*` (new), controllers | OpenAPI documents typed responses |
| 1.10 | **Auth hardening:** sign-in through `SignInManager.CheckPasswordSignInAsync` with lockout (5 attempts / 5 min); fixed-window rate limiter on `/api/auth/login`; `RequestedByUserId` stores the user id (`sub`). | `AuthController`, `Program.cs`, `ServiceCollectionExtensions` | Lockout and 429 verified by tests |
| 1.11 | **Readiness:** `/health/ready` checks the database. OpenAPI UI in Development only. | `Program.cs` | Readiness fails when SQL is unavailable |

## Phase 2: Real-time and client reliability (spec 3.8, 3.10)

| # | Change | Files | Acceptance |
|---|---|---|---|
| 2.1 | **SignalR diagnosis fixes:** add `.AllowCredentials()` to the CORS policy (origins are explicit). Add `JsonStringEnumConverter` through `AddSignalR().AddJsonProtocol`. Stop the connection when a run is terminal. | `Program.cs`, simulator feature | Browser shows "Live updates connected"; progress status is a string; warnings stay visible when the connection really fails |
| 2.2 | **Reconnect reconciliation:** on `onreconnected`, rejoin the group **and** refetch run state through REST. Polling remains as the fallback, at a slower interval while SignalR is healthy. | simulator + run detail features | Killing and restoring the connection resynchronizes counts |
| 2.3 | **Environment config:** API base URL in `environment.ts` plus a dev `proxy.conf.json`, replacing the two hard-coded `https://localhost:7256` strings. | `src/environments/*`, `angular.json`, `core` | One place to configure the origin |
| 2.4 | **Session handling:** a 401 interceptor clears the token and redirects to `/login?reason=expired`. The guard checks the JWT `exp`. The token stays in `localStorage`, a documented trade-off; `sessionStorage` is an option. | `core/auth/*` | Expired sessions never show silent empty pages |
| 2.5 | **Visible failure states** on every page (loading / error with retry / truly empty), so "no data" appears only when the API really returned none. | shared `state` component | Stopping the API shows errors, not empty states |
| 2.6 | **Layout split:** `ShellComponent` (sidebar with active indicator, top bar with theme toggle and account menu) for authenticated routes; bare `/login`. | `app.routes.ts`, `layout/*` | Login renders without the shell |

## Phase 3: Product workflows (spec 3.1–3.5, 3.7, 3.9, 5)

**3.0 Restructure first:** split `pages.ts` into feature folders (`features/{auth,dashboard,projects,suites,test-cases,runs,simulator,analytics,settings}`), with separate templates, typed models, lazy `loadComponent` routes, and **reactive forms** whose validators mirror the server. Shared components: `PageHeader`, `StatusBadge`, `Paginator`, `DataState`, `MetricCard`, `TrendChart`, `BarChart`, `ConfirmDialog`.

| # | Feature | Backend | Frontend | Acceptance |
|---|---|---|---|---|
| 3.1 | **Dashboard** | `DashboardSummaryDto` with clearly named metrics: `TestCaseCount` (unique catalog cases) vs `TotalResults` (historical executions), `ActiveRuns`, `LatestRun` | Metric cards with explanatory captions; trend chart; failure-category chart; recent and active runs; latest status; "Configure simulation" action | Every number traceable to an endpoint; no hardcoded metrics |
| 3.2 | **Projects** | List with `sort`, `status` filter and paging; `ProjectDetailDto` with suite/case/run counts, last run and pass rate; archive through status | List with search/filter/sort/paginator; detail page with counts and recent runs | Works with 100+ projects through server paging |
| 3.3 | **Suites** | `TestSuiteDto` adds enabled-case count and last run; suite run history through `GET /api/runs?testSuiteId=` | Suite detail (summary and history) at `/suites/:id`; cases list at `/suites/:id/cases` | Both routes are distinct and refresh-safe |
| 3.4 | **Test cases** | Search/filter (`enabled`, `priority`)/sort/paging; `PATCH` enable/disable; `GET /api/test-cases/{id}/results` (paged); delete allowed: results keep `TestCaseNameSnapshot`, and the FK is already `SET NULL` | Full create/edit form (outcome, duration, priority, enabled); enable toggle; execution history table | Editing or deleting a case leaves historical results unchanged (test) |
| 3.5 | **Simulator uses the catalog** | When a suite is selected (or "all suites"), cycle its enabled cases up to `TestCount`, honoring each case's `SimulatedOutcome`/`SimulatedDurationMs` (`Random` uses the configured percentages), and set `TestCaseId` plus the snapshot. With no cases, use synthetic generation, stated in the preview. Never materialize all cases: stream IDs in pages. | Workload source shown in the preview | Results link to real cases where applicable |
| 3.6 | **Simulator page** | — | Reactive form with limits from `/api/simulator/limits`; preview invalidated whenever the form changes; submit button locked while in flight; live panel showing state, progress, processed count, P/F/S, elapsed time, throughput (tests/s), live connection status, latest failures (paged logs), final summary and run-detail link | Preview always equals the submitted request |
| 3.7 | **Runs** | Add `testSuiteId` filter; `POST /api/runs/{id}/rerun` re-queues `ConfigurationSnapshot` (same seed), 409 while an active run exists for the target | Filters (label, project, status, date range), sort, paginator; detail shows configuration, seed, timestamps, duration and status; paged results and logs; Cancel (active only) and Rerun (terminal only) wired to real endpoints | No decorative actions |
| 3.8 | **Analytics** | Date basis: `CompletedAtUtc`, grouped by **UTC date**; `fromUtc` inclusive, `toUtc` **exclusive**; the client sends a local date range converted to UTC midnight boundaries and labels charts "UTC". `projectId` filter. Return trend dates as `yyyy-MM-dd`. | Date range and project filters; summary cards; daily stacked trend; category bars; accessible data tables | Totals equal the sum of run counters (dashboard ↔ analytics consistency test) |
| 3.9 | **Theme and settings** | — | One signal-based `ThemeService` shared by the top bar and Settings; dark-mode chart colors and focus states | The toggle stays in sync everywhere |

## Phase 4: Automated tests and CI (spec 7)

| Suite | Coverage |
|---|---|
| Unit (`CQPulse.UnitTests`) | All `TestRun` transitions (valid and invalid); `Validate` boundaries; largest-remainder preview; seed determinism; outcome selection from case configuration |
| Integration (`CQPulse.IntegrationTests`, `WebApplicationFactory` + test DB) | **AuthZ matrix:** every endpoint × {anonymous → 401, authenticated non-admin → 403, admin → 2xx}, plus the hub negotiate. Catalog CRUD and error mapping. **Simulator persistence:** queue → terminal; counters = rows; no duplicate rows. **Cancellation** mid-run. **Failure** injection → `Failed`. **Recovery:** queued/running → `Failed`, terminal untouched. **Analytics:** empty dataset, multiple runs, date boundaries (inclusive/exclusive at UTC midnight), failed and skipped cases, category counts, percentage rounding, runs with null suite/seed/category. Case edit/delete preserves history. Rerun. |
| SignalR | An authenticated `HubConnection` joins a run and receives `RunProgressUpdated` with a string status; unauthenticated connections are rejected |
| Performance (`Category=Performance`, opt-in) | 1,000 and 10,000 cases (×3 repetitions each), then 50,000 as a stretch. Records elapsed time, throughput, peak working set / GC allocated, persisted-row verification. Results go to `docs/performance.md`; 50,000 is marked verified, failed or not tested |
| Angular (Vitest) | Auth interceptor/guard; services (HTTP mocked through `provideHttpClientTesting`); each feature's loading/error/empty states; simulator form validation and preview invalidation; theme service |
| CI | Backend unit tests; integration tests with the SQL Server service container; `npm ci && npm run build && npm test -- --watch=false`. Performance tests excluded by default. The `UnitTest1` placeholder is removed |

## Phase 5: Documentation and deployment readiness (spec 8)

- **README:** product overview, screenshots (light/dark), prerequisites, user-secret setup (key names only), migrations, running API and client, test commands, simulator limits, known limitations (in-process non-durable queue, global Administrator model with no project membership or tenant isolation, `localStorage` token).
- `docs/architecture.md`: layered design, simulator pipeline diagram, consistency and recovery rules, date semantics.
- `docs/performance.md`: measured results only.
- **Azure readiness (documentation and configuration only, no resources created):** environment-variable configuration mapping (`ConnectionStrings__CQPulse`, `Jwt__SigningKey`, `Cors__AllowedOrigins__0`), `ForwardedHeaders`, SPA fallback config (`staticwebapp.config.json`) so nested routes survive refresh, and notes on App Service + Azure SQL + optional Azure SignalR Service.
- **Manual verification checklist** recorded in the audit: every route by direct URL and refresh, both themes, real simulator run → Runs → Analytics.

---

## Recommended sequence (milestones)

| # | Milestone | Needs approval? |
|---|---|---|
| M1 | Phase 0: build and styles | — |
| M2 | Integration test harness (test DB fixture, auth helper for admin and non-admin) | Creates and drops `CQPulse_Tests_*` databases |
| M3 | 1.1, 1.2, 1.4, 1.5, 1.6, 1.9, 1.11, with tests | — |
| M4 | 2.1–2.6 with tests (fixes the "Analytics empty" symptom and SignalR) | — |
| M5 | 1.3 + 1.8 migration, 1.7 logs, 1.10 auth hardening, with tests | **Apply the additive migration to the dev DB** |
| M6 | 3.0 restructure (behavior-preserving) | — |
| M7 | 3.1–3.9 feature by feature, backend and tests first, then UI | — |
| M8 | Performance runs 1k/10k (×3), then 50k; `docs/performance.md` | — |
| M9 | CI update, docs, Azure readiness, manual checklist, final audit refresh | — |

## Verification commands

```powershell
# Backend
dotnet build CQPulse.slnx
dotnet test CQPulse.slnx --filter "Category!=Performance"
dotnet test tests\CQPulse.IntegrationTests --filter "Category=Performance"     # opt-in
dotnet ef migrations has-pending-model-changes --project src\CQPulse.Infrastructure --startup-project src\CQPulse.Api

# Frontend
Set-Location src\CQPulse.Client
npm run build
npm test -- --watch=false

# End-to-end (manual)
dotnet run --project src\CQPulse.Api --launch-profile https
npm start   # src\CQPulse.Client
# Sign in → Simulator: 1,000 tests → "Live updates connected" → terminal status
# → appears in Runs (refresh-safe) → Analytics total increases by exactly 1,000
```

---

## Status update and Quality Intelligence phases (2026-10-13)

### Confirmed complete (verified in source and tests, not only in reports)

| Milestone | Evidence |
|---|---|
| M1 build/styles | Global partials in `src/styles/`; `ng build` passes |
| M2 test harness | `tests/CQPulse.IntegrationTests/Infrastructure/*`; temporary `CQPulse_Tests_<guid>` databases |
| M3 backend reliability | `ApiExceptionHandler`, `SimulationWorkload`, `SimulatorOptions`, worker failure paths; `ErrorMappingTests`, `SimulatorExecutionTests` |
| M4 real-time/client reliability | SignalR JSON enum + CORS credentials; `core/auth.ts` interceptor/guard; `DataState`; `RealtimeTests` |
| M5 concurrency/hardening | Migration `20261014023133_AddRunConcurrencyAndActiveRunIndex` (applied to the dev DB on 2026-10-14 after a verified backup); `ConcurrencyAndHardeningTests` |
| M6 client restructure | `core/`, `shared/`, `layout/`, `features/*` lazy routes |

### Remaining on 2026-10-13, in dependency order (all since completed; see below)

| Phase | Scope (from plan Phase 3 items 3.1–3.9, Phase 4, Phase 5 and the Quality Intelligence brief) | Depends on |
|---|---|---|
| **Q1 Environment** | User applies the M5 migration to the dev DB; restart the API and the dev server (`npm start` now uses `proxy.conf.json`) | User approval |
| **M7 Product workflows** | 3.1 dashboard charts and metric semantics; 3.2 project detail with counts and recent runs, list sort/filter/paging; 3.3 suite detail and history; 3.4 test-case filters, enable/disable, history, delete with preserved snapshots; 3.5 simulator runs real suite test cases; 3.7 runs filters (project/date) and **rerun**; 3.8 analytics date/project filters (UTC, exclusive upper bound) with an analytics test matrix; 3.9 theme (done in M6) | — |
| **M8 Performance** | Opt-in measured runs at 1k and 10k (×3), 50k stretch; `docs/performance.md` | M7 |
| **Q2 LogTriage integration** | `FailureAnalysis` entity + migration; bounded analysis queue; LTA typed client (CSRF/cookie service login, timeouts, transient retries, circuit breaker); idempotency digest; correlation IDs; integration status endpoint; run-detail triage UI; test double + manual verification against a real LTA started on a scratch SQLite path | M7 (test-case identity), migration approval for the new table |
| **Q3 Historical intelligence** | Failure signatures, recurrence, flaky candidates (with minimum data), baseline/anomaly detection with explicit thresholds; investigation page | M7, Q2 |
| **Q4 SmartDog integration** | **Blocked on decisions** (architecture doc §7): git snapshot of SmartDog; structured evaluate endpoint + weighted-objective ranking + API key + tests in SmartDog; CQ Pulse client and decision-support view | Decisions, Q3 |
| **M9 Release readiness** | CI (verified on GitHub once pushing is approved), README for all three projects, docs, security review, final audit | All |

Design and contracts: [quality-intelligence-architecture.md](quality-intelligence-architecture.md).

## Status on 2026-10-14: all phases complete

| Phase | Commit | Notes |
|---|---|---|
| Q1 Environment | — | All four pending migrations applied to the development database after backups, a rehearsal on a restored copy, and checksum verification (`feature-audit.md`) |
| M7 Product workflows | `b807e56` | |
| M8 Performance | `62c8f82` | Re-measured on 2026-10-14 ([performance.md](performance.md)) |
| Q2 LogTriage integration | `60ea966` | LogTriageAssistant itself unchanged |
| Q3 Historical intelligence | `c98cc0e` | |
| M9 Documentation | `90d91ea` | GitHub CI passed for this commit (backend on a SQL Server container, frontend) |
| Q4 SmartDog integration | SmartDog `f377a89` (baseline), `af04474`; CQ Pulse `7993db7` | Structured evaluation API in SmartDog; evidence, history and run-page panel in CQ Pulse |
| Release verification | this commit | Real end-to-end, offline, browser and performance verification; fixes found during it; secrets scan; documentation (`feature-audit.md`) |
