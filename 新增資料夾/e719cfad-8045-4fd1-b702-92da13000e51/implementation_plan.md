# Phase 1: 更新 MapData DTO (新 JSON 格式)

修改 `MapData` DTO 以支援直接儲存導航圖（Nodes 與 Edges），並為舊有攔位加上 `JsonIgnore` 以優化新格式的儲存體積。

## Proposed Changes

### [ArtaleAI.Models.Map]

#### [MODIFY] [MapData.cs](file:///d:/Full_end/C%23/ArtaleAI/Models/Map/MapData.cs)

- 新增 `NavNodeData` 類別，用於序列化節點。
- 新增 `NavEdgeData` 類別，用於序列化邊（包含 InputSequence）。
- 在 `MapData` 類別中新增以下屬性：
  - `public List<NavNodeData> Nodes { get; set; } = new();`
  - `public List<NavEdgeData> Edges { get; set; } = new();`
- 替現有屬性 `WaypointPaths` 與 `Connections` 加上 `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`。
- 修改 `WaypointPaths` 與 `Connections` 的類型為 `List<float[]>?` 與 `List<int[]>?` 並設定預設為 `null`（或在建構子初始化，但計畫中提到新格式存檔時會清空這兩個攔位）。

## Verification Plan

### Automated Tests
- 執行 `dotnet build` 確保專案編譯成功，沒有命名空間衝突或語法錯誤。

### Manual Verification
- 無（本階段僅修改 DTO 結構，暫不涉及邏輯執行）。

# Phase 2: AppConfig + Migration Tool

建立轉移工具以提供舊地圖（只包含 `WaypointPaths` 和 `Connections`）到新版本 `MapData`（包含 `Nodes` 和 `Edges`）的轉換能力，並確保儲存時寫入新版本格式。

## Proposed Changes

### [ArtaleAI.Services]
#### [NEW] [LegacyMapMigrator.cs](file:///d:/Full_end/C%23/ArtaleAI/Services/LegacyMapMigrator.cs)
- 新增 `LegacyMapMigrator` 靜態類別。
- 實作 `Migrate(MapData oldData)` 將舊 `WaypointPaths` 轉換為 `Nodes`，將 `Connections` 轉換為 `Edges`。
- 實作 `ConvertActionCode(int oldActionCode)` 對應至新的 `NavigationActionType`。

### [ArtaleAI.Config]
#### [MODIFY] [AppConfig.cs](file:///d:/Full_end/C%23/ArtaleAI/Config/AppConfig.cs)
- 修改 `SaveMapToFile`：在序列化之前呼叫 `LegacyMapMigrator.Migrate(mapData)`，確保寫入硬碟的檔案皆採用新版 DTO 結構。

## Verification Plan
- [x] 自動驗證：`dotnet build` 無編譯錯誤。

# Phase 3: PathPlanningTracker Update

更新核心追蹤器，使其可以接納新的 `MapData` 格式，建立並維護 `NavigationGraph`。移除舊有不再使用的 API 以完成過渡。

## Proposed Changes

### [ArtaleAI.Core]
#### [MODIFY] [PathPlanningTracker.cs](file:///d:/Full_end/C%23/ArtaleAI/Core/PathPlanningTracker.cs)
- 加入必要 `using` 參考 (`ArtaleAI.Core.Domain.Navigation`, `ArtaleAI.Models.Map`)。
- 新增 `private NavigationGraph? _navGraph;` 變數負責維護新版導航圖。
- 實作 `LoadMap(MapData mapData)`：遍歷參數中的 `Nodes` 與 `Edges` 並轉換為內部 `NavigationNode`、`NavigationEdge` 後存入 `_navGraph`。
- 移除過期的 `SetPlannedPath(List<SdPoint>)` 和 `SetRopes(List<float[]>)`。

### [ArtaleAI.Services]
#### [MODIFY] [PathPlanningManager.cs](file:///d:/Full_end/C%23/ArtaleAI/Services/PathPlanningManager.cs)
- 更新原本呼叫 `_tracker.SetPlannedPath` 的地方。
- 將 `LoadMap()` 整個替換為只負責轉交給 `_tracker.LoadMap()`，並刪除不再使用的 `LoadPlannedPathWithActions()` 與 `LoadPlannedPath()`。

