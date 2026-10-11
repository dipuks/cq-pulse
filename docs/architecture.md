# Architecture

CQ Pulse uses a layered .NET 10 backend and an Angular 22 standalone client.

- **Domain** contains entities, enums, and run state transitions.
- **Application** defines DTOs and contracts for catalog and simulation use cases.
- **Infrastructure** owns EF Core SQL Server persistence, Identity stores, migrations, development data, and the bounded in-process simulator worker.
- **API** exposes REST, OpenAPI, JWT login, health endpoints, and the SignalR runs hub.
- **Client** is a responsive Angular dashboard that starts configurable simulations and reconciles progress through REST polling.

The SQL Server database is authoritative. The simulator uses a bounded channel and bounded `Parallel.ForEachAsync` concurrency. It is an MVP in-process queue: it is not a durable distributed job system.
