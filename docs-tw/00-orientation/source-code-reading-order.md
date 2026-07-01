# 原始碼閱讀順序

## 目的

給想**理解架構**（而非業務領域）的新進工程師一條**自下而上**路徑。依序閱讀；每步含**為何讀**、**關鍵檔／區塊**、**閱讀時要回答的問題**。

不要從端點或業務功能開始。從共用核心與組合根開始。

## 範圍

本篇索引後續文件中的區塊。全文以 **Catalog**（聚合 `Product`）為標準生產者模組，以 **Notifications** 為標準消費者。

---

## 步驟 1 — BuildingBlocks（共用核心）

**為何：** 所有模組都建立在這些抽象上。不懂這層，後面都看不懂。

**關鍵區塊：**
- `BuildingBlocks.Domain/` — `Entity` / `ValueObject` / `IAggregateRoot` 基底型別、領域事件與業務規則抽象、強型別 ID 基底。
- `BuildingBlocks.Application/` — 事件契約（`IEventsBus`、`IntegrationEvent`）、領域事件通知、outbox 契約（`IOutbox`/`OutboxMessage`）、inbox 契約含 `Inbox/InboxRetryPolicy.cs`。
- `BuildingBlocks.Infrastructure/` — unit-of-work behavior 與領域事件派發。

**要回答的問題：**
- 聚合如何記錄領域事件？（透過 `Entity` 基底）
- 領域事件如何在進程內被中介，與 notification 抽象的關係？
- **領域事件**、**領域事件通知（notification）**、**整合事件** 三者差異？
- **一致性邊界**在哪宣告？（提示：見步驟 5。）

---

## 步驟 2 — 組合根

**為何：** 早懂模組如何接線可少踩坑。此套件使用**單一 MS.DI 容器**；每個模組透過 `Add<Module>Module(...)` 擴充方法貢獻註冊。

**關鍵檔：**
- `src/Api/ModulithReliabilityKit.Api/Program.cs` — Host 與 DI 組合。
- `src/Api/ModulithReliabilityKit.Api/Modules/CatalogEndpoints.cs` — HTTP → command/query 對應。

**要回答的問題：**
- 誰、何時註冊各模組？（`Program.cs` 呼叫 `Add<Module>Module`）
- Host 與模組共用什麼？（event bus、連線設定）
- Command 如何執行？（端點 → 模組門面 → 模組內 MediatR `Send`）

---

## 步驟 3 — 模組註冊與 Command 管線

**為何：** 註冊定義生命週期、管線行為與 typed 持久化；驗證／UoW／日誌的包裝都在這設定。

**關鍵區塊：**
- 各模組的 `Add<Module>Module` 擴充（位於 `<Module>.Infrastructure`）。
- `BuildingBlocks.Infrastructure` 的管線行為，特別是 `Pipeline/UnitOfWorkBehavior.cs`。

**要回答的問題：**
- `DbContext` 生命週期為何、如何每請求 scope？
- 管線行為的**順序**？（驗證 → UnitOfWork → 日誌 — 以註冊順序為準。）
- MediatR Handler 如何發現？

詳見 `01-foundation/dependency-injection.md`。

---

## 步驟 4 — Command 管線

**為何：** 所有狀態變更都經管線行為包住的 Command Handler；這是交易的敘事中心。

**關鍵檔：**
- `BuildingBlocks.Infrastructure/Pipeline/UnitOfWorkBehavior.cs`
- 真實 Handler 範例：`Catalog.Application/Products/.../CreateProductCommandHandler`

**要回答的問題：**
- Handler 會呼叫 `SaveChanges` 嗎？（不 — unit-of-work behavior 負責。）
- 驗證失敗會怎樣？（Handler 執行前拋出 invalid-command 例外）
- 領域事件派發相對於 `SaveChanges` 的時機？

---

## 步驟 5 — Unit of Work

**為何：** 這定義一致性邊界；實作很小，值得仔細讀。

