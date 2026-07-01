# 自建專案架構規則

## 目的

一份**可執行的規則集**，供新建模組化單體 / DDD 後端在寫程式**之前**採用，源自哪些做法有效、哪些常出錯。以常見失敗模式為證據，並錨定在此套件自己的設計（Catalog 生產者、Notifications 消費者）。刻意直接、帶批判。

## 依據

- `01-foundation/building-blocks.md`
- `01-foundation/dependency-injection.md`
- `02-application-pipeline/unit-of-work.md`
- `05-events-and-messaging/integration-events.md`
- `05-events-and-messaging/reliability-matrix.md`

---

## 1. 值得抄的模式（可直接或幾乎可直接）

1. **版面：** `BuildingBlocks` + `Modules/<context>/{Domain,Application,Infrastructure,IntegrationEvents}` + 薄 API Host。
2. **每個生產者模組獨立 `IntegrationEvents` 專案** 作為唯一跨模組契約（其他模組只引用此專案）。由架構測試強制。
3. **BuildingBlocks.Domain：** `Entity`（領域事件 + `CheckRule`）、`IAggregateRoot`、`IBusinessRule` + 例外、領域事件基底。
4. **三層事件：** 領域事件 → 領域事件 notification → 整合事件。
5. **Transactional Outbox：** 一筆交易同時提交聚合 + Outbox。
6. **Outbox 處理：** `FOR UPDATE SKIP LOCKED`、批次、冪等標記 processed。併發安全。
7. **Inbox：** `ON CONFLICT DO NOTHING` + 重試/死信 — 可靠消費的成熟核心，整包抄。
8. **Command 管線用 pipeline behaviors：** 驗證 → UoW → 日誌；Handler 保持純粹。
9. **Handler 不呼叫 `SaveChanges`** — unit-of-work behavior 負責提交。
10. **一等公民的執行／關聯環境存取器。**

## 2. 抄但必須修改

1. **UoW — 交易顯式化。** 只靠 EF 隱式單次 `SaveChanges` 交易，Command 內混第二個儲存就會靜默破壞原子性 → 開顯式交易（此套件如此）並納入所有寫入，或禁止在一條 Command 內混用。
2. **領域事件派發時機。** 若 in-process 領域事件 handler 有外部副作用 → 改為成功儲存後派發，或保證 handler 只動同一 Context。
3. **值物件。** 反射值物件慢 → 優先 `record`；反射基底僅在必要處保留。
4. **強型別 Id。** 從 day one 只選一種；兩種並存（舊基底 + record）純屬債。
5. **日誌。** 全庫只選 **一種**（pipeline behavior *或* decorator，不要兩套），處處套用。
6. **無用的介面方法**（例如什麼都不做的 outbox `Save()`）→ 刪除或實作清楚。
7. **Internal Commands。** 只在真有 delayed/retryable 操作時採用，且處理器應與 Outbox 同級（`SKIP LOCKED`、真正的失敗處理）。

## 3. 成熟度不足、勿直接抄

1. **記憶體 `IEventsBus` 當可靠跨模組傳輸** — 進程內字典沒有無訂閱投遞、也沒有跨進程持久性；耐久只在 Outbox/Inbox → 別把 `IEventsBus.Publish` 當可靠投遞；若可能多進程就在其後放 broker。
2. **直接 `IEventsBus.Publish`，尤其 `commit` 之後** — 「DB 已變、無人知、無紀錄」；僅限明確可丟棄事件。
3. **消費模型不一致**（有些走持久 inbox、有些直連 MediatR 吞錯）→ 決定一種並強制。
4. **靜態可變組合根持有者**（每模組 `static IContainer`）— 破壞測試隔離、隱藏依賴圖的全域單例，也易生 scope 洩漏。
5. **每模組容器模型**（獨立模組容器僅共用實例）— 重且易洩漏 → 新專案預設單容器 + 模組級註冊 + 架構測試，除非能說明硬隔離需求。
6. **未測試／壞掉的模組產生器**（例如引用不存在的模板目錄）— 別在沒有模板時抄產生器，並在 CI 測試它。
7. **空的 IntegrationEvents 專案** — 死專案；契約只放在真正存在之處。

## 4. 從 day one 強制執行的規則

