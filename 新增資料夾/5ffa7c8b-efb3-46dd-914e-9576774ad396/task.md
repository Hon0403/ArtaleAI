# 重構 Route B：徹底刪除舊版導航系統

## Phase 1: MapData DTO 更新
- [ ] 新增 `NavNodeData`, `NavEdgeData` DTO
- [ ] `MapData` 新增 `Nodes`, `Edges` 屬性
- [ ] **保留** `WaypointPaths`, `Connections` 供舊檔相容與編輯器使用

## Phase 2: AppConfig + 遷移工具
- [ ] 新增 `LegacyMapMigrator.cs` 轉換實作 (`Migrate`)
- [ ] 實作 `ToEditorFormat()` 供 MapEditor 自動反向載入新格式（**最後需清空 Nodes/Edges**）
- [ ] 實作 `ActionCodeToString()`, `ActionStringToCode()` 等靜態轉換
- [ ] 更新 `AppConfig.SaveMapToFile()`，使用乾淨的序列化副本避免污染 Editor 狀態

## Phase 3: PathPlanningTracker 重構
- [ ] 更新 `LoadMap()` — 解析 Nodes/Edges 建構 NavigationGraph
- [ ] 刪除 `_pathNodes`, `_currentTarget`, `_currentPathIndex` 等舊欄位
- [ ] 刪除 `SetPlannedPathWithActions()`, `BuildNavigationGraph()`, `ActionCodeToKeys()`
- [ ] 刪除 `GetNodePriorities()`, `FindClosestPathNodeIndex()`, `SetPlannedPath()`
- [ ] 修正 `SetActiveEdge()`, `UpdatePathState()`, `ForceAdvanceTarget()`, `Dispose()`

## Phase 4: MapEditor 儲存邏輯
- [ ] 修改 `LoadMapData()`：若遇新格式則自動呼叫 `ToEditorFormat()` 反向轉為內部索引陣列
- [ ] 確保 `LoadMapData()` 初始化 `WaypointPaths` 和 `Connections` 永不為 null
- [ ] 修改 `GetCurrentMapData()` 統一直出新格式，直接委派給 `LegacyMapMigrator.Migrate()`

## Phase 5: 型別遷移與刪除廢棄檔案
- [ ] 將 `PathActionType` enum 搬移到 `NavigationEnums.cs` 等共用檔
- [ ] 刪除 `PathNode.cs` 整個檔案
- [ ] 刪除 `SmartRopeNavigator.cs`
- [ ] 更新 `PathPlanningManager.cs`
- [ ] 更新 `MainForm.cs`
- [ ] 更新 `MinimapViewer.cs`（GetPriorityColor 簽名）
- [ ] 更新 `PathVisualizationModels.cs`

## Phase 6: 驗證
- [ ] `dotnet build` 通過
- [ ] 讀取新格式 JSON 運作正常
- [ ] 存檔後確認新格式 JSON 寫入正確
