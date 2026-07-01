# 已套用改善 + PostgreSQL 最小示範

## 目的

本章記錄先前 agreed improvement backlog 的實作落地，並提供可直接執行的 PostgreSQL 示範路徑。

## 已套用改善

### 1) Module facade（`ICatalogModule`）

已實作：

- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Application/Contracts/ICatalogModule.cs`
- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/CatalogModuleFacade.cs`
- `src/Api/ModulithReliabilityKit.Api/Modules/CatalogEndpoints.cs`（由 `ISender` 改為 `ICatalogModule`）

原因：

- API 不再直接依賴 raw MediatR。
- 模組 contract 明確且穩定。
- 維持 MS.DI-first 同時讓模組邊界更乾淨。

### 2) 移除裸 `DbContext` forwarding

已實作：

- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/ModulePersistence/IModuleUnitOfWork.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/ModulePersistence/ModuleUnitOfWork.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/ModulePersistence/IUnitOfWorkResolver.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/ModulePersistence/ModuleUnitOfWorkResolver.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/DomainEventsDispatching/IDomainEventsAccessorOfT.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/DomainEventsDispatching/DomainEventsAccessorOfT.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/DomainEventsDispatching/IDomainEventsDispatcherOfT.cs`
- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/DomainEventsDispatching/DomainEventsDispatcherOfT.cs`
- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/Configuration/CatalogModule.cs`（註冊改為 typed reusable adapter）

原因：

- 避免多模組情境下 `DbContext` last-wins。
- 保持單一 host container，讓各模組透過 `services.AddModulePersistence<TContext>(requestAssembly)` 註冊 typed persistence。
- 第二個模組可直接重用同一套 UoW/accessor/dispatcher，不需要複製 module-specific 類別。

### 3) PostgreSQL + migration 方式建立 schema

已實作：

- 本機 tool manifest：`.config/dotnet-tools.json`（`dotnet-ef`）
- migration 支援套件：
  - `src/Directory.Packages.props`（`Microsoft.EntityFrameworkCore.Design`）
  - `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/ModulithReliabilityKit.Modules.Catalog.Infrastructure.csproj`
  - `src/Api/ModulithReliabilityKit.Api/ModulithReliabilityKit.Api.csproj`
- design-time factory：
  - `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/Configuration/CatalogContextFactory.cs`
- 初始 migration：
  - `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/Migrations/20260601052203_InitialCatalog.cs`
  - `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/Migrations/20260601052203_InitialCatalog.Designer.cs`
  - `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/Migrations/CatalogContextModelSnapshot.cs`
- 啟動時 migration hosted service：
  - `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/Configuration/CatalogDatabaseInitializerHostedService.cs`
- PostgreSQL container：
  - `docker-compose.postgres.yml`

原因：

- 資料庫 schema 建立可版本化、可重現。
- 本地/示範環境啟動時可自動套用待執行 migration。

## 最小示範操作

### 1. 啟動 PostgreSQL

```bash
docker compose -f docker-compose.postgres.yml up -d
```

### 2. 顯式套用 migration（首次啟動建議執行）

```bash
dotnet tool run dotnet-ef database update \
  --project src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/ModulithReliabilityKit.Modules.Catalog.Infrastructure.csproj \
  --startup-project src/Api/ModulithReliabilityKit.Api/ModulithReliabilityKit.Api.csproj
```

### 3. 啟動 API

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

### 5. 驗證 schema

```bash
docker exec modulith_reliability_kit-postgres psql -U modulith_reliability_kit -d modulith_reliability_kit \
  -c "select schemaname, tablename from pg_tables where schemaname='catalog' order by tablename;"
```

預期資料表：

- `catalog.products`
- `catalog.outbox_messages`

## 本輪驗證結果

- `dotnet build src/ModulithReliabilityKit.sln --no-restore --disable-build-servers /m:1` 通過（0 warning / 0 error）。
- `dotnet test src/Tests/ModulithReliabilityKit.ArchitectureTests/ModulithReliabilityKit.ArchitectureTests.csproj --no-build` 通過（7/7）。
- PostgreSQL container 啟動且狀態 `healthy`。
- EF migration 套用成功。
- API smoke test 成功建立並查詢 product。
- Priority 2 typed module persistence smoke test 可成功 create/get product
  `1acbf051-3343-4462-9d01-f051bca69c5b`。

## 備註

- `src/Api/ModulithReliabilityKit.Api/appsettings.json` 已改為 plain connection string（不使用 `Search Path`），
  避免 migration history 在跨 schema 查詢時的問題。
- 此 initializer 主要給 local/dev bootstrap。Production 建議改由 pipeline/job 以受控方式執行 migration。
