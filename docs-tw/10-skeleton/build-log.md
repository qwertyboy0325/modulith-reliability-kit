# Skeleton 建置紀錄

## 目的

追蹤 `ModulithReliabilityKit` 骨架每個階段建立了哪些內容。

## Phase 1

已完成：

- 建立解決方案與根層建置檔：
  - `src/ModulithReliabilityKit.sln`
  - `src/global.json`
  - `src/Directory.Build.props`
  - `src/Directory.Packages.props`
- 建立並接入專案：
  - `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Domain`
  - `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Application`
- 完成 Domain 基礎：
  - `Entity`、`ValueObject`、`StronglyTypedId<TValue>`、`IDomainEvent`、`DomainEventBase`
  - `IBusinessRule`、`IAsyncBusinessRule`、`BusinessRuleValidationException`、`IAggregateRoot`
- 完成 Application 基礎：
  - Commands/Queries 契約
  - Domain event notifications
  - Integration event 契約與可靠性標記
  - Outbox/Inbox message 契約
  - `IUnitOfWork`、`IExecutionContextAccessor`
  - `EntityNotFoundException`、`InvalidCommandException`
  - 分頁 helper
- 新增章節：
  - `docs/10-skeleton/implementation-decisions.md`
  - `docs-tw/10-skeleton/implementation-decisions.md`
  - 本 build log（`docs` + `docs-tw`）

## Phase 2

已完成：

- 新增並接入 `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure`
- 完成：
  - 顯式交易 `UnitOfWork`
  - 領域事件派發服務（`DomainEventsAccessor`、`DomainEventsDispatcher`、mapper）
  - MediatR pipeline behaviors（`ValidationBehavior`、`LoggingBehavior`、`UnitOfWorkBehavior`）
  - 不吞例外的 `InMemoryEventBus`
  - 可重用 `OutboxProcessorBase`（消費端後續改用交易安全的 `NotificationsInboxProcessor`，不沿用共用基底）
  - StronglyTypedId 的 EF converter helper
  - DI 擴充 `AddModulithReliabilityKitBuildingBlocks(IServiceCollection)`
  - JSON 預設（`System.Text.Json`）
- 新增 outbox/inbox SQL 模板：
  - `docs/10-skeleton/outbox-inbox-schema-template.sql`
  - `docs-tw/10-skeleton/outbox-inbox-schema-template.sql`
- 已完成 Phase 2 後整體建置驗證。

待完成：

- Phase 3：API Host + 架構測試 + 最終驗證

## Phase 3

已完成：

- 新增 API Host：
  - `src/Api/ModulithReliabilityKit.Api`
  - Serilog、Swagger/OpenAPI、`/health`、root endpoint、模組註冊接縫註解
  - `AddModulithReliabilityKitBuildingBlocks(includePersistenceServices: false)`，確保在尚未接 module DbContext 前可啟動
- 新增架構測試：
  - `src/Tests/ModulithReliabilityKit.ArchitectureTests`
  - 規則：Domain 不依賴 MediatR/EF Core；Application 不依賴 Infrastructure
- 驗證結果：
  - `dotnet build src/ModulithReliabilityKit.sln` 通過
  - `dotnet run` + `curl /health` 回傳 `Healthy`
  - `dotnet test src/ModulithReliabilityKit.sln` 通過（3/3）

待完成：

- 本計畫範圍內無。

## Phase 4

已完成：

- 套用改善 #1（module facade）：
  - 新增 `ICatalogModule` 契約
  - 新增 `CatalogModuleFacade`
  - API endpoints 改為呼叫 `ICatalogModule`（endpoint 邊界不再直接依賴 raw `ISender`）
- 套用改善 #2（typed module persistence adapter）：
  - 新增 `CatalogUnitOfWork`
  - 新增 `CatalogDomainEventsAccessor`
  - 移除 Catalog 模組註冊中的裸 `DbContext` forwarding
- 新增 PostgreSQL 最小示範（migration 建 schema）：
  - 新增 `docker-compose.postgres.yml`
  - 新增本機 tool manifest + `dotnet-ef`
  - 新增 EF design-time 支援
  - 新增 Catalog 初始 migration 檔
  - 新增啟動時 migration hosted service
- 新增文件：
  - `docs/10-skeleton/applied-improvements-and-postgres-demo.md`
  - `docs-tw/10-skeleton/applied-improvements-and-postgres-demo.md`

驗證結果：

- `dotnet build src/ModulithReliabilityKit.sln --no-restore --disable-build-servers /m:1` 通過
- `dotnet test src/Tests/ModulithReliabilityKit.ArchitectureTests/ModulithReliabilityKit.ArchitectureTests.csproj --no-build` 通過（7/7）
- PostgreSQL container 啟動且 healthy
- migration 套用完成，且驗證 `catalog.products` / `catalog.outbox_messages`
- API smoke test（create/get product）成功

## Phase 5

已完成：

- 套用 Priority 2 完整版可重用 module persistence：
  - 新增 `IModuleUnitOfWork<TContext>` 與可重用 `ModuleUnitOfWork<TContext>`
  - 新增 typed `IDomainEventsAccessor<TContext>` / `DomainEventsAccessor<TContext>`
  - 新增 typed `IDomainEventsDispatcher<TContext>` / `DomainEventsDispatcher<TContext>`
  - 新增 `AddModulePersistence<TContext>(requestAssembly)` 作為模組註冊入口
  - 新增依 request assembly 選擇 UoW 的 `IUnitOfWorkResolver`，讓多個 module context 可共存於同一 root container
- Catalog 改用 Building Blocks 的可重用 adapter：
  - 移除 `CatalogUnitOfWork`
  - 移除 `CatalogDomainEventsAccessor`
  - 將客製 persistence wiring 改為 `services.AddModulePersistence<CatalogContext>(CatalogApplicationAssembly.Assembly)`

驗證結果：

- `dotnet build src/ModulithReliabilityKit.sln --no-restore --disable-build-servers /m:1` 通過
- `dotnet test src/Tests/ModulithReliabilityKit.ArchitectureTests/ModulithReliabilityKit.ArchitectureTests.csproj --no-build` 通過（7/7）
- API smoke test（create/get product）成功，且走 typed module UoW
