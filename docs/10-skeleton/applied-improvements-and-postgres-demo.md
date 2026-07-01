# Applied Improvements + PostgreSQL Minimal Demo

## Purpose

This chapter records the concrete implementation of the previously agreed improvement backlog and
provides a runnable PostgreSQL demo path.

## Applied improvements

### 1) Module facade (`ICatalogModule`)

Implemented:

- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Application/Contracts/ICatalogModule.cs`
- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/CatalogModuleFacade.cs`
- `src/Api/ModulithReliabilityKit.Api/Modules/CatalogEndpoints.cs` (switched from `ISender` to `ICatalogModule`)

Why:

- API no longer depends on raw MediatR.
- Module contract is explicit and stable.
- Keeps module boundary clean while staying MS.DI-first.

### 2) Remove bare `DbContext` forwarding

Implemented:

- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/ModulePersistence/IModuleUnitOfWork.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/ModulePersistence/ModuleUnitOfWork.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/ModulePersistence/IUnitOfWorkResolver.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/ModulePersistence/ModuleUnitOfWorkResolver.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/DomainEventsDispatching/IDomainEventsAccessorOfT.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/DomainEventsDispatching/DomainEventsAccessorOfT.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/DomainEventsDispatching/IDomainEventsDispatcherOfT.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/DomainEventsDispatching/DomainEventsDispatcherOfT.cs`
- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/Configuration/CatalogModule.cs` (registration changed)

Why:

- Avoids `DbContext` last-wins across modules.
- Keeps one host container and lets each module register typed persistence with
  `services.AddModulePersistence<TContext>(requestAssembly)`.
- Lets a second module reuse the same UoW/accessor/dispatcher package without creating module-specific copies.

### 3) PostgreSQL + migration-based schema creation

Implemented:

- Local tool manifest: `.config/dotnet-tools.json` (`dotnet-ef`)
- Migration support packages:
  - `src/Directory.Packages.props` (`Microsoft.EntityFrameworkCore.Design`)
  - `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/ModulithReliabilityKit.Modules.Catalog.Infrastructure.csproj`
  - `src/Api/ModulithReliabilityKit.Api/ModulithReliabilityKit.Api.csproj`
- Design-time factory:
  - `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/Configuration/CatalogContextFactory.cs`
- Initial migration:
  - `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/Migrations/20260601052203_InitialCatalog.cs`
  - `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/Migrations/20260601052203_InitialCatalog.Designer.cs`
  - `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/Migrations/CatalogContextModelSnapshot.cs`
- Startup migration host service:
  - `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/Configuration/CatalogDatabaseInitializerHostedService.cs`
- PostgreSQL container:
  - `docker-compose.postgres.yml`

Why:

- Database schema creation is deterministic and versioned.
- Startup applies pending migrations automatically for local/demo environments.

## Minimal demo runbook

### 1. Start PostgreSQL

```bash
docker compose -f docker-compose.postgres.yml up -d
```

### 2. Apply migration explicitly (optional, but recommended for first boot)

```bash
dotnet tool run dotnet-ef database update \
  --project src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/ModulithReliabilityKit.Modules.Catalog.Infrastructure.csproj \
  --startup-project src/Api/ModulithReliabilityKit.Api/ModulithReliabilityKit.Api.csproj
```

### 3. Run API

```bash
dotnet run --project src/Api/ModulithReliabilityKit.Api/ModulithReliabilityKit.Api.csproj --urls http://localhost:5099
```

### 4. Smoke test

```bash
curl -X POST "http://localhost:5099/catalog/products/" \
  -H "Content-Type: application/json" \
  -d '{"name":"Demo Product","price":12.50,"currency":"usd"}'
```

```bash
curl "http://localhost:5099/catalog/products/{id}"
```

### 5. Verify schema

```bash
docker exec modulith_reliability_kit-postgres psql -U modulith_reliability_kit -d modulith_reliability_kit \
  -c "select schemaname, tablename from pg_tables where schemaname='catalog' order by tablename;"
```

Expected tables:

- `catalog.products`
- `catalog.outbox_messages`

## Verification results in this pass

- `dotnet build src/ModulithReliabilityKit.sln --no-restore --disable-build-servers /m:1` passed (0 warnings, 0 errors).
- `dotnet test src/Tests/ModulithReliabilityKit.ArchitectureTests/ModulithReliabilityKit.ArchitectureTests.csproj --no-build` passed (7/7).
- PostgreSQL container started and reached `healthy`.
- EF migration applied successfully.
- API smoke test created and fetched products successfully.
- Priority 2 typed module persistence smoke test created and fetched product
  `1acbf051-3343-4462-9d01-f051bca69c5b` successfully.

## Notes

- `src/Api/ModulithReliabilityKit.Api/appsettings.json` now uses a plain connection string (no `Search Path`) to
  avoid migration-history lookup issues across schemas.
- This demo initializer is intended for local/dev bootstrap. Production rollout should use controlled
  migration execution (pipeline/job) with explicit change windows.
