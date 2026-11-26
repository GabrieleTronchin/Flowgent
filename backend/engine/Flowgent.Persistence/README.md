# Flowgent.Persistence

Saves runs, run events and inbox messages with Entity Framework Core (in-memory database today). It implements the store interfaces defined in `Flowgent.Engine.Core`.

## What is inside

| File | Role |
|------|------|
| `EfRunStore.cs` | **Core.** Saves a run and its events together in one `SaveChanges` (dual write). |
| `EfInboxStore.cs` | **Core.** The inbox queue. |
| `EngineDbContext.cs` and the `*Entity.cs` files | **Support.** The EF Core model. |
| `SequenceGenerator.cs` | **Support.** Gives each event a growing sequence number. |
| `PersistenceServiceExtensions.cs` | Registers the stores in dependency injection. |

## Rules

- May depend on `Flowgent.Engine.Core`, never the other way round: the engine does not know how data is stored.
- Must not reference any module project.