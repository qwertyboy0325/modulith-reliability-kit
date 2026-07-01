# Catalog 模組最佳實踐

## 1. 目的

本章記錄 `ModulithReliabilityKit` 骨架中的第一個具體生產者模組：`src/Modules/Catalog/`。它刻意保持輕 domain，但覆蓋完整模組範圍：Domain、Application、IntegrationEvents、Infrastructure、API endpoint、outbox、背景處理與架構測試。

## 2. 檢視了什麼

公開既有成果：**Kamil Grzybek 的「Modular Monolith with DDD」（公開參考）** —— 其 module facade、每模組啟動／註冊、EF context + repository + entity-type configuration 風格，以及公開整合事件契約。以此標明出處；見 `modular-monolith-ddd-comparison.md`。

新實作（此套件）：

- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Domain/`
- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Application/`
- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.IntegrationEvents/`
- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/`
- `src/Api/ModulithReliabilityKit.Api/Program.cs` 與 Catalog API 端點
- `ModulithReliabilityKit.ArchitectureTests`

## 3. 實際實作摘要

參考的每模組模式有價值，但不能照抄。

**ACTUAL in reference:** 每個模組可擁有自己的 EF context、repository、outbox、scheduler、integration events 與模組組合根，並為每個模組建立自己的 IoC container。

**VERDICT:** 抄模組邊界與完整度；不要抄每模組 container。

`Catalog` 保留可取部分：

- 一個模組資料夾拆成四個 assembly：`Domain`、`Application`、`IntegrationEvents`、`Infrastructure`
- `Domain` 不依賴 MediatR、FluentValidation、EF Core、Infrastructure
- `Application` 擁有 use case、validator、query port、notification handler、outbox enqueue
- `IntegrationEvents` 是唯一公開給其他模組引用的 contract assembly
- `Infrastructure` 擁有 EF Core、repository implementation、read store、outbox storage、outbox processor、module DI
- API 只把 HTTP 映射成 MediatR request，不放模組業務邏輯

## 4. 模組形狀

```text
src/Modules/Catalog/
  ModulithReliabilityKit.Modules.Catalog.Domain/
    Products/
      Product.cs
      ProductId.cs
      Money.cs
      Rules/
      Events/
      IProductRepository.cs
  ModulithReliabilityKit.Modules.Catalog.Application/
    Products/
      CreateProduct/
      RenameProduct/
      GetProduct/
      Events/
  ModulithReliabilityKit.Modules.Catalog.IntegrationEvents/
    ProductCreatedIntegrationEvent.cs
  ModulithReliabilityKit.Modules.Catalog.Infrastructure/
    CatalogContext.cs
    Configuration/CatalogModule.cs
    Domain/Products/
    Outbox/
    Processing/
```

重要方向：

```text
Infrastructure -> Application -> Domain
Application -> IntegrationEvents
Infrastructure -> IntegrationEvents
Other modules -> IntegrationEvents only
```

任何模組都不應引用另一個模組的 `Domain`、`Application`、`Infrastructure`。

## 5. 請求流程

Create product:

```text
HTTP POST /catalog/products
  -> CatalogEndpoints
  -> ICatalogModule.ExecuteCommandAsync(CreateProductCommand)
  -> CatalogModuleFacade 內部 ISender.Send(CreateProductCommand)
  -> ValidationBehavior
  -> LoggingBehavior
  -> CreateProductCommandHandler
  -> Product.Create(...)
  -> IProductRepository.AddAsync(...)
  -> UnitOfWorkBehavior
  -> IUnitOfWorkResolver
  -> ModuleUnitOfWork<CatalogContext>
  -> DomainEventsDispatcher<CatalogContext>
  -> ProductCreatedNotificationHandler
  -> IOutbox.Add(ProductCreatedIntegrationEvent payload)
  -> DbContext.SaveChangesAsync()
  -> commit
  -> CatalogOutboxProcessor（背景）
  -> IEventsBus.Publish(ProductCreatedIntegrationEvent)
```

Aggregate 寫入與 outbox insert 在同一個資料庫 transaction。bus publish 發生在 commit 之後，避免 dual-write。

## 6. 建議抄什麼

抄四 assembly 模組切法。它夠小、好懂，也足以被測試保護邊界。

抄獨立 `IntegrationEvents` assembly。其他模組只引用公開 contract，不碰模組內部。

抄 Application-layer outbox enqueue handler。Domain event 是模組內事實；integration event 是持久化公開訊息。

抄 API endpoint 風格。Endpoint 只做 transport input -> command/query -> HTTP result，不直接載入 EF context、repository、aggregate。

抄 architecture tests。現在測試會保證 module Domain 不依賴 MediatR、EF Core、FluentValidation；Application 不依賴 Infrastructure；IntegrationEvents 不洩漏模組內部。

## 7. 不建議盲目抄什麼

不要抄參考的每模組 IoC `IContainer`。`ModulithReliabilityKit` 的模組註冊走 MS.DI-first 的 `AddCatalogModule`。

不要在 command handler 直接 publish integration event。先從 domain notification handler 寫入 outbox，再由 processor 在 commit 後 publish。

Query 不要預設走 repository。`GetProductQueryHandler` 使用 `IProductReadStore`，未來可換 Dapper/projection，不影響 use case。

不要重新引入裸 `DbContext` forwarding。每個模組都應透過 Building Blocks 的可重用 typed adapter 註冊：

```csharp
services.AddModulePersistence<CatalogContext>(CatalogApplicationAssembly.Assembly);
```

這會註冊 `IModuleUnitOfWork<CatalogContext>`、typed domain-event accessor/dispatcher，並建立 request assembly mapping 給 `IUnitOfWorkResolver` 使用。如此可保留單一 root container，同時讓多個 module context 共存，不落入 last-wins 註冊問題。

## 8. 未來模組規則

1. 先定義 `IntegrationEvents` 作為公開 API，並保持穩定。
2. Domain events 保持模組內部，於 Application 轉成 integration events。
3. Repository 預設只服務 write-side，除非 aggregate 行為必須載入。
4. Query DTO/read-store port 放 Application，implementation 放 Infrastructure。
5. 每個模組只透過一個 `Add<ModuleName>Module` 擴展方法註冊。
6. 模組啟動維持 MS.DI-first，不碰 container-specific API。
7. 新增第二個模組前，先為每個模組加 architecture tests。
8. Durable cross-module event 走 outbox；direct publish 只給明確 best-effort signal。

## 9. 待釐清問題

- Outbox polling 要維持 hosted 背景服務，還是隨系統成長改用 scheduler？
- Read store 是否立即使用 Dapper，或先維持 EF projection 直到有性能需求？

## 10. 下一篇

- `docs-tw/10-skeleton/implementation-decisions.md`
- `docs-tw/07-module-architecture/modular-monolith-ddd-comparison.md`
- `docs-tw/05-events-and-messaging/integration-events.md`
- `docs-tw/02-application-pipeline/unit-of-work.md`