**關鍵檔：**
- `BuildingBlocks.Infrastructure/Pipeline/UnitOfWorkBehavior.cs`
- `BuildingBlocks.Infrastructure` 內的領域事件 dispatcher。

**要回答的問題：**
- Commit 會開顯式 DB 交易嗎？（會 — 此套件把交易做成顯式。）
- 聚合變更與 Outbox 列是否**原子**寫入？（是 — 同一交易。）

詳見 `02-application-pipeline/unit-of-work.md`。

---

## 步驟 6 — 領域事件 → Outbox

**為何：** 從進程內領域事件到可持久跨模組訊息的橋樑。

**關鍵檔：**
- `BuildingBlocks.Infrastructure` 內的領域事件 dispatcher。
- `Catalog.Application/Products/Events/ProductCreatedNotificationHandler.cs`（把整合事件放入 outbox）。
- `Catalog.IntegrationEvents/ProductCreatedIntegrationEvent.cs`（公開契約）。

**要回答的問題：**
- 領域事件如何變成 Outbox 列？（dispatcher 序列化的是 *notification*）
- `type` 字串如何對應型別？（顯式 notification 對應）
- Outbox 是否與聚合變更同一次 `SaveChanges`？

---

## 步驟 7 — Outbox / Inbox 處理

**為何：** 可靠投遞、重試、死信在這裡。

**關鍵檔：**
- `BuildingBlocks.Infrastructure/Processing/OutboxProcessorBase.cs`（共用處理器基底）
- `Catalog.Infrastructure/Processing/CatalogOutboxProcessor.cs`（生產者端 outbox drain）
- `Notifications.Infrastructure/Inbox/InboxWriter.cs`（冪等 inbox ingest）
- `Notifications.Infrastructure/Processing/NotificationsInboxProcessor.cs`（inbox drain，含重試／死信）
- `BuildingBlocks.Application/Inbox/InboxRetryPolicy.cs`（重試／退避排程）

**要回答的問題：**
- Outbox 如何 drain？（EF 批次取 `processed_on_utc IS NULL`、發佈、逐列在發佈後標記 → 至少一次）
- 消費者如何讓 ingest 冪等？（inbox writer 以 `(logical_id, occurred_on_utc)` 唯一索引 + 吞掉 `23505`）
- 重試／死信策略？（見 `InboxRetryPolicy`）

詳見 `05-events-and-messaging/integration-events.md`、`reliability-matrix.md`。

---

## 步驟 8 — 整合事件（Event Bus）

**為何：** 跨模組傳輸的實際載體；關鍵是**僅記憶體內**。

**關鍵區塊：**
- `BuildingBlocks.Application` 的 `IEventsBus` 契約。
- `BuildingBlocks.Infrastructure` 的記憶體 event bus。

**要回答的問題：**
- 重啟後 Bus 是否持久？（否 — 記憶體內。）
- 無訂閱者時 Publish 會怎樣？
- Publish 例外會往上拋還是吞掉？（此套件預設**不**吞掉；持久性仍來自 outbox/inbox，而非 bus。）

---

## 步驟 9 — 單一流程端到端（Catalog → Notifications）

**為何：** 跨生產者與消費者串起全貌。

**關鍵檔：**
- `Catalog.Domain/Products/Product.cs` 與其 repository 介面
- `Catalog.Application/Products/CreateProduct/`（command、handler、validator）
- `Catalog.Application/Products/Events/ProductCreatedNotificationHandler.cs`
- `Catalog.IntegrationEvents/ProductCreatedIntegrationEvent.cs`
- `Notifications.Infrastructure/Inbox/InboxWriter.cs` 與 `Processing/NotificationsInboxProcessor.cs`

**要回答的問題：**
- 追蹤一條 Command：HTTP → `ICatalogModule.ExecuteCommandAsync` → 管線 → Handler → Repository → UoW → 領域事件 → Outbox → Outbox 處理器 → `IEventsBus.Publish` → Notifications inbox → inbox 處理器 → 讀模型。
- 鏈上哪一段可能丟訊息？（對照可靠性矩陣。）

## 下一篇

- `01-foundation/building-blocks.md`
