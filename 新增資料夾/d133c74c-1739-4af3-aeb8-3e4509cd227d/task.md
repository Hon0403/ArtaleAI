# 導航穩定性與 SSOT 轉型任務清單

## 1. 診斷與計計畫完善 (PLANNING) [x]
- [x] 分析「來回觸碰」日誌與判定衝突關鍵
- [x] 修正實作計畫以符合「嚴格 Hitbox」反饋
- [x] 制定「無情清算 (Ruthless Purge)」舊架構計畫

## 2. 核心邏輯修正 (EXECUTION) [x]
- [x] **CharacterMovementController**: 
    - [x] 修改 `MoveToTargetAsync` 僅以 `isReachedExternally` 作為成功依據
- [x] **NavigationExecutor**:
    - [x] 對齊微調 (Micro-Taps) 邏輯已與 `IsPlayerAtTarget` 同步
- [/] **[同步與效能優化]**：修復視覺與路徑規劃的非同步斷層。
    - [ ] **[Pipeline]**：合併 `GamePipeline` 中重複的小地圖追蹤呼叫。
    - [ ] **[Cache]**：實作 `GameVisionCore` 的小地圖區域 (ROI) 快取。
    - [ ] **[Moments]**：使用影像矩運算 (`Cv2.Moments`) 優化玩家紅點定位。
    - [ ] **[Velocity]**：修正 `CharacterMovementController` 的速度歸零 Bug 並強化預測性煞車。
- [x] **[隊友血條]**：還原並優化隊友血條偵測。
- [x] **MapGenerationService**:
    - [x] 虛擬節點 ID 已改為座標 Hash 決定性 ID
- [x] **PathPlanningTracker**:
    - [x] 徹底清除 `rescue_from` 假邊緣 (避免跨樓層死鎖)
    - [x] 移除 Obsolete `StartTracking`/`StopTracking` 與 `_isTracking` 狀態

## 3. 架構對齊與舊代碼清除 (EXECUTION) [x]
- [x] **MainForm**: 
    - [x] 執行無情重構：物理刪除 `RenderingService`, `RopeAlignmentController`, `RouteRecorderService`
- [x] 執行無情重構：物理刪除 `GameVisionCore.Minimap.cs` (Zombie Fragment)
- [x] 執行無情重構：合併並刪除 `MainForm` 的 4 個冗餘偏類別檔案
- [x] 執行無情重構：合併並刪除 `MainForm_MinimapControl.cs`
- [x] 執行無情重構：物理刪除 `PathPlanningLogger.cs` (Dead Code)
- [x] 執行無情重構：物理刪除 `Core` 中的殭屍代碼 (`SharedGameState`, `MonsterThresholdEstimator`) 並清理 `GameVisionCore`
- [x] 執行無情重構：物理刪除 `Services` 中的最後兩級殭屍代碼 (`LogService`, `PathPlanningService`)
- [x] 執行無情重構：物理刪除 `UI` 中的最後一個殭屍代碼 (`FloatingMagnifier.cs`) 並清理 `MainForm.cs`
- [x] 執行無情重構：Models & Config SSOT 整合（移除重複定義與 `PartyRedBar` 殘留）
- [x] 配置持久化對齊：MainForm 存檔邏輯移至 OnFormClosing
- [x] 導航 SSOT 稽核：移除所有非 Hitbox 的抵達判定
  - [x] 刪除 `PlatformSegment.cs` (雙位址)
  - [x] 刪除 `PlatformBounds.cs` (Ghost Model)
  - [x] 刪除 `IPathPlanner.cs` 與 `ShortestPathPlanner.cs` (Obsolete)
  - [x] 刪除 `RopeData.cs` (Legacy)
- [x] **PathPlanningManager**:
    - [x] 重構以移除對 Obsolete 追蹤方法的依賴，改為 Reactive 模式

## 4. 驗證與回測 (VERIFICATION) [x]
- [x] 執行 `dotnet build` 確保 0 錯誤
- [x] 最終 Walkthrough 產出
