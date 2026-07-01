# BuildingBlocks（共用核心）

## 目的

說明 `src/BuildingBlocks/` 中各模組共用的基礎設施抽象：是什麼、在哪實作、模組如何依賴、是否值得抄到新專案。

## 已檢視的區塊

```text
src/BuildingBlocks/
  ModulithReliabilityKit.BuildingBlocks.Domain/
    Entity、ValueObject、IAggregateRoot
    領域事件 + 業務規則抽象
    StronglyTypedId（強型別 ID 基底）
  ModulithReliabilityKit.BuildingBlocks.Application/
    Events/（IEventsBus、IntegrationEvent、領域事件通知）
    Outbox/（IOutbox、OutboxMessage）
    Inbox/（inbox 契約、InboxRetryPolicy）
    執行環境、例外、查詢／分頁輔助
  ModulithReliabilityKit.BuildingBlocks.Infrastructure/
    Pipeline/UnitOfWorkBehavior、領域事件派發
    記憶體 event bus
    Processing/OutboxProcessorBase（+ inbox 處理）
    日誌、DI 擴充、強型別 ID 轉換
```

## 版面：三專案對應模組分層

`BuildingBlocks` 拆成 `Domain`、`Application`、`Infrastructure`，與各模組相同，依賴方向清楚：模組 `Domain` → `BuildingBlocks.Domain`，模組 `Application` → `BuildingBlocks.Application`，依此類推。

---

## A. Domain 抽象 — `BuildingBlocks.Domain`

### `Entity`

聚合／實體基底。私有領域事件清單並提供 add/clear 操作，以及 `CheckRule(IBusinessRule)` 強制不變量（破規則拋業務規則例外）。

- **依賴它：** 每模組每個聚合（例如 Catalog 的 `Product`）。
- **VERDICT：可抄。** 標準、正確的 DDD 實體基底。

### `ValueObject`

提供結構相等的值物件基底。

- **VERDICT：抄但建議改。** 優先 C# `record` 值物件以取得顯式、快速的相等；只在真需要時保留反射基底。反射相等較慢也容易微妙出錯。

### `IAggregateRoot`

空標記介面。**VERDICT：可抄**（泛型約束、架構測試有用）。

### 領域事件

帶 id + `OccurredOn` 的領域事件抽象。

- **耦合註記：** 常見捷徑是讓領域事件介面繼承中介函式庫（如 `INotification`），這會把基礎設施關注點滲進 Domain。本套件讓 Domain 不含 MediatR，於 Application／派發層適配。
- **VERDICT：抄 id/`OccurredOn` 形狀；讓 Domain 不含中介**，於派發層適配。

### 業務規則

顯式業務規則物件（`Message`、`IsBroken()`）；破規則拋業務規則驗證例外。

- **VERDICT：可抄。** 讓不變量成為一等公民且可測。

### 強型別 ID（`StronglyTypedId`）

全 repo 單一強型別 ID 風格。

- **教訓（來自既有成果）：** 避免同時存在**兩種**型別 ID 風格（例如舊基底類別加上較新的 record ID）。半途而廢的遷移純屬債。
- **VERDICT：從第一天就選一種。** 本套件用單一種。

---

## B. Application 抽象 — `BuildingBlocks.Application`

### 事件：`IEventsBus`、`IntegrationEvent`

跨模組訊息契約。`IEventsBus` 提供 publish/subscribe；`IntegrationEvent` 是公開跨模組事件的抽象基底（id、`OccurredOn`、version）。

> ⚠️ **教訓（來自既有成果）：** 不要把同一組事件契約放在**兩個**命名空間（例如 Application 一份、Infrastructure 一份「向後相容」別名）。那會逼每個讀碼者去看模組用哪個 import，是債不是設計。本套件在 Application 保留**單一標準定義**。

- **VERDICT：Application 層單一定義。**

### 領域事件通知

連接領域事件與 Outbox 的橋接型別。*notification* 包住領域事件，序列化進 Outbox 的就是它。

- **VERDICT：可抄。** 三層模型（領域事件 → notification → 整合事件）是可靠事件設計核心。見 `05-events-and-messaging`。

### Outbox：`IOutbox` / `OutboxMessage`

`IOutbox` 提供 `Add`；`OutboxMessage` 帶 logical id（UUID）、序列 PK、`OccurredOn`、`type` 字串、序列化 payload、processed 標記。

- **VERDICT：抄訊息形狀**（logical id + type/payload + processed 標記）。讓介面誠實 —— 不要放什麼都不做的介面方法。

### Inbox 契約 + `Inbox/InboxRetryPolicy.cs`

