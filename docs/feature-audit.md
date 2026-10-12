# CQ Pulse Feature Audit

Audit date: 2026-10-11. Evidence is limited to the current repository and local SQL Server verification; no database was reset.

| Feature | Status | Evidence | Gaps | Recommended next action |
|---|---|---|---|---|
| Projects, suites, and cases | Partially Implemented | `CatalogService`, catalog controllers; routed Angular project/suite/case pages in `src/CQPulse.Client/src/app/pages.ts` | API has create/read/update and page/search in places, but no delete/archive, consistent sorting/filtering, project-level aggregate counts, or list pagination controls | Add archive semantics, sortable/filterable query contracts, project aggregates, and list pagination controls |
| Configurable simulator | Partially Implemented | `SimulationRequest`, `SimulationService.Validate`, dashboard outcome inputs; failure categories and seed accepted | Suite workload resolution, generated-case storage, presets, preview UI, logging detail are absent | Add configuration presets/preview and workload source selection |
| 1,000/10,000 workloads | Partially Implemented | Verified after batched persistence: 1,000 persisted in 654 ms; 10,000 persisted in 4,235 ms, zero simulated duration, concurrency 128 | Measurements include no real delay or memory/DB write telemetry; 50,000 unmeasured | Add repeatable performance tests with memory/write metrics and a 50,000 scenario |
| Execution architecture | Partially Implemented | Bounded queue (100), `Parallel.ForEachAsync`, cancellation registry, bounded 2,000-item result channel, short-lived 500-result EF save batches, and startup `ExecutionRecoveryService` that marks queued/running runs failed after restart | No durable queue, retry policy, or multi-run worker scaling | Add retry/failure policy and durable job infrastructure when needed |
| Database and migrations | Partially Implemented | `CQPulseDbContext`, migration `20261011132614_InitialCreate`, indexes for runs/results/logs; result snapshot and nullable case foreign key | No migration test, archive semantics, or measured query plan review | Add relational integration coverage and data-retention policy |
| SignalR | Partially Implemented | Administrator authorization on `RunsHub`; JWT query-token handling in `Program`; dedicated Simulator page `HubConnection` joins the run group, handles `RunProgressUpdated`, reconnects and re-joins, while REST polls the authoritative run state | No resource-level project membership model or SignalR integration tests; live connectivity was not browser-verified in this increment | Add project membership and integration/reconnect tests |
| Dashboard | Partially Implemented | `DashboardController.GetSummary`, `/api/runs`, `/api/projects`, and `DashboardPage` | Dashboard presents persisted summary/recent run/project data, but does not yet render the persisted trend and failure-breakdown projections | Bind trends and failure categories to dashboard visualizations |
| Analytics | Partially Implemented | `AnalyticsController` provides persisted-result summary, date/project-filtered daily trends, and failure-category breakdown; consistent pass/failure-rate and average-duration calculations | Dashboard charts are not yet bound to these responses; no throughput/project comparison tests | Bind dashboard panels and add aggregate/date-filter tests |
| Run details | Partially Implemented | `RunsController` supports server-side run search/status/date filters, sorting, pagination, run-detail projection, and paged results/logs; `RunsPage`/`RunDetailPage` display persisted runs, outcomes, errors, and logs | UI does not yet expose date/project/sort filters or pagination controls; worker logs are normally empty; no rerun endpoint | Add remaining filters/pagination, persisted execution logs, and rerun support |
| Authentication and authorization | Partially Implemented | Identity/JWT login, `AddAuthorization`, administrator authorization on catalog/simulator/run/dashboard controllers and `RunsHub`; Angular REST interceptor and SignalR access-token factory | The current model has no project membership relation: administrators intentionally have global access; no non-admin project role or authorization test suite exists | Add project membership before introducing non-administrator users and automated 401/403 tests |
| Theme | Fully Implemented | `ThemeService`, CSS variables, local-storage preference, dark-mode control in application shell and Settings | System preference is initial-only | Add theme service tests |
| Routing, responsiveness, accessibility | Partially Implemented | `provideRouter(routes)`, `authGuard`, application shell, router-link sidebar, default/unknown redirects, all requested protected routes, responsive SCSS | Components are currently eagerly loaded; no automated accessibility coverage; suite/case navigation is contextual rather than a global menu item | Add lazy route boundaries and accessibility tests |
| Automated tests | Broken | `TestRunTests` has 3 lifecycle tests; integration project only template test | No simulator, API, Angular, SignalR, cancellation, or performance tests | Add relational integration and performance test suite first |
| Documentation, CI, deployment | Partially Implemented | `README.md`, architecture plan, GitHub Actions build/test workflow | No API/simulator docs, screenshots, Docker/deployment readiness | Add operations/API/performance documentation |

## Verified execution results

The current local SQL Server run used zero simulated duration and maximum configured concurrency. The original end-of-run collection measurement was replaced by the verified bounded-channel/batched-persistence measurement:

| Workload | Terminal status | Persisted results | Elapsed |
|---:|---|---:|---:|
| 1,000 | CompletedWithFailures | 1,000 | 654 ms |
| 10,000 | CompletedWithFailures | 10,000 | 4,235 ms |

These measurements do **not** establish 50,000-case feasibility, memory behavior, or production throughput. Results are now bounded before persistence, but 50,000 cases must not be claimed as validated.

## Priority and next steps

1. Add restart reconciliation, execution failure transitions, and durable/retry policy.
2. Add simulator execution, aggregation, failure, cancellation, and high-volume tests.
3. Authenticate SignalR and connect the Angular client for real-time progress with REST reconciliation.
4. Implement computed analytics endpoints and replace illustrative dashboard panels.
5. Complete management workflows, authorization, routing, and operational documentation.

## Commands verified in this audit increment

```powershell
dotnet build CQPulse.slnx --no-restore
dotnet test CQPulse.slnx --no-build
Set-Location src\CQPulse.Client
npm run build
```

The Angular SignalR client production build passed after adding `@microsoft/signalr`. Existing backend unit/integration suites remain limited to four placeholder/lifecycle tests; dedicated simulator performance tests are still required.

Authorization smoke test after registering `AddAuthorization`: anonymous `/api/projects` and `/hubs/runs/negotiate` returned `401`; anonymous `/health` remained `200`.

## Frontend implementation — 2026-10-11

- `app.routes.ts` now configures `/login`, `/dashboard`, `/projects`, project create/edit/suite routes, suite/case routes, `/runs`, `/runs/:runId`, `/simulator`, `/analytics`, `/settings`, a protected default redirect, and not-found handling.
- `App` is now a shared routed shell. Its sidebar uses `routerLink`/`routerLinkActive`; it no longer changes in-memory section state or renders the prior workflow-completion placeholder.
- `core.ts` provides typed API contracts/services, a JWT-presence route guard, authentication service, and safely persisted light/dark preference.
- `pages.ts` implements API-backed project, suite, case, run, run-detail, simulator, analytics, and settings screens. The Simulator invokes the secured preview/start/cancel endpoints, polls persisted state, and reports whether SignalR is connected, reconnecting, or unavailable while REST reconciliation remains active.
- Browser direct URL handling is provided by Angular routes. Production hosting must still rewrite unknown application paths to `index.html`; this is a deployment server responsibility not represented in the Angular source.

### Frontend verification

```powershell
Set-Location src\CQPulse.Client
npm run build
npm test -- --watch=false
```

Both commands completed successfully on 2026-10-11. The production build emits an existing component-style budget warning: `app.scss` is 11.24 kB against a 9 kB warning budget. The test suite contains two passing shell/router tests. Route-specific component, API-service, form-validation, and live SignalR integration tests remain required.
