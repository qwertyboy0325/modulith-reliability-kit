# Inbox 失敗記錄的過期寫入競態（由 claim 稽核發現）

> 英文版對照：`docs/09-lessons-learned/inbox-stale-failure-write-race.md`。

## 狀態

**已知問題，於目前程式碼可重現。** 這是可靠性 claim 稽核時浮現的缺口，並**刻意在修正前先記錄**：一個缺口
加上鎖住它的 regression test，比默默改掉一行更有價值。修正採 test-first —— 先在今天的程式上寫出一個會
**red** 的 deterministic 測試，修完再轉 **green**。

## 目的

記錄 inbox *失敗記錄*路徑上的一個具體併發 bug：什麼交錯會觸發、為何業務效果仍然正確、它實際汙染了什麼，
以及關閉它的最小修正。

## 位置

- `Notifications.Infrastructure/Processing/NotificationsInboxProcessor.cs` —— `ProcessOneCoreAsync`（claim +
  套用）與 `RecordFailureAsync`（rollback 之後才跑的失敗記帳）。

## 機制

正常路徑在 Postgres row lock 下 claim 該列，並把效果 + `processed` 標記於同一交易提交：

```text
SELECT ... WHERE id = ? AND processed_on_utc IS NULL FOR UPDATE SKIP LOCKED
  → dispatch → 設定 processed_on_utc + status = 'processed' → COMMIT
```

失敗時交易 rollback（釋放鎖），接著在**另一個**交易裡，`RecordFailureAsync` **只用 id** 讀取該列 —— 無
`FOR UPDATE`、不檢查 `processed_on_utc` —— 就把狀態覆寫成 `retrying` 或 `dead_letter`。

兩個 drainer A 與 B（合法的多實例部署）交錯：

1. A claim 到該列（`FOR UPDATE SKIP LOCKED`），dispatch 丟例外，A rollback → 鎖釋放。
2. B claim 到同一列（仍 `processed_on_utc IS NULL`），dispatch 成功，B 提交 `processed`。
3. A 此時才跑 `RecordFailureAsync`，**無鎖、不檢查 processed** 就讀取該列，覆蓋 B 的成功、寫入
   `retrying`/`dead_letter` 並累加 `retry_count`。

視窗很小（A rollback → A 開新交易 → A 讀取），但併發下真實存在，且可用測試 gate 完全確定地重現。

## 沒有被汙染的部分（為何有界）

業務效果仍是恰好一次。claim query 有 `AND processed_on_utc IS NULL`；B 已設定 `processed_on_utc`，而 A 的
失敗寫入不會清除它。因此下一輪 drain 的 claim 撈不到列，效果永遠不會被重跑。**沒有雙重效果** —— 這是
狀態／可觀測性的 bug，不是重複執行的 bug。

## 被汙染的部分

- **狀態說謊。** 該列最終 `status = 'retrying'`（或 `dead_letter`）卻同時 `processed_on_utc IS NOT NULL` ——
  一個永遠回不到 `processed` 的矛盾態。`retrying` 殭屍列會在每輪 drain 被重新選到、claim 不到任何列、永遠
  空轉。
- **指標說謊。** inbox *processed* 計數被 B 累加，而 *retried* / *dead-lettered* 計數被 A 為**同一則**訊息
  累加。
- **假死信。** 若 A 的重試額度已用盡，A 會為一則**其實成功**的訊息插入死信列。`inbox_dead_letters` 對
  `(logical_id, occurred_on_utc)` 沒有唯一性，操作者甚至會看到並去 reprocess 一則其實沒問題的「毒訊息」。

## 對 claim 的影響

它違反兩處明文 claim，在修正落地前先把措辭收斂成與程式一致：

- README 驗證對照表：「…applied exactly once (no double effect, **no spurious failure**)」。
- `05-events-and-messaging/integration-events.md`：稱 `FOR UPDATE SKIP LOCKED` 可避免「record a spurious
  failure」的註記。

`SKIP LOCKED` 防的是效果的併發*重複派工*；它**不**涵蓋 rollback 之後的失敗記錄路徑 —— 那段跑在後續的
另一個交易、無鎖、也不重新檢查狀態。

## 重現（test-first、deterministic）

別靠 `Task.WhenAll` 賭排程 —— 要強制交錯：

- Gate 住 dispatcher（例如用 `TaskCompletionSource` 撐住的測試用 `IInboxDispatcher`）。
- 時序：A 進入 dispatch 並阻塞 → 放 A 丟例外 → A rollback → B claim + 成功 + 提交 → 再放 A 進
  `RecordFailureAsync`。
- 斷言：該列仍 `processed`、`processed_on_utc` 不變、`retry_count` 不變、**沒有** `inbox_dead_letters` 列、
  retried / dead-lettered 指標不變。

此測試必須在今天的程式上 red，修完後 green。

## 修正（規劃中，最小）

`RecordFailureAsync` 在自己的交易裡用 `SELECT ... FOR UPDATE` 重新 claim 該列；若 `processed_on_utc IS NOT
NULL`（已成功）就提交一個 **no-op**。只有仍未處理的列才能累加 `retry_count` 或進死信。DLQ 唯一性約束
（`(logical_id, occurred_on_utc)` 索引 + upsert/guard）是 defense-in-depth 的後續項 —— 關閉本競態**並不
需要**它，因為源頭修正已能阻止假死信的插入。

## 教訓

Row lock 只保護它包住的臨界區。一個跑在*後續、另一個*交易裡的補償／記帳寫入，必須重新建立不變量 ——
重新鎖住該列並重新檢查狀態再寫。釋放鎖之後才提交「我稍早的決定」，正是對中間插進來的人的典型 lost update。

## 下一篇

- `09-lessons-learned/architecture-rules-for-my-own-project.md`