消費者端契約，含 `Inbox/InboxRetryPolicy.cs` 的重試／退避排程與 inbox 訊息形狀（retry count、last error、next-retry 時間、status ∈ {pending | retrying | processed | dead_letter}），以及帶解析流程的 dead-letter 記錄。

- **VERDICT：可抄。** 重試 + dead-letter 模型是可靠消費的成熟核心。

### 執行環境

環境請求上下文（`UserId`、`CorrelationId`、可用性旗標）。

- **依賴它：** 日誌／關聯與任何請求 scope 政策。
- **VERDICT：抄概念。** 一等公民的執行環境存取器對關聯與請求 scope 行為有用。把任何環境專屬欄位排除在共用核心之外。

### 例外與輔助

標準化 not-found 與 invalid-command 例外（後者由驗證行為拋出），以及分頁契約（`IPagedQuery`、`PageData`、分頁輔助）。

- **VERDICT：可抄。**

---

## C. Infrastructure 抽象 — `BuildingBlocks.Infrastructure`

### Unit of Work — `Pipeline/UnitOfWorkBehavior.cs`

見 `02-application-pipeline/unit-of-work.md`。摘要：該行為先派發領域事件再儲存，於**顯式交易**中。**VERDICT：可抄。**

### 領域事件派發

- 一個領域事件存取器從 EF `ChangeTracker` 取領域事件。
- 一個 dispatcher 在進程內發佈領域事件**並**把對應 notification 序列化進 Outbox。
- 一個顯式 notification mapper 對應 `type` 字串 ↔ notification 型別以供（反）序列化。

- **VERDICT：可抄。** 這是設計核心。優先**顯式** notification 對應（逐一註冊）勝過反射／容器掃描對應 —— 較易稽核且與容器無關。

### Event Bus（記憶體內）

綁定到 `IEventsBus` 的記憶體 event-bus 實作。**這是唯一傳輸。** 無持久 Broker 實作 `IEventsBus`。

- **VERDICT：作為預設／開發傳輸可以；勿當作可靠的跨模組投遞。** 持久性來自 **Outbox + Inbox 表**，不是 Bus。本套件的記憶體 Bus 預設**不**靜默吞掉 Publish 例外。

### Processing — `Processing/OutboxProcessorBase.cs`

可重用的 outbox drain 基底（取一批未處理列、逐一發佈、逐列在發佈後標記 processed），供各模組具體處理器擴充。Inbox 處理採相同批次形狀並加上重試 + dead-letter。inbox 處理器已對每列以 `FOR UPDATE SKIP LOCKED` claim,故多個 inbox drainer 併發是安全的(恰好套用一次)。outbox 基底則刻意維持至少一次;在其 fetch 加 `SKIP LOCKED` 只是減少重複的優化,而非正確性修正,因重複會被冪等 inbox 吸收。

- **VERDICT：可抄。** 併發安全且跨模組可重用。

### 其他

- 日誌行為（單一機制，處處套用）。**可抄。**
- 強型別 ID 的 EF 值轉換。若採用型別 ID 則**可抄**。
- 註冊共用基礎設施與 typed 模組持久化的 DI 擴充。**可抄。**

---

## 模組對 BuildingBlocks 的依賴方向

```text
Module.Domain            → BuildingBlocks.Domain
Module.IntegrationEvents → BuildingBlocks.Application
Module.Application       → Module.Domain + Module.IntegrationEvents + BuildingBlocks.Application
Module.Infrastructure    → Module.Application + BuildingBlocks.Infrastructure（+ BuildingBlocks.Application）
```

乾淨且值得複製。架構測試會強制它（見 `07-module-architecture`）。

## 建議抄什麼（摘要）

- Domain 基底：`Entity`、`IAggregateRoot`、領域事件基底、`IBusinessRule` + 例外。
- **領域事件 → notification → 整合事件** 模型與 dispatcher（顯式對應）。
- 帶 processed 標記 + 重試／dead-letter 欄位的 Outbox/Inbox **訊息形狀**。
- 執行環境、分頁輔助、標準化例外。
- 強型別 ID 的 EF 轉換（若採用型別 ID）。

## 不建議盲目抄什麼

- **同一組事件契約跨兩個命名空間** —— 保留單一標準 Application 定義。
- **兩種並存的強型別 ID 風格** —— 擇一。
- 把**記憶體 Bus** 當作可靠的跨模組傳輸。
- **Domain 層綁中介函式庫** —— 讓 Domain 純淨，於派發層適配。
- 什麼都不做的介面方法（例如 no-op 的 outbox `Save()`）。

## 待釐清問題

- 哪些執行環境欄位屬於共用核心、哪些屬於模組關注點？
- 讀模型／消費者端該重用同一套 building blocks，還是更精簡的子集？

## 下一篇

- `01-foundation/dependency-injection.md`
- `02-application-pipeline/unit-of-work.md`
