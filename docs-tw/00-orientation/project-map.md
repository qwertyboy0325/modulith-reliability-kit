# 專案地圖

## 目的

對應 `ModulithReliabilityKit` 骨架（`src/`）的主要原始碼目錄與**職責邊界**。只談**專案形狀**，不談業務功能。請最先讀本篇。

## 已檢視的來源資料夾

- `src/`（解決方案結構）
- `src/BuildingBlocks/`
- `src/Modules/`
- `src/Api/ModulithReliabilityKit.Api/`
- `src/Tests/`

## `src/` 目錄結構

```text
src/
  Api/ModulithReliabilityKit.Api/   → 組合 Host（ASP.NET Core）。啟動與模組接線。
  BuildingBlocks/                   → 共用核心：Domain / Application / Infrastructure 抽象。
    ModulithReliabilityKit.BuildingBlocks.Domain/
    ModulithReliabilityKit.BuildingBlocks.Application/
    ModulithReliabilityKit.BuildingBlocks.Infrastructure/
  Modules/                          → 限界上下文，各自垂直切片。
    Catalog/        → 生產者模組（聚合 Product；發佈 ProductCreatedIntegrationEvent）
    Notifications/  → 消費者模組（讀模型 ProductAnnouncement；以 inbox 消費）
  Tests/                            → 跨切面測試：ArchitectureTests、IntegrationTests、ReliabilityTests。
```

## 各區塊職責

### API Host — `src/Api/ModulithReliabilityKit.Api/`

- **負責**：進入點、HTTP 端點、中介軟體、**所有模組的組合**。
- **關鍵檔**：`Program.cs`（Host + DI 組合）、`Modules/CatalogEndpoints.cs`（HTTP → command/query 對應）。
- **注意**：API 是唯一引用各模組 `Infrastructure` 專案的地方；透過各模組的 `Add<Module>Module(...)` MS.DI 註冊擴充方法接線。

### BuildingBlocks — `src/BuildingBlocks/`

共用核心，三專案對應各模組分層：

- `BuildingBlocks.Domain/` — `Entity`、`ValueObject`、`IAggregateRoot`、領域事件與業務規則抽象、強型別 ID 基底。
- `BuildingBlocks.Application/` — `IEventsBus`、`IntegrationEvent`、領域事件通知、`IOutbox`/`OutboxMessage`、inbox 契約 + 重試策略、執行環境、例外、分頁。
- `BuildingBlocks.Infrastructure/` — unit-of-work behavior、領域事件派發、記憶體 Event Bus、outbox/inbox 處理器基底、日誌、DI 擴充。

詳見 `01-foundation/building-blocks.md`。

### Modules — `src/Modules/<Module>/`

目前兩個限界上下文：**Catalog** 生產者與 **Notifications** 消費者。

生產者（Catalog）用四層切片：

```text
Catalog/
  Domain/            → 聚合（Product）、值物件、領域事件、Repository 介面、業務規則
  Application/       → Command/Query、Handler、Validator、notification handler、outbox enqueue
  IntegrationEvents/ → 跨模組**公開**整合事件契約（其他模組只能引用此專案）
  Infrastructure/    → DbContext、EF Repository、DI 接線、outbox 處理器
```

消費者（Notifications）刻意較輕：一個 `Application` 層（讀模型、handler）與一個 `Infrastructure` 層（inbox writer、inbox 處理器、DbContext）。它不發佈自己的契約，因此沒有 `IntegrationEvents` 專案。

> **ACTUAL**：跨模組引用限於他模組的 `IntegrationEvents`。無模組直接引用他模組的 Domain/Application/Infrastructure（良好）。架構測試會強制此邊界 — 見 `07-module-architecture`。

### Infrastructure（橫切）— 兩層

1. **共用**：`BuildingBlocks.Infrastructure/`（unit-of-work behavior、event bus、派發、outbox/inbox 處理器基底）
2. **每模組**：`<Module>.Infrastructure/`（該模組 DbContext、Repository、outbox/inbox handler、DI 擴充）

### Application / Domain — 同樣兩層

共用抽象在 BuildingBlocks；業務 Command/聚合在各模組（例如 Catalog 的 `Product`）。

### IntegrationEvents — 每個生產者模組一專案

跨模組契約面；其他模組只依賴契約、不依賴實作（例如 `Catalog.IntegrationEvents/ProductCreatedIntegrationEvent.cs`）。

### Tests — `src/Tests/`

- `ArchitectureTests` — 架構邊界測試（分層方向、模組隔離、公開契約規則）。
- `IntegrationTests` — 跨模組／基礎設施整合測試（例如 outbox → bus → inbox 流程）。
- `ReliabilityTests` — 針對訊息可靠性保證的測試（重試、dead-letter、冪等）。

## 心智模型

```text
                 ┌──────────────────────────────────────────────┐
                 │  API Host (ModulithReliabilityKit.Api)         │
                 │  單一容器 + Add<Module>Module 接線             │
                 └───────────────┬──────────────────────────────┘
                                 │ 註冊各模組
             ┌───────────────────┴───────────────────┐
             ▼                                         ▼
      ┌──────────────┐   ProductCreated        ┌──────────────────┐
      │  Catalog     │ ─── 整合事件 ──────────►│  Notifications   │
      │ D/A/IE/Infra │  (outbox → bus → inbox) │  A/Infra         │
      └──────┬───────┘                          └────────┬─────────┘
             └───────────────────┬───────────────────────┘
                                 ▼
                        ┌───────────────────┐
                        │  BuildingBlocks    │
                        └───────────────────┘
```

## 建議抄什麼

- **`BuildingBlocks` + `Modules/<context>` + 薄 API Host** 的整體形狀。
- **每模組分層 + 獨立 `IntegrationEvents` 契約專案**（用於生產者模組）作為模組邊界。
- **集中套件版本**（`Directory.Packages.props`）。

## 不建議盲目抄什麼

- 不要替一個什麼都不發佈的純消費者模組加上 `IntegrationEvents` 專案（Notifications 刻意沒有）。契約只放在真正存在的地方。
- 不要讓消費者模組在需要之前就長成完整四層；讓它的重量符合角色。

## 待釐清問題

- 出現第三個模組時，兩模組生產者／消費者切分是否仍能描述拓撲，還是會演化成 hub/mesh？
- Notifications 的讀模型是否該有自己的 `Domain` 專案，還是維持 Application/Infrastructure 的關注點？

## 下一篇

- `00-orientation/source-code-reading-order.md`
- `01-foundation/building-blocks.md`
