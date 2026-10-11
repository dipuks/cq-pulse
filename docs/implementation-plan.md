# CQ Pulse Implementation Plan

## Environment assessment

- .NET SDK 10.0.401 and ASP.NET Core 10 runtime are installed.
- Node.js 24.18.1, npm 11.16.0, and Angular CLI 22.1.2 are installed.
- GitHub CLI is authenticated as `dipuks`; the private `dipuks/cq-pulse` repository was created.
- Docker is not installed, so local SQL Server containers are not an available default. The API will require an explicit SQL Server/Azure SQL connection string and will not create, reset, or modify a database until one is configured.

## Delivery phases

1. **Foundation** — Create the layered .NET 10 solution, Angular 22 client, shared conventions, documentation, and CI.
2. **Core API** — Implement domain entities, EF Core SQL Server persistence, validation, Problem Details, project/suite/test-case APIs, authentication foundation, and migrations.
3. **Simulator** — Implement validated simulation requests, bounded background execution, batched persistence, cancellation, lifecycle recovery, and SignalR progress.
4. **Frontend foundation** — Create the responsive Angular shell, design tokens, theme persistence, landing page, authentication flow, API client, and route protection.
5. **Product workflows** — Implement management pages, dashboard, simulator, live run details, results, logs, pagination, and analytics with real API integration.
6. **Quality and delivery** — Add focused unit/integration/frontend tests, scale-test tooling, CI, and complete operating documentation.

## Key decisions

- Use ASP.NET Core Identity with JWT bearer authentication and role policies.
- Use SQL Server through EF Core. Database schema changes are represented by migrations; applying them remains an explicit local setup step.
- Use an in-process bounded `Channel` queue for the MVP. On startup, interrupted queued/running runs are marked failed with an explanatory reason.
- Keep the database authoritative; SignalR sends throttled aggregate progress only, and clients reconcile with REST after reconnecting.
- Use server-side projections and pagination for all large datasets.

## Current status

Phase 0 and the initial persistence foundation are complete. The local `CQPulse` SQL Express database has an applied initial migration. A runnable simulator and dashboard vertical slice are in progress; authentication, catalog CRUD, robust batch persistence, analytics, and CI remain outstanding.
