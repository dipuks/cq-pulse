# CQ Pulse Feature Audit

Audit date: 2026-10-13. Branch `main` at `563f1c9` ("Implement routed CQ Pulse frontend"), with two uncommitted client changes (`src/app/pages.ts`, `src/styles.scss`).

This audit was read-only: no application source was edited, no migration was applied, no data was changed, and no service was started or restarted. Database figures come from `SELECT`-only queries against the local development database. Secrets are referred to by key name only.

## Status legend

| Status | Meaning |
|---|---|
| Verified working | Exercised during this audit and behaved correctly |
| Implemented but not verified | Code exists and looks complete, but was not exercised end to end |
| Partially implemented | Some of the expected behavior exists; there are known gaps |
| Missing | Not present |
| Broken | Present but fails, or provably misbehaves |

## Runtime state at audit time

| Item | Finding |
|---|---|
| API process | **Not running.** Nothing listens on 7256/5191, and the Angular dev server (4200) is not running either. The only `dotnet` processes are VS Code build hosts and MSBuild worker nodes, which hold no locks on the API output. |
| API build output | `src/CQPulse.Api/bin/Debug/net10.0/CQPulse.Api.dll`, built 2026-10-11 22:29, contains `AnalyticsController`. |
| User secrets (keys only) | `ConnectionStrings:CQPulse`, `Jwt:SigningKey`, `DevelopmentAdmin:Email`, `DevelopmentAdmin:Password` are all configured. |
| Database | Local SQL Express, integrated authentication, migration `20261011132614_InitialCreate` applied. `dotnet ef migrations has-pending-model-changes` reports no drift. |

### Database contents (verified)

The figures reported earlier (26 runs and 24,125 results: 19,281 passed, 3,699 failed, 1,145 skipped, 3,699 categorized failures) **match exactly**.

| Table | Rows |
|---|---:|
| TestRuns | 26 (4 Passed, 22 CompletedWithFailures, 0 active) |
| TestCaseResults | 24,125 |
| ExecutionLogs | **0** |
| Projects / TestSuites / TestCases | 3 / 1 / 2 |
| AspNetUsers | 1 |

For every run, `TotalTests = CompletedTests = Passed + Failed + Skipped = persisted result rows`. Results span 2026-10-11 (23,925) and 2026-10-12 (200).

## Feature status

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

## Highest-risk defects

1. **Frontend does not build** (Sass `@use` order). Blocks every UI change and CI.
2. **Routed pages are unstyled at `HEAD`** (design system scoped to `App`).
3. **Runs can hang or remain `Running`** after worker exceptions or a persistence failure.
4. **SignalR cannot connect** (CORS credentials), plus enum serialization once it does.
5. **Silent client failures**: no 401 handling and no error handlers on analytics/runs pages, which is how a working backend appears as "no persisted outcomes".
6. **Validation errors return 500.**
7. **Cancel/complete race** and unsaved counters on cancelled runs.
8. **Non-reproducible seeded runs.**

## Commands executed during this audit

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