1. 所有狀態變更經 **Command Handler**；Controller/Service 不直接寫業務狀態。
2. **Handler 不直接 `SaveChanges`/提交** — unit-of-work behavior 提交。
3. **UoW = 一致性邊界，且交易顯式** — 一 Command 一交易 = 聚合 + Outbox；除非兩者納入同一交易，否則不在一條 Command 內混用 EF 與第二個儲存。
4. **跨模組耐久事件走 Outbox** — outbox 路徑為預設，直接發佈是例外。
5. **BestEffort 直接發佈須明示 `Reliability: BestEffort` 分類並文件化** — 預設耐久。
6. **每個整合事件合併前必有可靠性矩陣一列** — 禁止 Unknown 上線。
7. **耐久事件的消費方必須 Inbox + 冪等** — 禁止對關鍵事件「直連 MediatR + 吞錯」。
8. **Inbox Handler 冪等** — `(logical_id, occurred_on)` 去重 + 重試 + 死信。
9. **模組間禁止直接呼叫** — 僅透過 `IntegrationEvents`；跨模組引用僅限 Application，禁止 Infrastructure/Domain。
10. **Application 不得引用 BuildingBlocks.Infrastructure。** 分層方向是法律。
11. **新模組跟固定、可運作的模板** — 任何產生器須 CI 驗證。
12. **一種強型別 Id、一種日誌、一種驗證** — 架構測試強制。
13. **Bus/消費方不靜默吞例外** — 死信、指標、告警。
14. **架構測試守邊界** — 模組隔離、分層、Handler 不 SaveChanges、`IEventsBus.Publish` 僅允許清單內。

## 5. 實作前要回答的問題

1. **永遠單進程還是可能多進程？** 若可能，現在就把 `IEventsBus` 抽象到 broker 後面；只用記憶體是死路。
2. **單容器還是每模組容器？** 預設單容器 + 架構測試。只有能說出理由才每模組。
3. **僅 EF 還是 EF + 另一儲存寫入？** 若允許，先定交易故事（規則 3）。
4. **哪些事件正確性關鍵 vs. best-effort？** 決定每事件 Outbox vs 直接發佈。
5. **背景處理週期與冪等？** 確保 Job 單飛且冪等。
6. **死信誰處理？** 死信表需要有人看管。

## 6. 建議先建的極簡骨架（自下而上）

業務功能從第 6 步再開始。

1. **BuildingBlocks.Domain** — `Entity`（+ 領域事件 + `CheckRule`）、`IAggregateRoot`、領域事件基底、`IBusinessRule` + 例外、一種強型別 Id。
2. **BuildingBlocks.Application** — `IEventsBus`、`IntegrationEvent`、領域事件 notification、`IOutbox`/`OutboxMessage`、inbox 契約 + 重試策略、執行環境、標準例外、分頁；**單一命名空間，不出重複契約定義**。
3. **BuildingBlocks.Infrastructure** — **顯式交易**的 UoW、領域事件 dispatcher + accessor + 顯式 notification mapper、inbox 處理 + 死信、單一日誌機制、強型別 Id 的 EF 轉換。
4. **組合根** — 單容器；每 Command 一 scope；pipeline behaviors（驗證 → UnitOfWork → 日誌）；MediatR 掃描；架構測試。
5. **訊息執行時** — Transactional outbox 處理器（`SKIP LOCKED`、批次）+ inbox 處理器（冪等、重試、死信）+ broker 可替換的 `IEventsBus`（開發可用記憶體實作，藏在介面後）；`docs/` 內可靠性矩陣模板。
6. **一個參考生產者 + 一個消費者端到端** — 含一條耐久整合事件（Outbox 發 → Inbox 收）；此套件即 Catalog（`Product` → `ProductCreatedIntegrationEvent`）→ Notifications。

```text
BuildingBlocks (Domain / Application / Infrastructure)
        ▲
Modules（Catalog 生產者、Notifications 消費者）+ IntegrationEvents
        ▲
API Host（單容器、pipeline behaviors、ArchTests）
```

## 一行 recap

- **抄：** 版面、契約專案、三層事件、Transactional Outbox、Inbox+死信、pipeline behaviors、Handler 不 Save。
- **抄但改：** 顯式交易、record VO/Id、單一日誌/驗證。
- **勿抄：** 記憶體 Bus 當可靠傳輸、關鍵事件直接發佈、吞錯消費、靜態組合根、預設每模組容器、壞產生器、死 scaffold。

## 待釐清（對你自己的專案）

- 是否真的需要每模組物理隔離容器，還是在盲從？
- day one 究竟需要多少維運機制（指標、死信處理）？

## 下一輪文件

- `03-domain-model/`、`04-persistence/`、`06-background-processing/`、`07-module-architecture/`、`08-operational-concerns/`
