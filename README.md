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

Open `http://localhost:4200`. The API listens on `https://localhost:7256`.

## Verification

```powershell
dotnet test CQPulse.slnx
Set-Location src\CQPulse.Client
npm run build
```

## Current limitations

The API provides JWT login and a development-only admin seeding mechanism. The browser login flow, route authorization, management screens, batched result persistence for very large workloads, analytics, and CI remain in active development. The simulator is an MVP in-process worker and is not a durable distributed job system.
