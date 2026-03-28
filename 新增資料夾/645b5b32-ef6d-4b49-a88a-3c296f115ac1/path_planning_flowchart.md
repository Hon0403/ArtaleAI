# 路徑規劃系統流程圖（修正版）

## 整體架構

```mermaid
flowchart TB
    subgraph Init["🔧 初始化階段"]
        A1[用戶啟動程式] --> A2[MainForm 初始化]
        A2 --> A3[建立 PathPlanningManager]
        A2 --> A4[建立 CharacterMovementController]
        A3 --> A5[建立 PathPlanningTracker]
        A5 --> A6[建立 FlexiblePathPlanner]
    end

    subgraph Load["📂 載入路徑"]
        B1[用戶選擇路徑檔] --> B2["cbo_LoadPathFile_SelectedIndexChanged"]
        B2 --> B3["ReloadPathWithCurrentResolution()"]
        B3 --> B4["PathPlanningManager.LoadPlannedPath()"]
        B4 --> B5["PathPlanningTracker.SetPlannedPath()"]
        B5 --> B6["FlexiblePathPlanner.LoadMapData()"]
        B4 --> B7["設定 PlatformBounds 邊界"]
    end

    subgraph Start["▶️ 啟動追蹤"]
        C1["用戶點擊「開始」"] --> C2["PathPlanningManager.StartAsync()"]
        C2 --> C3["PathPlanningTracker.StartTracking()"]
        C2 --> C4["設定 MovementController 邊界"]
    end

    subgraph Loop["🔄 主循環（每 100ms）"]
        D1["LiveViewManager 擷取畫面"] --> D2["OnFrameAvailable()"]
        D2 --> D3["ProcessPathPlanning()"]
        D3 --> D4["GameVisionCore.GetMinimapTracking()"]
        D4 --> D5{"找到小地圖？"}
        D5 -->|否| D1
        D5 -->|是| D6["取得玩家位置"]
        D6 --> D7["PathPlanningManager.ProcessTrackingResult()"]
    end

    Init --> Load --> Start --> Loop
```

---

## 核心邏輯：UpdatePathState（修正版）

```mermaid
flowchart TB
    A["UpdatePathState(trackingResult)"] --> B{"有路徑規劃？"}
    B -->|否| Z[返回]
    B -->|是| C["取得玩家當前位置"]
    
    C --> D{"有臨時目標？"}
    D -->|是| E["actualTarget = TemporaryTarget"]
    D -->|否| F{"使用靈活規劃？"}
    
    F -->|否| G["actualTarget = NextWaypoint"]
    F -->|是| H["FlexiblePathPlanner.GenerateRandomTargetPoint()"]
    
    H --> I{"選到安全點？"}
    I -->|是| J["設定 TemporaryTarget"]
    I -->|否| K["🔧 搜尋整個路徑的安全點"]
    K -->|找到| J1["選最接近中心的點 → TemporaryTarget"]
    K -->|未找到| K2["返回 null（讓上層處理）"]
    
    J --> L["計算距離"]
    J1 --> L
    E --> L
    G --> L
    
    L --> M{"距離 < 8px？"}
    M -->|是| N["⚠️ 太近，跳過（不觸發到達事件）"]
    N --> O["清除 TemporaryTarget"]
    O --> P["CurrentWaypointIndex++"]
    P --> Q{"Index >= PlannedPath.Count？"}
    Q -->|是| R["🔄 重置至 Index 0（循環巡邏）"]
    Q -->|否| S["觸發 OnPathStateChanged"]
    R --> S
    S --> Z["返回（不繼續到 XY 判定）"]
    
    M -->|否| T{"XY 誤差判定<br/>X ≤ 15px 且 Y ≤ 10px？"}
    T -->|否| U["繼續追蹤，等待接近"]
    U --> Z2[返回]
    
    T -->|是| V["✅ 判定到達"]
    V --> W["觸發 OnWaypointReached"]
    W --> X["清除 TemporaryTarget"]
    X --> Y["尋找目標索引並推進（FindIndex or +1）"]
    Y --> Q2{"Index >= Count？"}
    Q2 -->|是| R2["🔄 重置至 Index 0"]
    Q2 -->|否| S2["觸發 OnPathStateChanged"]
    R2 --> S2
    S2 --> Z3[返回]
```

---

## 移動控制：OnPathTrackingUpdatedUI（修正版）

```mermaid
flowchart TB
    A["OnPathTrackingUpdatedUI()"] --> B{"有路徑狀態？"}
    B -->|否| Z[返回]
    B -->|是| C{"啟用自動移動？"}
    
    C -->|否| Z
    C -->|是| D{"距離 > 8px？"}
    
    D -->|否| E["StopMovement()<br/>_isMovementInProgress = false"]
    D -->|是| F{"_isMovementInProgress？"}
    
    F -->|是| G["跳過（避免方向衝突）"]
    F -->|否| H["_isMovementInProgress = true"]
    
    H --> I["捕獲 latestTarget = TemporaryTarget ?? NextWaypoint"]
    I --> J["捕獲 capturedPlayerPos"]
    J --> K["Task.Run: MoveToTargetAsync()"]
    K --> L["finally: _isMovementInProgress = false"]
```

---

## 移動控制器：MoveToTargetAsync（修正版）

