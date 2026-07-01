# 整合事件可靠性矩陣

## 目的

依發佈者、觸發路徑、消費者、持久性等欄位分類每個整合事件。這是判斷什麼能安全上線的最重要產物。矩陣是一種**紀律**：每個整合事件在合併前都要有一列。證據不足標 **Unknown** 並說明缺什麼。

## 已檢視的區塊

- `Catalog.IntegrationEvents/ProductCreatedIntegrationEvent.cs`
- `Catalog.Application/Products/Events/ProductCreatedNotificationHandler.cs`
- `Catalog.Infrastructure/Processing/CatalogOutboxProcessor.cs`
- `Notifications.Infrastructure/Inbox/InboxWriter.cs`、`Processing/NotificationsInboxProcessor.cs`
- `BuildingBlocks.Application/Inbox/InboxRetryPolicy.cs`

## 欄位說明

- **觸發路徑：** **A** = 領域事件 → Outbox → Outbox 處理器 → Notification Handler → Bus（寫入側持久）。**B** = 直接 `IEventsBus.Publish`（無 Outbox）。
- **持久？** = 發佈意圖是否先寫 Outbox（**不是** Bus 是否持久 — Bus 永不持久）。
- **可丟？** = 崩潰或吞例外下是否可能端到端遺失。
- **重試？** = 消費端自動重試。
- **Inbox？** = 消費是否經 Inbox（冪等 + 重試／死信）或直連 MediatR。
- **風險** = 綜合評估。

> **每一列都適用：** `IEventsBus` **預設**為記憶體內。端到端持久路徑因此需要**發佈側 Outbox** *加上* **消費側 Inbox**。「持久」僅指發佈側 Outbox **寫入**；端到端持久仍需一個冪等、會重試的消費者。另有**可選的 NATS JetStream 傳輸**(`NatsEventBus`)接在同一個 `IEventsBus` 之後,提供跨進程持久 —— 見 `01-foundation/building-blocks.md` 與 `NatsCrossProcessReliabilityTests`。

## 矩陣

此套件目前上線一個完整建模的事件。其餘為**示意模式**（用泛型「Module A / Module B」佔位），展示真實矩陣必須捕捉的風險類別。

| 事件 | 發佈者 | 路徑 | 消費者 | Outbox 持久？ | 可丟？ | 重試？ | Inbox？ | 風險 |
| ----- | --------- | ------------ | --------- | ----------------- | --------- | ------ | ------ | ---- |
| `ProductCreatedIntegrationEvent` | Catalog（`ProductCreatedNotificationHandler`） | A | Notifications | 是 | 僅最後一跳可能 | 是（Notifications Inbox） | 是（Notifications） | **低** — 此套件的參考持久路徑 |
| *(示意)* Module A「entity registered」 | Module A | A | Module B（直連 MediatR） | 是 | 是（消費者吞錯） | 否 | 否（直連） | **高** — 持久發佈在最後一跳被丟棄 |
| *(示意)* Module A「setting changed」 | Module A（直接發佈） | **B** | Module B | **否** | **是** | 否 | 否（直連） | **高** — 直接發佈 + 直接消費；崩潰即丟 |
| *(示意)* 高頻訊號 | Module A（背景） | **B** | Module B | **否** | **是（設計接受）** | 否 | 否（快速路徑） | **可接受** — 刻意 best-effort，須明文記錄 |
| *(示意)* 發佈但無訂閱者 | Module A | A | 未見 | 是 | 是（無訂閱即靜默丟） | 否 | 否 | **中** — 持久但可能無人消費／死重量 |

### 訂閱方對照（此套件）

| 消費模組 | 訂閱 | 消費方式 |
| --------------- | ------------- | -------------- |
| Notifications | `ProductCreatedIntegrationEvent` | Inbox（持久、冪等、重試 + 死信） |
| Catalog | 無（僅發佈） | — |

## 核心結論（教訓）

1. **持久發佈 ≠ 持久投遞。** 事件雖持久寫入 Outbox（路徑 A），但若被會吞例外的**直連 MediatR** handler 消費，耐久性在最後一跳被丟棄。消費者必須用 Inbox 才能保住保證。
2. **路徑 B 本質可丟。** 直接發佈無 Outbox；若在提交 DB 寫入*之後*才觸發，系統可能到達「DB 已變、無人被通知、無記錄」的狀態。
3. **完整持久模式** 是 Outbox 發佈 → Inbox 消費 + 重試／死信。此套件即 `ProductCreatedIntegrationEvent`（Catalog → Notifications）。它是參考；任何較弱的做法都必須是有意識的選擇。
4. **Unknown 列是真缺口。** 發佈但未見訂閱者的事件，不是死重量就是訂閱藏在尚未檢視之處 —— 上線前解決它。
5. **死信是暫存狀態,不是墳墓。** 用盡重試的訊息是被「停放」而非遺失 —— payload 與最後錯誤都保留。下游原因修好後,由操作者重新排入,正常(冪等)的 Inbox 排空會**恰好套用一次**;死信在同一交易內標記為已解決,因此訊息不會同時處於死信與待處理兩種狀態。恢復迴圈:`InboxDeadLetterReprocessor` + `POST /notifications/inbox/dead-letters/{id}/reprocess`,由 `InboxDeadLetterReprocessTests` 釘住。

## 補齊 Unknown 所需證據

- 完整列舉每個發佈者的整合事件與每個消費者的訂閱，建立發佈→消費鄰接表。
- 確認每個訂閱類型的消費者是 Inbox 還是直連。
- 是否有 best-effort（路徑 B）事件被當作正確性關鍵（那會使「可丟 = 是」成為真 bug）。

## 建議抄什麼

- 分類紀律本身：**每個整合事件上線前必有一列矩陣**。
- 唯一完整持久模式：Outbox 發佈 + Inbox 消費 + 重試／死信。
- 一條**從死信表出來的恢復路徑**(重新排入 → 冪等重新套用),讓操作者在修復後排空毒訊息積壓,而非手動改資料列。

## 不建議盲目抄什麼

- 任何 **高** 風險列。尤其對重要事件，勿抄提交後直接發佈，或會吞錯的直連消費。

## 待釐清問題

- 哪些事件是正確性關鍵 vs. 真正 best-effort？（需領域判斷。）
- 是否要求每個持久事件消費者都用 Inbox，並以架構／可靠性測試強制？

## 下一篇

- `09-lessons-learned/architecture-rules-for-my-own-project.md`
