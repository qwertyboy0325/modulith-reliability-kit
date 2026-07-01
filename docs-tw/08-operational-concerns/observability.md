# 維運關注:可觀測性

## 1. 目的

讓可靠性管線在**維運層面可觀測**,而不只靠 log。維運者應能一眼回答:訊息有在流動嗎?inbox 套用失敗/重試多頻繁?有東西被死信嗎?操作員有在復原死信嗎?這些都直接從已承載可靠性保證的程式碼路徑輸出成指標(與 span)。

## 2. 檢視的檔案／模式

- `BuildingBlocks.Infrastructure/Diagnostics/ReliabilityMetrics.cs` —— 領域特定量測儀器。
- `BuildingBlocks.Infrastructure/Diagnostics/ReliabilityInstrumentation.cs` —— 公認的 meter / activity-source 名稱。
- `BuildingBlocks.Infrastructure/Processing/OutboxProcessorBase.cs` —— outbox 計數器 + span。
- `Modules/Notifications/…/Processing/NotificationsInboxProcessor.cs` —— inbox 計數器 + span。
- `Modules/Notifications/…/Inbox/InboxDeadLetterReprocessor.cs` —— 操作員復原計數器。
- `BuildingBlocks.Infrastructure/Events/NatsEventBus.cs` + `NatsSubscriptionBackgroundService.cs` —— 傳輸計數器 + span。
- `Api/ModulithReliabilityKit.Api/Program.cs` —— OpenTelemetry 接線 + `/metrics`。

## 3. 實際實作摘要

插樁使用 BCL 的 `System.Diagnostics.Metrics.Meter` / `ActivitySource`(處理器不綁定任何函式庫)。單一 `ReliabilityMetrics` singleton 由所有處理器共用。指標如下:

| 指標 | 型別 | 意義 |
| ---- | ---- | ---- |
| `messaging.outbox.published` | counter | 發佈到 bus 的 outbox 列(tag:`module`) |
| `messaging.outbox.publish_failures` | counter | 拋例外的 outbox 發佈嘗試 |
| `messaging.outbox.process.duration` | histogram (ms) | 發佈一筆 outbox 訊息耗時 |
| `messaging.inbox.processed` | counter | 本地效果 + `processed` 標記已提交的 inbox 訊息(恰好一次*本地*套用) |
| `messaging.inbox.retried` | counter | 失敗後排入重試的 inbox 訊息 |
| `messaging.inbox.dead_lettered` | counter | 移入死信表的 inbox 訊息 |
| `messaging.inbox.process.duration` | histogram (ms) | 套用一筆 inbox 訊息耗時 |
| `messaging.inbox.dead_letter.reprocessed` | counter | 操作員重新排入的死信 |
| `messaging.transport.published` | counter | 在持久(NATS)傳輸上發佈的事件 |
| `messaging.transport.redelivered` | counter | 被 nak 而重送的傳輸投遞 |

Span(`ActivitySource` = `ModulithReliabilityKit.Reliability`):`outbox.publish`、`inbox.process`、`nats.publish`、`nats.consume`;失敗會把 span 狀態設為 error。這些是**逐進程**的 span:trace context **不會**透過 NATS header 傳播,因此發佈端的 span 與下游消費端的 span 尚未關聯成同一條分散式 trace(見待釐清問題)。

**API host** 接上 OpenTelemetry:指標(自訂 meter + ASP.NET Core + runtime)由 Prometheus scraping 端點 `GET /metrics` 匯出;traces 只有在設定 `Observability:OtlpEndpoint` 時才經 OTLP 匯出(否則 span 仍記錄但不外送,預設執行不需要 collector)。

## 4. 流程

```text
outbox drain ──▶ OutboxPublished / duration ─┐
inbox drain  ──▶ processed|retried|dead_lettered / duration ├─▶ Meter "…Reliability" ─▶ OTel ─▶ /metrics（Prometheus）
reprocess    ──▶ dead_letter.reprocessed      │                                     └─▶ OTLP traces（opt-in）
NATS bus     ──▶ transport.published / redelivered ─┘
```

## 5. 值得抄

- **量測可靠性結果,而非只有請求延遲。** 在非同步系統裡,重試率與死信數才是預測事故的數字。
- **插樁留在 Infrastructure、只用 BCL 型別。** 處理器只依賴 `Meter`/`ActivitySource`;匯出器選擇留在 host,換 Prometheus/OTLP 不動領域碼。
- **從保證所在的同一段程式碼記錄**,指標永不會與行為漂移。

## 6. 別盲目抄

- **Prometheus AspNetCore 匯出器是 prerelease**(`1.16.0-beta.1`);其所依賴的 OpenTelemetry metrics API 本身穩定。若你只能用穩定套件,改用 OTLP 匯出到 collector 並拿掉 `/metrics` 端點。
- Tag 刻意**低基數**(`module`、`transport`),勿把逐訊息 ID 當 tag。
- 這些是**計數與耗時**,不是佇列深度 gauge。加一個 pending-depth observable gauge(觀測 inbox/outbox 表)是合理的下一步。

## 7. 待解／下一步

- Pending-depth observable gauge(outbox 未處理、inbox 待處理、未結死信)。
- 讓 trace context **穿過** NATS header 做分散式追蹤(目前 span 為單進程)。
- 現成的 Grafana 儀表板 / 重試率與死信門檻告警規則。

## 8. 下一份文件

- `05-events-and-messaging/reliability-matrix.md` —— 這些指標所觀測的保證。
- `01-foundation/building-blocks.md` —— 處理器與 bus 的所在。
```
