# Skeleton Implementation Decisions (Reference vs ModulithReliabilityKit)

## Purpose

Record what changed while building the `ModulithReliabilityKit` skeleton, why it changed, and the trade-offs.
This is a comparison chapter, not a feature chapter.

## Scope

- Reference inspected: prior-art modular-monolith codebases (see `07-module-architecture/modular-monolith-ddd-comparison.md`)
- Skeleton implemented: `src/*`
- Module-level implementation is intentionally out of scope in this pass.

## Decision Matrix

| Topic | Reference approach | ModulithReliabilityKit approach | Pros | Cons | Affected files |
|------|------|------|------|------|------|
| DI container topology | Host Autofac + per-module static `IContainer` roots | Single MS.DI container (module seam reserved for later) | Simpler lifecycle, less hidden global state, fewer scope leaks | We lose hard runtime module isolation until module layer is added | `src/Api/*` (later), `src/BuildingBlocks/*` |
| Command pipeline | Mixed decorators and behaviors; module differences | Single pattern: MediatR `IPipelineBehavior` stack (Validation/Logging/UoW) | Uniform and testable flow | Requires strict registration discipline in one composition root | `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/*` |
| Unit of Work transaction boundary | Implicit EF `SaveChanges` transaction | Explicit transaction in UoW | Cross-store consistency policy is explicit and auditable | Slightly more boilerplate and failure paths to test | `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/UnitOfWork.cs` |
| Domain event coupling | `IDomainEvent : INotification` (Domain depends on MediatR) | Domain MediatR-free; adaptation via Application notifications | Cleaner domain boundary | Requires explicit mapping layer | `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Domain/*`, `...Application/Events/*` |
| Value object style | Reflection-based equality base class | Explicit components (`GetEqualityComponents`) | Predictable behavior and performance | More manual implementation per type | `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Domain/ValueObject.cs` |
| Strongly typed ID style | Mixed (`TypedIdValueBase` and newer record IDs) | Single style (`StronglyTypedId<TValue>`) | Consistency, easier converters | Migration cost if legacy IDs are imported later | `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Domain/StronglyTypedId.cs` |
| Event contracts namespace | Duplicate definitions in App + Infrastructure aliases | Canonical single definition in Application | Eliminates namespace drift and alias debt | Requires upfront migration discipline | `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Application/Events/*` |
| Outbox/Inbox abstraction location | Outbox in Application, Inbox mostly Infrastructure | Outbox + Inbox contracts in Application | Symmetric messaging abstractions | Infrastructure still must own persistence mechanics | `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Application/Outbox/*`, `.../Inbox/*` |
| Event bus failure policy | In-memory bus client logs and swallows exceptions by default | In-memory bus will not swallow by default | Fail-fast for critical paths, clearer reliability semantics | Can increase visible failures without retry policies | `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/Events/*` |
| Package version management | Conditional package blocks by project name | Central package management (`Directory.Packages.props`) | One source of truth, faster upgrades | Requires package version hygiene across all projects | `src/Directory.Packages.props` |

## What was changed in this pass

1. Created the new `src/` solution skeleton.
2. Implemented `BuildingBlocks.Domain` baseline types (MediatR-free domain events, business rules, value object base, strongly typed id base).
3. Implemented `BuildingBlocks.Application` baseline contracts (commands/queries, event contracts, outbox/inbox models, UoW contract, execution context, exceptions, paging).
4. Implemented `BuildingBlocks.Infrastructure` (explicit-transaction UoW, domain-events dispatcher, pipeline behaviors, in-memory bus, processor bases, strongly-typed-id converters, DI extension).
5. Added `ModulithReliabilityKit.Api` host (Swagger, health endpoint, module seam) and `ModulithReliabilityKit.ArchitectureTests`.
6. Added this comparison chapter and build log files (`docs` + `docs-tw`).

## Open questions for next pass

- How strict should module isolation be once module projects are added?
- Which integration events are `Durable` vs `BestEffort` by policy?
- Should direct publish ever be allowed in module handlers?
