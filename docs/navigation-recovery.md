# 導航恢復架構與數值契約

本文件定義導航系統對「不確定性」的分層處理原則，以及 `config.yaml` → `NavigationSettings` 的參數對照。  
**共識：救援是產品能力，不是除錯手段；數量要少、契約要清楚。**

---

## 一、不確定性分類

| 類型 | 例子 | 處理方式 |
|------|------|----------|
| **程式 Bug** | 旁路推進 waypoint、silent fallback 掩蓋錯誤 | 修主路徑，**禁止**用救援掩蓋 |
| **資料契約缺失** | Climb 邊缺 `ropeX:`、NavGraph 未載入 | **fail-fast** + 載入驗證 Warning/Error |
| **人工地圖標記誤差** | 節點手點偏幾 px、相鄰 waypoint 過密、折線與節點略脫鉤 | **容差帶**吸收小誤差；超出則走統一救援 |
| **執行期外在擾動** | 被怪擊退、lag、視覺抖動、跳躍物理誤差 | **少數通用救援** + 熔斷 |

人工標記誤差與被擊退**共用同一套恢復原語**，不為每種偏差各加一條 `if`。

---

## 二、允許的恢復原語（僅此四類）

```
正常：TryStartNavigation → Executor → TryAcknowledgeWaypointCompletion → index++

異常：
  (1) 執行失敗恢復   Executor Failed/Error → TryRescuePath
  (2) 停滯恢復       StuckDetectionMs 位移不足 → TryRescuePath
  (3) 熔斷           連續救援 / Approach 失敗達上限 → 停止自動導航
  (4) 航程護欄       token/index 過期、重複 ack → 忽略幽靈回報
```

SideJump approach、繩上暫停 Walk 屬**主路徑子階段**，不是第五種救援。

### 禁止清單

- Idle 補推進（繞過 Executor 推進 index）
- 缺 metadata 時靜默猜值（如 `ropeX` fallback）
- Live 驗收誤判後在編排層偷推進
- 每種異常各寫一種專用 rescue 分支

---

## 三、三層數值契約

### 層 1 — 標記可接受誤差帶（設計期噪音）

在容差內視為「到達」，不要求座標與節點完全一致。

| 參數 (`config.yaml`) | 預設 | 用途 | 程式位置 |
|----------------------|------|------|----------|
| `walkAlignTolerancePx` | 1.0 | Walk / JumpTakeoff / Rope X 對齊 | `ArrivalValidator` |
| `slopeStandYTolerancePx` | 4.5 | 平台折線投影 Y、斜坡可站帶 | `ArrivalValidator`, `IsOnPlatformStandBand` |
| `ropeLandingYTolerancePx` | 1.5 | 爬繩落地 Y（嚴於斜坡） | `ArrivalValidator` RopeLanding |
| `ropeSegmentXTolerancePx` | 1.5 | 繩段 X、掛繩判定 | `NavigationRopeHelper`, RopeLanding |
| `platformHitboxWidth/Height` | 3.0 | PointHitbox 策略 | `ArrivalValidator` |
| `walkBrakeDistancePx` | 3.5 | Walk 煞車區，防 overshoot | `CharacterMovementController` |
| `2 × walkAlignTolerancePx` | 2.0 | 載入時「同平台 Walk 過密」警告門檻 | `NavigationGraph.LogMapDataIssues` |

**extrapolated 區**：折線外推時 Y 容差 ×1.5（`ArrivalValidator`），對應標記超出平台段範圍。

### 層 2 — 觸發救援的門檻（執行期假設被打破）

| 參數 / 常數 | 預設 | 用途 | 程式位置 |
|-------------|------|------|----------|
| `stuckDetectionMs` | 3000 | 停滯多久視為卡點 → `TryRescuePath` | `PathPlanningTracker.UpdateTrackingHistory` |
| `stuckDetectionMs` | 3000 | Jump 著陸等待上限 | `NavigationExecutor` WaitForLanding |
| `approachFailureRescueCutoff` | 3 | Jump approach Walk 連續失敗 → 熔斷放行或停止 | `RecordApproachWalkFailure` |
| 硬編碼 `150px` | — | 救援時最近節點搜尋半徑 | `TryRescuePath`, `ResolveApproachWalkEdge` |
| 硬編碼 `100px` | — | 隨機巡邏起點最近節點 | `SelectRandomPhysicalTarget` |
| 硬編碼 `60px` | — | `CurrentNavigationEdge` 邊解析 fallback | `PathPlanningTracker` |

> **技術債**：`150` / `100` / `60` px 尚未進 `config.yaml`，後續可收斂為 `rescueNearestNodeRadiusPx` 等。

位移門檻（卡點判定）：`0.5px` 位移（`distSq > 0.25`）視為有移動，硬編碼於 `UpdateTrackingHistory`。

### 層 3 — 熔斷（避免 livelock / 掩蓋 Bug）

| 參數 | 預設 | 用途 | 程式位置 |
|------|------|------|----------|
| `rescueRepeatCutoff` | 3 | 同 rescue key 連續救援次數上限 | `RecordRescueAttempt` |
| `approachFailureRescueCutoff` | 3 | 同一起跳節點 approach 失敗上限 | `_approachCutoffNodes` |
| 移動重置 | — | 角色位移 > 0.5px 時重置救援熔斷計數 | `UpdateTrackingHistory` |

熔斷觸發後：編排層 `CancelNavigation("救援熔斷生效")`，停止 `TryStartNavigation`。

---

## 四、主路徑與 Frozen 驗收

| 概念 | 說明 |
|------|------|
| `BeginNavigationFlight` | 凍結本 leg 驗收目標（`ExecutionTarget`） |
| `IsPlayerAtFrozenFlightTarget` | Executor 收尾唯一驗收（不受 Live 編排干擾） |
| `TryAcknowledgeWaypointCompletion` | 單次 ack；token + index 必須匹配 |

人工標記誤差在**單次 flight 內**由 Frozen 目標 + 層 1 容差共同吸收。

---

## 五、加新邏輯前的檢查清單

1. **無外在擾動時會重現嗎？** → 是 = Bug，修主路徑  
2. **修地圖或補 metadata 能解決嗎？** → 能 = 資料債，不用新救援  
3. **現有四原語能覆蓋嗎？** → 能 = 只調 `config.yaml` 或加 log  
4. **必須新原語嗎？** → 更新本文件：觸發條件、動作、與熔斷關係  

---

## 六、健康指標（實機 log）

| 指標 | 健康 | 需調查 |
|------|------|--------|
| `source=Executor` ack 比例 | 100% | 出現其他 source |
| `IdleSupplement` / 補推進 | 0 | 任何旁路推進 |
| `救援熔斷` | 偶發 | 頻繁觸發 |
| `[地圖驗證]` Warning | 載入時可知 | 忽略不修 |
| `X_OVER` + 救援 | 有戰鬥/跳躍時可接受 | 無干擾仍大量發生 → 調執行層或容差 |

救援次數多**不一定是架構問題**；熔斷頻繁或旁路推進才是紅旗。

---

## 七、相關檔案

- 設定 SSOT：`Data/config.yaml` → `Models/Config/NavigationSettings.cs`
- 驗收：`Core/Domain/Navigation/ArrivalValidator.cs`
- 恢復：`Core/PathPlanningTracker.cs`（`TryRescuePath`, 卡點偵測, 熔斷）
- FSM：`Services/NavigationStateMachine.cs`
- 地圖驗證：`Core/Domain/Navigation/NavigationGraph.LogMapDataIssues`