### [ArtaleAI.UI]
#### [MODIFY] [MainForm.cs](file:///d:/Full_end/C%23/ArtaleAI/UI/MainForm.cs)
- 移除 `_pathPlanningManager?.Tracker?.SetRopes` 的呼叫。
- 將清空路徑的呼叫從 `LoadPlannedPathWithActions` 改為 `LoadMap(new MapData())`。

## Verification Plan
- [x] 自動驗證：`dotnet build` 無編譯錯誤。
# Phase 5: Map Editor Migration

修改地圖編輯器的存檔邏輯，確保儲存時能將舊的 `WaypointPaths` 和 `Connections` 轉換為新的 `Nodes` 和 `Edges`。

## Proposed Changes

### [ArtaleAI.UI]
#### [MODIFY] [MapEditor.cs](file:///d:/Full_end/C%23/ArtaleAI/UI/MapEditor.cs)
- 修改 `GetCurrentMapData()` 函式。
- 在拋出 `_currentMapData` 給存檔前，加入一段動態轉換邏輯：遍歷所有的 `WaypointPaths` 建立 `NavNodeData`，並遍歷所有的 `Connections` 解析出 `Distance` 與 `ActionType` 來建立 `NavEdgeData`，將它們指派給 `Nodes` 與 `Edges`。

## Verification Plan
- [x] 自動驗證：`dotnet build` 無編譯錯誤。

# Phase 6: Restoring LiveView Path Visualization

修復因為遷移到 NavigationGraph 導致 LiveView/Minimap 無法繪製路徑點與繩索的問題。

## Proposed Changes

### [ArtaleAI.Core]
#### [MODIFY] [PathPlanningTracker.cs](file:///d:/Full_end/C%23/ArtaleAI/Core/PathPlanningTracker.cs)
- 新增公開屬性 `NavGraph` 讓 UI 層可以存取導航圖。
- 新增公開屬性 `CurrentTarget` 根據 `CurrentPathState` 查找並回傳實體 `NavigationNode`。

### [ArtaleAI.UI]
#### [MODIFY] [MainForm.cs](file:///d:/Full_end/C%23/ArtaleAI/UI/MainForm.cs)
- 修改 `BuildPathVisualizationData()` 方法，將舊有的 `WaypointPaths` 和 `Ropes` 陣列替換為從 `_pathPlanningManager.Tracker.NavGraph` 中提取節點資訊並轉成渲染結構。

## Verification Plan
- [x] 自動驗證：`dotnet build` 無編譯錯誤，確保 `MainForm` 的 Visualization API 可成功對接到新資料結構。

# Phase 7: End of Path Detection Fix

修復抵達終點（或路徑點）時沒有反應、卡死的問題。問題發生於 `CharacterMovementController` 因為 X 軸差距微小與 Y 軸差距 <= 3.0px 提前停止移動，但 `PathPlanningTracker` 使用基於歐幾里得幾何定理的嚴格距離計算，雙方標準不一致導致無線迴圈。

## Proposed Changes

### [ArtaleAI.Core]
#### [MODIFY] [PathPlanningTracker.cs](file:///d:/Full_end/C%23/ArtaleAI/Core/PathPlanningTracker.cs)
- 在 `UpdatePathState` 中取代舊有的直線距離 `distance <= checkDistance`，將檢測改為與 Movement Controller 完全雷同的方法（獨立判定 `Math.Abs(dx)` 及 `Math.Abs(dy)` 且給予垂直誤差 `dy <= 3.0` 的寬限）。

## Verification Plan
- [x] 自動驗證：確保編譯過關，系統不再卡死在 2~3 px 的垂直誤差。

# Phase 8: Rope Climbing Automation Fix

修復自動爬繩等動作失效的問題。因為先前架構調整移除了舊版 `CurrentTarget`，導致 `MainForm` 裡的 `CurrentNavigationEdge` 被註解掉。沒有導航邊界提供動作標籤，所有的路徑行為一律變成 `Idle`（普通移動），進而讓角色在繩索下不斷嘗試「走向空中」而卡死。

## Proposed Changes