```mermaid
flowchart TB
    A["MoveToTargetAsync(currentPos, targetPos)"] --> B{"玩家超出邊界？<br/>X < MinX - 2 or X > MaxX + 2"}
    B -->|是| C["🛑 緊急停止"]
    C --> D["觸發 OnBoundaryHit → return"]
    
    B -->|否| E{"接近邊界？<br/>剩餘 < 5px"}
    E -->|是| F["觸發減速預警"]
    
    E -->|否| G{"目標超出邊界？"}
    F --> G
    G -->|是| H["觸發 OnTargetOutOfBounds"]
    H --> I["StopMovement() → return"]
    
    G -->|否| J["計算 dx = target.X - current.X<br/>計算 dy = target.Y - current.Y"]
    
    J --> K{"Y軸誤差 > 5px？"}
    K -->|是| L{"dy > 0？<br/>（掉下去）"}
    L -->|是| M["🛑 掉下去！StopMovement() → return"]
    L -->|否| N["禁止水平移動（跳躍中）<br/>dx = 0"]
    
    K -->|否| O["正常判斷移動方向"]
    N --> O
    
    O --> P["選擇方向鍵<br/>dx > 0 → 右, dx < 0 → 左"]
    
    P --> Q{"距離判斷"}
    Q -->|"> 60px"| R["全速前進：長按方向鍵"]
    Q -->|"15-60px"| S["減速點按：按 40ms 放開"]
    Q -->|"< 15px"| T["微調蹭入：極短點按 + 延遲 80ms"]
    
    R --> U["SendKeyInput()"]
    S --> U
    T --> U
```

---

## 靈活路徑規劃：GenerateRandomTargetPoint（修正版）

```mermaid
flowchart TB
    A["GenerateRandomTargetPoint(currentPos, currentIndex)"] --> B{"已到路徑終點？<br/>index >= count - 1"}
    B -->|是| Z["返回 null"]
    
    B -->|否| C["從 currentIndex+1 開始掃描<br/>LookAheadCount = 1"]
    
    C --> D{"動作類型相同？"}
    D -->|是| E["加入候選清單"]
    D -->|否| F["停止掃描"]
    E --> C2{"繼續掃描？"}
    C2 --> D
    
    F --> G{"有邊界限制？"}
    G -->|否| H["直接從候選清單隨機選"]
    
    G -->|是| I["過濾安全範圍內的點<br/>MinX + 15 ~ MaxX - 15"]
    I --> J{"候選點為空？"}
    
    J -->|否| K["candidateIndices = safeCandidates"]
    J -->|是| L["🔧 搜尋整個路徑的安全點"]
    
    L --> M{"找到安全點？"}
    M -->|是| N["選最接近中心的點"]
    M -->|否| O["返回 null（讓上層處理）"]
    
    K --> P["從候選清單隨機選一個"]
    N --> P
    P --> Q["返回目標點座標"]
```

---

## 邊界處理：OnBoundaryHit（修正版）

```mermaid
flowchart TB
    A["OnBoundaryHit(direction)"] --> B{"direction = right？"}
    
    B -->|是| C["搜尋左側路徑點<br/>X < 玩家X - 10px"]
    B -->|否| D["搜尋右側路徑點<br/>X > 玩家X + 10px"]
    
    C --> E["排除邊界附近的點<br/>safetyMargin = 15px"]
    D --> E
    
    E --> F["按距離排序（優先選最遠的）"]
    F --> G{"找到候選點？"}
    
    G -->|是| H["選擇最遠的點"]
    G -->|否| I["⚠️ 強制選中間點<br/>middleIndex = Count / 2"]
    
    H --> J["更新 CurrentWaypointIndex"]
    I --> J
    J --> K["清除 TemporaryTarget"]
    K --> L["觸發 OnPathStateChanged"]
```

---

## 關鍵類別職責

| 類別 | 職責 |
|------|------|
| `MainForm` | UI 控制、事件訂閱、調用移動控制器（含互斥鎖） |
| `PathPlanningManager` | 管理 Tracker 和 Controller 的協調 |
| `PathPlanningTracker` | 路徑狀態管理、到達判定、循環重置、事件觸發 |
| `FlexiblePathPlanner` | 隨機選點、邊界過濾、保底搜尋 |
| `CharacterMovementController` | 鍵盤控制、三段式煞車、Y軸鎖死、邊界保護 |
| `GameVisionCore` | 小地圖偵測、玩家位置追蹤 |

---

## 關鍵變數與 Index 推進機制

| 變數 | 所在類別 | 用途 | 推進機制 |
|------|----------|------|----------|
| `TemporaryTarget` | PathPlanningState | 臨時目標（優先於 NextWaypoint） | 到達或跳過時清除 |
| `CurrentWaypointIndex` | PathPlanningState | 當前路徑點索引 | 見下表 |
| `_isMovementInProgress` | MainForm | 防止移動任務並行執行 | 任務開始設 true，finally 設 false |
| `_platformBounds` | 多處 | 平台邊界限制 | 由 MapData 設定 |

### CurrentWaypointIndex 推進機制

| 情況 | 觸發條件 | 處理方式 |
|------|----------|----------|
| 1️⃣ 太近跳過 | `distance < 8px` | `++` 或 `FindIndex` |
| 2️⃣ 到達確認 | `X ≤ 15px 且 Y ≤ 10px` | `FindIndex` 或 `++` |
| 3️⃣ 循環重置 | `Index >= Count` | 重置至 `0` |
| 4️⃣ 邊界處理 | `OnBoundaryHit` 觸發 | 選擇反方向的點索引 |

---

## 修正對照表

| # | 修正點 | 原本問題 | 修正內容 |
|---|--------|----------|----------|
| 1 | 太近閾值 | 寫 2px | 改為 8px (WaypointReachDistance) |
| 2 | 循環重置 | 只顯示一處 | 在兩處（跳過、到達）都顯示 |
| 3 | Y 軸鎖死 | 未區分 dy 正負 | `dy > 0` 掉下去 return，`dy < 0` 禁止水平 |
| 4 | 邊界過濾 | 未顯示保底搜尋 | 加入「搜尋整個路徑」分支 |
| 5 | Index 推進 | 說明不清 | 補充四種推進情況表格 |
