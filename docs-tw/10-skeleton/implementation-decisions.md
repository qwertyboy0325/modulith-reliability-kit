# Skeleton 實作決策比較（Reference vs ModulithReliabilityKit）

## 目的

記錄 `ModulithReliabilityKit` 骨架建立時「改了什麼、為什麼改、優劣取捨」，作為方法比較章節。

## 範圍

- 參考來源：既有模組化單體程式庫（見 `07-module-architecture/modular-monolith-ddd-comparison.md`）
- 新骨架：`src/*`
- 本輪不做模組層實作（你已同意可先不做）。

## 決策比較矩陣

| 主題 | 參考作法 | ModulithReliabilityKit 作法 | 優點 | 缺點 | 影響檔案 |
|------|------|------|------|------|------|
| DI 容器拓撲 | Host Autofac + 每模組 static `IContainer` | 單一 MS.DI 容器（先留模組接縫） | 生命週期簡化、降低全域狀態與 scope 洩漏 | 尚未提供每模組硬隔離 | `src/Api/*`（後續）, `src/BuildingBlocks/*` |
| Command 管線 | Decorator/Behavior 混用且跨模組不一致 | 統一 MediatR `IPipelineBehavior`（Validation/Logging/UoW） | 管線一致、可測 | 註冊順序與覆蓋需嚴格治理 | `...Infrastructure/*` |
| UoW 交易邊界 | 依賴 `SaveChanges` 隱式交易 | UoW 顯式交易 | 邊界可審計、跨儲存策略清楚 | 程式碼稍增、失敗路徑需測 | `...Infrastructure/UnitOfWork.cs` |
| 領域事件耦合 | `IDomainEvent : INotification` | Domain 不依賴 MediatR，於 Application 適配 | 邊界更乾淨 | 需要明確 mapping 層 | `...Domain/*`, `...Application/Events/*` |
| 值物件實作 | 反射比對 | 明確 `GetEqualityComponents` | 行為與效能可預期 | 每型別需手寫 components | `...Domain/ValueObject.cs` |
| Strongly Typed ID | `TypedIdValueBase` + record 混用 | 單一 `StronglyTypedId<TValue>` | 一致性高、Converter 容易統一 | 匯入舊模型需遷移 | `...Domain/StronglyTypedId.cs` |
| 事件契約命名空間 | Application + Infrastructure alias 雙份 | Application 單一正規定義 | 移除命名漂移與別名技術債 | 需要一次性整理引用 | `...Application/Events/*` |
| Outbox/Inbox 抽象位置 | Outbox 在 App；Inbox 多在 Infra | Outbox + Inbox 抽象都放 Application | 訊息抽象對稱 | 落地持久化仍在 Infra | `...Application/Outbox/*`, `...Application/Inbox/*` |
| EventBus 失敗策略 | 預設吞例外 | 預設不吞例外（fail-fast） | 關鍵路徑失敗可見 | 無重試策略時失敗更顯性 | `...Infrastructure/Events/*` |
| 套件版本管理 | 依專案條件區塊 | `Directory.Packages.props` 中央版控 | 單一真相、升版快 | 需維持跨專案版本紀律 | `src/Directory.Packages.props` |

## 本輪已改內容

1. 建立新的 `src/` 解決方案骨架。
2. 完成 `BuildingBlocks.Domain` 基礎型別（Domain 無 MediatR）。
3. 完成 `BuildingBlocks.Application` 抽象契約（Commands/Queries、Events、Outbox/Inbox、UoW、例外、分頁）。
4. 完成 `BuildingBlocks.Infrastructure`（顯式交易 UoW、領域事件派發、pipeline behaviors、in-memory bus、processor base、StronglyTypedId converters、DI extension）。
5. 新增 `ModulithReliabilityKit.Api` host（Swagger、health endpoint、module seam）與 `ModulithReliabilityKit.ArchitectureTests`。
6. 新增本比較章節與 build log（`docs` + `docs-tw`）。

## 下一輪待決

- 模組層加入後，要多嚴格的隔離策略？
- 整合事件 `Durable` vs `BestEffort` 分類規則如何落地？
- 模組內是否允許直接 publish？
