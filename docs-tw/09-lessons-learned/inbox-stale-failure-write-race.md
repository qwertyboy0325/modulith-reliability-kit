# Inbox 失敗記錄的過期寫入競態（由 claim 稽核發現）

> 英文版對照：`docs/09-lessons-learned/inbox-stale-failure-write-race.md`。

## 狀態

**已修正**,並由 regression test 釘住。`RecordFailureAsync` 現在會以 `FOR UPDATE` 重新 claim 該列,若已被
併發 drainer 處理過就 no-op。失敗狀態**保留供參考**:先以 red 提交(commit `6bd3018`),再於下一個 commit
修正,因此只要 checkout 該 red commit 即可重現此 bug —— 見[重現](#重現test-firstdeterministic)。

## 快速結論

`FOR UPDATE SKIP LOCKED` 可以序列化「套用 inbox 效果」的那個 worker。

它本身並不會讓後續的狀態寫入變安全。

任何後續的 retry、dead-letter 或記帳交易，在寫入前都必須重新建立該列的不變量。

## 證據時間軸

1. 一個 deterministic regression 先以 red 提交：`6bd3018`。
2. 修正以阻塞式 `FOR UPDATE` 重新 claim 該列：`7d37540`。
3. 同一個測試在 `main` 上為 green。

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

## 對 claim 的影響（修正前）

修正前，這個交錯違反了兩處明文 claim：

- README 驗證對照表：「…applied exactly once (no double effect, **no spurious failure**)」。
- `05-events-and-messaging/integration-events.md`：稱 `FOR UPDATE SKIP LOCKED` 可避免「record a spurious
  failure」的註記。

`SKIP LOCKED` 防的是效果的併發*重複派工*；它**本身不**涵蓋 rollback 之後的失敗記錄路徑 —— 那段跑在後續的
另一個交易、無鎖、也不重新檢查狀態。

README 與實作現在陳述的是更窄、已驗證的保證：

- 套用路徑以 `FOR UPDATE SKIP LOCKED` claim 該列；
- 失敗記錄以阻塞式 `FOR UPDATE` **重新 claim** 該列；
- 當另一 drainer 已設定 `processed_on_utc` 時，失敗記錄為 **no-op**。

## 重現（test-first、deterministic）

由
`InboxConcurrencyReliabilityTests.Failure_Recording_After_A_Concurrent_Success_Does_Not_Overwrite_The_Processed_Row`
釘住（對真實 PostgreSQL 的整合測試）。

交錯是**強制的,不是賭排程**。失敗的嘗試 rollback 後緊接著記錄失敗,中間沒有 `await` 空檔,因此處理器
暴露一個最小的內部測試 seam(`AfterRollbackBeforeRecordFailureForTests`,在生產環境為 no-op),它在
rollback 之後（鎖已釋放）、`RecordFailureAsync` 之前觸發：

1. 處理器 A claim 到該列,dispatch 失敗 → rollback（鎖釋放）。
2. 在 seam 裡,處理器 B claim 到剛釋放的列,套用效果,並提交為 `processed`。
3. A 接著對這筆*已 processed* 的列跑 `RecordFailureAsync`。

斷言：效果恰好套用一次、該列仍 `processed` 且 `processed_on_utc` 有值、`retry_count` 仍為 `0`、沒有
`inbox_dead_letters` 列。

修正後,對已 processed 的列做失敗記錄會變成 no-op,測試在 `main` 上為 **green**。

**重現 red 供參考。** checkout 修正前的 commit 並跑該測試：

```bash
git checkout 6bd3018   # 刻意 red 的 commit
dotnet test src/Tests/ModulithReliabilityKit.IntegrationTests/ModulithReliabilityKit.IntegrationTests.csproj \
  --filter Failure_Recording_After_A_Concurrent_Success_Does_Not_Overwrite_The_Processed_Row
```

它會以下列訊息失敗：

```text
Assert.Equal() Failure: Strings differ
Expected: "processed"
Actual:   "retrying"
```

等效做法:在 `main` 上把 `RecordFailureAsync` 的 `FOR UPDATE` 重新 claim 換回單純的
`FirstOrDefaultAsync(x => x.Id == id)`（修正前的讀取）,同一個斷言就會失敗。

## 修正

`RecordFailureAsync` 在自己的交易裡用 `SELECT ... FOR UPDATE` 重新 claim 該列；若 `processed_on_utc IS NOT
NULL`（已成功）就提交一個 **no-op**。只有仍未處理的列才能累加 `retry_count` 或進死信。這裡刻意用**阻塞式**
`FOR UPDATE`(而非 `SKIP LOCKED`):若併發 drainer 仍在套用中,這會等它提交後看到已 processed 的列,而不是
跳過後照樣記一筆失敗。DLQ 唯一性約束（`(logical_id, occurred_on_utc)` 索引 + upsert/guard）是
defense-in-depth 的後續項 —— 關閉本競態**並不需要**它，因為源頭修正已能阻止假死信的插入。

## 教訓

Row lock 只保護它包住的臨界區。一個跑在*後續、另一個*交易裡的補償／記帳寫入，必須重新建立不變量 ——
重新鎖住該列並重新檢查狀態再寫。釋放鎖之後才提交「我稍早的決定」，正是對中間插進來的人的典型 lost update。

## 下一篇

- `09-lessons-learned/architecture-rules-for-my-own-project.md`
