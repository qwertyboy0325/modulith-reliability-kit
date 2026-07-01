# Modular Monolith with DDD 比較

## 1. 目的

比較目前 `ModulithReliabilityKit` 骨架 / Catalog 範例與一個知名公開參考 —— **Kamil Grzybek 的「Modular Monolith with DDD」（[github.com/kgrzybek/modular-monolith-with-ddd](https://github.com/kgrzybek/modular-monolith-with-ddd)，公開參考）**。目標不是整包照抄，而是判斷哪些設計應該吸收、哪些只能當參考、以及如何改善與原因。

## 2. 檢視了什麼

來自該公開參考：其架構決策紀錄（關於 module facade、CQRS、two-layered reads、write path 用 clean architecture、模組間事件驅動溝通、每模組 IoC container、architecture tests 的 ADR）與其範例模組（facade contract、模組註冊、processing/data-access 接線、outbox processor、internal commands scheduler、一個 domain aggregate、一個 query handler，以及其 architecture tests）。以上皆為公開可得，並歸屬於 Kamil Grzybek。

目前 ModulithReliabilityKit：

- `src/Modules/Catalog/`
- `src/Api/ModulithReliabilityKit.Api/Program.cs`
- API 的模組端點（Catalog）
- Building Blocks 的 DI 擴充
- `ModulithReliabilityKit.ArchitectureTests`

## 3. 現況摘要

該公開參考是很好的架構參考，因為它的 ADR 明確，且模組邊界有測試保護。

它的主要模式：

- API 依賴 module facade，不是直接依賴 MediatR。
- Commands / Queries 是 facade input。
- Write path 使用 Clean Architecture：API -> Application -> Domain，Infrastructure 藏在抽象後。
- Read path 採較簡單的 two-layer style：Application query handler 直接用 Dapper 讀資料。
- 模組間透過 integration events 非同步溝通。
- 每個模組擁有自己的 IoC container 與 composition root。
- Architecture tests 保護 module / layer / domain 規則。

目前 `ModulithReliabilityKit`：

- API endpoint 依賴 `ICatalogModule`；MediatR 藏在 `CatalogModuleFacade` 後。
- Commands / Queries 仍是 module facade input。
- Write path 已分 Application / Domain / Infrastructure。
- Read path 使用 Application port（`IProductReadStore`），Infrastructure 實作。
- Integration events 是獨立公開 assembly。
- DI 採 MS.DI-first，單一 host container，不使用每模組 container。
- Module persistence 使用 typed `IModuleUnitOfWork<TContext>` 與 typed domain-event accessor/dispatcher 註冊。
- Architecture tests 已覆蓋基本 layer rules 與公開 `IntegrationEvents` 邊界。

## 4. 應採用的部分

### 4.1 Module facade

**Reference 強項：** 該公開參考只透過一個小 facade 暴露模組：

```csharp
Task<TResult> ExecuteCommandAsync<TResult>(ICommand<TResult> command);
Task ExecuteCommandAsync(ICommand command);
Task<TResult> ExecuteQueryAsync<TResult>(IQuery<TResult> query);
```

這比讓 controller / endpoint 直接依賴 MediatR 更像模組邊界。Module contract 明確，MediatR 只是內部實作。

**ModulithReliabilityKit 落地方式：** 新增 `ICatalogModule`，由 `Catalog.Infrastructure` 實作，並維持 `AddCatalogModule` 的 MS.DI-first 註冊方式。

**原因：** API 只依賴 `ICatalogModule` 與公開 command/query DTO；MediatR 被藏在 facade 後，模組封裝更完整，且不需要回到每模組 container。

### 4.2 明確 CQRS policy

**Reference 強項：** 該參考的 ADR 清楚區分 write complexity 與 read simplicity：

- writes 才需要 Domain + Application + Infrastructure
- reads 可以較簡單、可最佳化

**ModulithReliabilityKit 改善方式：** 將其寫成預設模組規則：

- command handler 載入 aggregate 並執行行為
- query handler 回傳 DTO，可使用 read-store port、EF projection 或 Dapper
- read model 不需要追求 aggregate purity

**原因：** 避免把簡單讀取過度 domain model 化，把 rich domain 留給真正有不變式的 write path。

### 4.3 更強的 architecture tests

**Reference 強項：** 它的 module arch tests 不只檢查 Domain 不依賴 EF，還檢查：

- domain event / value object 不可變
- 非 aggregate root entity 不暴露 public member
- entity 不直接持有其他 aggregate root reference
- 命名規則（`DomainEvent`、`Rule`）
- 模組不依賴其他模組，除了明確允許的 integration handler / startup point

**ModulithReliabilityKit 改善方式：** 擴充 `ModulithReliabilityKit.ArchitectureTests`：

- immutable `ValueObject` / `IDomainEvent`
- aggregate-root reference checks
- module-to-module dependency rules
- `*DomainEvent`、`*Rule`、`*IntegrationEvent` 命名規則
- 第二個模組出現後，每個模組一組測試

**原因：** 讓架構規則由測試保護，不只靠 code review。

### 4.4 Internal command / async operation pattern

**Reference 強項：** 該參考能持久化非同步 command，供背景重試執行（internal-commands scheduler）。

**ModulithReliabilityKit 改善方式：** 等到真的有 delayed/retryable module operation 時再加入 internal-command abstraction。

**原因：** email、通知、reconciliation、長時間 side effect 會需要它；但第一個 lightweight module 不該先背 tables、scheduler、failure semantics。

## 5. 不應直接採用的部分

### 5.1 每模組 IoC container

**Reference 選擇：** 該參考接受每模組一個 IoC container 以換取 autonomy。

**不要直接抄。** 它提供強 runtime isolation，但帶來 static composition root、重複 container module、生命週期複雜度與非標準 host。

**ModulithReliabilityKit 保持方向：** 單一 host container、MS.DI-first registration。

**目前替代方案：** 用 module facade + typed module persistence adapter 取得大部分邊界好處，不引入多 container。

### 5.2 Application 直接 Dapper 作為唯一 read pattern

**Reference 選擇：** read handler 直接依賴 SQL connection factory 與 Dapper。

**不要當通用規則抄。** 這很快、簡單，但 Application 會耦合 SQL shape 與 Dapper。

**ModulithReliabilityKit 保持方向：** Application 擁有 read intent / DTO，Infrastructure 擁有 read-store implementation。Dapper 可在 `IProductReadStore` 後面使用。

**原因：** 簡單讀取可用 EF projection，熱路徑可換 Dapper，不改 use case。

### 5.3 反射 + container-heavy 的 outbox notification mapping

**Reference 選擇：** 該 dispatcher 透過 container resolve notification 型別，再用雙向字典對應 notification type 與字串名稱。

**不要直接抄。** 功能強，但隱性、container-specific，且除錯成本高。

**ModulithReliabilityKit 保持方向：** 透過 `IDomainNotificationsMapper.Register<TDomainEvent>(...)` 顯式註冊。

**改善方式：** 加 startup validation，確保應進 outbox 的 module domain event 都有明確 mapping。

**原因：** 顯式 mapping 比較好審計，也同時支援 MS.DI 與 Autofac。

### 5.4 UoW 無顯式 transaction

**Reference 選擇：** 該參考的 unit-of-work 先 dispatch domain events，再 `SaveChangesAsync`。

**不要直接抄。** EF `SaveChanges` 會有隱式 transaction，但邊界不明顯；未來 outbox/inbox、多 context、retry policy 出現時會更難審計。

**ModulithReliabilityKit 保持方向：** unit-of-work 使用顯式 transaction。

**原因：** transaction boundary 可讀、可 review、可測。

## 6. 建議改善 backlog

### Priority 1: 加 module facade，但不改 DI topology

狀態：已完成。

這是該公開參考最值得吸收的點，同時保留 MS.DI-first。

### Priority 2: 第二個模組前移除裸 `DbContext` forwarding

目前 `CatalogModule` 將 `DbContext` forwarding 到 `CatalogContext`。單模組可接受；多模組會 last-wins。

狀態：已用 Building Blocks 的可重用服務完成：

- `IModuleUnitOfWork<TContext>`
- `ModuleUnitOfWork<TContext>`
- per-context typed domain-event accessor / dispatcher
- `AddModulePersistence<TContext>(requestAssembly)`
- `IUnitOfWorkResolver` request-assembly mapping

這是取代每模組 container 的正確方向。

### Priority 3: 強化 architecture tests

搬運該公開參考中有價值的 domain/module tests，但調整成 `ModulithReliabilityKit` 慣例：

- immutable `ValueObject` / `IDomainEvent`
- entity 不直接 reference aggregate root
- domain object constructor rules（若與 EF 需求相容）
- module dependency rules，且 `IntegrationEvents` 是唯一公開跨模組 assembly

### Priority 4: 加 domain notification mapping validation

維持顯式 mapper registration，但 startup 時 fail fast：應進 outbox 的 domain notification 未 mapping 就直接失敗。

這保留該參考 startup mapping 檢查的安全性，但不複製 container-specific reflection。

### Priority 5: Internal commands 等需要時再加

現在不要加 internal commands。等真的有 delayed/retryable side effect 再加入。

## 7. 更新結論

目前 `Catalog` sample 方向正確，但還少一個重要邊界：API 還看得到 MediatR。下一步最值得改善的是 module facade。

保留：

- MS.DI-first、單一 container design
- 獨立 `IntegrationEvents` assembly
- 顯式 transaction UoW
- Application read-store port
- 顯式 domain notification mapping

採用：

- module facade
- 更強 architecture tests
- 明確 CQRS 文件規則
- 未來 internal command pattern

拒絕：

- 每模組 static container
- Application 預設直接 Dapper
- container-specific outbox reflection

## 8. 驗證清單

- API 只引用 module contracts，不引用 module Infrastructure 或 raw MediatR。
- 其他模組只引用 `*.IntegrationEvents`。
- Domain 維持無 MediatR / EF / FluentValidation。
- Query path 簡單，但與 aggregate behavior 有意識分離。
- 多模組時 persistence 不依賴共享裸 `DbContext`。
- Outbox mapping 明確且 startup 驗證。
- Architecture tests 保護規則，而不只是文件描述。