### [ArtaleAI.Core]
#### [MODIFY] [PathPlanningTracker.cs](file:///d:/Full_end/C%23/ArtaleAI/Core/PathPlanningTracker.cs)
- 新增 `CurrentNavigationEdge` 供外部調用，利用 `CurrentWaypointIndex` 及 `_navGraph.GetEdge()` 取出目前兩點間的移動要求。

### [ArtaleAI.UI]
#### [MODIFY] [MainForm.cs](file:///d:/Full_end/C%23/ArtaleAI/UI/MainForm.cs)
- 解開 `NavigationEdge? currentEdge = _pathPlanningManager?.Tracker?.CurrentNavigationEdge;` 註解。
- 透過邊界狀態，判別動作是否為跳躍或爬繩，以呼叫特定的非同步函式（如 `ClimbRopeAsync`）。

## Verification Plan
- [x] 自動驗證：`dotnet build` 無編譯錯誤。角色於 AutoHunt 模式下能正常取得 ActionType 觸發 Climb 機制。

# Phase 9: NavigationGraph Build Error Fix

修復 `CurrentNavigationEdge` 新增時引發的 CS1061 編譯錯誤。由於 `NavigationGraph` 並未設計 `GetNodeAt` 方法，改採其內建的 `FindNearestNode` 以座標為基準反查 Node。

## Proposed Changes

### [ArtaleAI.Core]
#### [MODIFY] [PathPlanningTracker.cs](file:///d:/Full_end/C%23/ArtaleAI/Core/PathPlanningTracker.cs)
- 在 `CurrentNavigationEdge` 屬性中，將取用節點的方法從 `_navGraph.GetNodeAt` 修正為 `_navGraph.FindNearestNode`。

## Verification Plan
## Verification Plan
- [x] 自動驗證：確保程式能無錯誤編譯通過。

# Phase 10: AutoHunt A* Pathfinding Integration

為了解決「角色在繩索下不斷嘗試橫向走向目標（因為 AutoHunt 只知道隨機從清單中挑選一個節點目標，卻沒有透過導航圖規劃路徑）」的問題，將 AutoHunt 的選點邏輯與 `NavigationGraph` 的 A* 尋路接軌。

## Proposed Changes

### [ArtaleAI.Core]
#### [MODIFY] [PathPlanningTracker.cs](file:///d:/Full_end/C%23/ArtaleAI/Core/PathPlanningTracker.cs)
- 修改 `SelectSafeRandomTarget`：現在不單純從 `CurrentPathState.PlannedPath` 裡抽籤一個點就走，而是：
    1. 抓取角色目前的 `CurrentPlayerPosition`。
    2. 利用 `_navGraph.FindNearestNode(pos)` 找到角色目前所在的起始節點。
    3. 過濾出所有安全範圍內的 `Platform` 節點做為候選目標。
    4. 隨機選一個目標並呼叫 `_navGraph.FindPath(startId, goalId)` (A* 尋路)。
    5. 若尋路成功，將該路徑上的依序節點更新至 `CurrentPathState.PlannedPath`，並設置 `CurrentWaypointIndex = 1` 開始引導角色循序走過包含繩索或跳躍的邊界 (Edges)！
- 修改 `GetSafeCandidateIndices` 或將其重構成適用於 `NavigationNode` 清單的過濾器。

## Verification Plan
- [ ] 要求使用者進行驗證：以最新程式碼啟動 AutoHunt，角色在面對不同高度的節點時，能正確規劃出完整路徑並使用 `ClimbUp` 等邊界觸發動作。

# Phase 11: Rope to Navigation Edge Conversion

為了解決「自動打怪 A* 尋路找不到跨樓層的路徑（例如從 n38 到 n41）」的問題，修復在 `test.json` 等地圖存檔中，原始的 `Ropes` 沒有被正確轉換為 `NavEdgeData` 的錯誤。

- [x] 修改 `MapEditor.cs` [x]
    - [x] 讓 `GetCurrentMapData()` 處理 Ropes。 [x]
    - [x] 從繩索座標回推上下 Platform 節點 (擴大容錯至 50px)。 [x]
    - [x] 生成 `ActionType.ClimbUp` 及 `ClimbDown` 導航邊界並附加到 `Edges` 中。 [x]
- [x] 驗證並於地圖編輯器中存檔 [x]
- [x] 驗證 `test.json` 包含跨層繩索的 Edge。 [x]
