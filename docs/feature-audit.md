# CQ Pulse Feature Audit

How CQ Pulse was verified. The current status comes first. The dated log below it keeps the evidence from each milestone, starting with the initial audit of the code before any fixes.

Related: [README](../README.md) · [architecture](architecture.md) · [integration design](quality-intelligence-architecture.md) · [performance](performance.md) · [operations](operations.md)

## Current status (2026-10-15)

### Verified

| Area | Result | Evidence |
|---|---|---|
| Release build | ✅ `dotnet build CQPulse.slnx -c Release`: 0 warnings, 0 errors. Angular production build succeeds (initial bundle 303.95 kB) | [Release-readiness pass](#release-readiness-pass-2026-10-14-evening), [portfolio pass](#portfolio-and-documentation-pass-2026-10-14) |
| Automated tests | ✅ 91 unit, 122 integration (real SQL Server, temporary databases) and 52 client tests, locally on the Release build | [Remediation](#remediation-exact-signatures-analysis-backpressure-severity-sample-2026-10-15) |
| Continuous integration | ✅ GitHub Actions workflow `CI` (`.github/workflows/ci.yml`): `backend` job with a SQL Server service container, and a `frontend` job. Passed for `409c093` and `44cb0da` | Runs 38665530212 and 38665944654 |
| Catalog, simulator, run history, analytics, insights | ✅ Exact previewed totals, deterministic seeds, rerun, filters, insight rules. Failure signatures are stored per result and counted exactly in SQL (checked against SQL ground truth on 960,000 results) | Integration tests (M3–M7, Q3); end-to-end runs; [remediation](#remediation-exact-signatures-analysis-backpressure-severity-sample-2026-10-15) |
| Live progress | ✅ SignalR through the dev proxy (WebSockets) and REST fallback; the browser view never moves backwards | `RealtimeTests`; [release verification](#release-verification-2026-10-14) in headless Edge |
| Failure triage (LogTriageAssistant) | ✅ Automatic analysis, deduplication, retry without re-running, per-signature severity | `FailureAnalysisTests`; end-to-end against the real service |
| Decision support (SmartDog) | ✅ Real SmartDog ranking, recomputed independently from the submitted evidence; missing evidence left out with the reason | `DecisionSupportTests`; [Q4](#q4--smartdog-decision-support-2026-10-14), release verification and the [cross-repository run](#cross-repository-end-to-end-verification-2026-10-15) |
| Dependency outages | ✅ Either or both services down: runs, dashboard and readiness keep working; failures are recorded as *Unavailable* with the reason; recovery after restart | Offline checks (API and browser) |
| Migrations and data preservation | ✅ The first 5 migrations applied to the development database after verified backups and a rehearsal; data fingerprint identical before and after. `AddFailureSignatures` rehearsed on a restored copy and on 960,000 results (including rollback); **not yet applied to the development database** | [Q4 migration table](#q4--smartdog-decision-support-2026-10-14); [operations](operations.md#rehearsal-results) |
| Performance | ✅ 1k, 10k and 50k-test runs with exact totals; peak managed heap under 40 MB | [performance.md](performance.md) |
| Secrets | ✅ Pattern and exact-value scans of the files and full history: no secrets; no database files tracked | [Release verification](#release-verification-2026-10-14) |

### Not verified

- Browsers other than Microsoft Edge (Chromium, headless).
- SmartDog's optional OpenAI assistant with a real provider key. CQ Pulse does not use it.
- Hosting outside a local developer machine. The Release build and `dotnet publish` output were produced but not deployed.
- Concurrent runs, remote or cloud SQL latency, and many simultaneous browser connections ([performance.md](performance.md#not-measured)).

Known limitations are listed in the [README](../README.md#limitations).

## Initial audit (2026-10-13, before any fixes)

> **Historical.** This section describes the code as first audited, before milestone M1. Every defect it lists was fixed later: build and styling in M1; hangs, error mapping, seeds and cancellation counters in M3; SignalR and silent client failures in M4; the cancel/complete race in M5; execution logs in M5; catalog-driven simulation in M7. See the [milestone log](#milestone-log).

Audit date: 2026-10-13. Branch `main` at `563f1c9` ("Implement routed CQ Pulse frontend"), with two uncommitted client changes (`src/app/pages.ts`, `src/styles.scss`).

This audit was read-only: no application source was edited, no migration was applied, no data was changed, and no service was started or restarted. Database figures come from `SELECT`-only queries against the local development database. Secrets are referred to by key name only.

### Status legend

| Status | Meaning |
|---|---|
| Verified working | Exercised during this audit and behaved correctly |
| Implemented but not verified | Code exists and looks complete, but was not exercised end to end |
| Partially implemented | Some of the expected behavior exists; there are known gaps |
| Missing | Not present |
| Broken | Present but fails, or provably misbehaves |

### Runtime state at audit time

| Item | Finding |
|---|---|
| API process | **Not running.** Nothing listens on 7256/5191, and the Angular dev server (4200) is not running either. The only `dotnet` processes are VS Code build hosts and MSBuild worker nodes, which hold no locks on the API output. |
| API build output | `src/CQPulse.Api/bin/Debug/net10.0/CQPulse.Api.dll`, built 2026-10-11 22:29, contains `AnalyticsController`. |
| User secrets (keys only) | `ConnectionStrings:CQPulse`, `Jwt:SigningKey`, `DevelopmentAdmin:Email`, `DevelopmentAdmin:Password` are all configured. |
| Database | Local SQL Express, integrated authentication, migration `20261011132614_InitialCreate` applied. `dotnet ef migrations has-pending-model-changes` reports no drift. |

#### Database contents (verified)

The figures reported earlier (26 runs and 24,125 results: 19,281 passed, 3,699 failed, 1,145 skipped, 3,699 categorized failures) **match exactly**.

| Table | Rows |
|---|---:|
| TestRuns | 26 (4 Passed, 22 CompletedWithFailures, 0 active) |
| TestCaseResults | 24,125 |
| ExecutionLogs | **0** |
| Projects / TestSuites / TestCases | 3 / 1 / 2 |
| AspNetUsers | 1 |

For every run, `TotalTests = CompletedTests = Passed + Failed + Skipped = persisted result rows`. Results span 2026-10-11 (23,925) and 2026-10-12 (200).

### Feature status

| Feature | Status | Evidence | Known defects / gaps | Missing tests |
|---|---|---|---|---|
| **Frontend build & styling** | Implemented but not verified (visually) — **fixed in M1** | **Before M1:** `npm run build` and `ng test` both failed (`@use rules must be written before any other rules`). Root cause of the unstyled pages: the design system lived in `app.scss`, scoped to `App` by emulated encapsulation, so routed pages never received its styles. **M1:** the design system is now global partials (`src/styles/_tokens.scss`, `_base.scss`, `_layout.scss`, `_components.scss`), `@use`d at the top of `styles.scss`. `app.scss` and `App.styleUrl` were removed (no double emission). Dead pre-routing CSS dropped. `.row`/`.three-col` styled. The metric-card grid no longer reserves an unused icon column. Status badges are coloured per status through `data-status`. Hard-coded light colours were replaced with theme tokens, and focus-visible outlines were added. Evidence: production build passes (global CSS 9.50 kB, no budget warnings); `ng test` 2/2 pass; every class used in `pages.ts`/`app.html` has a rule in the compiled CSS (only `active` is runtime-applied). | Visual check in a browser (light and dark) is still pending: no browser was available in this session. `/login` still renders inside the shell (planned for 2.6). | Style smoke test per route |
| Routing & shell | Partially implemented | `app.routes.ts` defines all routes behind `authGuard`, with a not-found redirect. | `/login` renders **inside** the shell: the sidebar, nav and "Sign out" are visible before sign-in (`app.html` has no layout split). All components are eager-loaded from one 26 KB file (`pages.ts`). Suite and case screens are reachable only through Projects → Suites. | Route/guard tests |
| Authentication (API) | Implemented but not verified | Identity + JWT (`TokenService`), 32+ character key enforced at startup, role claims, `[Authorize(Roles="Administrator")]` on every data controller and on `RunsHub`. The previous audit reported anonymous 401s. | `CheckPasswordAsync` skips lockout, and there is no rate limiting on `/api/auth/login`. `RequestedByUserId` stores the email (`Identity.Name`), not the user id. | 401/403 integration tests |
| Authentication (client) | Partially implemented | Login page, `localStorage` token, bearer interceptor, `authGuard`. | **Regression:** the 401 → "session expired" handling from `44366cd` lived in the old single-file `app.ts` and was lost in `563f1c9`. The guard checks only that a token exists, so after 60 minutes every page silently shows empty states. | Interceptor/guard tests |
| Projects | Partially implemented | Create/read/update/search API and pages. | No archive/delete endpoint (the status field exists). No pagination controls. Validation errors return HTTP 500 (see API error handling). | API + component tests |
| Test suites | Partially implemented | Create/read/update API, `ProjectSuitesPage`, `SuitePage`. | A duplicate suite name hits a unique index and returns 500. No delete. Create errors are not shown in the UI. | API + component tests |
| Test cases | Partially implemented | Create/read/update API, `CasePage`. | The create form captures only name and priority. No delete. The **simulator never uses test cases**: `SimulationWorker` ignores `TestSuiteId` and generates `"Generated test N"` with `TestCaseId = null`, so `SimulatedOutcome` and `SimulatedDurationMs` on cases have no effect. | API + simulator tests |
| Simulator: validation & queueing | Implemented but not verified | `SimulationService.Validate`, project/suite ownership check, bounded queue (100). | Validation exceptions become HTTP 500. The single-active-run check is a check-then-insert race. Preview counts use integer division, so they may not sum to the test count. | Unit tests for `Validate`, preview |
| Simulator: execution | Partially implemented | The database shows 26 consistent completed runs, including 2 × 10,000. | **Run can hang or get stuck Running:** any non-cancellation exception is only logged (`SimulationWorker.cs:767`), and `run.Fail()` is never called. If `PersistResultsAsync` faults, the bounded 2,000-item channel is never drained, so `WriteAsync` blocks forever. **Seed not reproducible:** `HashCode.Combine` is randomized per process (`SimulationWorker.cs:799`). | Execution, failure, seed tests |
| Simulator: cancellation | Partially implemented | `RunCancellationRegistry`, `CancelAsync`. | **Lost-update race:** `CancelAsync` saves `Cancelled` from one context while the worker's tracked entity (still `Running`) can call `Complete()` and overwrite it, with no concurrency token. Cancelled runs keep their persisted results but their counters are never saved (they stay 0). Cancelling a finished run returns 500. | Cancellation race test |
| Startup recovery | Implemented but not verified | `ExecutionRecoveryService` marks Queued/Running as Failed. | No active runs existed, so this was not exercised. | Integration test |
| Result persistence | Verified working (data level) | Every persisted run has result rows equal to its counters. 500-row batches plus `ChangeTracker.Clear()`. | 50,000-case maximum still unmeasured. | Volume test |
| Execution logs | Missing | Table, endpoint and UI panel exist. | Nothing writes `ExecutionLog`: 0 rows, so the Diagnostics panel is always empty. | — |
| SignalR live progress | **Broken** | Hub, JWT query-string token, group join, client reconnect and REST fallback exist. | (1) `@microsoft/signalr` defaults to `withCredentials: true` (confirmed in `node_modules`), but the CORS policy lacks `AllowCredentials()`, so the browser blocks cross-origin negotiate. Live updates cannot connect and the UI always falls back to 700 ms polling. (2) SignalR does not inherit the MVC `JsonStringEnumConverter`, so `RunProgress.Status` arrives as a number and breaks `terminal()` and the status label once (1) is fixed. | Hub integration test |
| REST polling fallback | Implemented but not verified | `SimulatorPage.watch` polls `/api/simulator/runs/{id}` every 700 ms and stops at a terminal status. | The endpoint returns the raw EF entity. The SignalR connection is not stopped when the run finishes. | Component test |
| Run history & detail | Partially implemented | `RunsController` filters/sort/paging verified (`TotalCount 26`, page of 25). Run detail, results and logs pages exist. | The UI exposes no pagination (only the first 25 runs and 50 results), no date/project/sort filters, and no rerun. List pages have no error handlers. | API + component tests |
| Dashboard | Verified working (API) | `DashboardController.GetSummary` against the real database: 3 projects, 26 runs, 19,281/3,699/1,145, 79.9% pass. | Shows no trend or failure charts (only tables and metrics). Its run-counter totals diverge from analytics once a run is cancelled mid-flight. | API test |
| Analytics | Verified working (API) / Broken (UI symptom) | All three endpoints, called through the real controllers against the real database, return correct data: summary 24,125 results, 79.9% pass, 15.3% failure; trends for 2 days; 5 failure categories (1,234 / 1,196 / 1,187 / 42 / 40). | The backend is **not** the cause of "no persisted outcomes". `AnalyticsPage` subscribes without error handlers, so any HTTP failure (expired token, API not running or stale, CORS) renders as the empty state "No persisted outcomes yet". The two likeliest causes are an expired token (no 401 handling) or an API process that was not running current code; the rebuild/restart was never confirmed. No date or project filter in the UI. No chart rendering. | API + component tests |
| API error handling | Broken | `AddProblemDetails` + `UseExceptionHandler`. | All domain/validation exceptions (`ArgumentException`, `KeyNotFoundException`, `InvalidOperationException`, `DbUpdateException` on unique keys) map to **500** instead of 400/404/409. | API tests |
| Theme | Implemented but not verified | `ThemeService`, dark tokens, Settings page, header toggle. | The header toggle and the Settings select hold separate state. | Service test |
| Health checks | Partially implemented | `/health`, `/health/ready`. | `/health/ready` does not check the database. | — |
| Database & migrations | Verified working | Single migration applied; no pending model changes; relationships and indexes as designed. | Enums are stored as integers (fine, but undocumented). No migration test. | Relational integration test |
| Automated tests | Broken | Backend: 3 real `TestRun` lifecycle tests plus 1 empty `UnitTest1` placeholder, all passing. Frontend: 2 shell tests, **failing to build** because of the Sass error. | No API, simulator, SignalR, analytics, auth, or component tests. The integration project has no `WebApplicationFactory` package. | Everything listed above |
| CI | Partially implemented | `.github/workflows/ci.yml` builds and tests .NET and builds Angular. | Does not run `ng test`. Would fail right now if the uncommitted Sass change were pushed. | — |
| Documentation | Partially implemented | README, architecture, this audit, implementation plan. | No API reference, screenshots, demo script, or seeded demo data story for recruiters. | — |

### Highest-risk defects

1. **Frontend does not build** (Sass `@use` order). Blocks every UI change and CI.
2. **Routed pages are unstyled at `HEAD`** (design system scoped to `App`).
3. **Runs can hang or remain `Running`** after worker exceptions or a persistence failure.
4. **SignalR cannot connect** (CORS credentials), plus enum serialization once it does.
5. **Silent client failures**: no 401 handling and no error handlers on analytics/runs pages, which is how a working backend appears as "no persisted outcomes".
6. **Validation errors return 500.**
7. **Cancel/complete race** and unsaved counters on cancelled runs.
8. **Non-reproducible seeded runs.**

### Commands executed during this audit

| Command | Result |
|---|---|
| `git status`, `git log`, `git diff`, `git ls-files` | Inspected; 2 uncommitted client files |
| `dotnet test CQPulse.slnx` | Build succeeded; 3/3 unit and 1/1 placeholder integration tests passed |
| `npx ng build` (in `src/CQPulse.Client`) | **Failed**: Sass `@use` ordering error |
| `npx ng test --watch=false` | **Failed**: same Sass error |
| `dotnet ef migrations has-pending-model-changes --no-build` | No changes since last migration |
| `dotnet user-secrets list` (values redacted) | 4 expected keys present |
| `sqlcmd … -K ReadOnly` with `SELECT` counts and aggregates | Figures in "Database contents" |
| Scratchpad console app (outside the repo) that called `AnalyticsController`, `DashboardController`, `RunsController` directly against the database (read-only queries, no host or seeders started) | All 5 calls returned correct data |
| Process and port inspection | API and dev server not running; no build-locking processes |

## Milestone log

### M1 — Build and styling (2026-10-13)

| Command | Result |
|---|---|
| `npx ng build` (production) | ✅ Passed. `main` 421.29 kB, `styles` 9.50 kB, no budget warnings |
| `npx ng test --watch=false` | ✅ 2/2 passed |
| Class coverage check (template classes vs compiled `styles-*.css`) | ✅ No template class without a rule |

Not yet verified: visual rendering in a real browser in both themes.

### Login investigation and layout fix (2026-10-13)

**Symptom:** `/login` showed "Sign in failed. Check your administrator credentials." and rendered inside the authenticated shell.

**Root cause (login):** the API was not running (nothing listening on 7256; only the Angular dev server on 4200), so the request never connected (HTTP status 0). `LoginPage` showed the same "credentials" message for every error, which made a connectivity failure look like bad credentials. Everything else was verified healthy:

| Check | Result |
|---|---|
| Dev admin account vs user secrets (read-only, booleans only) | 1 user; email matches `DevelopmentAdmin:Email`; role `Administrator`; email confirmed; not locked out; stored hash verifies against `DevelopmentAdmin:Password` |
| `Jwt:SigningKey` | Present, at least 32 characters |
| HTTPS dev certificate | Trusted |
| API started temporarily (`dotnet run --launch-profile https`), then stopped | Started cleanly; no warnings or errors logged |
| CORS preflight `OPTIONS /api/auth/login` from `http://localhost:4200` | 204, origin allowed |
| `POST /api/auth/login` with configured credentials | 200, three-part JWT issued |
| `POST /api/auth/login` with wrong password | 401 `Invalid email or password.` |
| `GET /api/auth/me` with token | 200 |
| `GET /api/projects` anonymous / with token | 401 / 200 |

**Root cause (layout):** the sidebar shell was the root `App` template, so it wrapped every route including `/login`.

**Fix:** the shell moved to a `Shell` component used as the parent of the guarded routes (`app.routes.ts`). `App` is now a bare `<router-outlet>` that initializes the theme. `LoginPage` distinguishes status 0 (API unreachable), 401 (invalid credentials) and other statuses. The backend, authentication configuration and database are unchanged.

| Command | Result |
|---|---|
| `npx ng build` | ✅ Passed, no budget warnings |
| `npx ng test --watch=false` | ✅ 8/8 passed (new: `/login` has no shell; guard redirects without token; guarded route renders in shell with active nav; login error mapping) |
| Mutation check (login placed back inside the guarded shell) | ✅ Two routing tests failed as expected; file restored |

Backend authentication has no automated tests yet (planned for M2/M3 authorization matrix). The HTTP checks above were run manually.

### M2 — Integration test harness (2026-10-13)

**Added** (`tests/CQPulse.IntegrationTests/Infrastructure/`):
- `TestDatabase`: creates `CQPulse_Tests_<guid>` on `.\SQLEXPRESS` (or `CQPULSE_TEST_SQL`), applies migrations, and drops it on dispose (`SINGLE_USER WITH ROLLBACK IMMEDIATE`). Every create/drop is guarded by the strict pattern `^CQPulse_Tests_[0-9a-f]{32}$`. Orphans older than 6 hours from killed runs are swept on the next run.
- `CQPulseApiFactory`: the real API on `WebApplicationFactory<Program>` in the `Testing` environment (no user secrets, no dev seeders) with the test connection string and a random per-fixture JWT key.
- `ApiFixture` + `ApiCollection`: one database, host and seeded users per collection. It creates an Administrator user and an authenticated user with no role, both with random passwords. Helpers sign in through the real `/api/auth/login` endpoint. Disposal drops the database even when tests fail.
- `Microsoft.AspNetCore.Mvc.Testing` 10.0.12 added. The empty `UnitTest1` placeholder was removed.

| Check | Result |
|---|---|
| `dotnet test CQPulse.slnx` (run 3×) | ✅ Unit 3/3, Integration 16/16 each run (~4–7 s) |
| Harness tests | API connects to the generated test DB with no pending migrations; dev seed data absent; `/health` anonymous 200; anonymous `/api/projects` 401; admin `/api/auth/me` 200 (matching email) and `/api/projects` 200; non-admin `/api/auth/me` 200 and `/api/projects` **403**; wrong password 401 |
| Lifecycle tests | Database exists and is migrated after create, gone after dispose; dispose is idempotent; guard rejects `CQPulse`, `master`, malformed and injection-style names |
| Cleanup on failure (temporary deliberately failing test, then removed) | 1 failed / 16 passed; **0** `CQPulse_Tests_*` databases remaining |
| Dev DB before vs after (read-only `sqlcmd`) | Identical: 26 runs, 24,125 results, 3 projects, 1 user, same run-state checksum |

**Known gap:** the GitHub Actions `backend` job has no SQL Server, so `dotnet test` in CI will now fail at the integration tests until a SQL Server service container and `CQPULSE_TEST_SQL` are added (planned CI work; not changed in M2).

### M3 — Backend correctness and reliability (2026-10-13)

| Plan item | Change | Evidence |
|---|---|---|
| 1.1 Error mapping | `ValidationException`/`NotFoundException`/`ConflictException` (Application) and `ApiExceptionHandler`. Unique-index `DbUpdateException` → 409. Suite-name uniqueness checked explicitly. Catalog and simulator validation report every invalid field. | `ErrorMappingTests` (10): 400 with `errors`, 404 for unknown project/run and cross-project suite, 409 for duplicate suite create and rename and for cancelling a finished run, `application/problem+json` |
| 1.2 Worker failure handling | Any execution or persistence failure → `Failed` with summary. A failed persistence consumer cancels producers (no bounded-channel deadlock). Progress notification failures are logged and ignored. Final counters are computed from persisted rows. Each saved batch atomically increments run counters in the same transaction, so REST polling shows live progress and counters always equal persisted rows. | `Persistence_failure_marks_run_failed_without_deadlocking` (buffer 4, batch 10, writer fails on batch 2 → `Failed`, 10 counted = 10 rows). **Mutation check:** removing the producer cancellation makes this test time out with the run stuck `Running`. `Progress_notification_failures_do_not_fail_the_run`, `Running_run_exposes_live_progress_through_rest` |
| 1.4 Deterministic seed | `SimulationWorkload`: SplitMix64 per-index streams replace per-process `HashCode.Combine` | Pinned-value unit test; two seeded runs in separate projects produce identical results |
| 1.5 Configurable limits | `SimulatorOptions` ("Simulator" section, validated on start), `GET /api/simulator/limits` | `Limits_endpoint_reports_configured_limits`; validator unit tests at limit boundaries |
| 1.6 Accurate preview | Outcomes allocated **exactly** by percentage (largest remainder) through a seeded affine permutation in constant memory. Preview returns those exact counts plus project/suite names, durations, categories, seed, label and estimated duration. | Run of 1,237 tests: final P/F/S equal the preview exactly; 1,237 distinct persisted rows (no duplicates); every failure categorized |
| 1.9 Typed DTOs | Runs, run detail, results, logs, dashboard and analytics return typed records (same JSON names). The simulator `GetRun` no longer returns the EF entity. Dashboard and analytics return 0 instead of `null` when empty. Trend dates are `yyyy-MM-dd`. | Client builds; 8/8 client tests |
| 1.11 Readiness | `/health` = liveness, `/health/ready` checks the database; OpenAPI mapped only in Development | `Readiness_check_reports_database_healthy` |
| Cancellation | The worker reloads the run before finishing, so a user cancellation saved mid-run is kept; partial counters equal persisted rows | `Cancelled_run_stays_cancelled_with_counters_equal_to_persisted_rows` |
| Recovery | Unchanged behaviour, now covered | `Startup_recovery_fails_interrupted_runs_and_leaves_terminal_runs_untouched` |
| Consistency | — | `Dashboard_and_analytics_totals_agree_after_runs` |
| CI | Backend job runs a SQL Server 2022 service container with `CQPULSE_TEST_SQL`; frontend job runs `npm test` | **Not yet verified on GitHub** (workflow runs on `main` pushes and PRs) |

Remaining race (planned M5): a cancellation saved between the worker's final reload and its save can still be overwritten by `Complete()`. The window is now milliseconds, and the `rowversion` concurrency token closes it.

| Command | Result |
|---|---|
| `dotnet test CQPulse.slnx --artifacts-path <scratch>` (×4) | ✅ Unit 43/43, Integration 37/37 every run (~23–26 s) |
| `npx ng build` / `npx ng test --watch=false` | ✅ / ✅ 8/8 |
| Dev DB baseline after all runs (read-only) | Unchanged (26 runs, 24,125 results, same checksum); 0 `CQPulse_Tests_*` databases left |

Builds used `--artifacts-path` because a running `CQPulse.Api.exe` (started by the user) locks `src/CQPulse.Api/bin`.

### M4 — Real-time and client reliability, and M6 — client restructure (2026-10-13)

**Server (M4 2.1):** SignalR now uses `JsonStringEnumConverter` (statuses arrive as names), and the CORS policy adds `AllowCredentials()` (origins remain explicit).

| Test (`RealtimeTests`) | Result |
|---|---|
| Admin joins a run group and receives `RunProgressUpdated` with **string** statuses; final message 300/300 | ✅ |
| Anonymous hub connection → 401; non-admin → 403 | ✅ / ✅ |
| Credentialed CORS preflight on `/hubs/runs/negotiate` from `http://localhost:4200` → `Access-Control-Allow-Credentials: true`; unknown origin not allowed | ✅ / ✅ |
| **Mutation check:** removing the enum converter and `AllowCredentials()` | The string-status and CORS tests fail as expected; code restored |

**Client (M4 2.2–2.5 and M6 3.0):**
- The 26 KB single-file `pages.ts` and `core.ts` were split into `core/` (models, API service, auth, errors, theme), `shared/` (`loadable`, `DataState`, `Pager`), `layout/shell` and `features/*` (one folder per route). Routes are lazy-loaded: the initial bundle went from 431 kB to 301 kB, and the SignalR client now ships only with the simulator.
- **Environment config (2.3):** relative `/api` and `/hubs` through `API_BASE_URL` (default same origin). `npm start` uses `proxy.conf.json`, so no CORS is involved in development. Verified through a temporary dev server on :4300 against the running API: `/api/projects` 401, `/hubs/runs/negotiate` 401, `/health` 200 (proxied); a deep link `/runs/abc` serves the app.
- **Session handling (2.4):** the interceptor turns a 401 on any call except sign-in into `/login?reason=expired&returnUrl=…` (open-redirect safe). The guard checks the JWT `exp`. A 403 keeps the session.
- **Failure states (2.5):** every list and detail page shows loading → error with Retry → truly empty. Empty states only appear after a successful empty response. Server Problem Details messages (validation, 404, 409) are shown on forms.
- **Simulator (2.1/2.2/2.6):** `RunLiveConnection` joins the run group, reconciles through REST after connect and every reconnect, reports the real connection state and error text, and stops when the run is terminal. REST polling runs every 1 s, or every 5 s while live updates are healthy. Out-of-order live events never move progress backwards. Preview is cleared on any edit, and queueing requires a current preview. Queue is disabled while a run is active (duplicate-submission guard). Live panel shows processed, P/F/S, elapsed, throughput and final summary. Failure category checkboxes and optional seed added. Client validation mirrors the server, using `/api/simulator/limits`.
- Run detail: configuration, seed, timestamps and duration; paginated results with outcome filter; working **Cancel** for active runs. Runs list: server-side pagination. Theme state is shared by the top bar and Settings.

| Command | Result |
|---|---|
| `dotnet test CQPulse.slnx --artifacts-path <scratch>` | ✅ Unit 43/43, Integration 42/42 |
| `npx ng build` | ✅ initial 301 kB, all feature routes lazy |
| `npx ng test --watch=false` | ✅ 30/30 (auth guard, interceptor 401/403, return-URL safety, Problem Details messages, analytics error-vs-empty **regression test**, simulator validation and preview invalidation) |
| Dev DB baseline (read-only) | Unchanged; 0 test databases left |

**Not verified in a real browser:** live SignalR in the browser. The server path is covered by integration tests; the browser client could not be exercised in this session.

### M5 — Concurrency, single active run, execution logs, sign-in hardening (2026-10-13)

**Migration `20261014023133_AddRunConcurrencyAndActiveRunIndex` (additive, NOT applied to the development database):**

```sql
ALTER TABLE [TestRuns] ADD [RowVersion] rowversion NULL;
CREATE UNIQUE INDEX [UX_TestRuns_ActiveTarget] ON [TestRuns] ([ProjectId], [TestSuiteId]) WHERE [Status] IN (0, 1);
```

Precondition checked read-only on the dev DB: 0 queued/running runs, so the unique index can be created. Until it is applied, the new API code cannot run against the development database (EF expects the `RowVersion` column).

| Plan item | Change | Evidence |
|---|---|---|
| 1.3 Cancel/complete race | `RowVersion` concurrency token (shadow property). The worker re-decides the terminal state from fresh data and retries on `DbUpdateConcurrencyException`, so an accepted cancellation always wins. Start is skipped if the run changed after dequeue. Cancel retries on conflicts and returns 409 if the run finished meanwhile. | `Run_rowversion_detects_concurrent_updates`; `Accepted_cancellation_is_never_overwritten_by_completion` (12 cancels at varied timings: every 204 ends `Cancelled`, every 409 ends non-cancelled, counters = rows) |
| 1.8 Duplicate submission | Filtered unique index `UX_TestRuns_ActiveTarget` plus a pre-check. An index violation maps to a 409 with a clear message. | `Concurrent_submissions_for_one_target_queue_exactly_one_run` (8 parallel POSTs → exactly 1×202, 7×409); `Database_rejects_a_second_active_run_for_the_same_target`; `Terminal_runs_do_not_block_new_runs_for_the_same_target` |
| 1.7 Execution logs | Run started/finished entries (with seed, totals, error summary), and one entry per failed test linked to its result, written in the same transaction as the result batch | `Runs_record_lifecycle_and_failed_test_logs` (logs = failures + 2; paged newest-first) |
| 1.10 Sign-in hardening | Identity lockout (5 failures → 5 minutes) with the same 401 for locked and wrong-password cases (no account enumeration). Per-IP fixed-window rate limit on `/api/auth/login` (default 10/min, `RateLimiting:Login`). Runs store the requesting user **id**. | `Repeated_wrong_passwords_lock_the_account`, `Successful_sign_in_resets_the_failed_attempt_count`, `Sign_in_is_rate_limited` (3 × 401 then 429), `Run_records_the_requesting_user_id` |

| Command | Result |
|---|---|
| `dotnet test CQPulse.slnx --artifacts-path <scratch>` (×4) | ✅ Unit 43/43, Integration 52/52 every run (~30 s) |
| `dotnet ef migrations has-pending-model-changes` | ✅ No changes since last migration |
| Dev DB baseline (read-only) | Unchanged; only `InitialCreate` applied; 0 test databases left |

The migration was created with `--configuration Release` because the user's running API locks the Debug output.

### M7 — Product workflows (2026-10-13)

**Migration `AddRunRerunLink` (additive, NOT applied to the development database):** `ALTER TABLE TestRuns ADD RerunOfRunId uniqueidentifier NULL` plus an index and a self-referencing FK (no cascade).

| Plan item | Delivered | Evidence |
|---|---|---|
| 3.5 Simulator runs real catalog cases | New `WorkloadSource` (`Catalog` is the default in the UI; `Synthetic` is still available). Enabled cases in the suite (or in all of the project's suites) run in a stable order and cycle to the test count. A fixed `SimulatedOutcome` and per-case duration are honoured; `Random` cases take exactly allocated percentages. Results carry `TestCaseId` and a name snapshot. The preview reports the exact totals, the case count and a description. A catalog target with no enabled cases → 400; cases disabled after queueing → run `Failed` with a reason. | `CatalogWorkloadTests` (order and cycling, fixed outcomes and durations, exact allocation for mixed catalogs, reproducibility); `Catalog_run_executes_real_cases_with_exact_preview_totals`; `Catalog_run_without_enabled_cases_is_rejected` |
| 3.4 Test cases | Filters (search, enabled, priority) and sort; `PATCH /api/test-cases/{id}/enabled`; `DELETE` (results keep their snapshot, FK set to null); `GET /api/test-cases/{id}/results` (paged history with run label). UI: full create form, enable toggles, history table, delete with confirmation. | `Editing_and_deleting_a_case_preserves_its_execution_history`; `Disabling_a_case_excludes_it_from_future_runs_only` |
| 3.7 Rerun | `POST /api/runs/{id}/rerun` re-queues the stored configuration and seed with `RerunOfRunId`. Run detail links to the original and its reruns. The runs list marks reruns. 404 for an unknown run; 409 when the target is busy. | `Rerun_creates_a_linked_run_with_the_same_configuration_and_outcomes` (identical P/F/S for the same seed); `Rerun_errors_are_reported` |
| 3.2 Projects | `GET /api/projects` status filter and sort; `GET /api/projects/{id}/summary` (suite/case/enabled/run/active counts, result totals, pass rate, 5 recent runs). New project detail page with an inline edit form. | `Project_summary_reports_persisted_counts`; `List_filters_narrow_projects_cases_and_runs` |
| 3.3 Suites | `enabledTestCaseCount`; suite run history through `GET /api/runs?testSuiteId=`. Suite page tabs: Overview (details + paged run history) and Test cases (filters, paging, toggles). | Same filter test |
| 3.7/3.8 Filters | Runs: project, status, label, UTC date range, sort, paging. Analytics: project and UTC date range. **Both API ranges are now half-open `[fromUtc, toUtc)`**; the UI's inclusive end date maps to the next UTC midnight. | `AnalyticsTests` (empty dataset, multiple runs, exact boundaries at UTC midnight, failed/skipped, category counts excluding uncategorized failures, 33.3% rounding, project isolation, runs without suite/seed); client `dates.spec`, analytics filter spec |
| 3.1 Dashboard | 30-day stacked trend chart (SVG + hidden data table), failure categories, recent runs, queued/running runs; metric captions distinguish unique catalog cases from executed results | `trend-chart.spec` |

| Command | Result |
|---|---|
| `dotnet test CQPulse.slnx --artifacts-path <scratch>` | ✅ Unit 51/51, Integration 65/65 |
| `npx ng build` | ✅ initial 302.85 kB |
| `npx ng test --watch=false` | ✅ 36/36 |
| `dotnet ef migrations has-pending-model-changes` | ✅ none |
| Dev DB (read-only) | Unchanged; still only `InitialCreate` applied |

### Q2 — LogTriageAssistant integration (2026-10-13)

**Migration `AddFailureAnalyses` (additive, NOT applied to the development database):** a new `FailureAnalyses` table (FK → `TestRuns`, cascade delete), with indexes on `(TestRunId, RequestedAtUtc)` and `Status`.

**No changes were made to LogTriageAssistant** (its working tree still has the same 27 uncommitted user entries). CQ Pulse uses LTA's existing contract: `GET /api/auth/csrf` → `POST /api/auth/login` (CSRF + cookie, dedicated account) → CSRF token re-issued for the signed-in identity → `POST /api/analyze`.

| Concern | Implementation | Evidence |
|---|---|---|
| Lifecycle | `FailureAnalysis` record: Pending → Running → Succeeded / Failed (rejected or malformed) / Unavailable (down, timeout, not configured); attempts, last error, timestamps, external id | `FailureAnalysisTests` (13) |
| Never alters runs | Analysis runs after the run is final, through `IRunCompletionObserver` (failures logged and ignored); only `FailureAnalyses` rows are written | Run DTO identical before and after analysis, including after an outage |
| Input | Bounded JSONL of persisted failed results (≤ 2,000 failures, ≤ 1.5 MB; truncation recorded); each line has `runId`, `resultId`, `testCaseId`, CQ Pulse `signature`, category and message | Document assertions in tests |
| Idempotency | SHA-256 digest of the input; a repeat request returns the existing pending/running/succeeded analysis (200) | `Repeated_requests_for_unchanged_input_reuse_the_analysis` (LTA called once) |
| Retries | `AddStandardResilienceHandler`: per-attempt timeout, bounded retries for transient failures only, circuit breaker. A re-login is attempted once when the session or CSRF token is rejected. | 503 → 2 calls (1 + RetryCount 1) → Unavailable; malformed → Failed, 1 call; bad credentials → Failed; slow → Unavailable; expired session → 2 logins, success |
| Safe retry | `POST /api/failure-analyses/{id}/retry` rebuilds input from stored results; the workload is not re-run | Outage → retry → Succeeded (attempt 2) |
| Recovery | On startup, Running → Unavailable ("interrupted by restart"); Pending → re-queued | `Startup_recovery_…` test |
| Correlation | `CorrelationMiddleware` accepts or creates `X-Correlation-ID`, echoes it, scopes logs; stored on the analysis and forwarded to LTA | Header asserted at API, record and outbound request |
| Dependency health | `GET /api/integrations/status` (admin): LTA NotConfigured / Healthy / Unavailable; SmartDog **NotIntegrated**. CQ Pulse `/health/ready` is unaffected by LTA outages | Status tests |
| Authorization | Administrator role on all analysis and integration endpoints | 401 anonymous / 403 non-admin tests |
| UI | Run detail → Failure triage panel (state, provider severity, **heuristic** 0–100 signal labelled as such, LTA categories labelled as LTA's, findings with redacted evidence, next steps, correlation id, errors, retry); Settings → Connected services | `failure-triage-panel.spec` (4) |

**End-to-end against a real LogTriageAssistant** (scratch-built from its current working tree, run with `Storage__DatabasePath` in the scratchpad, so LTA's own `App_Data/logtriage.db` was not touched; CQ Pulse API against a temporary `CQPulse_Tests_*` database with all four migrations applied in order, then dropped):

| Check | Result |
|---|---|
| Integration status | LogTriageAssistant `Healthy` |
| Auto-analysis of a 300-test run (210/60/30) | `Succeeded` on attempt 1: severity HIGH, signal 90, findings and 3 recommendations; stored in LTA history for the service account as `cqpulse-run-<runId>.jsonl` |
| Manual analysis of a 200-test run (30 failures), `X-Correlation-ID: e2e-manual-0002` | 202 with the header echoed; `Succeeded`; **30 findings for 30 failures**; correlation id stored |
| Repeat request | 200, same analysis id |
| Run after analysis | Unchanged |
| Logs | No errors in either service |

Observed and fixed during E2E: a run-summary header line produced a spurious finding (LTA's keyword analyzer matched "failed"), so documents now contain failure lines only. LTA reports its signal score on 0–100; the test double was aligned.

**Observed limitation (LTA behaviour, shown as-is):** LTA's keyword categories do not map onto CQ Pulse's failure categories (e.g. "Simulated Timeout failure" was classified as Network, General or Authentication). The UI labels them as LTA's categories next to CQ Pulse's own.

| Command | Result |
|---|---|
| `dotnet test CQPulse.slnx --filter "Category!=Performance"` | ✅ Unit 51/51, Integration 78/78 |
| `npx ng build` / `npx ng test --watch=false` | ✅ / ✅ 40/40 |
| Dev DB (read-only) | Unchanged; 0 test databases left |

### Q3 — Historical quality intelligence (2026-10-13)

All computed by CQ Pulse from its own persisted results; no schema change. Windows are the last N whole UTC days including today, compared with the previous N days.

| Endpoint (admin) | Rule (shown in the UI) | Evidence |
|---|---|---|
| `GET /api/insights/failure-signatures` | Signature = category + message normalized (GUIDs, hex ids, quoted values, numbers → placeholders), 12-hex SHA-256 id. Exact occurrence counts; affected runs/tests exact per message variant, flagged as upper bounds when variants merge. Change vs the previous window. Grouping in SQL (top 500 message groups), merging in memory. | `InsightsTests.Failure_signatures_…`; normalization unit tests |
| `GET /api/insights/flaky-tests` | Catalog case with ≥ `minExecutions` executions and both passes and failures; failure rate; chronological pass↔fail changes (skips ignored). States when the case's simulated outcome is `Random`, i.e. the variation is configured. | Fixture test plus an end-to-end catalog run with a 50/50 Random case |
| `GET /api/insights/anomalies` | Day flagged when \|rate − baseline mean\| > k·σ (z-score), baseline = preceding days with ≥ minimum results, σ floor 1 point; days lacking volume or baseline are reported as "too few results" or "insufficient baseline", not guessed | `InsightCalculatorTests` (spike, normal, flat-baseline floor, drop, insufficient data, low-volume baseline days); integration test with 8 baseline days and a 60% day |
| `GET /api/insights/quality-trends` | Per-project failure rate now vs the previous window, change in percentage points. **Prediction explicitly not offered** (statement returned by the API and shown) | `Quality_trends_compare_windows_and_decline_to_predict` |

UI: new **Insights** page (navigation) with project and window filters, flagged days with baseline, σ and z, all-days table, signatures with change vs the previous window, flaky candidates with the configured-randomness caveat, project trend table and the prediction statement. `insights.page.spec`.

| Command | Result |
|---|---|
| `dotnet test CQPulse.slnx --filter "Category!=Performance"` | ✅ Unit 66/66, Integration 84/84 |
| `npx ng build` / `npx ng test --watch=false` | ✅ / ✅ 41/41 |

### Q4 — SmartDog decision support (2026-10-14)

**SmartDog** (separate repository, `git init` for this work): `f377a89` is a baseline snapshot of the existing code, excluding build output, databases and local secrets. `af04474` adds `POST /api/decisions/evaluate`: a deterministic weighted ranking with per-criterion contributions and explanations, hard constraints, `X-Api-Key` authentication (SHA-256 digests compared in constant time, fail-closed with 503 when no key is configured), correlation ids and `/health`. The conversational endpoints are unchanged. 49 tests (36 evaluator, 13 API).

**CQ Pulse** `7993db7`:

| Concern | Implementation | Evidence |
|---|---|---|
| Evidence | Four criteria from persisted data: occurrences in the run, 30-day project recurrence, affected catalog test cases, LTA triage severity (highest finding severity per signature, stored with the analysis). The rules are in `quality-intelligence-architecture.md` §6 | `DecisionSupportTests` (16), `SeverityMappingTests` |
| Missing evidence | Severity is left out for all options, with the reason, unless the latest successful analysis matches the run's current failure data and covers every signature; never zero-filled | Tests: no analysis, stale digest, incomplete coverage, truncated analysis |
| Failures | Not configured / down / timeout → `Unavailable`; rejected key, 400, malformed response → `Failed`; error stored, no ranking shown | Fake modes ServerError, Unauthorized, BadRequest, Malformed, Slow |
| History | Every request is stored (request JSON, evidence, notes, result or error, correlation id); newest 50 per run | History test |
| UI | Run detail → Decision support: weights, minimum occurrences, ranking with score bars, breakdown, SmartDog notes/method/limitations, evidence, history, links to `/runs/{id}?signature=…#results` | `decision-support-panel.spec` (5), `run-detail.page.spec` (2) |
| Status | `GET /api/integrations/status` checks SmartDog `/health` | Status test |

**Migration `20261014131810_AddDecisionEvaluations`:**

| Step | Result |
|---|---|
| SQL inspected (`dotnet ef migrations script`) | Additive only: adds nullable `FailureAnalyses.SignatureSeveritiesJson nvarchar(max)` and creates `DecisionEvaluations` (FK → `TestRuns` with cascade delete, 2 indexes). No existing column is altered or dropped |
| Target verified | `.\SQLEXPRESS`, database `CQPulse` |
| Backup | `CQPulse_before_AddDecisionEvaluations_20261014T132732Z.bak` (with checksum); `RESTORE VERIFYONLY` passed |
| Rehearsal | Backup restored to a `CQPulse_Tests_<guid>` copy; migration applied there first; data fingerprint unchanged |
| Applied to development | 5 migrations in `__EFMigrationsHistory`; 18 columns in `DecisionEvaluations`, severity column present, cascade FK, both indexes, filtered active-run index still present |
| Data | Fingerprint identical before and after: `3 1 2 26 24125 0 0 1 -944826009 782910834 -539428645 1327653973` (counts of projects, suites, cases, runs, results, logs, analyses, users; checksums of runs, results, projects, cases). Re-checked identical at the end of the release verification |

The earlier migrations (`AddRunConcurrencyAndActiveRunIndex`, `AddRunRerunLink`, `AddFailureAnalyses`) were applied the same way from `CQPulse_before_M5_M7_Q2_migrations_20261014T041852Z.bak`, after a test restore of that backup.

### Release verification (2026-10-14)

**Environment.** All processes ran on the developer machine with these services:

- LogTriageAssistant: built from its current working tree into a scratch folder and run with `Storage__DatabasePath` pointing at a scratch SQLite file, using a dedicated service account. LTA's own `App_Data/logtriage.db` and tree were not touched.
- SmartDog: scratch build, with its API key passed from a scratch file through an environment variable.
- CQ Pulse API: Debug build, against the rehearsal copy (development data plus all 5 migrations).
- Angular dev server: `proxy.conf.json` on port 4300.

Checks ran as Node scripts through the dev proxy, and as browser checks in headless Microsoft Edge driven over the DevTools protocol. The rehearsal copy, scratch secrets, the scratch LTA database and the browser profiles were deleted afterwards.

| Area | Check | Result |
|---|---|---|
| Existing data | Dashboard, analytics, run history on the migrated copy | 26 runs, 19,281/3,699/1,145, 24,125 results, as before migration; insights endpoints 200 |
| Proxy | `/health`, `/health/ready`, anonymous `/api/runs`, deep link | 200, 200, 401, 200; invalid bearer token → 401 |
| Real time | SignalR over WebSockets through the proxy, 500-test catalog run | 102 progress messages with string statuses; final message equals the persisted run; counters equal the preview (220/160/120) |
| Real time | 5,000-test run, REST polled every 40 ms | REST advanced mid-run in persisted steps of 500 (one batch); passed + failed + skipped = completed in every sample; SignalR monotonic, 102 messages |
| LTA | Automatic analysis; repeat request | `Succeeded` (100 stored findings for 160 failures), per-signature severities stored; repeat → 200, same id |
| SmartDog | Decision on the catalog run (`X-Correlation-ID: e2e-q4-decision-0001`) | `Succeeded`/`Ranked`; correlation id stored and echoed; severity used (note names the analysis); affected test cases 3 per signature |
| Scoring | Independent recomputation in the script from the submitted evidence | Ranking and every score identical (±1e-5); contributions sum to each score; normalized weights sum to 1.000000 |
| Links | `GET /runs/{id}/results?signature=…` for each option | 54, 54, 52 = occurrences |
| Constraint | Minimum occurrences 1,000,000 | `NoFeasibleOption`, all 3 listed as infeasible |
| Rerun | Rerun of the catalog run | Linked; identical totals |
| Correlation | Log scopes with `IncludeScopes` | `CorrelationId:e2e-q4-corr-decision-04` in both CQ Pulse and SmartDog logs, same W3C trace id |
| Largest run | 50,000 tests, then decision support | All 12,500 failures counted across 4 signatures; truncated-analysis note shown; 596 / 242 / 208 ms per request |

**Service-offline checks** (each service stopped and confirmed by its closed port):

| Scenario | Result |
|---|---|
| SmartDog down | Decision recorded `Unavailable` ("connection refused"), with evidence and without a result; status Unavailable; a new run completes; LTA analysis still succeeds |
| SmartDog restored | Decision succeeds again |
| LTA down | Run completes; analysis `Unavailable`; status Unavailable; decision still ranks, without severity, with the reason in the notes |
| Both down | 80-test run completes and persists; dashboard works; analysis and decision both `Unavailable`; CQ Pulse `/health/ready` 200 |
| LTA restored | Retry of the same analysis succeeds on attempt 2 without creating a run; severity used again |

**Browser (headless Edge 2026-10-14, real login form, no console errors or uncaught exceptions in any check):**

| Check | Result |
|---|---|
| Simulator live view, 20,000-test catalog run | SignalR connected in 1,643 of 1,645 samples; 80 distinct live values between the 5-second REST checks; progress never moved backwards; final summary `CompletedWithFailures` |
| Ranking panel | Ranking with scores and explanations, expanded breakdown, notes, method, evidence, history |
| Signature link | Opens `/runs/{id}?signature=…#results` showing only that signature's 50 failed rows (first page of 54); "Show all results" clears it |
| Settings | Both integrations Healthy |
| SmartDog stopped | Panel shows `Unavailable` with the reason and "No ranking was produced"; results still load; Settings shows SmartDog Unavailable |

**Defects found during verification and fixed:**

1. **Live progress moved backwards** every 5 s on the simulator page. A REST snapshot (persisted, 500-row batches) replaced newer SignalR progress (executed tests). `mergeRunSnapshot` now keeps the newer live counters until the run is terminal. Covered by 2 client tests and re-verified in the browser (0 backwards moves).
2. **Evidence could be silently understated.** Both evidence queries took 10,000 groups with no ordering or overflow check, so a large project could get undercounted or zero recurrence. The run query is now exact; it is bounded by the run itself, at most `Simulator:MaxTestCount`. The project-wide recurrence query refuses with 409 above 10,000 distinct messages. Covered by a test.
3. **Undisclosed sampling.** Severity from a truncated analysis now carries a note naming the number of failures it covered. Covered by a test.
4. **Unreadable log scopes.** Dictionary scopes printed as type names in console logs. They are now message-template scopes in CQ Pulse (3 places) and SmartDog (1).
5. **Unhelpful status detail.** A status-check timeout showed "A task was canceled."; it now says "Unreachable: timed out waiting for a response." Covered by a test.
6. **Test isolation.** The new overflow test's seeded rows broke the shared-database aggregate invariant for a later test; it now removes them. Found by the full suite run.

**Secrets scan.** No secret scanner is installed, so two scans were run:

- A pattern scan of tracked files and the full history of CQ Pulse (35 commits) and SmartDog (2) for private keys, cloud and VCS tokens, JWTs, connection-string passwords and non-empty secret JSON values. The pattern was self-tested against known samples. No matches.
- An exact-value scan for the actual user-secret values (JWT signing key, development admin password, SmartDog's OpenAI key). It covered tracked, untracked and ignored files and history; LogTriageAssistant got a read-only scan of tracked and untracked files. 0 hits. No value was printed.

Tracked `appsettings*.json` files contain no credentials, and the final E2E service logs contain neither the SmartDog key nor the LTA password.

**Test results (final code):**

| Command | Result |
|---|---|
| `dotnet test CQPulse.slnx --filter "Category!=Performance"` | ✅ Unit 66/66, Integration 109/109; 0 test databases left |
| `dotnet test tests\CQPulse.IntegrationTests --filter "Category=Performance"` | ✅ 7/7 ([performance.md](performance.md)) |
| `npx ng test --watch=false` / `npx ng build` | ✅ 50/50 (11 files) / ✅ initial 303.95 kB |
| SmartDog `dotnet test` | ✅ 49/49 |
| LogTriageAssistant `dotnet test` (built to a scratch folder) | ✅ 57/57; working tree unchanged (same 27 uncommitted entries) |

**Not verified:** GitHub CI for these commits (not pushed); browsers other than Edge/Chromium; SmartDog's conversational flow (needs Ollama); a Release build.

### Release-readiness pass (2026-10-14, evening)

| Item | Result |
|---|---|
| Published commits | `7993db7`, `409c093` and `44cb0da` (README overview) on `origin/main` by fast-forward; no force push; the unrelated `angular.json` change was left unstaged |
| GitHub Actions | Run 38665530212 (`409c093`) ✅ and run 38665944654 (`44cb0da`) ✅: unit 66/66, integration 109/109 on a SQL Server container; frontend job passed |
| Release build | `dotnet build CQPulse.slnx -c Release`: 0 warnings, 0 errors. `dotnet publish src/CQPulse.Api -c Release` succeeded, with no credentials in the published appsettings |
| Tests on the Release build | Unit 66/66, Integration 109/109 (`-c Release --no-build`); client 50/50 and production build (initial 303.95 kB) |

**Clean smoke test with the documented commands.**

- **CQ Pulse:** a new empty database `CQPulse_Tests_<guid>` was created with `dotnet ef database update` (all 5 migrations). The API ran with `dotnet run --project src\CQPulse.Api --launch-profile https`; only `ConnectionStrings__CQPulse` and the `Integrations__*` settings came from environment variables. The client ran with `npm start` on port 4200.
- **SmartDog:** `dotnet run --project SmartDog.Api --launch-profile https`, with `DecisionApi__ApiKeys__0` from the environment.
- **LogTriageAssistant:** its unchanged code, built to a scratch folder and run on `https://localhost:7291` with scratch storage, and a newly registered service account.
- **Cleanup:** the database, scratch credentials and browser profiles were removed afterwards.

| Check | Result |
|---|---|
| Startup | Proxy `/health` 200, `/health/ready` 200, anonymous `/api/runs` 401; SmartDog evaluate without key 401; both integrations Healthy |
| Login, execution, live updates | Login through the form (browser); 500-test catalog run with 102 SignalR messages and exact totals 220/160/120; 5,000-test run with consistent REST progress; 20,000-test run live in the browser with no backward progress |
| Automatic triage | `Succeeded` automatically (100 findings for 160 failures); repeat request deduplicated |
| Decision ranking | Ranked by real SmartDog; independent recomputation identical; signature links exact (54/54/52); infeasible constraint reported |
| History | Decision history newest first; panel history in the browser; rerun linked with identical totals |
| SmartDog down / restored | Evaluation `Unavailable` with the reason and no ranking (API and browser); runs and triage unaffected; succeeds again after restart |
| LTA down / restored | Analysis `Unavailable`; ranking without severity, with the reason; retry succeeds on attempt 2 without a new run |
| Both down | Run completes; dashboard works; both features `Unavailable`; `/health/ready` 200 |

The first browser attempt timed out on the run page. The browser log showed SignalR's "page is being frozen" warning: headless Edge had frozen the tab. The harness now disables background freezing and throttling; rerun with 0 freeze warnings and all checks passed. No application error was logged.

Development database fingerprint after the pass: identical to the pre-migration fingerprint. LogTriageAssistant: still 27 uncommitted entries, `App_Data/logtriage.db` unchanged.

### Portfolio and documentation pass (2026-10-14)

Documentation and screenshots only; no application code, schema or configuration changed.

| Check | Command | Result |
|---|---|---|
| Release build | `dotnet build CQPulse.slnx -c Release` | ✅ 0 warnings, 0 errors |
| Backend tests | `dotnet test CQPulse.slnx -c Release --no-build --filter "Category!=Performance"` | ✅ Unit 66/66, Integration 109/109; 0 test databases left |
| Angular production build | `npm run build` (default configuration `production`) | ✅ No warnings; initial bundle 303.95 kB (84.55 kB transferred) |
| Client tests | `npm test -- --watch=false` | ✅ 50/50 in 11 files |
| CI badge | `…/actions/workflows/ci.yml/badge.svg?branch=main` | ✅ HTTP 200; workflow `CI` at `.github/workflows/ci.yml` is active and reports "passing" |
| Mermaid | Every `mermaid` block in the README and `docs/` parsed and rendered with mermaid 10.9.3 and 11.4.1 in headless Edge | ✅ 2 diagrams, no errors |
| Links | Relative links, images and `#anchors` in the README and `docs/`, checked with GitHub's heading-slug rules | ✅ All valid |
| Secrets and database files | Pattern scan of the changes; no `*.db`, `*.mdf`, `*.ldf` or `*.bak` files tracked; PNG files carry no metadata chunks | ✅ None found |

**How the screenshots were produced.** `docs/images/*.png` were captured with headless Microsoft Edge at 1440 px from the running application.

- **Database:** an isolated `CQPulse_Tests_<guid>` database, restored from the verified development backup with the latest migration applied.
- **Services:** real LogTriageAssistant and SmartDog processes, with scratch storage and credentials.
- **Data:** the "Checkout Service" project, its cases and its runs were created through the API and the UI; the ranking was requested from the run page.

The database, credentials and browser profiles were removed afterwards, and the development database fingerprint is unchanged.

**Documentation changes.**

- The README was rewritten for a first-time reader: summary, features, screenshots, architecture diagram, quality summary, setup, tests, configuration, troubleshooting and limitations.
- The README diagram was redrawn top-to-bottom to show the dev proxy, SignalR, the workers and the request and response directions. The previous diagram rendered correctly on GitHub, but it did not show those parts.
- This audit now opens with the current status, and the initial audit is marked as historical.
- `architecture.md` gained a Testing section, which replaces the test-harness detail removed from the README.
- All documents now link to each other.

### Remediation: exact signatures, analysis backpressure, severity sample (2026-10-15)

Fixes for findings C1–C5 of the cross-repository audit.

| Finding | Fix | Evidence |
|---|---|---|
| C1 Insights merged the 500 most frequent raw messages in memory, so signatures whose messages contain ids were undercounted (16×–187×) | `TestCaseResults.Signature` stored when results are saved; insights group by it in SQL, report unsigned failures separately (`countsComplete`) and no longer report per-variant upper bounds | 960,000 results: 189,600 failures reported vs 163,357 before; every signature equals SQL ground truth. Regression test with 3,600 failures and 3,000 distinct ids |
| C2 Recurrence refused above 10,000 distinct messages | Counted in SQL by stored signature; refused (409) only while the backfill is still running for that window | Test with 12,001 distinct messages; 960,000 results: 200 instead of 409 |
| C3 A full analysis queue blocked the simulation worker, shutdown and startup recovery | Non-blocking queue; Pending analyses that do not fit wait in the database (`waitingForQueueSlot`) and a periodic sweep queues them; completion hooks are limited to 10 s | `AnalysisQueueTests`: saturation, shutdown under saturation, restart recovery, more Pending analyses than the queue holds at startup, orphaned Pending analyses, failing hooks |
| C4 No time index | `IX_TestCaseResults_Outcome_CompletedAtUtc` (covering) | Time-window endpoints equal or faster with the index; writes about 10–20% slower |
| C5 Severity sample of 2,000 lines vs LogTriageAssistant's 500 findings | At most 500 lines, sampled round-robin across signatures (most frequent first); startup rejects larger values; the note states the sample | Test with signatures first failing after line 500 |

| Check | Result |
|---|---|
| Release build | ✅ 0 warnings, 0 errors |
| Backend tests (`--filter "Category!=Performance"`) | ✅ Unit 91/91 (`FailureSignatureTests` pins the normalization rules and known hashes), Integration 122/122 |
| Client tests and production build | ✅ 52/52; build succeeds |
| Migration rehearsal | ✅ Restored copy of the development database and a generated 960,000-result database: idempotent script, backfill, independent recomputation of every stored signature (0 mismatches), checksums of all original data unchanged, rollback restores identical data ([operations](operations.md#rehearsal-results)) |
| Development database | Read only (`COPY_ONLY` backup for the rehearsal); fingerprint identical before and after. The migration awaits approval |

### Cross-repository end-to-end verification (2026-10-15)

All three services ran as real processes over HTTPS, from their pushed commits, on isolated storage. Those were CQ Pulse `4bdaa92` plus the timeout-message fix below, LogTriageAssistant `ea5806d` and SmartDog `f21fe79`. CQ Pulse and SmartDog used temporary SQL Server databases, and LogTriageAssistant a scratch SQLite file. The CQ Pulse service account was registered through LogTriageAssistant's sign-up endpoint. The databases and credentials were removed afterwards.

| Area | Result |
|---|---|
| Analysis and ranking | ✅ Automatic analysis through LogTriageAssistant (severity HIGH, 40 findings). SmartDog ranking with all four criteria equals an independent recomputation from the stored evidence |
| Correlation | ✅ `X-Correlation-ID` stored on the evaluation and present in SmartDog's log scope |
| Idempotency and retries | ✅ An unchanged run reuses its analysis; an explicit retry resubmits stored data (attempt 2, succeeded) |
| Exact analytics | ✅ Failure-signature counts equal SQL ground truth (228 / 206 / 206 of 640) |
| Severity sample | ✅ A 3,000-test run (600 failures): 500-line sample, severity for every signature, and a note stating "500 of the run's 600" |
| Outages | ✅ SmartDog stopped: status Unavailable with the reason, evaluation recorded Unavailable, runs unaffected, and ranking works again after a restart. LogTriageAssistant stopped: analysis Unavailable, succeeding on retry after a restart |
| Queue saturation | ✅ Queue capacity 1 and an unresponsive LogTriageAssistant: four runs completed in 1.9 s, with one analysis in flight, one queued and two explicitly waiting for a slot |
| Timeouts | ✅ In-flight analyses time out and are recorded Unavailable. **Defect found and fixed:** the recorded reason was "A task was canceled."; analyses and evaluations now say "timed out waiting for a response." (re-verified end to end) |
| Restart integrity | ✅ Hard kill while saturated, then restart: the waiting analysis succeeded, the interrupted one was marked for retry, every retry succeeded, and all 8 runs kept consistent counters |
| SmartDog ownership | ✅ Two registered users: every decision path returns 404 to the other user and 200 to the owner; an `ownerId` in the body is ignored; a user JWT is rejected by the evaluation API, and the API key by user endpoints |
| LogTriageAssistant accounts | ✅ Analyses appear only in the service account's history; a second account sees none |

Also observed: `Integrations:LogTriage:RetryCount = 0` stops the API at startup, because the standard resilience handler requires at least one retry. The README documents the default (2); a value of 0 is not supported.
