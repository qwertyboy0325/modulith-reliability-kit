# 架構筆記：模組化單體可靠性套件

本文件集記錄本 repo 中 `ModulithReliabilityKit` 骨架背後的架構與可靠性模式。它把可重用的模組化單體 / DDD 經驗，萃取成**新建模組化單體後端**的實作指南，並錨定在本 repo 自己的中性範例領域（`Catalog` 生產者模組與 `Notifications` 消費者模組）。

先寫基礎層，業務功能最後（且多半不在本輪範圍）。

必要時會參考公開的既有成果 —— 特別是 **Kamil Grzybek 的「Modular Monolith with DDD」（公開參考，[github.com/kgrzybek/modular-monolith-with-ddd](https://github.com/kgrzybek/modular-monolith-with-ddd)）** —— 並於行內標明出處。所有具體程式碼論點都對應本 repo 的 `src/` 骨架。

> 英文版對照：`docs/README.md`（內容與英文版一致）。

---

## 1. 目的

- 為模組化單體萃取**可重用、有程式碼依據**的架構模式。
- 區分**成熟可抄**與**有風險／抄前需重設計**的模式。
- 產出可供新專案使用的**具體架構規則集**。

這不是功能導覽，也不是行銷文案。每個具體論點都對應本 repo `src/` 骨架下的檔案；公開既有成果則標明出處。

## 2. 如何閱讀

每篇固定結構：

1. **目的**
2. **已檢視的檔案／模式**
3. **實際實作摘要**（程式現在怎麼做，不是設計意圖）
4. **流程圖**（文字，必要時）
5. **建議抄什麼**
6. **不建議盲目抄什麼**
7. **待釐清問題**
8. **下一篇連結**

行為不一致時會**明寫**，不會粉飾。全文使用三種標籤：

- **ACTUAL（實際）** — 程式今天怎麼跑。
- **INTENT（意圖）** — 結構看起來想達成什麼。
- **VERDICT（結論）** — 可抄 / 抄但需改 / 勿抄，附理由。

## 3. 建議閱讀順序

1. `00-orientation/project-map.md`
2. `00-orientation/source-code-reading-order.md`
3. `01-foundation/building-blocks.md`
4. `01-foundation/dependency-injection.md`
5. `02-application-pipeline/unit-of-work.md`
6. `05-events-and-messaging/integration-events.md`
7. `05-events-and-messaging/reliability-matrix.md`
8. `07-module-architecture/catalog-module-best-practices.md`
9. `07-module-architecture/modular-monolith-ddd-comparison.md`
10. `09-lessons-learned/architecture-rules-for-my-own-project.md`
11. `09-lessons-learned/inbox-stale-failure-write-race.md`
12. `architecture/container-view.md` —— 高階事件流程圖（視覺概覽）
13. `architecture/handoff-components.md` —— 元件與 failure boundaries（視覺概覽）

## 4. 本文件集「不是」什麼

- **不是**業務功能目錄（領域功能邏輯本輪不寫）。
- **不是**背書。真實世界的模組化單體都有不一致與可靠性缺口，會標出來當作教訓。
- **不是**完整版。這是**第一輪自下而上**；後續見各篇「下一篇」與狀態表。
- **不是**可直接貼上的程式。部分模式標為「勿直接抄」。

## 5. 目錄分類（與調整理由）

與 `docs/` 相同編號，僅建立第一輪所需檔案：

```text
docs-tw/
  README.md
  architecture/
    container-view.md
    handoff-components.md
  00-orientation/
  01-foundation/
  02-application-pipeline/
  05-events-and-messaging/
  07-module-architecture/
  08-operational-concerns/
    observability.md
  09-lessons-learned/
    high-write-time-series-ingest.md
    inbox-stale-failure-write-race.md
  10-skeleton/
```

調整說明（與英文版相同）：

- 未建立 `03`～`06` 空殼，避免寫未對程式查證的內容；`08-operational-concerns/observability.md` 已建立（指標 + 追蹤）。
- `solution-structure` / `composition-root` 併入 `dependency-injection.md`（組合根與 DI 策略一起讀較清楚）。

## 6. 文件狀態

| 區塊 | 狀態 | 備註 |
| ------------------------ | ------ | ------------------------------------------------------------------------------------------- |
| 專案地圖 | 草稿 | 依 `src/` 目錄對應職責 |
| 閱讀順序 | 草稿 | 新進工程師自下而上路徑 |
| 基礎（BuildingBlocks） | 草稿 | 錨定於 `src/BuildingBlocks`；事件契約與領域耦合取捨已標 |
| 依賴注入 | 草稿 | 單一 MS.DI 容器，含 typed 每模組持久化 |
| Unit of Work | 草稿 | 顯式交易 UoW；先派發事件再 `SaveChanges` |
| 整合事件 | 草稿 | Outbox 發佈 vs 直接 Publish；僅記憶體匯流排 |
| 可靠性矩陣 | 草稿 | 每事件分類範本；best-effort vs durable 決策已記錄 |
| 教訓與規則 | 草稿 | 個人規則集;新增去識別化案例:高寫入時序攝取(寫入放大、分層儲存、列寬);inbox 失敗記錄過期寫入競態已 test-first 修正(claim 稽核發現的併發 bug;保留可重現的 red 於其 commit,由 regression test 釘住) |
| Skeleton 實作 | 草稿 | `src/ModulithReliabilityKit` 骨架 + Catalog/Notifications 模組；已新增 typed module persistence 與 PostgreSQL migration 示範章節 |
| 領域模型 | 未開始 | 下一輪 |
| 持久化（EF/Dapper） | 未開始 | 下一輪 |
| 背景處理 | 未開始 | 下一輪 |
| 模組架構 | 草稿 | 已新增 Catalog 範例模組與公開模組化單體比較；facade + typed persistence 優先項已落地 |
| 維運議題 | 草稿 | 可觀測性(可靠性指標經 Prometheus `/metrics` + 選用 OTLP 追蹤)已記於 `08-operational-concerns/observability.md`;重試/死信/復原/多實例安全見 `05-events-and-messaging` |

---

## 事實來源

所有具體程式碼論點皆相對於本 repo 根目錄，指向 `src/` 骨架；公開既有成果標明出處。無法對應檔案者標 **Unknown**。
