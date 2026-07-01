# 整合事件（Integration Events）

## 目的

說明跨模組訊息如何**觸發、傳輸、消費**。有**兩條發佈路徑**與**僅記憶體內**的傳輸；可靠性差異很大。本篇以此套件的具體範例區分它們。

## 已檢視的檔案／區塊

- `BuildingBlocks.Application` 的 `IEventsBus` / `IntegrationEvent` 契約
- `BuildingBlocks.Infrastructure` 的記憶體 event bus
- `BuildingBlocks.Infrastructure` 的領域事件 dispatcher
- `Catalog.Application/Products/Events/ProductCreatedNotificationHandler.cs`（路徑 A 發佈者）
- `Catalog.Infrastructure/Processing/CatalogOutboxProcessor.cs`（outbox 處理器）
- `Catalog.IntegrationEvents/ProductCreatedIntegrationEvent.cs`（公開契約）
- `Notifications.Infrastructure/Inbox/InboxWriter.cs`（冪等 inbox ingest）
- `Notifications.Infrastructure/Processing/NotificationsInboxProcessor.cs`（inbox 處理器）
- `BuildingBlocks.Application/Inbox/InboxRetryPolicy.cs`（重試／退避）

## 傳輸層：僅記憶體內

`IEventsBus` 綁定到啟動時建立、各模組共用的**單一進程內記憶體 bus**。兩點決定可靠性：

1. **無訂閱者 → 發佈無效果**（沒有東西消費它）。
2. **Publish 例外** —— 此套件的記憶體 bus 預設**不**靜默吞掉 Publish 例外；失敗會浮現而非被藏起。

> ⚠️ **`IEventsBus` 背後沒有持久 Broker。** 跨模組整合事件僅在進程內；持久性**只來自 Outbox/Inbox 表**，不是 bus。若模組未來可能跑成獨立進程，就在 `IEventsBus` 抽象後放一個 broker。

---

## 路徑 A — 領域事件 → Outbox → Outbox 處理器 → Notification Handler → `IEventsBus.Publish`

```text
聚合產生 DomainEvent
  → (UnitOfWorkBehavior 提交) DomainEventsDispatcher
        → 對應 DomainEvent → notification
        → 序列化 notification → OutboxMessage（與聚合同一交易原子寫入）
  → CatalogOutboxProcessor（背景）
        → 取一批未處理列（WHERE processed_on_utc IS NULL、ORDER BY id、LIMIT 50）
        → 進程內發佈 notification
              → notification handler（ProductCreatedNotificationHandler）
                    → IEventsBus.Publish(IntegrationEvent)
        → 標記 outbox 列 processed（processed_on_utc = now）   ← 發佈後才標記
```

### 步驟 1 — notification → outbox（於 dispatcher）

提交過程中，dispatcher 把每個領域事件 notification 序列化為 `OutboxMessage` 並加到同一 `DbContext`，因此 outbox 列與聚合變更**原子**寫入（見 `02-application-pipeline/unit-of-work.md`）。

### 步驟 2 — outbox 處理器發佈

`Catalog.Infrastructure/Processing/CatalogOutboxProcessor.cs`（擴充共用的 `OutboxProcessorBase`）以 EF Core 取一批未處理列（`WHERE processed_on_utc IS NULL ORDER BY id LIMIT 50`），逐一在記憶體 bus 發佈，然後標記列為 processed。由於在**發佈之後**才標記，投遞為至少一次。（目前僅單一背景 drainer；若要多 drainer 併發,fetch 需加 `FOR UPDATE SKIP LOCKED` 以維持併發安全。）

### 步驟 3 — notification handler 呼叫 bus

`ProductCreatedNotificationHandler` 把模組內部 notification 對應到公開的 `ProductCreatedIntegrationEvent` 並呼叫 `IEventsBus.Publish(...)`。

**何時用路徑 A：** 由領域驅動、**交易提交後不可丟**的整合事件。

**可靠性（路徑 A）：**
- ✅ 整合意圖的**寫入**持久：outbox 列與聚合原子提交（transactional outbox）。投遞前崩潰不會丟事件 —— 處理器會重試。
- ⚠️ **投遞**步驟（`IEventsBus.Publish`）在記憶體內。端到端持久投遞仍取決於消費者持久化到 inbox。
- ⚠️ outbox drain 是**至少一次**，消費者須容忍重複（在 inbox 去重）。

**能否直接抄？** *Transactional outbox 寫入* — 可。*Outbox → 記憶體 bus 投遞* 只與消費者的 inbox 一樣可靠。

---

## 路徑 B — 直接 `IEventsBus.Publish`（無 Outbox）

Command handler、應用服務或背景工作直接發佈，**無 outbox 列**。

**何時用路徑 B：**
- 高頻／大量訊號，outbox 會成瓶頸（須是有意識的決定）。
- 背景／best-effort 通知。

**可靠性（路徑 B）：**
- ⚠️ **不持久。** 程序在 DB 寫入與 `Publish` 之間崩潰、或 `Publish` 失敗，事件即**遺失**。若在提交 DB 寫入*之後*才發佈，DB 狀態可能已提交而事件從未到達。

