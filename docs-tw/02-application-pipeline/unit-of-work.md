# Unit of Work（工作單元）

## 目的

精確說明此套件中 Unit of Work **實際做什麼**。實作很小；本篇將它與教科書模式、以及常見的隱式交易捷徑對照。

## 已檢視的檔案／區塊

- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/Pipeline/UnitOfWorkBehavior.cs`
- `BuildingBlocks.Infrastructure` 內的領域事件 dispatcher + accessor
- `BuildingBlocks.Infrastructure` 內的 typed 模組 unit-of-work + resolver
- `Catalog.Application/Products/Events/ProductCreatedNotificationHandler.cs`（outbox enqueue）

## 它做什麼

Unit of Work 是一個包住 command handler 的 MediatR pipeline behavior（`Pipeline/UnitOfWorkBehavior.cs`）。內層 handler 成功返回後，它：

1. **派發領域事件** —— 從 EF `ChangeTracker` 取領域事件，進程內發佈，並把對應 notification 序列化進 outbox（把 outbox 列加到**同一個** `DbContext`）。
2. **於顯式交易中儲存 + 提交** —— 持久化**全部**（聚合變更 + outbox 列）並提交一筆交易。

因為它會依請求解析正確的模組 `DbContext`（透過 typed unit-of-work resolver），多個模組 context 能共存而不發生 last-wins 註冊。

## 必答問題

### 是否顯式管理 DB 交易？

**是。** 此套件在 unit-of-work 中開一筆**顯式交易**，而非只依賴 EF 單次 `SaveChanges` 的隱式交易。

> 教訓（來自既有成果）：常見捷徑是跳過顯式交易，倚賴 EF Core 把一次 `SaveChanges` 包在一筆交易的事實。那可行 —— *直到* 某條 Command 同時透過 EF 與第二條路徑（例如 raw SQL/Dapper）寫入，原子性便會靜默破裂。把交易做成顯式能讓邊界可見、可審，並讓你刻意把額外寫入納入其中。

### 與 DbContext 的關係？

它使用單一請求 scope 的 `DbContext`（每 Command 一個）。領域事件 accessor 從該 context 的 `ChangeTracker` 讀取被追蹤實體，outbox enqueue 把 outbox 列加到同一 context。一 context、一 save、一交易。

### 誰呼叫？

Handler 從不直接呼叫。它是包住 command handler 的 pipeline behavior。典型 handler 透過 repository 變更聚合後返回，**不**呼叫 `SaveChanges`。由該行為提交。

Handler 拋錯（含更早行為拋出的業務規則或驗證例外）則不會到達提交，什麼都不持久化。

### 真實一致性邊界？

**一 Command = 一 DbContext = 一筆顯式交易**，含：

- 本 Command 期間的所有聚合狀態變更，**以及**
- 本 Command 領域事件產生的所有 outbox 列。

→ **Transactional Outbox 保證**：Command 提交則 outbox 訊息與之原子提交；回滾則無 outbox 訊息外洩。之後由 outbox 處理器非同步投遞給其他模組。

```text
Command handler 執行
   ├─ repo.Add/Update(aggregate)        (被 DbContext 追蹤)
   └─ aggregate.AddDomainEvent(...)     (實體上的記憶體)
        │
   UnitOfWorkBehavior（handler 後）
        ├─ 派發領域事件
        │     ├─ 進程內發佈
        │     └─ 序列化 notification → IOutbox.Add(...)   (被 DbContext 追蹤)
        └─ SaveChanges + commit  ← 一筆顯式交易：聚合 + outbox 列
```

### 取捨／限制

1. **進程內派發 vs. 儲存順序。** 領域事件在提交過程中派發。若進程內領域事件 handler 對*不同*儲存產生副作用，那些不在本交易內 —— 讓進程內 handler 留在同一 context，或在成功儲存後才派發。
2. **顯式交易多一點樣板與更多失敗路徑要測** —— 但邊界可審，這正是重點。
3. **不要放 no-op 介面方法**（例如什麼都不做的 outbox `Save()`）—— 會誤導。

## 教科書 UoW vs. 本實作

| 面向 | 教科書 UoW | 本套件 |
| ------ | ------------ | ------------ |
| 變更追蹤 | 自管變更集 | 交給 EF `ChangeTracker` |
| 交易 | 顯式開/提交交易 | 行為中**顯式交易** |
| 提交範圍 | 「所有已註冊變更」 | 「本 Command 那個 DbContext 追蹤的全部」 |
| 領域事件 | 常在提交後派發 | 提交過程中派發（notification 進同次 save） |
| Outbox | 可選 | 核心：outbox 列寫在同一交易（transactional outbox） |
| 多資料源 | 可協調 | 每 Command 單一 EF context；額外寫入須顯式納入 |

誠實總結：這是 **「EF SaveChanges + 領域事件派發 + transactional outbox，包在顯式交易內」**，以 pipeline behavior 封裝。它是好的 *transactional outbox*，不是通用多資源 UoW。

## 建議抄什麼

- **單一交易的 transactional outbox** 點子：原子寫入聚合 + outbox。
- **行為負責提交**，讓 handler 從不呼叫 `SaveChanges`。
- **顯式交易**邊界。
- 提交過程中派發領域事件。

## 不建議盲目抄什麼

- 讓**交易隱式**。若任何 Command 同時透過 EF 與另一路徑寫入，隱式做法會靜默破壞原子性。
- 在儲存持久化**之前**發佈帶外部副作用的進程內領域事件。
- 放什麼都不做的 outbox `Save()`（或類似）。

## 待釐清問題

- 是否存在需要同時透過 EF 與第二儲存寫入的 Command？若有，定義兩者如何納入交易（或禁止在一條 Command 內混用）。
- 進程內領域事件 handler 是否可對同一 context 寫入，實際上有嗎？

## 下一篇

- `05-events-and-messaging/integration-events.md`
- `05-events-and-messaging/reliability-matrix.md`
