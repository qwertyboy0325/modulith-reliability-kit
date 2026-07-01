# 依賴注入與組合根

## 目的

說明本套件的 DI 策略：容器拓撲、模組級註冊、生命週期、Command 管線，以及模組隔離與共用。這是模組化單體最重要的結構決策之一，也很容易過度設計。

## 已檢視的檔案／區塊

- `src/Api/ModulithReliabilityKit.Api/Program.cs`
- `src/Api/ModulithReliabilityKit.Api/Modules/CatalogEndpoints.cs`
- 各模組的 `Add<Module>Module(...)` 註冊擴充（位於 `<Module>.Infrastructure`）
- `BuildingBlocks.Infrastructure/Pipeline/UnitOfWorkBehavior.cs`

## 核心結論：單一容器 + 模組級註冊

本套件是**單一容器**應用。ASP.NET Core Host 擁有一個 MS.DI 容器，每個模組透過從 `Program.cs` 呼叫的 `Add<Module>Module(...)` 擴充貢獻註冊。模組邊界由**專案引用 + 架構測試**強制，而非讓每個模組跑在自己的容器裡。

```text
HTTP 請求
  → 端點（Host 容器）
    → I<Module>Module 門面
      → 每請求 scope 內 MediatR Send(command)
        → 從單一容器解析管線行為 + Handler
```

### 設計註記：單容器 vs. 每模組容器

一個著名替代方案（部分公開模組化單體參考採用）是讓**每個模組各有自己的 IoC 容器**，Host 與模組僅共用少數物件實例。這能最大化執行期隔離，但會引入靜態組合根、重複的容器模組、更多生命週期複雜度與非標準的 hosting。

> ⚠️ **應避免的反模式**（見於既有成果，此處刻意**不**抄）：
> - **靜態可變容器持有者**（每模組於啟動時設定一個 `static IContainer`）。它們是全域單例，使平行測試隔離變難，並隱藏依賴圖。
> - **洩漏的 lifetime scope** — 開一個 scope（`BeginLifetimeScope()`）解析服務卻沒 `using`，導致 scope 永不釋放。
>
> 本套件選單一容器以避開兩者。只有在你能明確說出硬性執行期隔離需求時，才採用每模組容器。

---

## 1. Host 組合根

`Program.cs` 建立 Host 並呼叫每個模組的註冊擴充來組合模組。端點很薄：把傳輸輸入對應到 command/query 並經模組門面（`ICatalogModule`）轉發，因此 API 從不直接依賴 MediatR。

Host 擁有跨切面單例（event bus、連線設定），並把設定傳入各模組的 `Add<Module>Module(...)`。

---

## 2. 每模組註冊

每個模組的 `Add<Module>Module(...)` 擴充把該模組自己的服務 —— 它的 `DbContext`、repository、handler、outbox/inbox 處理、typed 持久化 —— 註冊進共用容器。因為只有一個容器，就沒有每模組組合根持有者，也沒有另一棵 lifetime tree 要推敲。

模組持久化透過可重用的 Building Blocks adapter（`AddModulePersistence<TContext>(...)`）註冊，使多個模組 `DbContext` 能共存而不發生 last-wins 註冊。見 `07-module-architecture/catalog-module-best-practices.md`。

---

## 3. Command 管線（MediatR pipeline behaviors）

狀態變更 Command 流經一組有序的 MediatR `IPipelineBehavior<,>`：

**驗證 → UnitOfWork → 日誌 → Handler。**

- **驗證**先跑（FluentValidation）：任一 validator 失敗即在 Handler 執行前拋出。
- **UnitOfWork**（`Pipeline/UnitOfWorkBehavior.cs`）包住 Handler，在其成功返回後 commit —— 於一個顯式交易中派發領域事件並持久化聚合變更 + outbox 列（見 `02-application-pipeline/unit-of-work.md`）。
- **日誌**記錄請求名稱／型別／耗時與錯誤。

> 教訓（來自既有成果）：讓管線**跨模組一致**。各模組 decorator 集合不同，或兩套平行日誌機制（一模組用 pipeline behavior、另一模組用 handler decorator），都是債。一套管線，處處套用。

Handler 與 validator 以每模組組件掃描發現。

---

## 4. 生命週期（證據）

| 服務 | 生命週期 |
| ------- | -------- |
| 模組 `DbContext`（例如 `CatalogContext`） | Scoped（每請求／Command 一個） |
| Repository | Scoped |
| unit-of-work behavior / 領域事件 dispatcher | Scoped |
| MediatR `IMediator` | Scoped |
| Outbox/inbox 處理器（背景工作） | Singleton host + scoped 工作單元 |
| `IEventsBus`（記憶體內） | Singleton |

**每 Command 一個新 scope** 且 `DbContext` 為 **Scoped**，意即**每操作一個 `DbContext`** —— 這使 Unit of Work 邊界等同「一條 Command」。

---

## 5. 隔離 vs. 共用

**每模組擁有：** 各模組的 `DbContext`、repository、handler 與 outbox/inbox 處理。模組不引用彼此的 Domain/Application/Infrastructure —— 只引用他模組的 `IntegrationEvents` 契約。

**共用：** 記憶體 `IEventsBus`、連線設定，與 Building Blocks 抽象。

結果：
- 邊界由**專案引用 + 架構測試**強制，比容器階層簡單，且能在 CI 抓到違規。
- 沒有來自靜態容器持有者的隱藏全域狀態。

---

## 建議抄什麼

- **單一容器 + `Add<Module>Module` 註冊擴充** —— 簡單、標準的 hosting。
- **MediatR pipeline behaviors** 作 Command 管線（驗證／UnitOfWork／日誌）—— 核心可重用點子。
- **每 Command/Query 一 scope**，`DbContext` 註冊為 Scoped。
- **Typed 模組持久化**（`AddModulePersistence<TContext>`），讓多個 context 乾淨共存。

## 不建議盲目抄什麼

- **靜態可變組合根持有者** —— 破壞測試隔離、隱藏依賴圖的全域單例。
- **Scope 洩漏**（`BeginLifetimeScope()` 沒 `using`）。
- **各模組管線註冊不一致** —— 標準化它。
- **兩套日誌機制**（pipeline behavior vs decorator）—— 擇一。
- 預設採用**每模組容器**模型。它笨重且容易洩漏。優先單一容器 + 模組級註冊 + 架構測試；只有在能說出隔離需求時才用每模組容器。

## 待釐清問題

- 何時模組隔離需要超越專案引用 + 架構測試？
- 背景處理是否需要每 Command scope 之外的 scope factory？

## 下一篇

- `02-application-pipeline/unit-of-work.md`
- `05-events-and-messaging/integration-events.md`