**能否直接抄？** 僅限**明確標為可丟棄（best-effort）**的事件。絕不用於他模組正確性所依賴的事件。

---

## 消費：Inbox vs. 直連 MediatR

消費者可用兩種方式處理收到的整合事件：

- **Inbox writer**（持久路徑，Notifications 採用）：收到時 ingest 進 `notifications.inbox_messages`,以 `(logical_id, occurred_on_utc)` 上的唯一索引達成冪等。writer 先檢查是否已存在;若併發投遞先插入了重複列,則吞掉唯一鍵違反（`23505`）—— 因此重送不會產生第二列。再由獨立的 inbox 處理器以重試 + 死信 drain。見 `Notifications.Infrastructure/Inbox/InboxWriter.cs` 與 `Notifications.Infrastructure/Processing/NotificationsInboxProcessor.cs`。
- **直連 MediatR**（風險路徑）：收到即直接發佈到模組的 mediator，無 inbox、無重試、無死信。**勿用於重要事件** —— 被吞的 handler 例外會靜默丟失事件。

Inbox 處理器是成熟的部分：

```text
SELECT 到期列（status IN ('pending','retrying') 且 next_retry_on_utc 為 NULL/≤ now），
       ORDER BY occurred_on_utc、LIMIT 50
  → 逐列，各自獨立交易：
        → dispatch 到模組 handler
        → 成功：標記 processed + commit（業務效果與標記同一交易提交）
        → 失敗：rollback，再依 InboxRetryPolicy 記錄重試；N 次後 → 死信
```

重試／退避定義於 `BuildingBlocks.Application/Inbox/InboxRetryPolicy.cs`；用盡重試的訊息移到帶解析流程的死信記錄。

---

## 順序與時序（sequencing）

投遞是非同步且至少一次，因此本 kit 的設計刻意做到**不假設**全域順序或精確一次的*時序*也能正確：

- **生產端順序。** outbox 以 `ORDER BY id`（插入順序）drain，故單一模組的事件依提交順序發佈。
- **消費端順序。** inbox 以 `ORDER BY occurred_on_utc` drain，故一批內的到期訊息依事件時間順序處理。
- **端到端無順序保證。** 失敗的訊息會轉為 `retrying` 並帶退避延遲（`InboxRetryPolicy`），其效果可能**晚於**較晚到達的訊息。有重試與重送時，到達順序 ≠ 提交順序。
- **設計後果。** Handler 必須**冪等**，且盡量**與順序無關（可交換）**。inbox 的冪等 ingest 無論何時、重送幾次都能吸收重複；每則訊息的效果與「processed」標記於同一交易提交，故訊息不會半套用或與其他訊息交錯。
- **時序是最終一致而非即時。** 效果只在背景 drain tick（加上任何重試退避）後才出現；消費者與讀模型為**最終一致**。HTTP e2e 測試即以「先顯式 drain 再斷言」反映此點。

## 端到端可靠性摘要

| 區段 | 機制 | 保證 |
| --- | --------- | --------- |
| 聚合變更 → outbox 列（A） | 一筆顯式交易 | **原子**（transactional outbox） |
| Outbox 列 → notification handler | 批次 EF drain、發佈後才標記 | 至少一次（可能重複） |
| Notification handler → `IEventsBus.Publish` | 記憶體 bus | 進程內；例外會浮現（不吞） |
| `IEventsBus` → 訂閱方（inbox writer） | 唯一索引 `(logical_id, occurred_on_utc)` + 吞掉 `23505` | 冪等 ingest → 持久 |
| `IEventsBus` → 訂閱方（直連） | mediator 發佈 | **best-effort，可丟棄** |
| Inbox 列 → handler | drain + 重試 + 死信 | 至少一次 + 死信 |
| 路徑 B 直接發佈 | 僅 `IEventsBus.Publish` | **best-effort、可丟棄、無紀錄** |

## 建議抄什麼

- **Transactional outbox** 寫入（路徑 A 步驟 1）與 **批次、發佈後才標記的處理器**（`OutboxProcessorBase`）。
- **冪等 ingest + 重試／死信的 Inbox**（此處最成熟的訊息機制）。
- **領域事件 → notification → 整合事件** 分離。

## 不建議盲目抄什麼

- 若模組未來可能跑成獨立進程，勿把**記憶體 bus 當跨模組傳輸**。
- **路徑 B（直接發佈）** 用於非明確可丟棄的事件。
- **不一致的消費模型**（inbox vs 直連）。擇一並一致套用，或逐一明確分類每個事件（見可靠性矩陣）。

## 待釐清問題

- 記憶體 bus 是刻意的「永久單進程」決策，還是 broker 前的過渡？
- 是否要求每個持久事件消費者都用 inbox（以測試強制）？

## 下一篇

- `05-events-and-messaging/reliability-matrix.md`
- `09-lessons-learned/architecture-rules-for-my-own-project.md`
